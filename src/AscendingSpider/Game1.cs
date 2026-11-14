using System;
#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using AscendingSpider.Domain;
using AscendingSpider.Solver;

namespace AscendingSpider;

public enum GameScreen { MainMenu, NewGameSetup, Settings, Playing, Generating, Achievements, Paused, Leaderboard }
public enum HintState { None, ShowSource, ShowTarget }
// Keep Classic=0 and Dark=1 so existing settings.json values remain valid.
public enum Theme { Classic = 0, Dark = 1, Ocean = 2, Burgundy = 3, Sand = 4 }

public partial class Game1 : Game
{
    private GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch = null!;
    private Rectangle _renderDestination;
    private Texture2D _pixel = null!;
    private Texture2D _starTexture = null!;
    private Texture2D? _tableBackdrop;
    private Texture2D? _cardShadow;
    
    private SpriteFont _fontLatin = null!;
    private SpriteFont _fontLatinItalic = null!;
    private SpriteFont _fontCjk = null!;
    private SpriteFont _fontSuit = null!;
    private SpriteFont _fontSuitLarge = null!;
    
    private SpriteFont _font = null!;
    private SpriteFont _fontItalic = null!;
    private float _fontScale = 0.25f;

    private GameScreen _currentScreen = GameScreen.MainMenu;
    private GameScreen _returnScreen = GameScreen.MainMenu;
    private Theme _currentTheme = Theme.Classic;
    private int _currentCardBack = 0;
    private GameMode _currentMode = GameMode.Standard;
    private Suit _currentSuit = Suit.Spades;

    private SoundEffect _clickSound = null!;
    private SoundEffect _hoverButtonSound = null!;
    private SoundEffect _hoverCardSound = null!;
    private SoundEffect _cardMoveSound = null!;
    private SoundEffect _dealCollectSound = null!;
    private SoundEffect _winSound = null!;
    private SoundEffect _stuckSound = null!;
    private SoundEffect _wrongSound = null!;
    private SoundEffect _dealDistSound = null!;
    private float _volume = 0.5f;

    private Texture2D _borderBlack2 = null!;
    private Texture2D _borderYellow3 = null!;
    private Texture2D _borderGold4 = null!;
    private Texture2D _hintMask = null!;
    private Texture2D _hintBorderThick = null!;
    // Rounded hint outlines are cached by logical size so tall card runs keep
    // the same corner radius as a single card without stretching its corners.
    private readonly Dictionary<(int Width, int Height, int Thickness), Texture2D> _hintOutlineCache = new();
    private readonly Queue<(int Width, int Height, int Thickness)> _hintOutlineCacheOrder = new();
    private readonly Dictionary<(int Width, int Height), Texture2D> _hintMaskCache = new();
    private readonly Queue<(int Width, int Height)> _hintMaskCacheOrder = new();
    private Texture2D _cardBack1 = null!;
    private Texture2D _cardBack2 = null!;
    private Texture2D _cardBack3 = null!;
    private Texture2D _cardBackMinimalist = null!;
    private Texture2D _cardBackSpace = null!;
    private Texture2D _cardBackGolden = null!;

    private UIButton _btnNewGame = null!, _btnContinue = null!, _btnSettings = null!, _btnExit = null!, _btnAchievements = null!, _btnLeaderboard = null!;
    // Main-menu hover help: the tooltip animates independently of the menu buttons.
    private bool _infoIconHovered;
    private float _infoTooltipProgress;
    private UIButton _btnStart = null!, _btnBackNewGame = null!, _btnBackSettings = null!, _btnBackAchievements = null!, _btnBackLeaderboard = null!;
    private UIButton _btnResetStats = null!;
    private UISelector _selectorSuit = null!, _selectorDifficulty = null!, _selectorLanguage = null!, _selectorTableStyle = null!, _selectorCardBack = null!, _selectorMode = null!;
    private UISlider _sliderVolume = null!;
    
    private UIButton _btnInGameMenu = null!;
    private UIButton _btnUndo = null!;
    private UIButton _btnHint = null!;

    private UIButton _btnWinRestart = null!, _btnWinMenu = null!;
    private UIButton _btnStuckUndo = null!, _btnStuckRestart = null!, _btnStuckMenu = null!;
    private bool _stuckSoundPlayed = false;

    private UIButton _btnPauseResume = null!, _btnPauseSettings = null!, _btnPauseMainMenu = null!, _btnPauseRestart = null!;

    private GameState _gameState = null!;
    private int _faceDownPerColumn = 5;
    private List<GameState> _undoStack = new();

    private const int CardWidth = 100;
    private const int CardHeight = 140;
    // Card masks and procedural backs are generated at 2x resolution so they
    // remain clean when the logical 1500x1000 scene is shown on large displays.
    private const int TextureQualityScale = 2;
    private const int CardSpacing = 25;
    private const int TableauTopY = 66;
    private const int TableauBottomY = 866;
    private const int FaceDownOffset = 19;
    private const int FaceUpOffset = 38;
    private const int StockX = 1500 - CardWidth - 50;
    private const int StockY = 1000 - CardHeight - 50;
    private const int CollectedX = 550;
    private const int CollectedY = 820;
    private const int HitboxPaddingX = 10;
    private const int HitboxPaddingY = 5;

    private double _gameTimeSeconds = 0;
    private bool _isGameActive = false;
    private double _bestTimeEasy = 0, _bestTimeMedium = 0, _bestTimeHard = 0;
    private float _winConfettiTimer = 0f;

    private bool _usedUndoThisGame = false;
    private bool _usedHintThisGame = false;
    // Исторический флаг текущей партии: остаётся true даже после отмены хода.
    private bool _mixedSuitPlacementUsedThisGame = false;
    private bool _winHandled = false;
    private int _currentDifficulty = 0;

    private Task<GeneratedDeal>? _dealGenerationTask = null;
    private int _generationAttempts = 0;
    private int _generationUnsolvableAttempts = 0;
    private int _generationUnknownAttempts = 0;
    private SolvabilityStatus _lastDealSolvability = SolvabilityStatus.Unknown;
    private bool _showDealSolvabilityStatus = false;

    // Dead-end reachability is evaluated once per distinct board position on a
    // worker thread. This avoids freezing the MonoGame update loop on complex
    // endgames and lets us distinguish reversible cycles from real progress.
    private string? _progressCheckKey;
    private Task<ProgressReachability>? _progressCheckTask;
    private CancellationTokenSource? _progressCheckCancellation;
    private ProgressReachability _progressCheckResult = ProgressReachability.Unknown;

    private bool _confirmReset = false;
    private float _resetConfirmTimer = 0f;

    private bool _showTutorial = false;

    private Queue<string> _achievementQueue = new();
    private AchievementPopup? _activeAchievementPopup = null;

    private class AchievementPopup
    {
        public string NameKey = "";
        public float Timer = 3f;
    }

    private readonly record struct ThemePalette(
        Color Table,
        Color Button,
        Color ButtonHover,
        Color Panel,
        Color PanelAccent,
        Color Accent,
        Color Hint,
        Color CardFace,
        Color CardBack,
        bool IsDark);

    private sealed record GeneratedDeal(GameState State, SolvabilityStatus Status);

    private bool _isDragging = false;
    private int _dragFromColumn = -1;
    private int _dragFromIndex = -1;
    private Point _dragStartMousePos;
    private float _dragHoldTime = 0f;
    private Vector2 _dragOffset;
    private Vector2 _dragPos;
    private Vector2 _mouseVelocity;
    private Vector2 _previousMousePos;
    private MouseState _previousMouseState;
    private KeyboardState _previousKeyboardState;

    private int _heldColumn = -1;
    private int _heldIndex = -1;

    private int _hoveredCol = -1;
    private int _hoveredIndex = -1;
    private bool _isStockHovered = false;

    private HintState _hintState = HintState.None;
    private float _hintTimer = 0f;
    private Move _hintMove;
    private bool _isHintStock = false;
    private readonly List<HintCandidate> _hintCandidates = new();
    private string _hintBoardFingerprint = string.Empty;
    private int _hintCandidateIndex = -1;
    private string _hintMessageKey = string.Empty;
    private float _hintMessageTimer;

    private sealed record HintCandidate(Move Move, bool IsStockDeal, int Score);

    private class CardAnimation
    {
        public Card Card;
        public Vector2 StartPos;
        public Vector2 EndPos;
        public float Duration;
        public float Time;
        public float Delay;
        public int TargetCol;
        public int TargetIndex;
        public bool IsCollecting;
        public int CollectSequenceIndex = -1;
        public bool IsFaceDown;
        public bool IsArc;
        public bool IsBounceBack;
    }
    private List<CardAnimation> _animations = new();

    private class FlipAnimation
    {
        public int Col;
        public int Index;
        public float Time;
        public float Duration = 0.25f;
    }
    private List<FlipAnimation> _flipAnims = new();

    private class Particle
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public float Life;
        public float MaxLife;
        public Color Color;
        public float Size;
    }
    private List<Particle> _particles = new();
    private Random _rng = new();

    private HashSet<(int col, int index)> _hiddenCards = new();

    public Game1()
    {
        _graphics = new GraphicsDeviceManager(this);
        // This game only needs the Reach feature set. It is less demanding on
        // older integrated GPUs than HiDef, while retaining the DesktopGL backend.
        _graphics.GraphicsProfile = GraphicsProfile.Reach;
        // Enable hardware multisampling to smooth rasterized edges without
        // applying texture filtering or blurring the card artwork and text.
        _graphics.PreferMultiSampling = true;
        // Use desktop/borderless fullscreen instead of changing the monitor mode.
        // Exclusive fullscreen would switch the display to 1500x1000 and the GPU
        // would then scale that lower resolution to the monitor, making the image blurry.
        _graphics.HardwareModeSwitch = false;
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        // Logical game coordinates remain 1500x1000, while drawing is scaled
        // directly into the real back buffer to avoid a blurry intermediate texture.
        _graphics.PreferredBackBufferWidth = 1500;
        _graphics.PreferredBackBufferHeight = 1000;
        Window.AllowUserResizing = true;
        Window.ClientSizeChanged += (s, e) => UpdateRenderDestination();

        Exiting += (sender, args) => SaveGame();
    }

    protected override void Initialize()
    {
        SaveManager.LoadSettings();
        _volume = SaveManager.Settings.Volume;
        Localization.Language = SaveManager.Settings.Language;
        int savedTheme = SaveManager.Settings.Theme;
        _currentTheme = Enum.IsDefined(typeof(Theme), savedTheme)
            ? (Theme)savedTheme
            : Theme.Classic;
        SaveManager.Settings.Theme = (int)_currentTheme;
        _bestTimeEasy = SaveManager.Settings.BestTimeEasy;
        _bestTimeMedium = SaveManager.Settings.BestTimeMedium;
        _bestTimeHard = SaveManager.Settings.BestTimeHard;

        Window.Title = Localization.Get("Title");

        _btnNewGame = new UIButton { TextKey = "NewGame", OnClick = () => { _currentScreen = GameScreen.NewGameSetup; } };
        _btnContinue = new UIButton { TextKey = "Continue", OnClick = () => { LoadGame(); } };
        _btnAchievements = new UIButton { TextKey = "Achievements", OnClick = () => { _currentScreen = GameScreen.Achievements; } };
        _btnLeaderboard = new UIButton { TextKey = "Leaderboard", OnClick = () => { _currentScreen = GameScreen.Leaderboard; } };
        _btnSettings = new UIButton { TextKey = "Settings", OnClick = () => { _returnScreen = GameScreen.MainMenu; _currentScreen = GameScreen.Settings; } };
        _btnExit = new UIButton { TextKey = "Exit", OnClick = () => Exit() };
        
        _selectorSuit = new UISelector(new Rectangle(560, 245, 380, 72), "Suit", new List<string> { "Hearts", "Clubs", "Spades", "Diamonds" }) { SelectedIndex = (int)Suit.Spades };
        _selectorDifficulty = new UISelector(new Rectangle(560, 365, 380, 72), "Difficulty", new List<string> { "Easy", "Medium", "Hard" });
        _selectorMode = new UISelector(new Rectangle(560, 485, 380, 72), "Mode", new List<string> { "Standard", "Blitz", "Endless" });
        _btnStart = new UIButton { Rect = new Rectangle(625, 610, 250, 65), TextKey = "Start", OnClick = () => { StartNewGame(); } };
        _btnBackNewGame = new UIButton { Rect = new Rectangle(625, 710, 250, 65), TextKey = "Back", OnClick = () => { _currentScreen = GameScreen.MainMenu; } };

        _selectorLanguage = new UISelector(new Rectangle(510, 205, 480, 68), "Language", new List<string> { "Русский", "English", "Español", "中文", "日本語" });
        _selectorLanguage.SelectedIndex = Localization.Language switch { "EN" => 1, "ES" => 2, "ZH" => 3, "JA" => 4, _ => 0 };
        _selectorLanguage.OnChanged = (idx) => { 
            Localization.Language = idx switch { 1 => "EN", 2 => "ES", 3 => "ZH", 4 => "JA", _ => "RU" }; 
            SaveManager.Settings.Language = Localization.Language;
            SaveManager.SaveSettings();
            UpdateFonts();
            Window.Title = Localization.Get("Title");
        };

        _sliderVolume = new UISlider(new Rectangle(560, 335, 380, 34), "Volume", _volume);
        _sliderVolume.OnChanged = (val) => { _volume = val; SoundEffect.MasterVolume = _volume; SaveManager.Settings.Volume = _volume; SaveManager.SaveSettings(); };

        _selectorTableStyle = new UISelector(new Rectangle(510, 440, 480, 68), "TableStyle",
            new List<string> { "Classic", "Dark", "Ocean", "Burgundy", "Sand" });
        _selectorTableStyle.SelectedIndex = (int)_currentTheme;
        _selectorTableStyle.OnChanged = (idx) => { _currentTheme = (Theme)idx; ApplyThemeColors(); RefreshThemeCardBacks(); RefreshTableBackdrop(); SaveManager.Settings.Theme = idx; SaveManager.SaveSettings(); };

        _selectorCardBack = new UISelector(new Rectangle(510, 575, 480, 68), "CardBack", new List<string>());
        _selectorCardBack.OnChanged = (idx) => { 
            if (idx < _selectorCardBack.OptionKeys.Count) {
                _currentCardBack = GetCardBackIdFromSelectorIndex(idx);
                SaveManager.Settings.CardBack = _currentCardBack; 
                SaveManager.SaveSettings(); 
            }
        };
        
        _btnResetStats = new UIButton { Rect = new Rectangle(510, 700, 480, 58), TextKey = "ResetStats", OnClick = () => {
            if (_confirmReset)
            {
                SaveManager.Settings.Wins = 0; SaveManager.Settings.Losses = 0;
                SaveManager.Settings.BestTimeEasy = 0; SaveManager.Settings.BestTimeMedium = 0; SaveManager.Settings.BestTimeHard = 0;
                SaveManager.Settings.AchFirstWin = false; SaveManager.Settings.AchNoUndo = false; SaveManager.Settings.AchNoHint = false;
                SaveManager.Settings.AchSpeedrun = false; SaveManager.Settings.Ach4Suits = false; SaveManager.Settings.AchHardMode = false; 
                SaveManager.Settings.AchDedicated = false; SaveManager.Settings.AchPerfectOrder = false;
                _achievementQueue.Clear(); _activeAchievementPopup = null;
                SaveManager.SaveSettings(); RecalcMainMenuButtons(); _confirmReset = false;
                RefreshCardBackSelector();
            }
            else { _confirmReset = true; _resetConfirmTimer = 3f; }
        } };
        _btnResetStats.BaseColor = new Color(140, 60, 60); _btnResetStats.HoverColor = new Color(180, 80, 80);
        
        _btnBackSettings = new UIButton { Rect = new Rectangle(625, 805, 250, 58), TextKey = "Back", OnClick = () => { _currentScreen = _returnScreen; } };
        _btnBackAchievements = new UIButton { Rect = new Rectangle(625, 810, 250, 58), TextKey = "Back", OnClick = () => { _currentScreen = GameScreen.MainMenu; } };
        _btnBackLeaderboard = new UIButton { Rect = new Rectangle(625, 820, 250, 58), TextKey = "Back", OnClick = () => { _currentScreen = GameScreen.MainMenu; } };

        _btnInGameMenu = new UIButton { Rect = new Rectangle(1325, 64, 125, 62), TextKey = "Menu", OnClick = () => { _currentScreen = GameScreen.Paused; } };
        _btnUndo = new UIButton { Rect = new Rectangle(25, 900, 225, 62), TextKey = "Undo", OnClick = () => PerformUndo() };
        _btnHint = new UIButton { Rect = new Rectangle(275, 900, 225, 62), TextKey = "Hint", OnClick = () => TriggerHint() };

        _btnWinRestart = new UIButton { Rect = new Rectangle(625, 485, 250, 68), TextKey = "NewGame", OnClick = () => { SaveManager.DeleteSave(); UpdateContinueButton(); _currentScreen = GameScreen.NewGameSetup; } };
        _btnWinMenu = new UIButton { Rect = new Rectangle(625, 585, 250, 68), TextKey = "Menu", OnClick = () => { SaveManager.DeleteSave(); UpdateContinueButton(); _currentScreen = GameScreen.MainMenu; } };

        _btnStuckUndo = new UIButton { Rect = new Rectangle(500, 560, 225, 68), TextKey = "Undo", OnClick = () => PerformUndo() };
        _btnStuckRestart = new UIButton { Rect = new Rectangle(775, 560, 225, 68), TextKey = "Restart", OnClick = () => { SaveManager.DeleteSave(); StartNewGame(); } };
        _btnStuckMenu = new UIButton { Rect = new Rectangle(625, 660, 250, 68), TextKey = "Menu", OnClick = () => { _currentScreen = GameScreen.MainMenu; } };

        _btnPauseResume = new UIButton { Rect = new Rectangle(625, 330, 250, 70), TextKey = "Resume", OnClick = () => { _currentScreen = GameScreen.Playing; } };
        _btnPauseRestart = new UIButton { Rect = new Rectangle(625, 435, 250, 70), TextKey = "Restart", OnClick = () => { SaveManager.DeleteSave(); StartNewGame(); } };
        _btnPauseSettings = new UIButton { Rect = new Rectangle(625, 540, 250, 70), TextKey = "Settings", OnClick = () => { _returnScreen = GameScreen.Paused; _currentScreen = GameScreen.Settings; } };
        _btnPauseMainMenu = new UIButton { Rect = new Rectangle(625, 645, 250, 70), TextKey = "MainMenu", OnClick = () => { SaveGame(); _currentScreen = GameScreen.MainMenu; } };

        RecalcMainMenuButtons();
        ApplyThemeColors();
        base.Initialize();
    }


    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ClearHintState();
            _tableBackdrop?.Dispose();
            _cardShadow?.Dispose();
            foreach (Texture2D texture in _hintOutlineCache.Values)
                texture.Dispose();
            _hintOutlineCache.Clear();
            _hintOutlineCacheOrder.Clear();
            foreach (Texture2D texture in _hintMaskCache.Values)
                texture.Dispose();
            _hintMaskCache.Clear();
            _hintMaskCacheOrder.Clear();
            _pixel?.Dispose();
            _starTexture?.Dispose();
        }
        base.Dispose(disposing);
    }

}
