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

public class Game1 : Game
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
    
    private SpriteFont _font = null!;
    private SpriteFont _fontItalic = null!;
    private float _fontScale = 0.25f;

    private GameScreen _currentScreen = GameScreen.MainMenu;
    private GameScreen _returnScreen = GameScreen.MainMenu;
    private Theme _currentTheme = Theme.Classic;
    private int _currentCardBack = 0;
    private GameMode _currentMode = GameMode.Standard;

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
    private UIButton _btnStart = null!, _btnBackNewGame = null!, _btnBackSettings = null!, _btnBackAchievements = null!, _btnBackLeaderboard = null!;
    private UIButton _btnResetStats = null!;
    private UISelector _selectorSuits = null!, _selectorDifficulty = null!, _selectorLanguage = null!, _selectorTableStyle = null!, _selectorCardBack = null!, _selectorMode = null!;
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
        _graphics.PreferMultiSampling = false;
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
        
        _selectorSuits = new UISelector(new Rectangle(560, 265, 380, 72), "Suits", new List<string> { "1Suit", "2Suits" });
        _selectorDifficulty = new UISelector(new Rectangle(560, 395, 380, 72), "Difficulty", new List<string> { "Easy", "Medium", "Hard" });
        _selectorMode = new UISelector(new Rectangle(560, 525, 380, 72), "Mode", new List<string> { "Standard", "Blitz", "Endless" });
        _btnStart = new UIButton { Rect = new Rectangle(625, 650, 250, 65), TextKey = "Start", OnClick = () => { StartNewGame(); } };
        _btnBackNewGame = new UIButton { Rect = new Rectangle(625, 735, 250, 65), TextKey = "Back", OnClick = () => { _currentScreen = GameScreen.MainMenu; } };

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

        _btnInGameMenu = new UIButton { Rect = new Rectangle(1325, 50, 125, 62), TextKey = "Menu", OnClick = () => { _currentScreen = GameScreen.Paused; } };
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

    private static byte ClampColorChannel(float value) => (byte)Math.Clamp((int)MathF.Round(value), 0, 255);

    private void RefreshTableBackdrop()
    {
        if (GraphicsDevice == null || _pixel == null) return;
        _tableBackdrop?.Dispose();
        const int width = 1500;
        const int height = 1000;
        var palette = GetThemePalette();
        var pixels = new Color[width * height];
        var random = new Random(7319 + (int)_currentTheme * 97);
        float centerX = width * 0.5f;
        float centerY = height * 0.48f;
        float maxDistance = MathF.Sqrt(centerX * centerX + centerY * centerY);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float dx = (x - centerX) / maxDistance;
                float dy = (y - centerY) / maxDistance;
                float distance = MathF.Sqrt(dx * dx + dy * dy);
                float light = MathHelper.Clamp(0.15f - distance * 0.17f, -0.05f, 0.12f);
                int grain = random.Next(-2, 3);
                pixels[y * width + x] = new Color(
                    ClampColorChannel(palette.Table.R * (1f + light) + grain),
                    ClampColorChannel(palette.Table.G * (1f + light) + grain),
                    ClampColorChannel(palette.Table.B * (1f + light) + grain),
                    (byte)255);
            }
        }
        _tableBackdrop = new Texture2D(GraphicsDevice, width, height, false, SurfaceFormat.Color);
        _tableBackdrop.SetData(pixels);
    }

    private void DrawPanel(Rectangle rect, Color fill, Color accent, float opacity = 0.94f)
    {
        _spriteBatch.Draw(_pixel, new Rectangle(rect.X + 8, rect.Y + 10, rect.Width, rect.Height), new Color(0, 0, 0, 78));
        _spriteBatch.Draw(_pixel, rect, fill * opacity);
        _spriteBatch.Draw(_pixel, new Rectangle(rect.X + 3, rect.Y + 3, rect.Width - 6, 2), Color.White * 0.08f);
        _spriteBatch.Draw(_pixel, new Rectangle(rect.X, rect.Y, rect.Width, 3), accent * 0.92f);
        _spriteBatch.Draw(_pixel, new Rectangle(rect.X, rect.Bottom - 2, rect.Width, 2), accent * 0.48f);
        _spriteBatch.Draw(_pixel, new Rectangle(rect.X, rect.Y, 2, rect.Height), accent * 0.42f);
        _spriteBatch.Draw(_pixel, new Rectangle(rect.Right - 2, rect.Y, 2, rect.Height), accent * 0.42f);
    }

    private void DrawScreenHeading(string key, int y, float scale = 0.9f)
    {
        DrawCenteredText(key, y, new Color(245, 225, 174), scale);
        _spriteBatch.Draw(_pixel, new Rectangle(610, y + 55, 280, 2), GetThemePalette().Accent * 0.72f);
    }

    private void UpdateRenderDestination()
    {
        int w = GraphicsDevice.PresentationParameters.BackBufferWidth;
        int h = GraphicsDevice.PresentationParameters.BackBufferHeight;
        if (w == 0 || h == 0) return;
        float windowAspect = (float)w / h;
        float targetAspect = 1.5f;
        if (windowAspect > targetAspect) {
            int width = (int)(h * targetAspect);
            int x = (w - width) / 2;
            _renderDestination = new Rectangle(x, 0, width, h);
        } else {
            int height = (int)(w / targetAspect);
            int y = (h - height) / 2;
            _renderDestination = new Rectangle(0, y, w, height);
        }
    }

    private MouseState GetScaledMouseState()
    {
        var state = Mouse.GetState();
        if (_renderDestination.Width == 0 || _renderDestination.Height == 0) return state;
        float scaleX = 1500f / _renderDestination.Width;
        float scaleY = 1000f / _renderDestination.Height;
        int mx = (int)((state.X - _renderDestination.X) * scaleX);
        int my = (int)((state.Y - _renderDestination.Y) * scaleY);
        return new MouseState(mx, my, state.ScrollWheelValue, state.LeftButton, state.MiddleButton, state.RightButton, state.XButton1, state.XButton2);
    }

    private void RecalcMainMenuButtons()
    {
        // All menu buttons use one measured column. The final button stays well
        // inside the panel, with enough room for its shadow and hover lift.
        const int x = 625;
        const int firstY = 205;
        const int spacing = 78;
        const int width = 250;
        const int height = 68;

        int y = firstY;
        _btnNewGame.Rect = new Rectangle(x, y, width, height); y += spacing;
        _btnContinue.Rect = new Rectangle(x, y, width, height); y += spacing;
        if (HasAnyAchievement()) { _btnAchievements.Rect = new Rectangle(x, y, width, height); y += spacing; }
        _btnLeaderboard.Rect = new Rectangle(x, y, width, height); y += spacing;
        _btnSettings.Rect = new Rectangle(x, y, width, height); y += spacing;
        _btnExit.Rect = new Rectangle(x, y, width, height);
    }

    private bool HasAnyAchievement()
    {
        return SaveManager.Settings.AchFirstWin || SaveManager.Settings.AchNoUndo || SaveManager.Settings.AchNoHint
            || SaveManager.Settings.AchSpeedrun || SaveManager.Settings.Ach4Suits || SaveManager.Settings.AchHardMode 
            || SaveManager.Settings.AchDedicated || SaveManager.Settings.AchPerfectOrder;
    }

    private void UpdateFonts()
    {
        if (Localization.Language == "ZH" || Localization.Language == "JA") { _font = _fontCjk; _fontItalic = _fontCjk; _fontScale = 0.3f; }
        else { _font = _fontLatin; _fontItalic = _fontLatinItalic; _fontScale = 0.25f; }
    }

    private ThemePalette GetThemePalette() => _currentTheme switch
    {
        Theme.Dark => new ThemePalette(
            new Color(25, 25, 30), new Color(50, 50, 70), new Color(70, 70, 100),
            new Color(40, 40, 55), new Color(100, 150, 220), Color.Gold,
            Color.DeepSkyBlue, new Color(220, 220, 220), new Color(50, 50, 70), true),
        Theme.Ocean => new ThemePalette(
            new Color(18, 69, 78), new Color(22, 95, 111), new Color(33, 126, 143),
            new Color(22, 74, 85), new Color(48, 146, 163), new Color(153, 226, 225),
            new Color(105, 225, 235), new Color(245, 252, 252), new Color(20, 76, 93), true),
        Theme.Burgundy => new ThemePalette(
            new Color(83, 36, 52), new Color(119, 48, 67), new Color(151, 64, 85),
            new Color(98, 43, 59), new Color(167, 77, 98), new Color(242, 205, 130),
            new Color(255, 215, 120), new Color(255, 246, 241), new Color(110, 36, 55), true),
        Theme.Sand => new ThemePalette(
            new Color(122, 105, 75), new Color(83, 68, 48), new Color(109, 89, 62),
            new Color(96, 81, 58), new Color(153, 125, 82), new Color(255, 218, 160),
            new Color(255, 225, 160), new Color(255, 249, 235), new Color(103, 83, 55), true),
        _ => new ThemePalette(
            new Color(0, 100, 0), new Color(40, 130, 70), new Color(60, 180, 100),
            new Color(30, 60, 30), new Color(60, 180, 100), Color.Gold,
            Color.Gold, Color.White, new Color(30, 60, 120), false)
    };

    private void ApplyThemeColors()
    {
        ThemePalette palette = GetThemePalette();
        var buttons = new List<UIButton> {
            _btnNewGame, _btnContinue, _btnAchievements, _btnLeaderboard, _btnSettings, _btnExit, _btnStart, _btnBackNewGame, _btnBackSettings, _btnBackAchievements, _btnBackLeaderboard,
            _btnInGameMenu, _btnUndo, _btnHint, _btnWinRestart, _btnWinMenu, _btnStuckUndo, _btnStuckRestart, _btnStuckMenu,
            _btnPauseResume, _btnPauseRestart, _btnPauseSettings, _btnPauseMainMenu, _btnResetStats
        };
        foreach (var btn in buttons)
        {
            btn.BaseColor = palette.Button;
            btn.HoverColor = palette.ButtonHover;
        }
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        UpdateRenderDestination();

        _pixel = new Texture2D(GraphicsDevice, 1, 1); _pixel.SetData(new[] { Color.White });
        RefreshTableBackdrop();
        _starTexture = CreateStarTexture(30 * TextureQualityScale);
        _fontLatin = Content.Load<SpriteFont>("OpenSans"); _fontLatinItalic = Content.Load<SpriteFont>("OpenSansItalic"); _fontCjk = Content.Load<SpriteFont>("NotoSansSC"); _fontSuit = Content.Load<SpriteFont>("Arial");
        UpdateFonts();

        _clickSound = Content.Load<SoundEffect>("Sounds/audio_click"); _hoverButtonSound = Content.Load<SoundEffect>("Sounds/audio_hover_button"); _hoverCardSound = Content.Load<SoundEffect>("Sounds/audio_hover_card"); _cardMoveSound = Content.Load<SoundEffect>("Sounds/audio_card_move"); _dealCollectSound = Content.Load<SoundEffect>("Sounds/audio_deal_collect"); _winSound = Content.Load<SoundEffect>("Sounds/audio_win");
        try { _stuckSound = Content.Load<SoundEffect>("Sounds/audio_stuck"); } catch (ContentLoadException) { _stuckSound = _winSound; }
        try { _wrongSound = Content.Load<SoundEffect>("Sounds/audio_wrong"); } catch (ContentLoadException) { _wrongSound = _clickSound; }
        try { _dealDistSound = Content.Load<SoundEffect>("Sounds/audio_deal_dist"); } catch (ContentLoadException) { _dealDistSound = _cardMoveSound; }

        int textureWidth = CardWidth * TextureQualityScale;
        int textureHeight = CardHeight * TextureQualityScale;
        int textureRadius = 10 * TextureQualityScale;
        _borderBlack2 = CreateRoundedBorder(textureWidth, textureHeight, textureRadius, 2 * TextureQualityScale);
        _borderYellow3 = CreateRoundedBorder(textureWidth, textureHeight, textureRadius, 3 * TextureQualityScale);
        _borderGold4 = CreateRoundedBorder(textureWidth, textureHeight, textureRadius, 4 * TextureQualityScale);
        _hintMask = CreateRoundedMask(textureWidth, textureHeight, textureRadius);
        _hintBorderThick = CreateRoundedBorder(textureWidth, textureHeight, textureRadius, 5 * TextureQualityScale);
        _cardShadow = CreateSoftCardShadow();
        
        // Card backs use colors derived from the active table theme.
        // Special unlocked backs keep their motifs, but share the same palette.
        RefreshThemeCardBacks();

        SoundEffect.MasterVolume = _volume;
        UpdateContinueButton();
        RefreshCardBackSelector();
    }

    // Shared 2x soft shadow from the first visual-polish pass.
    private Texture2D CreateSoftCardShadow()
    {
        const int scale = TextureQualityScale;
        const int paddingX = 12;
        const int paddingTop = 10;
        const int paddingBottom = 16;
        const float cornerRadius = 11f;

        int width = (CardWidth + paddingX * 2) * scale;
        int height = (CardHeight + paddingTop + paddingBottom) * scale;
        float centerX = (paddingX + CardWidth * 0.5f) * scale;
        float centerY = (paddingTop + CardHeight * 0.5f + 3f) * scale;
        float halfWidth = CardWidth * 0.5f * scale;
        float halfHeight = CardHeight * 0.5f * scale;
        float radius = cornerRadius * scale;
        float blur = 6.5f * scale;

        var texture = new Texture2D(GraphicsDevice, width, height);
        var pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            float qx = MathF.Abs(x - centerX) - (halfWidth - radius);
            float qy = MathF.Abs(y - centerY) - (halfHeight - radius);
            float outsideX = MathF.Max(qx, 0f);
            float outsideY = MathF.Max(qy, 0f);
            float signedDistance = MathF.Sqrt(outsideX * outsideX + outsideY * outsideY)
                + MathF.Min(MathF.Max(qx, qy), 0f) - radius;

            float alpha;
            if (signedDistance <= 0f)
            {
                alpha = 58f;
            }
            else
            {
                float normalized = signedDistance / blur;
                alpha = 76f * MathF.Exp(-normalized * normalized * 1.65f);
            }

            pixels[y * width + x] = new Color(
                (byte)0, (byte)0, (byte)0,
                (byte)Math.Clamp((int)MathF.Round(alpha), 0, 255));
        }

        texture.SetData(pixels);
        return texture;
    }

    private void DrawCardShadow(Rectangle cardRect, float opacity)
    {
        if (_cardShadow == null || _cardShadow.IsDisposed || opacity <= 0f)
            return;

        float horizontalScale = cardRect.Width / (float)CardWidth;
        int paddingX = Math.Max(1, (int)MathF.Round(12f * horizontalScale));
        int paddingTop = Math.Max(1, (int)MathF.Round(10f * cardRect.Height / CardHeight));
        int paddingBottom = Math.Max(1, (int)MathF.Round(16f * cardRect.Height / CardHeight));
        var shadowRect = new Rectangle(
            cardRect.X - paddingX,
            cardRect.Y - paddingTop,
            cardRect.Width + paddingX * 2,
            cardRect.Height + paddingTop + paddingBottom);

        _spriteBatch.Draw(_cardShadow, shadowRect, Color.White * MathHelper.Clamp(opacity, 0f, 1f));
    }

    private Texture2D CreateMinimalistCardBack(Color borderColor, Color faceColor)
    {
        int scale = TextureQualityScale;
        int width = CardWidth * scale;
        int height = CardHeight * scale;
        var tex = new Texture2D(GraphicsDevice, width, height);
        Color[] data = new Color[width * height];

        for (int x = 0; x < width; x++) for (int y = 0; y < height; y++) {
            bool inside = IsInsideRoundedRect(x, y, width, height, 10 * scale);
            if (!inside) { data[y * width + x] = Color.Transparent; continue; }
            bool border = !IsInsideRoundedRect(x - 4 * scale, y - 4 * scale,
                width - 8 * scale, height - 8 * scale, 6 * scale);
            data[y * width + x] = border ? borderColor : faceColor;
        }

        tex.SetData(data);
        return tex;
    }

    private Texture2D CreateSpaceCardBack(Color backgroundColor, Color starColor)
    {
        int scale = TextureQualityScale;
        int width = CardWidth * scale;
        int height = CardHeight * scale;
        var tex = new Texture2D(GraphicsDevice, width, height);
        Color[] data = new Color[width * height];
        var rng = new Random(42);

        // Generate stars on the logical-resolution grid and expand each sample
        // into a scale x scale block. This keeps the original pattern density.
        for (int baseX = 0; baseX < CardWidth; baseX++)
        for (int baseY = 0; baseY < CardHeight; baseY++) {
            Color color = rng.Next(100) < 5 ? starColor : backgroundColor;
            for (int dx = 0; dx < scale; dx++)
            for (int dy = 0; dy < scale; dy++) {
                int x = baseX * scale + dx;
                int y = baseY * scale + dy;
                bool inside = IsInsideRoundedRect(x, y, width, height, 10 * scale);
                data[y * width + x] = inside ? color : Color.Transparent;
            }
        }

        tex.SetData(data);
        return tex;
    }

    private Texture2D CreateGoldenCardBack(Color outerColor, Color innerColor)
    {
        int scale = TextureQualityScale;
        int width = CardWidth * scale;
        int height = CardHeight * scale;
        var tex = new Texture2D(GraphicsDevice, width, height);
        Color[] data = new Color[width * height];

        for (int x = 0; x < width; x++) for (int y = 0; y < height; y++) {
            bool inside = IsInsideRoundedRect(x, y, width, height, 10 * scale);
            if (!inside) { data[y * width + x] = Color.Transparent; continue; }
            bool inner = IsInsideRoundedRect(x - 6 * scale, y - 6 * scale,
                width - 12 * scale, height - 12 * scale, 5 * scale);
            data[y * width + x] = inner ? innerColor : outerColor;
        }

        tex.SetData(data);
        return tex;
    }

    private static Color MixColors(Color first, Color second, float amount)
    {
        amount = MathHelper.Clamp(amount, 0f, 1f);
        return new Color(
            (byte)MathHelper.Lerp(first.R, second.R, amount),
            (byte)MathHelper.Lerp(first.G, second.G, amount),
            (byte)MathHelper.Lerp(first.B, second.B, amount),
            (byte)MathHelper.Lerp(first.A, second.A, amount));
    }

    private static void ReplaceTexture(ref Texture2D target, Texture2D replacement)
    {
        target?.Dispose();
        target = replacement;
    }

    private void RefreshThemeCardBacks()
    {
        ThemePalette palette = GetThemePalette();

        Color primaryBase = MixColors(palette.CardBack, palette.Table, 0.18f);
        Color primaryInner = MixColors(primaryBase, palette.PanelAccent, 0.38f);
        Color alternateBase = MixColors(palette.Button, palette.CardBack, 0.35f);
        Color alternateInner = MixColors(palette.Panel, palette.Accent, 0.28f);
        Color patternBase = MixColors(palette.Table, palette.Panel, 0.42f);
        Color patternInner = MixColors(palette.PanelAccent, palette.CardBack, 0.30f);

        ReplaceTexture(ref _cardBack1, CreateCardBack(primaryBase, primaryInner, 0));
        ReplaceTexture(ref _cardBack2, CreateCardBack(alternateBase, alternateInner, 0));
        ReplaceTexture(ref _cardBack3, CreateCardBack(patternBase, patternInner, 1));

        // Achievement backs retain their identity (minimal, space, gold), while
        // their colors are gently adapted to the currently selected table theme.
        ReplaceTexture(ref _cardBackMinimalist,
            CreateMinimalistCardBack(
                MixColors(palette.PanelAccent, palette.Table, 0.20f),
                MixColors(palette.CardFace, palette.Table, 0.10f)));
        ReplaceTexture(ref _cardBackSpace,
            CreateSpaceCardBack(
                MixColors(palette.CardBack, Color.Black, 0.42f),
                MixColors(palette.Accent, Color.White, 0.18f)));
        ReplaceTexture(ref _cardBackGolden,
            CreateGoldenCardBack(
                MixColors(palette.CardBack, new Color(115, 86, 28), 0.30f),
                MixColors(Color.Gold, palette.Accent, 0.20f)));
    }

    private void RefreshCardBackSelector()
    {
        _selectorCardBack.OptionKeys.Clear();
        _selectorCardBack.OptionKeys.Add("Classic");
        _selectorCardBack.OptionKeys.Add("Red");
        _selectorCardBack.OptionKeys.Add("Pattern");
        if (SaveManager.Settings.AchFirstWin) _selectorCardBack.OptionKeys.Add("Minimalism");
        if (SaveManager.Settings.AchNoUndo) _selectorCardBack.OptionKeys.Add("Space");
        if (SaveManager.Settings.Ach4Suits) _selectorCardBack.OptionKeys.Add("Golden");

        int realId = SaveManager.Settings.CardBack;
        int idx = GetSelectorIndexFromCardBackId(realId);
        if (idx == -1) { // Card back is locked, reset to 0
            _currentCardBack = 0;
            SaveManager.Settings.CardBack = 0;
            SaveManager.SaveSettings();
            idx = 0;
        }
        _selectorCardBack.SelectedIndex = idx;
    }

    private int GetCardBackIdFromSelectorIndex(int idx)
    {
        if (idx < 3) return idx;
        int result = 3;
        if (SaveManager.Settings.AchFirstWin) { if (idx == result) return 3; result++; }
        if (SaveManager.Settings.AchNoUndo) { if (idx == result) return 4; result++; }
        if (SaveManager.Settings.Ach4Suits) { if (idx == result) return 5; result++; }
        return 0;
    }

    private int GetSelectorIndexFromCardBackId(int id)
    {
        if (id < 3) return id;
        int result = 3;
        if (SaveManager.Settings.AchFirstWin) { if (id == 3) return result; result++; }
        if (SaveManager.Settings.AchNoUndo) { if (id == 4) return result; result++; }
        if (SaveManager.Settings.Ach4Suits) { if (id == 5) return result; result++; }
        return -1;
    }

    private Texture2D CreateStarTexture(int size)
    {
        var tex = new Texture2D(GraphicsDevice, size, size); var data = new Color[size * size];
        Vector2 center = new Vector2(size / 2f, size / 2f); float outerR = size / 2f - 1; float innerR = outerR * 0.42f;
        var points = new Vector2[10];
        for (int i = 0; i < 10; i++) { float angle = -MathF.PI / 2f + i * MathF.PI / 5f; float r = (i % 2 == 0) ? outerR : innerR; points[i] = center + new Vector2(MathF.Cos(angle) * r, MathF.Sin(angle) * r); }
        for (int x = 0; x < size; x++) for (int y = 0; y < size; y++) data[y * size + x] = PointInPolygon(new Vector2(x + 0.5f, y + 0.5f), points) ? Color.White : Color.Transparent;
        tex.SetData(data); return tex;
    }

    private bool PointInPolygon(Vector2 p, Vector2[] poly)
    {
        bool inside = false; int n = poly.Length;
        for (int i = 0, j = n - 1; i < n; j = i++)
            if (((poly[i].Y > p.Y) != (poly[j].Y > p.Y)) && (p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)) inside = !inside;
        return inside;
    }

    private Texture2D CreateCardBack(Color baseColor, Color innerColor, int patternType)
    {
        int scale = TextureQualityScale;
        int width = CardWidth * scale;
        int height = CardHeight * scale;
        var tex = new Texture2D(GraphicsDevice, width, height);
        Color[] data = new Color[width * height];

        for (int x = 0; x < width; x++) for (int y = 0; y < height; y++) {
            bool inside = IsInsideRoundedRect(x, y, width, height, 10 * scale);
            if (!inside) { data[y * width + x] = Color.Transparent; continue; }
            bool inner = IsInsideRoundedRect(x - 8 * scale, y - 8 * scale,
                width - 16 * scale, height - 16 * scale, 5 * scale);
            Color color = inner ? innerColor : baseColor;
            if (inner && patternType == 1 && (x % (8 * scale) == 0 || y % (8 * scale) == 0))
                color = baseColor;
            data[y * width + x] = color;
        }

        tex.SetData(data);
        return tex;
    }

    private Texture2D CreateRoundedMask(int width, int height, int radius)
    {
        var tex = new Texture2D(GraphicsDevice, width, height); Color[] data = new Color[width * height];
        for (int x = 0; x < width; x++) for (int y = 0; y < height; y++) data[y * width + x] = IsInsideRoundedRect(x, y, width, height, radius) ? Color.White : Color.Transparent;
        tex.SetData(data); return tex;
    }

    private Texture2D CreateRoundedBorder(int width, int height, int radius, int thickness)
    {
        var tex = new Texture2D(GraphicsDevice, width, height); Color[] data = new Color[width * height];
        for (int x = 0; x < width; x++) for (int y = 0; y < height; y++) {
            bool isBorder = false; float dx = 0, dy = 0; bool inCorner = false;
            if (x < radius && y < radius) { dx = x - radius; dy = y - radius; inCorner = true; }
            else if (x >= width - radius && y < radius) { dx = x - (width - radius - 1); dy = y - radius; inCorner = true; }
            else if (x < radius && y >= height - radius) { dx = x - radius; dy = y - (height - radius - 1); inCorner = true; }
            else if (x >= width - radius && y >= height - radius) { dx = x - (width - radius - 1); dy = y - (height - radius - 1); inCorner = true; }
            if (inCorner) { float dist = (float)Math.Sqrt(dx * dx + dy * dy); if (dist <= radius && dist >= radius - thickness) isBorder = true; }
            else if (x < thickness || x >= width - thickness || y < thickness || y >= height - thickness) isBorder = true;
            data[y * width + x] = isBorder ? Color.White : Color.Transparent;
        }
        tex.SetData(data); return tex;
    }

    private bool IsInsideRoundedRect(int x, int y, int width, int height, int radius)
    {
        if (x < 0 || y < 0 || x >= width || y >= height) return false;
        float dx = 0, dy = 0; bool inCorner = false;
        if (x < radius && y < radius) { dx = x - radius; dy = y - radius; inCorner = true; }
        else if (x >= width - radius && y < radius) { dx = x - (width - radius - 1); dy = y - radius; inCorner = true; }
        else if (x < radius && y >= height - radius) { dx = x - radius; dy = y - (height - radius - 1); inCorner = true; }
        else if (x >= width - radius && y >= height - radius) { dx = x - (width - radius - 1); dy = y - (height - radius - 1); inCorner = true; }
        if (inCorner) return Math.Sqrt(dx * dx + dy * dy) <= radius; return true;
    }

    private void UpdateContinueButton() => _btnContinue.IsEnabled = SaveManager.HasSave();

    private void StartNewGame()
    {
        ClearHintState();
        SaveManager.DeleteSave(); UpdateContinueButton();
        int suitCount = Math.Clamp(_selectorSuits.SelectedIndex + 1, 1, 2);
        _currentDifficulty = _selectorDifficulty.SelectedIndex;
        _faceDownPerColumn = _currentDifficulty switch { 0 => 3, 1 => 5, 2 => 7, _ => 5 };
        _currentMode = (GameMode)_selectorMode.SelectedIndex;
        
        _generationAttempts = 0;
        _generationUnsolvableAttempts = 0;
        _generationUnknownAttempts = 0;
        _showDealSolvabilityStatus = false;
        _usedUndoThisGame = false; _usedHintThisGame = false;
        _mixedSuitPlacementUsedThisGame = false;
        _currentScreen = GameScreen.Generating;
        int suitCountLocal = suitCount; int faceDownLocal = _faceDownPerColumn; var modeLocal = _currentMode;
        _dealGenerationTask = Task.Run(() => GenerateSolvableParallel(suitCountLocal, faceDownLocal, modeLocal));
    }

    private GeneratedDeal GenerateSolvableParallel(
        int suitCount,
        int faceDown,
        GameMode mode,
        int maxSeconds = 35)
    {
        // Endless has no terminal win condition, so its initial deal cannot be
        // certified by the standard Spider solver.
        if (mode == GameMode.Endless)
        {
            return new GeneratedDeal(
                DeckFactory.Deal(suitCount, faceDown, new Random(), mode),
                SolvabilityStatus.Unknown);
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(maxSeconds));

        GameState? solvedDeal = null;
        GameState? bestUnknownDeal = null;
        int bestUnknownScore = int.MaxValue;
        int bestUnknownStates = -1;
        object resultLock = new();

        // Keep CPU and memory use bounded for older integrated-GPU/CPU systems.
        int threadCount = Math.Max(1, Math.Min(Environment.ProcessorCount, 2));
        int maxStates = suitCount switch
        {
            1 => 100_000,
            2 => 75_000,
            3 => 50_000,
            4 => 35_000,
            _ => 50_000
        };

        try
        {
            Parallel.For(
                0,
                threadCount,
                new ParallelOptions
                {
                    CancellationToken = cts.Token,
                    MaxDegreeOfParallelism = threadCount
                },
                workerIndex =>
                {
                    var solver = new SpiderSolver();
                    var rng = new Random(HashCode.Combine(
                        Environment.TickCount,
                        workerIndex,
                        Guid.NewGuid().GetHashCode()));

                    while (!cts.Token.IsCancellationRequested)
                    {
                        cts.Token.ThrowIfCancellationRequested();

                        GameState candidate = DeckFactory.Deal(suitCount, faceDown, rng, mode);
                        Interlocked.Increment(ref _generationAttempts);

                        SolverSearchResult search;
                        try
                        {
                            search = solver.SolveDetailed(candidate, maxStates, cts.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            // If the global timer expired while this candidate
                            // was being searched (rather than another worker
                            // finding a solution), this attempt is Unknown.
                            lock (resultLock)
                            {
                                if (solvedDeal == null)
                                    Interlocked.Increment(ref _generationUnknownAttempts);
                            }
                            return;
                        }

                        if (search.Status == SolvabilityStatus.Solvable)
                        {
                            lock (resultLock)
                            {
                                if (solvedDeal == null)
                                    solvedDeal = candidate.Clone();
                            }

                            cts.Cancel();
                            return;
                        }

                        if (search.Status == SolvabilityStatus.Unsolvable)
                        {
                            // This candidate was exhaustively searched. It is
                            // safe to discard; unlike Unknown, this is a proof.
                            Interlocked.Increment(ref _generationUnsolvableAttempts);
                            continue;
                        }

                        // The state/time budget was reached. Keep the strongest
                        // partial candidate, but never label it as solved or
                        // proven impossible.
                        Interlocked.Increment(ref _generationUnknownAttempts);
                        lock (resultLock)
                        {
                            bool isBetter =
                                search.BestHeuristicScore < bestUnknownScore
                                || (search.BestHeuristicScore == bestUnknownScore
                                    && search.StatesChecked > bestUnknownStates);

                            if (isBetter)
                            {
                                bestUnknownDeal = candidate.Clone();
                                bestUnknownScore = search.BestHeuristicScore;
                                bestUnknownStates = search.StatesChecked;
                            }
                        }
                    }
                });
        }
        catch (OperationCanceledException)
        {
            // Expected when a certified deal is found or the overall time limit expires.
        }
        catch (AggregateException)
        {
            // Parallel.For may wrap cancellation in AggregateException.
        }

        lock (resultLock)
        {
            if (solvedDeal != null)
                return new GeneratedDeal(solvedDeal, SolvabilityStatus.Solvable);

            if (bestUnknownDeal != null)
                return new GeneratedDeal(bestUnknownDeal, SolvabilityStatus.Unknown);
        }

        // Every candidate may have been proven impossible before the global
        // time limit. A final fresh candidate has not been searched, so its
        // status is explicitly Unknown rather than Unsolvable.
        return new GeneratedDeal(
            DeckFactory.Deal(suitCount, faceDown, new Random(), mode),
            SolvabilityStatus.Unknown);
    }

    private void ApplyGeneratedDeal(GeneratedDeal generated)
    {
        _gameState = generated.State;
        _lastDealSolvability = generated.Status;
        _showDealSolvabilityStatus = _gameState.Mode != GameMode.Endless;

        GameEngine.Reset();
        _undoStack.Clear();
        _animations.Clear();
        _hiddenCards.Clear();
        _flipAnims.Clear();
        _particles.Clear();
        _hintState = HintState.None;
        _isDragging = false;
        _stuckSoundPlayed = false;
       

        if (_gameState.Mode == GameMode.Blitz)
            _gameTimeSeconds = 180.0;
        else
            _gameTimeSeconds = 0;

        _isGameActive = true;
        _winHandled = false;
        _winConfettiTimer = 0f;
        SaveManager.Settings.CurrentGameTime = 0;
        _currentScreen = GameScreen.Playing;
        if (!SaveManager.Settings.HasSeenTutorial)
            _showTutorial = true;

        _dealDistSound.Play();

        for (int col = 0; col < 10; col++)
        {
            var pile = _gameState.Tableau[col];
            for (int i = 0; i < pile.Count; i++)
            {
                var targetPos = GetCardPosition(col, i, _gameState);
                var startPos = new Vector2(StockX, StockY);
                _animations.Add(new CardAnimation
                {
                    Card = pile.Cards[i],
                    StartPos = startPos,
                    EndPos = new Vector2(targetPos.X, targetPos.Y),
                    Duration = 0.4f,
                    Time = 0,
                    Delay = (col * 5 + i) * 0.04f,
                    IsFaceDown = i < pile.FaceDownCount,
                    IsArc = true,
                    TargetCol = col,
                    TargetIndex = i
                });
                _hiddenCards.Add((col, i));
            }
        }
    }

    private void LoadGame()
    {
        var loaded = SaveManager.Load();
        if (loaded.HasValue) {
            _gameState = loaded.Value.State;
            _faceDownPerColumn = loaded.Value.FaceDownPerColumn;
            _currentDifficulty = _faceDownPerColumn switch { 3 => 0, 7 => 2, _ => 1 };
            _selectorDifficulty.SelectedIndex = _currentDifficulty;
            GameEngine.Reset(); _undoStack.Clear(); _animations.Clear(); _hiddenCards.Clear(); _flipAnims.Clear(); _particles.Clear();
            _hintState = HintState.None; _isDragging = false; _stuckSoundPlayed = false; _currentScreen = GameScreen.Playing;
            double savedGameTime = loaded.Value.CurrentGameTime
                ?? SaveManager.Settings.CurrentGameTime;

            if (_gameState.Mode == GameMode.Blitz)
            {
                // New saves preserve the exact remaining time in save.json.
                // Older saves fall back to settings; when that value was never
                // written, avoid incorrectly resuming an in-progress game at zero.
                _gameTimeSeconds = loaded.Value.CurrentGameTime.HasValue
                    ? Math.Clamp(savedGameTime, 0.0, 180.0)
                    : (savedGameTime > 0.0
                        ? Math.Clamp(savedGameTime, 0.0, 180.0)
                        : 180.0);
            }
            else
            {
                _gameTimeSeconds = Math.Max(0.0, savedGameTime);
            }

            _isGameActive = true;
            _winHandled = false;
            _winConfettiTimer = 0f;
            _showDealSolvabilityStatus = false;
            _usedUndoThisGame = false;
            _usedHintThisGame = false;
            // Старые сохранения без этого поля считаются неподходящими для
            // достижения: по ним невозможно достоверно узнать историю ходов.
            _mixedSuitPlacementUsedThisGame = loaded.Value.MixedSuitPlacementUsed;
            RefreshCardBackSelector();
        }
    }

    private void SaveGame()
    {
        if (_gameState != null && !_gameState.IsWon)
        {
            SaveManager.Settings.CurrentGameTime = Math.Max(0.0, _gameTimeSeconds);
            SaveManager.Save(
                _gameState,
                _faceDownPerColumn,
                _mixedSuitPlacementUsedThisGame,
                _gameTimeSeconds);
            // Keep the legacy settings field up to date for compatibility with
            // save files created by older versions of the game.
            SaveManager.SaveSettings();
            UpdateContinueButton();
        }
    }

    // Hints follow the original Love2D implementation: rank all legal moves once
    // for a board position, then cycle through that cached list on subsequent presses.
    private void ClearHintState(bool clearMessage = true)
    {
        _hintState = HintState.None;
        _hintTimer = 0f;
        _isHintStock = false;
        _hintCandidates.Clear();
        _hintBoardFingerprint = string.Empty;
        _hintCandidateIndex = -1;

        if (clearMessage)
        {
            _hintMessageKey = string.Empty;
            _hintMessageTimer = 0f;
        }
    }

    private void TriggerHint()
    {
        if (_gameState == null || _currentScreen != GameScreen.Playing
            || !_isGameActive || _showTutorial || _gameState.IsWon)
            return;

        _usedHintThisGame = true;
        string fingerprint = GetHintBoardFingerprint(_gameState);
        if (!string.Equals(fingerprint, _hintBoardFingerprint, StringComparison.Ordinal))
        {
            BuildHintCandidates(_gameState);
            _hintBoardFingerprint = fingerprint;
            _hintCandidateIndex = -1;
        }

        if (_hintCandidates.Count == 0)
        {
            _hintState = HintState.None;
            _hintMessageKey = "HintNoUsefulMove";
            _hintMessageTimer = 2.5f;
            return;
        }

        _hintCandidateIndex = (_hintCandidateIndex + 1) % _hintCandidates.Count;
        HintCandidate candidate = _hintCandidates[_hintCandidateIndex];
        _hintMove = candidate.Move;
        _isHintStock = candidate.IsStockDeal;
        _hintTimer = 0f;
        _hintState = HintState.ShowTarget;

        if (_isHintStock)
        {
            _hintMessageKey = "HintDeal";
            _hintMessageTimer = 2.8f;
        }
        else
        {
            _hintMessageKey = string.Empty;
            _hintMessageTimer = 0f;
        }
    }

    private void BuildHintCandidates(GameState state)
    {
        _hintCandidates.Clear();

        for (int sourceColumn = 0; sourceColumn < state.Tableau.Count; sourceColumn++)
        {
            var source = state.Tableau[sourceColumn];
            if (source.Count == 0 || source.FaceDownCount >= source.Count)
                continue;

            int sourceEnd = source.Count - 1;
            int sourceStart = sourceEnd;
            while (sourceStart > source.FaceDownCount)
            {
                Card lower = source.Cards[sourceStart - 1];
                Card upper = source.Cards[sourceStart];
                if (lower.Suit != upper.Suit
                    || (int)lower.Rank != (int)upper.Rank + 1)
                    break;
                sourceStart--;
            }

            if (!GameRules.CanPickUp(source, sourceStart))
                continue;

            int sourceLength = sourceEnd - sourceStart + 1;
            Card movingCard = source.Cards[sourceStart];
            bool revealsFaceDown = sourceStart > 0
                && sourceStart - 1 < source.FaceDownCount;

            for (int targetColumn = 0; targetColumn < state.Tableau.Count; targetColumn++)
            {
                if (targetColumn == sourceColumn)
                    continue;

                var target = state.Tableau[targetColumn];
                int score;
                if (target.IsEmpty)
                {
                    // As in Lua, do not recommend moving a whole column into
                    // another empty column: it only relocates the same stack.
                    if (sourceStart == 0)
                        continue;
                    score = 5;
                }
                else
                {
                    if (!GameRules.CanPlaceOn(movingCard, target.TopCard))
                        continue;

                    int targetRunStart = GetTrailingHintRunStart(target);
                    int targetRunLength = target.Count - targetRunStart;
                    Card targetRunFirst = target.Cards[targetRunStart];
                    score = 30 + targetRunLength + sourceLength;

                    if (targetRunFirst.Rank == Rank.King && source.TopCard.Rank == Rank.Ace)
                        score += 300;
                    if (sourceStart == 0)
                        score += 60;
                }

                if (revealsFaceDown)
                    score += 100;

                _hintCandidates.Add(new HintCandidate(
                    new Move(sourceColumn, sourceStart, targetColumn), false, score));
            }
        }

        // LINQ's ordering is stable, matching Lua's score ordering while keeping
        // the natural source/target column order for equal scores.
        List<HintCandidate> ordered = _hintCandidates
            .OrderByDescending(candidate => candidate.Score)
            .ToList();
        _hintCandidates.Clear();
        _hintCandidates.AddRange(ordered);

        // Stock is intentionally the last recommendation, just like the Lua game.
        if (GameEngine.CanDealFromStock(state))
            _hintCandidates.Add(new HintCandidate(new Move(-1, -1, -1), true, -1));
    }

    private static int GetTrailingHintRunStart(AscendingSpider.Domain.Pile pile)
    {
        int start = pile.Count - 1;
        while (start > pile.FaceDownCount)
        {
            Card lower = pile.Cards[start - 1];
            Card upper = pile.Cards[start];
            if (lower.Suit != upper.Suit
                || (int)lower.Rank != (int)upper.Rank + 1)
                break;
            start--;
        }
        return start;
    }

    private string GetHintBoardFingerprint(GameState state)
    {
        // Preserve physical column order: Move stores concrete column indexes.
        string orderedColumns = string.Join(";", state.Tableau.Select(pile =>
            pile.FaceDownCount + ":" + string.Join(",", pile.Cards.Select(card =>
                $"{(int)card.Suit}:{(int)card.Rank}"))));
        string stock = string.Join(",", state.Stock.Select(card =>
            $"{(int)card.Suit}:{(int)card.Rank}"));

        return state.Mode + "#" + state.SuitCount + "#" + state.CompletedSequences
            + "#" + state.TotalCompletedSequences + "#" + state.EndlessLoops
            + "#" + stock + "#" + orderedColumns;
    }

    private double GetElapsedGameTimeSeconds()
        => _gameState.Mode == GameMode.Blitz ? Math.Max(0, 180.0 - _gameTimeSeconds) : _gameTimeSeconds;

    private void CheckAchievements()
    {
        bool wasEmpty = !HasAnyAchievement();
        bool unlockedNewCardBack = false;

        if (!SaveManager.Settings.AchFirstWin) { SaveManager.Settings.AchFirstWin = true; _achievementQueue.Enqueue("AchFirstWin"); unlockedNewCardBack = true; }
        if (!SaveManager.Settings.AchNoUndo && !_usedUndoThisGame) { SaveManager.Settings.AchNoUndo = true; _achievementQueue.Enqueue("AchNoUndo"); unlockedNewCardBack = true; }
        if (!SaveManager.Settings.AchNoHint && !_usedHintThisGame) { SaveManager.Settings.AchNoHint = true; _achievementQueue.Enqueue("AchNoHint"); }
        if (!SaveManager.Settings.AchSpeedrun && GetElapsedGameTimeSeconds() < 300) { SaveManager.Settings.AchSpeedrun = true; _achievementQueue.Enqueue("AchSpeedrun"); }
        if (!SaveManager.Settings.Ach4Suits && _gameState.SuitCount == 4) { SaveManager.Settings.Ach4Suits = true; _achievementQueue.Enqueue("Ach4Suits"); unlockedNewCardBack = true; }
        if (!SaveManager.Settings.AchHardMode && _currentDifficulty == 2) { SaveManager.Settings.AchHardMode = true; _achievementQueue.Enqueue("AchHardMode"); }
        if (!SaveManager.Settings.AchDedicated && SaveManager.Settings.Wins >= 10) { SaveManager.Settings.AchDedicated = true; _achievementQueue.Enqueue("AchDedicated"); }
        // «Идеальный порядок»: режимы на 2, 3 или 4 масти и ни одного
        // успешного перемещения карты (или последовательности) на другую масть.
        // Использование резерва само по себе достижению не мешает.
        if (!SaveManager.Settings.AchPerfectOrder
            && IsPerfectOrderEligibleSuitCount(_gameState.SuitCount)
            && !_mixedSuitPlacementUsedThisGame)
        {
            SaveManager.Settings.AchPerfectOrder = true;
            _achievementQueue.Enqueue("AchPerfectOrder");
        }
        
        SaveManager.SaveSettings(); 
        if (wasEmpty && HasAnyAchievement()) RecalcMainMenuButtons();
        if (unlockedNewCardBack) RefreshCardBackSelector();
    }

    protected override void Update(GameTime gameTime)
    {
        if (!IsActive) {
            if (_hintState != HintState.None || _hintMessageTimer > 0f) ClearHintState();
            _previousMouseState = Mouse.GetState(); 
            _previousKeyboardState = Keyboard.GetState(); 
            _isDragging = false; 
            _dragFromColumn = -1; 
            _dragFromIndex = -1; 
            _dragHoldTime = 0;
            base.Update(gameTime); 
            return; 
        }
        var kState = Keyboard.GetState(); var mouseState = GetScaledMouseState(); float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;

        if (_activeAchievementPopup != null) { _activeAchievementPopup.Timer -= deltaTime; if (_activeAchievementPopup.Timer <= 0) _activeAchievementPopup = null; }
        else if (_achievementQueue.Count > 0) _activeAchievementPopup = new AchievementPopup { NameKey = _achievementQueue.Dequeue() };
        if (_confirmReset) { _resetConfirmTimer -= deltaTime; if (_resetConfirmTimer <= 0) _confirmReset = false; }
        if (_currentScreen != GameScreen.Settings) _confirmReset = false;
        if (_currentScreen != GameScreen.Playing
            && (_hintState != HintState.None || _hintMessageTimer > 0f))
            ClearHintState();

        if (kState.IsKeyDown(Keys.F) && !_previousKeyboardState.IsKeyDown(Keys.F)) {
            _graphics.ToggleFullScreen();
            UpdateRenderDestination();
        }

        if (kState.IsKeyDown(Keys.Escape)) {
            if (_currentScreen == GameScreen.Playing && !_gameState.IsWon && !IsGameStuck()) { 
                _currentScreen = GameScreen.Paused; 
            }
            else if (_currentScreen == GameScreen.Paused) {
                _currentScreen = GameScreen.Playing;
            }
            else if (_currentScreen == GameScreen.MainMenu) Exit();
            else if (_currentScreen != GameScreen.Generating) _currentScreen = GameScreen.MainMenu;
        }
        if (_currentScreen == GameScreen.Playing && _isGameActive && !_showTutorial) {
            if (kState.IsKeyDown(Keys.Z) && !_previousKeyboardState.IsKeyDown(Keys.Z)) PerformUndo();
            if (kState.IsKeyDown(Keys.H) && !_previousKeyboardState.IsKeyDown(Keys.H)) TriggerHint();
        }

        _mouseVelocity = mouseState.Position.ToVector2() - _previousMousePos; _previousMousePos = mouseState.Position.ToVector2();
        UpdateParticles(deltaTime); UpdateFlipAnimations(deltaTime);

        switch (_currentScreen) {
            case GameScreen.MainMenu: 
                _btnNewGame.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); 
                _btnContinue.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); 
                if (HasAnyAchievement()) _btnAchievements.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); 
                _btnLeaderboard.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); 
                _btnSettings.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); 
                _btnExit.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); 
                break;
            case GameScreen.NewGameSetup: _selectorSuits.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); _selectorDifficulty.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); _selectorMode.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); _btnStart.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); _btnBackNewGame.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); break;
            case GameScreen.Settings: _selectorLanguage.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); _sliderVolume.Update(mouseState, _previousMouseState); _selectorTableStyle.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); _selectorCardBack.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); _btnResetStats.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); _btnBackSettings.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); break;
            case GameScreen.Achievements: _btnBackAchievements.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); break;
            case GameScreen.Leaderboard: _btnBackLeaderboard.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); break;
            case GameScreen.Generating:
                if (_dealGenerationTask != null && _dealGenerationTask.IsCompleted) {
                    GeneratedDeal generated;
                    try
                    {
                        generated = _dealGenerationTask.Result;
                    }
                    catch
                    {
                        generated = new GeneratedDeal(
                            DeckFactory.Deal(Math.Clamp(_selectorSuits.SelectedIndex + 1, 1, 2), _faceDownPerColumn, new Random(), _currentMode),
                            SolvabilityStatus.Unknown);
                    }
                    _dealGenerationTask = null;
                    ApplyGeneratedDeal(generated);
                }
                break;
            case GameScreen.Playing: UpdatePlaying(gameTime, mouseState); break;
            case GameScreen.Paused: 
                _btnPauseResume.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); 
                _btnPauseRestart.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound);
                _btnPauseSettings.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); 
                _btnPauseMainMenu.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); 
                break;
        }

        _previousMouseState = mouseState; _previousKeyboardState = kState; base.Update(gameTime);
    }

    private void UpdateParticles(float dt) { for (int i = _particles.Count - 1; i >= 0; i--) { var p = _particles[i]; p.Position += p.Velocity * dt; p.Velocity.Y += 500f * dt; p.Life -= dt; if (p.Life <= 0) _particles.RemoveAt(i); } }
    private void UpdateFlipAnimations(float dt) { for (int i = _flipAnims.Count - 1; i >= 0; i--) { _flipAnims[i].Time += dt; if (_flipAnims[i].Time >= _flipAnims[i].Duration) _flipAnims.RemoveAt(i); } }

    private void SpawnParticles(Vector2 pos, int count, Color color)
    {
        for (int i = 0; i < count; i++) { float angle = (float)(_rng.NextDouble() * Math.PI * 2); float speed = (float)(_rng.NextDouble() * 200 + 100); _particles.Add(new Particle { Position = pos, Velocity = new Vector2((float)Math.Cos(angle) * speed, (float)Math.Sin(angle) * speed - 100), Life = (float)(_rng.NextDouble() * 0.8 + 0.4), MaxLife = 1.2f, Color = color, Size = (float)(_rng.NextDouble() * 4 + 2) }); }
    }

    private void SpawnConfetti()
    {
        _particles.Add(new Particle { Position = new Vector2(_rng.Next(1500), -20), Velocity = new Vector2((float)(_rng.NextDouble() * 100 - 50), (float)(_rng.NextDouble() * 100 + 50)), Life = 3.0f, MaxLife = 3.0f, Color = _rng.Next(4) switch { 0 => Color.Gold, 1 => Color.Red, 2 => Color.DeepSkyBlue, _ => Color.White }, Size = (float)(_rng.NextDouble() * 6 + 4) });
    }

    private void UpdatePlaying(GameTime gameTime, MouseState mouseState)
    {
        if (_showTutorial) { if (mouseState.LeftButton == ButtonState.Pressed && _previousMouseState.LeftButton == ButtonState.Released) { _showTutorial = false; SaveManager.Settings.HasSeenTutorial = true; SaveManager.SaveSettings(); } return; }
        float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (_gameState.IsWon)
            _isGameActive = false;

        if (_isGameActive) {
            if (_gameState.Mode == GameMode.Blitz) {
                _gameTimeSeconds -= deltaTime;
                if (_gameTimeSeconds <= 0) {
                    _gameTimeSeconds = 0;
                    _isGameActive = false;
                    _stuckSoundPlayed = true;
                    UpdateStats(false);
                    SaveManager.Settings.CurrentGameTime = 0;
                }
            } else {
                _gameTimeSeconds += deltaTime;
            }
        }

        for (int i = _animations.Count - 1; i >= 0; i--) { var anim = _animations[i]; if (anim.Delay > 0) anim.Delay -= deltaTime; else { anim.Time += deltaTime; if (anim.Time >= anim.Duration) { _animations.RemoveAt(i); _hiddenCards.Remove((anim.TargetCol, anim.TargetIndex)); } } }
        if (_currentScreen != GameScreen.Playing) return;
        if (_hintMessageTimer > 0f)
            _hintMessageTimer = Math.Max(0f, _hintMessageTimer - deltaTime);
        if (_hintState != HintState.None)
        {
            _hintTimer += deltaTime;
            if (_hintTimer > 7.0f)
                _hintState = HintState.None;
        }
        bool isStuck = IsGameStuck();
        
        if (_gameState.IsWon) {
            // Freeze the clock and input as soon as the eighth sequence is
            // collected. Let its animations finish before showing the result.
            _isGameActive = false;
            if (_animations.Count > 0)
                return;

            // Process a win exactly once, independent of _isGameActive. A player
            // may undo out of a previous stuck state, which previously left this
            // flag false and silently skipped every achievement check.
            if (!_winHandled) {
                _winHandled = true;
                _winSound.Play();
                UpdateStats(true);
                SaveManager.Settings.CurrentGameTime = 0;
                _winConfettiTimer = 0;
                CheckAchievements();
            }
            if (_winConfettiTimer < 3.0f) { _winConfettiTimer += deltaTime; for (int i = 0; i < 15; i++) SpawnConfetti(); }
            _btnWinRestart.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound);
            _btnWinMenu.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound);
            return;
        } else if (isStuck) {
            if (!_stuckSoundPlayed) { _stuckSound.Play(); _stuckSoundPlayed = true; _isGameActive = false; UpdateStats(false); SaveManager.Settings.CurrentGameTime = 0; }
            bool timeExpired = _gameState.Mode == GameMode.Blitz && _gameTimeSeconds <= 0;
            LayoutStuckButtons(timeExpired);
            if (!timeExpired)
                _btnStuckUndo.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound);
            _btnStuckRestart.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound);
            _btnStuckMenu.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound);
            return;
        }

        _btnInGameMenu.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound);
        _btnUndo.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound);
        _btnHint.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound);

        var mousePoint = new Point(mouseState.X, mouseState.Y);
        
        if (!_isDragging && !_animations.Any(a => a.IsBounceBack)) {
            int oldCol = _hoveredCol; int oldIdx = _hoveredIndex; _hoveredCol = -1; _hoveredIndex = -1; _isStockHovered = false;
            if (new Rectangle(StockX - HitboxPaddingX, StockY - HitboxPaddingY, CardWidth + HitboxPaddingX * 2, CardHeight + HitboxPaddingY * 2).Contains(mousePoint)) 
                _isStockHovered = true;
            else 
            { 
                for (int col = 0; col < 10; col++) 
                { 
                    var pile = _gameState.Tableau[col]; 
                    for (int i = pile.Count - 1; i >= 0; i--) 
                    { 
                        if (_hiddenCards.Contains((col, i))) continue; 
                        var pos = GetCardPosition(col, i); 
                        if (new Rectangle(pos.X - HitboxPaddingX, pos.Y - HitboxPaddingY, CardWidth + HitboxPaddingX * 2, CardHeight + HitboxPaddingY * 2).Contains(mousePoint)) 
                        { 
                            _hoveredCol = col; 
                            _hoveredIndex = i; 
                            break; 
                        } 
                    } 
                    if (_hoveredCol != -1) break; 
                } 
            }
            if ((oldCol != _hoveredCol || oldIdx != _hoveredIndex) && _hoveredCol != -1) _hoverCardSound.Play();
        }

        if (_isDragging) { 
            _dragHoldTime += deltaTime;
            Vector2 targetPos = new Vector2(mouseState.X - _dragOffset.X, mouseState.Y - _dragOffset.Y); 
            _dragPos = Vector2.Lerp(_dragPos, targetPos, 0.3f); 
            _dragPos += _mouseVelocity * 0.15f; 
        }

        if (mouseState.LeftButton == ButtonState.Pressed && _previousMouseState.LeftButton == ButtonState.Released && !_isDragging) {
            _heldColumn = -1;
            _heldIndex = -1;
            if (new Rectangle(StockX - HitboxPaddingX, StockY - HitboxPaddingY, CardWidth + HitboxPaddingX * 2, CardHeight + HitboxPaddingY * 2).Contains(mousePoint)) {
                bool hasEmptyColumns = _gameState.Tableau.Any(p => p.IsEmpty);
                if (!hasEmptyColumns && _gameState.Stock.Count >= 10) {
                    _hintState = HintState.None;
                   
                    var stateBefore = _gameState.Clone();
                    _undoStack.Add(stateBefore);
                    if (_undoStack.Count > 30) _undoStack.RemoveAt(0);
                    int previousCompletedInRound = _gameState.CompletedSequences;
                    int previousTotalCompleted = _gameState.TotalCompletedSequences;
                    int previousEndlessLoops = _gameState.EndlessLoops;
                    ClearHintState();
                    GameEngine.TryDealFromStock(_gameState);
                    _dealCollectSound.Play();

                    if (_gameState.TotalCompletedSequences > previousTotalCompleted)
                    {
                        int sequenceIndex = previousCompletedInRound;
                        foreach (var collected in GameEngine.LastCollectedSequences)
                            AddCollectAnimation(collected.Column, sequenceIndex++, collected.Suit, stateBefore);
                    }

                    if (_gameState.EndlessLoops > previousEndlessLoops) {
                        AddTableauDealAnimations();
                    }
                    else {
                        for (int col = 0; col < 10; col++) {
                            var pile = _gameState.Tableau[col];
                            int targetIndex = pile.Count - 1;
                            var endPos = GetCardPosition(col, targetIndex);
                            Card cardToAnimate = pile.Count > 0 ? pile.TopCard : new Card(Suit.Spades, Rank.Ace);
                            _animations.Add(new CardAnimation { Card = cardToAnimate, StartPos = new Vector2(StockX, StockY), EndPos = new Vector2(endPos.X, endPos.Y), Duration = 0.35f, Time = 0, Delay = col * 0.05f, IsArc = true, TargetCol = col, TargetIndex = targetIndex, IsCollecting = false });
                            _hiddenCards.Add((col, targetIndex));
                        }
                        CheckFlips(stateBefore);
                    }
                    SaveGame();
                }
            } else TryPickUpCard(mousePoint);
        } else if (mouseState.LeftButton == ButtonState.Released && _previousMouseState.LeftButton == ButtonState.Pressed) { 
            if (_isDragging) { 
                float dist = Vector2.Distance(mousePoint.ToVector2(), _dragStartMousePos.ToVector2());
                if (dist < 10f && _dragHoldTime < 0.25f) {
                    TryAutoMove();
                    _isDragging = false;
                } else {
                    TryDropCard(mousePoint); 
                    _isDragging = false; 
                }
            } 
            _heldColumn = -1;
            _heldIndex = -1;
        }
    }

    // Достижение «Идеальный порядок» доступно в режимах на 2, 3 и 4 масти.
    private static bool IsPerfectOrderEligibleSuitCount(int suitCount)
        => suitCount >= 2 && suitCount <= 4;

    // Проверяет именно ручное действие игрока по исходному состоянию поля.
    // Перемещение на пустую колонку не считается наложением одной масти на другую.
    private static bool IsCrossSuitPlacement(GameState state, int fromColumn, int fromIndex, int toColumn)
    {
        var sourcePile = state.Tableau[fromColumn];
        var targetPile = state.Tableau[toColumn];
        if (targetPile.IsEmpty || fromIndex < 0 || fromIndex >= sourcePile.Count)
            return false;

        return sourcePile.Cards[fromIndex].Suit != targetPile.TopCard.Suit;
    }

    private void TryAutoMove()
    {
        var pile = _gameState.Tableau[_dragFromColumn];
        var card = pile.Cards[_dragFromIndex];
        int bestCol = -1;
        int bestScore = int.MinValue;
        
        for (int col = 0; col < 10; col++) {
            if (col == _dragFromColumn) continue;
            var targetPile = _gameState.Tableau[col];
            if (!targetPile.IsEmpty && GameRules.CanPlaceOn(card, targetPile.TopCard)) {
                int score = 0;
                if (targetPile.TopCard.Suit == card.Suit) score += 100;
                if (score > bestScore) {
                    bestScore = score;
                    bestCol = col;
                }
            }
        }
        
        if (bestCol == -1) {
            for (int col = 0; col < 10; col++) {
                if (col == _dragFromColumn) continue;
                if (_gameState.Tableau[col].IsEmpty) {
                    bestCol = col;
                    break;
                }
            }
        }
        
        if (bestCol != -1) {
            var stateBefore = _gameState.Clone();
            int prevCompleted = _gameState.CompletedSequences;
            int prevTotalCompleted = _gameState.TotalCompletedSequences;
            int prevEndlessLoops = _gameState.EndlessLoops;
            int cardsMoved = GameRules.GetSequenceEnd(stateBefore.Tableau[_dragFromColumn], _dragFromIndex) - _dragFromIndex + 1;
            bool crossSuitPlacement = IsCrossSuitPlacement(stateBefore, _dragFromColumn, _dragFromIndex, bestCol);
            bool success = GameEngine.TryMove(_gameState, _dragFromColumn, _dragFromIndex, bestCol);
            if (success) {
                ClearHintState();
                if (IsPerfectOrderEligibleSuitCount(_gameState.SuitCount) && crossSuitPlacement)
                    _mixedSuitPlacementUsedThisGame = true;

                _undoStack.Add(stateBefore);
                if (_undoStack.Count > 30) _undoStack.RemoveAt(0);
                
                _cardMoveSound.Play(1.0f, cardsMoved > 1 ? -0.2f : 0.0f, 0f);
                
                if (_gameState.TotalCompletedSequences > prevTotalCompleted) {
                    _dealCollectSound.Play();
                    int sequenceIndex = prevCompleted;
                    foreach (var collected in GameEngine.LastCollectedSequences)
                        AddCollectAnimation(collected.Column, sequenceIndex++, collected.Suit, stateBefore);
                }

                if (_gameState.EndlessLoops > prevEndlessLoops) {
                    AddTableauDealAnimations();
                }
                else if (_gameState.TotalCompletedSequences == prevTotalCompleted) {
                    var toPile = _gameState.Tableau[bestCol];
                    int startIndexInTarget = toPile.Count - cardsMoved;
                    for (int i = 0; i < cardsMoved; i++) {
                        var cardToAnim = toPile.Cards[startIndexInTarget + i];
                        var endPos = GetCardPosition(bestCol, startIndexInTarget + i);
                        var startPos = GetCardPosition(_dragFromColumn, _dragFromIndex + i, stateBefore);
                        _animations.Add(new CardAnimation { Card = cardToAnim, StartPos = new Vector2(startPos.X, startPos.Y), EndPos = new Vector2(endPos.X, endPos.Y), Duration = 0.2f, Time = 0, Delay = 0, TargetCol = bestCol, TargetIndex = startIndexInTarget + i, IsBounceBack = false });
                        _hiddenCards.Add((bestCol, startIndexInTarget + i));
                    }
                }

                if (_gameState.EndlessLoops == prevEndlessLoops)
                    CheckFlips(stateBefore); 
                SaveGame();
                _dragFromColumn = -1; 
                _dragFromIndex = -1;
            } else {
                BounceBack();
            }
        } else {
            BounceBack();
        }
    }

    private void UpdateStats(bool won) { 
        if (won) {
            SaveManager.Settings.Wins++; 
            SaveManager.AddScore(_gameState.Mode, _gameState.Mode == GameMode.Blitz ? (180 - _gameTimeSeconds) : _gameTimeSeconds);
        } else {
            SaveManager.Settings.Losses++; 
        }
        int diff = _currentDifficulty; 
        if (diff == 0) { if (SaveManager.Settings.BestTimeEasy == 0 || _gameTimeSeconds < SaveManager.Settings.BestTimeEasy) SaveManager.Settings.BestTimeEasy = _gameTimeSeconds; } 
        if (diff == 1) { if (SaveManager.Settings.BestTimeMedium == 0 || _gameTimeSeconds < SaveManager.Settings.BestTimeMedium) SaveManager.Settings.BestTimeMedium = _gameTimeSeconds; } 
        if (diff == 2) { if (SaveManager.Settings.BestTimeHard == 0 || _gameTimeSeconds < SaveManager.Settings.BestTimeHard) SaveManager.Settings.BestTimeHard = _gameTimeSeconds; } 
        SaveManager.SaveSettings(); 
    }

    private void CheckFlips(GameState stateBefore) { for (int col = 0; col < 10; col++) { int oldCount = stateBefore.Tableau[col].FaceDownCount; int newCount = _gameState.Tableau[col].FaceDownCount; if (newCount < oldCount && _gameState.Tableau[col].Count > 0) _flipAnims.Add(new FlipAnimation { Col = col, Index = newCount, Time = 0 }); } }

    protected override void Draw(GameTime gameTime)
    {
        // Recalculate from the actual back buffer after fullscreen/resolution changes.
        UpdateRenderDestination();

        ThemePalette palette = GetThemePalette();
        Color bgColor = palette.Table;

        // Fill the entire back buffer with the current table color first.
        // This also colors the unused side/top/bottom areas around the 3:2 game view.
        GraphicsDevice.Clear(bgColor);

        float scaleX = _renderDestination.Width / 1500f;
        float scaleY = _renderDestination.Height / 1000f;
        Matrix screenTransform = Matrix.CreateScale(scaleX, scaleY, 1f)
            * Matrix.CreateTranslation(_renderDestination.X, _renderDestination.Y, 0f);

        // PointClamp disables texture interpolation. Pixels are sampled directly
        // instead of blended with neighboring texels, keeping cards and UI crisp.
        // MSAA is already disabled in the GraphicsDeviceManager configuration.
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: screenTransform);
        if (_tableBackdrop != null)
            _spriteBatch.Draw(_tableBackdrop, new Rectangle(0, 0, 1500, 1000), Color.White);
        else
            _spriteBatch.Draw(_pixel, new Rectangle(0, 0, 1500, 1000), bgColor);
        var mouseState = GetScaledMouseState();

        switch (_currentScreen) {
            case GameScreen.MainMenu: DrawMainMenu(mouseState, gameTime); break;
            case GameScreen.NewGameSetup: DrawNewGameMenu(mouseState); break;
            case GameScreen.Settings: DrawSettingsMenu(mouseState); break;
            case GameScreen.Achievements: DrawAchievementsMenu(mouseState, gameTime); break;
            case GameScreen.Leaderboard: DrawLeaderboard(mouseState); break;
            case GameScreen.Playing: DrawPlaying(gameTime, mouseState); break;
            case GameScreen.Generating: DrawGenerating(gameTime, mouseState); break;
            case GameScreen.Paused:
                DrawPlaying(gameTime, mouseState);
                _spriteBatch.Draw(_pixel, new Rectangle(0, 0, 1500, 1000), new Color(0, 0, 0, 176));
                DrawPanel(new Rectangle(470, 205, 560, 610), palette.Panel, palette.Accent, 0.98f);
                DrawScreenHeading("Paused", 240, 1.15f);
                _btnPauseResume.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouseState);
                _btnPauseRestart.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouseState);
                _btnPauseSettings.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouseState);
                _btnPauseMainMenu.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouseState);
                break;
        }

        if (_activeAchievementPopup != null) DrawAchievementPopup(_activeAchievementPopup);
        _spriteBatch.End();

        base.Draw(gameTime);
    }

    private void DrawLeaderboard(MouseState mouse)
    {
        ThemePalette palette = GetThemePalette();
        DrawPanel(new Rectangle(390, 75, 720, 850), palette.Panel, palette.Accent, 0.94f);
        DrawScreenHeading("Leaderboard", 90, 0.9f);

        // Both lists can contain ten entries. Fixed row heights keep the second
        // list and the Back button from colliding, even when both lists are full.
        DrawCenteredText("Standard", 170, Color.Gold, 0.68f);
        DrawLeaderboardRows(SaveManager.Settings.LeaderboardStandard, 205);

        DrawCenteredText("Blitz", 475, Color.Gold, 0.68f);
        DrawLeaderboardRows(SaveManager.Settings.LeaderboardBlitz, 510);

        _btnBackLeaderboard.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse);
    }

    private void DrawLeaderboardRows(IReadOnlyList<double> scores, int startY)
    {
        if (scores.Count == 0)
        {
            DrawCenteredText("NoScores", startY, Color.Gray, 0.52f);
            return;
        }

        int visibleCount = Math.Min(scores.Count, 10);
        for (int i = 0; i < visibleCount; i++)
        {
            string text = $"{i + 1}. {FormatTime(scores[i])}";
            DrawCenteredTextRaw(text, startY + i * 24, Color.White, 0.5f);
        }
    }

    private void DrawAchievementPopup(AchievementPopup popup) { float alpha = MathHelper.Clamp(popup.Timer / 0.5f, 0f, 1f); int y = 130; var bgRect = new Rectangle(1050, y, 420, 80); _spriteBatch.Draw(_pixel, bgRect, new Color(0, 0, 0) * (0.85f * alpha)); _spriteBatch.Draw(_pixel, new Rectangle(bgRect.X, bgRect.Y, bgRect.Width, 3), Color.Gold * alpha); _spriteBatch.Draw(_pixel, new Rectangle(bgRect.X, bgRect.Bottom - 3, bgRect.Width, 3), Color.Gold * alpha); _spriteBatch.Draw(_pixel, new Rectangle(bgRect.X, bgRect.Y, 3, bgRect.Height), Color.Gold * alpha); _spriteBatch.Draw(_pixel, new Rectangle(bgRect.Right - 3, bgRect.Y, 3, bgRect.Height), Color.Gold * alpha); var starRect = new Rectangle(bgRect.X + 15, bgRect.Y + 25, 30, 30); _spriteBatch.Draw(_starTexture, starRect, Color.Gold * alpha); string name = Localization.Get(popup.NameKey); string unlocked = Localization.Get("AchUnlocked"); _spriteBatch.DrawString(_font, name, new Vector2(bgRect.X + 60, bgRect.Y + 12), Color.Gold * alpha, 0f, Vector2.Zero, _fontScale, SpriteEffects.None, 0f); _spriteBatch.DrawString(_font, unlocked, new Vector2(bgRect.X + 60, bgRect.Y + 45), Color.White * alpha, 0f, Vector2.Zero, _fontScale * 0.8f, SpriteEffects.None, 0f); }


    private void DrawGenerating(GameTime gameTime, MouseState mouse)
    {
        const string label = "Загрузка";
        float scale = _fontScale * 1.0f;
        float time = (float)gameTime.TotalGameTime.TotalSeconds;

        float labelWidth = _font.MeasureString(label).X * scale;
        float dotWidth = _font.MeasureString(".").X * scale;
        float spacing = dotWidth * 0.15f;
        float totalWidth = labelWidth + dotWidth * 3 + spacing * 3;

        float startX = (1500f - totalWidth) / 2f;
        float baseY = 465f;

        _spriteBatch.DrawString(
            _font,
            label,
            new Vector2(startX, baseY),
            Color.White,
            0f,
            Vector2.Zero,
            scale,
            SpriteEffects.None,
            0f);

        float dotsStartX = startX + labelWidth;

        for (int i = 0; i < 3; i++)
        {
            // Каждая точка по очереди подпрыгивает и увеличивается.
            float phase = time * 4.5f - i * 0.9f;
            float wave = (float)Math.Sin(phase);

            float jump = Math.Max(0f, wave) * 9f;
            float breathing = 1f + Math.Max(0f, wave) * 0.25f;

            float dotScale = scale * breathing;
            float x = dotsStartX + i * (dotWidth + spacing);
            float y = baseY - jump;

            _spriteBatch.DrawString(
                _font,
                ".",
                new Vector2(x, y),
                Color.White,
                0f,
                Vector2.Zero,
                dotScale,
                SpriteEffects.None,
                0f);
        }
    }


    private void DrawMainMenu(MouseState mouse, GameTime gameTime)
    {
        ThemePalette palette = GetThemePalette();
        DrawPanel(new Rectangle(480, 185, 540, 535), palette.Panel, palette.Accent, 0.90f);
        DrawPanel(new Rectangle(480, 45, 540, 125), palette.Panel, palette.Accent, 0.82f);

        float pulse = 2.25f + (float)Math.Sin(gameTime.TotalGameTime.TotalSeconds * 2.2f) * 0.035f;
        string titleText = Localization.Get("Title");
        float titleScale = _fontScale * pulse;
        Vector2 titleSize = _font.MeasureString(titleText) * titleScale;
        // Fit long localized titles within the title plate.
        if (titleSize.X > 490f)
        {
            titleScale *= 490f / titleSize.X;
            titleSize = _font.MeasureString(titleText) * titleScale;
        }
        Vector2 titlePos = new Vector2((1500 - titleSize.X) / 2f, 82f);
        _spriteBatch.DrawString(_font, titleText, titlePos + new Vector2(2, 3), Color.Black * 0.35f, 0f, Vector2.Zero, titleScale, SpriteEffects.None, 0f);
        _spriteBatch.DrawString(_font, titleText, titlePos, new Color(246, 222, 166), 0f, Vector2.Zero, titleScale, SpriteEffects.None, 0f);

        _btnNewGame.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse);
        _btnContinue.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse);
        if (HasAnyAchievement()) _btnAchievements.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse);
        _btnLeaderboard.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse);
        _btnSettings.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse);
        _btnExit.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse);

        string statsText = $"{Localization.Get("Wins")}: {SaveManager.Settings.Wins}   •   {Localization.Get("BestTime")} ({Localization.Get("Easy")}): {FormatTime(SaveManager.Settings.BestTimeEasy)}";
        Vector2 statsSize = _font.MeasureString(statsText) * (_fontScale * 0.78f);
        float statsScale = _fontScale * 0.78f;
        if (statsSize.X > 1400f)
            statsScale *= 1400f / statsSize.X;
        statsSize = _font.MeasureString(statsText) * statsScale;
        _spriteBatch.DrawString(_font, statsText, new Vector2((1500 - statsSize.X) / 2f, 930), new Color(230, 225, 208) * 0.88f, 0f, Vector2.Zero, statsScale, SpriteEffects.None, 0f);
    }

    private void DrawNewGameMenu(MouseState mouse)
    {
        ThemePalette palette = GetThemePalette();
        DrawPanel(new Rectangle(420, 125, 660, 735), palette.Panel, palette.Accent, 0.94f);
        DrawScreenHeading("NewGame", 145, 0.9f);
        _selectorSuits.Draw(_spriteBatch, _pixel, _font, mouse, palette.Panel, palette.PanelAccent);
        _selectorDifficulty.Draw(_spriteBatch, _pixel, _font, mouse, palette.Panel, palette.PanelAccent);
        _selectorMode.Draw(_spriteBatch, _pixel, _font, mouse, palette.Panel, palette.PanelAccent);
        _btnStart.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse);
        _btnBackNewGame.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse);
    }

    private void DrawSettingsMenu(MouseState mouse)
    {
        ThemePalette palette = GetThemePalette();
        DrawPanel(new Rectangle(400, 65, 700, 865), palette.Panel, palette.Accent, 0.94f);
        DrawScreenHeading("Settings", 85, 0.9f);
        _selectorLanguage.Draw(_spriteBatch, _pixel, _font, mouse, palette.Panel, palette.PanelAccent);
        _sliderVolume.Draw(_spriteBatch, _pixel, _font, palette.Panel, palette.PanelAccent);
        _selectorTableStyle.Draw(_spriteBatch, _pixel, _font, mouse, palette.Panel, palette.PanelAccent);
        _selectorCardBack.Draw(_spriteBatch, _pixel, _font, mouse, palette.Panel, palette.PanelAccent);
        string originalKey = _btnResetStats.TextKey;
        _btnResetStats.TextKey = _confirmReset ? "ConfirmReset" : "ResetStats";
        _btnResetStats.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse);
        _btnResetStats.TextKey = originalKey;
        _btnBackSettings.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse);
    }

    private void DrawAchievementsMenu(MouseState mouse, GameTime gameTime)
    {
        ThemePalette palette = GetThemePalette();
        DrawPanel(new Rectangle(285, 65, 930, 850), palette.Panel, palette.Accent, 0.94f);
        DrawScreenHeading("Achievements", 90, 0.9f);
        var achievements = new (string NameKey, string DescKey, bool Unlocked)[] {
            ("AchFirstWin", "AchFirstWinDesc", SaveManager.Settings.AchFirstWin),
            ("AchNoUndo", "AchNoUndoDesc", SaveManager.Settings.AchNoUndo),
            ("AchNoHint", "AchNoHintDesc", SaveManager.Settings.AchNoHint),
            ("AchSpeedrun", "AchSpeedrunDesc", SaveManager.Settings.AchSpeedrun),
            ("Ach4Suits", "Ach4SuitsDesc", SaveManager.Settings.Ach4Suits),
            ("AchHardMode", "AchHardModeDesc", SaveManager.Settings.AchHardMode),
            ("AchDedicated", "AchDedicatedDesc", SaveManager.Settings.AchDedicated),
            ("AchPerfectOrder", "AchPerfectOrderDesc", SaveManager.Settings.AchPerfectOrder)
        };
        int startY = 170;
        int rowHeight = 70;
        foreach (var (nameKey, descKey, unlocked) in achievements)
        {
            Rectangle row = new Rectangle(345, startY, 810, 58);
            _spriteBatch.Draw(_pixel, row, palette.Table * 0.36f);
            _spriteBatch.Draw(_pixel, new Rectangle(row.X, row.Y, 3, row.Height), unlocked ? palette.Accent : Color.Gray * 0.65f);
            Rectangle iconRect = new Rectangle(365, startY + 9, 40, 40);
            _spriteBatch.Draw(_pixel, iconRect, unlocked ? palette.Button : new Color(65, 67, 65));
            if (unlocked) _spriteBatch.Draw(_starTexture, new Rectangle(iconRect.X + 5, iconRect.Y + 5, 30, 30), palette.Accent);
            else
            {
                string icon = "?";
                Vector2 iconSize = _font.MeasureString(icon) * _fontScale;
                _spriteBatch.DrawString(_font, icon, new Vector2(iconRect.X + (40 - iconSize.X) / 2, iconRect.Y + (40 - iconSize.Y) / 2), new Color(145, 145, 140), 0f, Vector2.Zero, _fontScale, SpriteEffects.None, 0f);
            }
            Color nameColor = unlocked ? new Color(245, 220, 160) : new Color(155, 155, 148);
            Color descColor = unlocked ? new Color(239, 236, 222) : new Color(125, 125, 120);
            _spriteBatch.DrawString(_font, Localization.Get(nameKey), new Vector2(430, startY + 5), nameColor, 0f, Vector2.Zero, _fontScale, SpriteEffects.None, 0f);
            _spriteBatch.DrawString(_font, Localization.Get(descKey), new Vector2(430, startY + 33), descColor, 0f, Vector2.Zero, _fontScale * 0.72f, SpriteEffects.None, 0f);
            startY += rowHeight;
        }
        _btnBackAchievements.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse);
    }

    private void DrawCenteredText(string key, int y, Color color, float scale = 1f) { string text = Localization.Get(key); Vector2 size = _font.MeasureString(text) * (_fontScale * scale); _spriteBatch.DrawString(_font, text, new Vector2((1500 - size.X) / 2, y), color, 0f, Vector2.Zero, _fontScale * scale, SpriteEffects.None, 0f); }
    private void DrawCenteredTextRaw(string text, int y, Color color, float scale = 1f) { Vector2 size = _font.MeasureString(text) * (_fontScale * scale); _spriteBatch.DrawString(_font, text, new Vector2((1500 - size.X) / 2, y), color, 0f, Vector2.Zero, _fontScale * scale, SpriteEffects.None, 0f); }
    private string FormatTime(double seconds) { if (seconds <= 0 && _gameState?.Mode != GameMode.Blitz) return "--:--"; int m = (int)(seconds / 60); int s = (int)(seconds % 60); return $"{m:00}:{s:00}"; }

    private void LayoutStuckButtons(bool timeExpired)
    {
        _btnStuckUndo.Rect = new Rectangle(500, 560, 225, 68);
        _btnStuckRestart.Rect = timeExpired
            ? new Rectangle(625, 560, 250, 68)
            : new Rectangle(775, 560, 225, 68);
        _btnStuckMenu.Rect = new Rectangle(625, 660, 250, 68);
    }

    private void DrawPlaying(GameTime gameTime, MouseState mouse)
    {
        bool terminalScreen = _gameState.IsWon || IsGameStuck();
        ThemePalette hudPalette = GetThemePalette();
        DrawPanel(new Rectangle(18, 8, 1464, 42), hudPalette.Panel, hudPalette.Accent, 0.68f);
        if (!terminalScreen)
        {
            DrawPanel(new Rectangle(12, 880, 505, 92), hudPalette.Panel, hudPalette.Accent, 0.88f);
            _btnInGameMenu.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse);
            _btnUndo.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse);
            _btnHint.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse);
        }

        string timeText = $"{Localization.Get("Time")}: {FormatTime(_gameTimeSeconds)}"; 
        Color timeColor = Color.White;
        if (_gameState.Mode == GameMode.Blitz && _gameTimeSeconds <= 30) timeColor = Color.Red;
        Vector2 timeSize = _font.MeasureString(timeText) * _fontScale; 
        _spriteBatch.DrawString(_font, timeText, new Vector2((1500 - timeSize.X) / 2, 15), timeColor, 0f, Vector2.Zero, _fontScale, SpriteEffects.None, 0f);

        if (_gameState.Mode == GameMode.Endless) {
            string loopsText = $"{Localization.Get("Loops")}: {_gameState.EndlessLoops}";
            // Keep the counter in the free top margin; tableau cards begin at Y=50.
            _spriteBatch.DrawString(_font, loopsText, new Vector2(50, 15), Color.Gold, 0f, Vector2.Zero, _fontScale, SpriteEffects.None, 0f);
        }

        if (_hintMessageTimer > 0f && !string.IsNullOrEmpty(_hintMessageKey))
        {
            string hintStatus = Localization.Get(_hintMessageKey);
            float statusScale = _fontScale * 0.55f;
            Vector2 statusSize = _font.MeasureString(hintStatus) * statusScale;
            if (statusSize.X > 420f)
                statusScale *= 420f / statusSize.X;
            _spriteBatch.DrawString(_font, hintStatus, new Vector2(255, 15), new Color(255, 222, 145), 0f, Vector2.Zero, statusScale, SpriteEffects.None, 0f);
        }
        var collectingSequenceIndexes = _animations
            .Where(animation => animation.IsCollecting && animation.CollectSequenceIndex >= 0)
            .Select(animation => animation.CollectSequenceIndex)
            .ToHashSet();
        int completedToDraw = Math.Min(8, _gameState.CompletedSequenceSuits.Count);
        for (int i = 0; i < completedToDraw; i++)
        {
            if (collectingSequenceIndexes.Contains(i)) continue;
            DrawCard(new Card(_gameState.CompletedSequenceSuits[i], Rank.Ace),
                CollectedX + (i * 18), CollectedY, false, 1.0f);
        }
        
        for (int col = 0; col < 10; col++) {
            var pile = _gameState.Tableau[col]; int slotX = 50 + col * (CardWidth + CardSpacing); int slotY = TableauTopY;
            if (pile.IsEmpty) { var slotRect = new Rectangle(slotX, slotY, CardWidth, CardHeight); _spriteBatch.Draw(_hintMask, slotRect, Color.Black * 0.2f); _spriteBatch.Draw(_borderBlack2, slotRect, Color.Black * 0.6f); }
            
            int dragEndIndex = _isDragging && col == _dragFromColumn ? GameRules.GetSequenceEnd(pile, _dragFromIndex) : -1;

            for (int i = 0; i < pile.Count; i++) {
                if (_isDragging && col == _dragFromColumn && i >= _dragFromIndex && i <= dragEndIndex) continue; 
                if (_hiddenCards.Contains((col, i))) continue;
                
                bool isFaceDown = i < pile.FaceDownCount;
                
                float darkenFactor = 1.0f;
                if (!isFaceDown && _heldColumn == col && !GameRules.CanPickUp(pile, i)) darkenFactor = 0.5f;

                var pos = GetCardPosition(col, i); bool isHovered = (col == _hoveredCol && i == _hoveredIndex);
                var flip = _flipAnims.FirstOrDefault(f => f.Col == col && f.Index == i);
                if (flip != null) { float progress = flip.Time / flip.Duration; float scaleX = 1f; if (progress < 0.5f) scaleX = 1f - (progress * 2); else { scaleX = (progress - 0.5f) * 2; isFaceDown = false; } DrawCardScaled(pile.Cards[i], pos.X + (CardWidth / 2), pos.Y, isFaceDown, scaleX, darkenFactor, gameTime); }
                else DrawCard(pile.Cards[i], pos.X, pos.Y, isFaceDown, darkenFactor, false, 0f, isHovered, gameTime);
            }
        }
        
        int dealsLeftGlobal = _gameState.Stock.Count / 10;
        if (dealsLeftGlobal > 0) {
            for (int i = 0; i < dealsLeftGlobal; i++)
            {
                DrawCard(
                    new Card(Suit.Hearts, Rank.Ace),
                    StockX - (i * 3),
                    StockY - (i * 3),
                    true,
                    1.0f,
                    false,
                    0f,
                    false,
                    gameTime);
            }
            if (_isStockHovered && !_isDragging) { float pulse = (float)Math.Sin(gameTime.TotalGameTime.TotalSeconds * 6) * 0.5f + 0.5f; int topCardX = StockX - (dealsLeftGlobal - 1) * 3; int topCardY = StockY - (dealsLeftGlobal - 1) * 3; _spriteBatch.Draw(_borderYellow3, new Rectangle(topCardX, topCardY, CardWidth, CardHeight), Color.LightGoldenrodYellow * (0.5f + 0.5f * pulse)); }
        } else { var stockRect = new Rectangle(StockX, StockY, CardWidth, CardHeight); _spriteBatch.Draw(_hintMask, stockRect, Color.Black * 0.2f); _spriteBatch.Draw(_borderBlack2, stockRect, Color.Black * 0.6f); }
        
        foreach (var anim in _animations) {
            if (anim.Delay > 0) continue; float progress = MathHelper.Clamp(anim.Time / anim.Duration, 0f, 1f); Vector2 currentPos; if (anim.IsArc) { Vector2 mid = (anim.StartPos + anim.EndPos) / 2; mid.Y -= 200; currentPos = Vector2.Lerp(Vector2.Lerp(anim.StartPos, mid, progress), Vector2.Lerp(mid, anim.EndPos, progress), progress); } else currentPos = Vector2.Lerp(anim.StartPos, anim.EndPos, progress); DrawCard(anim.Card, (int)currentPos.X, (int)currentPos.Y, anim.IsFaceDown, 1.0f, false, 0f, false, gameTime);
        }
        
        if (_isDragging) {
            var pile = _gameState.Tableau[_dragFromColumn]; 
            int endIndex = GameRules.GetSequenceEnd(pile, _dragFromIndex);
            
            int currentY = (int)_dragPos.Y; int currentX = (int)_dragPos.X;
            for (int i = _dragFromIndex; i <= endIndex; i++) { DrawCard(pile.Cards[i], currentX, currentY, false, 1.0f, true, 0f, false, gameTime); currentY += GetFaceUpOffset(_gameState, _dragFromColumn); }
        }
        
        DrawLuaStyleHint();

        bool isStuck = IsGameStuck();
        if (_gameState.IsWon && _animations.Count == 0)
        {
            _spriteBatch.Draw(_pixel, new Rectangle(0, 0, 1500, 1000), new Color(0, 0, 0, 184));
            DrawPanel(new Rectangle(455, 235, 590, 555), hudPalette.Panel, hudPalette.Accent, 0.98f);
            float winPulse = 2.55f + MathF.Sin((float)gameTime.TotalGameTime.TotalSeconds * 2.4f) * 0.045f;
            DrawCenteredText("Win", 300, new Color(255, 224, 158), winPulse);
            DrawCenteredText($"{Localization.Get("Time")}: {FormatTime(_gameTimeSeconds)}", 410, new Color(246, 242, 226), 0.95f);
            _btnWinRestart.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse);
            _btnWinMenu.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse);
        }
        else if (isStuck)
        {
            _spriteBatch.Draw(_pixel, new Rectangle(0, 0, 1500, 1000), new Color(0, 0, 0, 184));
            DrawPanel(new Rectangle(440, 300, 620, 500), hudPalette.Panel, hudPalette.Accent, 0.98f);
            bool timeExpired = _gameState.Mode == GameMode.Blitz && _gameTimeSeconds <= 0;
            LayoutStuckButtons(timeExpired);
            string stuckText = timeExpired ? "TimeUp" : "Stuck";
            DrawCenteredText(stuckText, 385, GetThemePalette().IsDark ? Color.IndianRed : Color.Red, 1.05f);
            if (!timeExpired)
                _btnStuckUndo.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse);
            _btnStuckRestart.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse);
            _btnStuckMenu.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse); 
        }
        
        if (_showTutorial)
        {
            _spriteBatch.Draw(_pixel, new Rectangle(0, 0, 1500, 1000), new Color(0, 0, 0, 220));
            DrawPanel(new Rectangle(300, 145, 900, 730), hudPalette.Panel, hudPalette.Accent, 0.98f);
            DrawCenteredText("TutorialTitle", 195, new Color(246, 222, 166), 1.35f);
            string[] lines = Localization.Get("TutorialText").Split('\n');
            int startY = 315;
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) { startY += 18; continue; }
                DrawCenteredTextRaw(line, startY, Color.White, 0.55f);
                startY += 38;
            }
            float pulse = (float)Math.Sin(gameTime.TotalGameTime.TotalSeconds * 3) * 0.5f + 0.5f;
            DrawCenteredText("TutorialClick", 820, Color.Gold * (0.5f + 0.5f * pulse), 0.72f);
        }
        DrawParticles();
    }

    private void DrawParticles() { foreach (var p in _particles) { float alpha = MathHelper.Clamp(p.Life / p.MaxLife, 0f, 1f); _spriteBatch.Draw(_pixel, new Rectangle((int)p.Position.X, (int)p.Position.Y, (int)p.Size, (int)p.Size), p.Color * alpha); } }

    private bool IsGameStuck()
    {
        if (_gameState.IsWon)
            return false;
        if (_gameState.Mode == GameMode.Blitz && _gameTimeSeconds <= 0)
            return true;
        if (_animations.Count > 0 || _isDragging)
            return false;

        // No legal move and no legal stock deal is an immediate loss.
        if (!GameEngine.HasAnyMoves(_gameState))
            return true;

        // A legal move may still be part of a closed cycle. Search all reachable
        // arrangements for an irreversible advance instead of treating every
        // rearrangement as evidence that the game remains playable.
        string key = GameEngine.GetReachabilityKey(_gameState);
        if (!string.Equals(key, _progressCheckKey, StringComparison.Ordinal))
        {
            _progressCheckCancellation?.Cancel();
            _progressCheckCancellation?.Dispose();

            _progressCheckKey = key;
            _progressCheckResult = ProgressReachability.Unknown;
            var cancellation = new CancellationTokenSource();
            _progressCheckCancellation = cancellation;
            var snapshot = _gameState.Clone();
            // Do not pass the token to Task.Run: if it is cancelled before the
            // delegate starts, Task.Run would create a Canceled task whose
            // GetAwaiter().GetResult() throws. Cancellation is handled inside
            // CheckReachableProgress as the ordinary Unknown result instead.
            _progressCheckTask = Task.Run(() =>
            {
                try
                {
                    return GameEngine.CheckReachableProgress(
                        snapshot, 50_000, cancellation.Token);
                }
                catch (OperationCanceledException)
                {
                    // Defensive fallback: cancellation must never be interpreted
                    // as a dead end or escape onto the game/update thread.
                    return ProgressReachability.Unknown;
                }
            });
        }

        if (_progressCheckTask != null && _progressCheckTask.IsCompleted)
        {
            try
            {
                _progressCheckResult = _progressCheckTask.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                // A cancelled analysis is inconclusive, never a loss.
                _progressCheckResult = ProgressReachability.Unknown;
            }
            catch (Exception)
            {
                // A failed analysis is also inconclusive, never a loss.
                _progressCheckResult = ProgressReachability.Unknown;
            }
            finally
            {
                _progressCheckTask = null;
                _progressCheckCancellation?.Dispose();
                _progressCheckCancellation = null;
            }
        }

        return _progressCheckResult == ProgressReachability.DeadEnd;
    }

    private void TryPickUpCard(Point mousePoint)
    {
        for (int col = 0; col < 10; col++) { var pile = _gameState.Tableau[col]; for (int i = pile.Count - 1; i >= 0; i--) { if (_hiddenCards.Contains((col, i))) continue; var pos = GetCardPosition(col, i); if (new Rectangle(pos.X - HitboxPaddingX, pos.Y, CardWidth + HitboxPaddingX * 2, CardHeight).Contains(mousePoint)) { 
            _heldColumn = col;
            _heldIndex = i;
            if (GameRules.CanPickUp(pile, i)) { 
                _isDragging = true; _dragFromColumn = col; _dragFromIndex = i; _dragOffset = new Vector2(mousePoint.X - pos.X, mousePoint.Y - pos.Y); _dragPos = new Vector2(pos.X, pos.Y); _dragStartMousePos = mousePoint; _dragHoldTime = 0f; _cardMoveSound.Play(0.8f, 0f, 0f); _hintState = HintState.None; 
            } return; } } }
    }

    private void TryDropCard(Point mousePoint)
    {
        for (int col = 0; col < 10; col++) {
            int x = 50 + col * (CardWidth + CardSpacing); if (mousePoint.X >= x - HitboxPaddingX && mousePoint.X <= x + CardWidth + HitboxPaddingX && mousePoint.Y >= TableauTopY) {
                var stateBefore = _gameState.Clone();
                int prevCompleted = _gameState.CompletedSequences;
                int prevTotalCompleted = _gameState.TotalCompletedSequences;
                int prevEndlessLoops = _gameState.EndlessLoops;
                int cardsMoved = GameRules.GetSequenceEnd(stateBefore.Tableau[_dragFromColumn], _dragFromIndex) - _dragFromIndex + 1;
                bool crossSuitPlacement = IsCrossSuitPlacement(stateBefore, _dragFromColumn, _dragFromIndex, col);
                bool attemptedWrongPlacement =
                    col != _dragFromColumn
                    && !stateBefore.Tableau[col].IsEmpty
                    && !GameRules.CanPlaceOn(
                        stateBefore.Tableau[_dragFromColumn].Cards[_dragFromIndex],
                        stateBefore.Tableau[col].TopCard);
                bool success = GameEngine.TryMove(_gameState, _dragFromColumn, _dragFromIndex, col);
                if (success) {
                    ClearHintState();
                    if (IsPerfectOrderEligibleSuitCount(_gameState.SuitCount) && crossSuitPlacement)
                        _mixedSuitPlacementUsedThisGame = true;

                    _undoStack.Add(stateBefore); 
                    if (_undoStack.Count > 30) _undoStack.RemoveAt(0);
                    _cardMoveSound.Play(1.0f, cardsMoved > 1 ? -0.2f : 0.0f, 0f);
                    if (_gameState.TotalCompletedSequences > prevTotalCompleted) {
                        _dealCollectSound.Play();
                        int sequenceIndex = prevCompleted;
                        foreach (var collected in GameEngine.LastCollectedSequences)
                            AddCollectAnimation(collected.Column, sequenceIndex++, collected.Suit, stateBefore);
                    }

                    if (_gameState.EndlessLoops > prevEndlessLoops) {
                        AddTableauDealAnimations();
                    }
                    else if (_gameState.TotalCompletedSequences == prevTotalCompleted) {
                        var toPile = _gameState.Tableau[col];
                        int startIndexInTarget = toPile.Count - cardsMoved;
                        for (int i = 0; i < cardsMoved; i++) {
                            var card = toPile.Cards[startIndexInTarget + i];
                            var endPos = GetCardPosition(col, startIndexInTarget + i);
                            var startPos = _dragPos + new Vector2(0, i * GetFaceUpOffset(stateBefore, _dragFromColumn));
                            _animations.Add(new CardAnimation { Card = card, StartPos = startPos, EndPos = new Vector2(endPos.X, endPos.Y), Duration = 0.2f, Time = 0, Delay = 0, TargetCol = col, TargetIndex = startIndexInTarget + i, IsBounceBack = false });
                            _hiddenCards.Add((col, startIndexInTarget + i));
                        }
                    }
                    if (_gameState.EndlessLoops == prevEndlessLoops)
                        CheckFlips(stateBefore);
                    SaveGame(); _dragFromColumn = -1; _dragFromIndex = -1;
                } else BounceBack(attemptedWrongPlacement);
                return;
            }
        }
        BounceBack();
    }

    private void BounceBack(bool playWrongSound = false)
    {
        if (playWrongSound)
            _wrongSound.Play();

        var pile = _gameState.Tableau[_dragFromColumn];
        int endIndex = GameRules.GetSequenceEnd(pile, _dragFromIndex);
        for (int i = _dragFromIndex; i <= endIndex; i++) {
            var originalPos = GetCardPosition(_dragFromColumn, i); var startPos = _dragPos + new Vector2(0, (i - _dragFromIndex) * GetFaceUpOffset(_gameState, _dragFromColumn));
            _animations.Add(new CardAnimation { Card = pile.Cards[i], StartPos = startPos, EndPos = new Vector2(originalPos.X, originalPos.Y), Duration = 0.2f, Time = 0, Delay = 0, TargetCol = _dragFromColumn, TargetIndex = i, IsBounceBack = true }); _hiddenCards.Add((_dragFromColumn, i));
        }
        _dragFromColumn = -1; _dragFromIndex = -1;
    }

    private void AddTableauDealAnimations()
    {
        // A completed Endless cycle starts a fresh Spider deal (54 cards in the
        // tableau and 50 in stock), rather than putting all 104 cards in stock.
        // Replace stale hidden indexes because the columns have been re-dealt.
        _animations.RemoveAll(animation => !animation.IsCollecting);
        _flipAnims.Clear();
        _hiddenCards.Clear();
        for (int col = 0; col < 10; col++) {
            var pile = _gameState.Tableau[col];
            for (int i = 0; i < pile.Count; i++) {
                var target = GetCardPosition(col, i);
                _animations.Add(new CardAnimation {
                    Card = pile.Cards[i],
                    StartPos = new Vector2(StockX, StockY),
                    EndPos = new Vector2(target.X, target.Y),
                    Duration = 0.35f,
                    Time = 0,
                    Delay = (col * 5 + i) * 0.015f,
                    IsFaceDown = i < pile.FaceDownCount,
                    IsArc = true,
                    TargetCol = col,
                    TargetIndex = i
                });
                _hiddenCards.Add((col, i));
            }
        }
    }

    private void AddCollectAnimation(int colIndex, int seqIndex, Suit collectedSuit, GameState? stateBefore = null)
    {
        int targetX = CollectedX + (seqIndex * 18);
        int startIndex = 0;
        if (stateBefore != null)
            startIndex = Math.Max(0, stateBefore.Tableau[colIndex].Count - 13);

        for (int i = 0; i < 13; i++)
        {
            var startPos = stateBefore != null
                ? GetCardPosition(colIndex, startIndex + i, stateBefore)
                : new Point(50 + colIndex * (CardWidth + CardSpacing), 50 + i * 19);

            _animations.Add(new CardAnimation
            {
                Card = new Card(collectedSuit, Rank.Ace),
                StartPos = new Vector2(startPos.X, startPos.Y),
                EndPos = new Vector2(targetX, CollectedY),
                Duration = 0.25f,
                Time = 0,
                Delay = i * 0.03f,
                TargetCol = -1,
                TargetIndex = -1,
                IsCollecting = true,
                CollectSequenceIndex = seqIndex
            });
        }

        SpawnParticles(new Vector2(targetX + CardWidth / 2, CollectedY + CardHeight / 2), 30, Color.Gold);
    }

    private void PerformUndo()
    {
        ClearHintState();
        _usedUndoThisGame = true; if (_undoStack.Count == 0) return; _hintState = HintState.None; _stuckSoundPlayed = false;
        var prevState = _undoStack[^1]; 
        _undoStack.RemoveAt(_undoStack.Count - 1);
        var currentState = _gameState;
        _gameState = prevState;
        if (_gameState.Mode != GameMode.Blitz || _gameTimeSeconds > 0)
            _isGameActive = true;
        _winHandled = false;
        GameEngine.Reset(); _animations.Clear(); _hiddenCards.Clear(); _flipAnims.Clear();
        for (int col = 0; col < 10; col++) {
            var pile = currentState.Tableau[col];
            for (int i = 0; i < pile.Count; i++) {
                var card = pile.Cards[i]; var startPos = GetCardPosition(col, i, currentState); var targetInfo = FindCardPositionWithIndex(_gameState, card);
                if (targetInfo.HasValue) { var endPos = GetCardPosition(targetInfo.Value.col, targetInfo.Value.index, _gameState); if (startPos.X != endPos.X || startPos.Y != endPos.Y) { _animations.Add(new CardAnimation { Card = card, StartPos = new Vector2(startPos.X, startPos.Y), EndPos = new Vector2(endPos.X, endPos.Y), Duration = 0.2f, Time = 0, Delay = 0, TargetCol = targetInfo.Value.col, TargetIndex = targetInfo.Value.index, IsCollecting = false }); _hiddenCards.Add((targetInfo.Value.col, targetInfo.Value.index)); } }
                else _animations.Add(new CardAnimation { Card = card, StartPos = new Vector2(startPos.X, startPos.Y), EndPos = new Vector2(StockX, StockY), Duration = 0.2f, Time = 0, Delay = 0, TargetCol = -1, TargetIndex = -1, IsCollecting = false });
            }
        }
        SaveGame();
    }

    private (int col, int index)? FindCardPositionWithIndex(GameState state, Card card) { for (int c = 0; c < 10; c++) for (int i = 0; i < state.Tableau[c].Count; i++) if (state.Tableau[c].Cards[i].Id == card.Id) return (c, i); return null; }

    private int GetFaceUpOffset(GameState state, int column)
    {
        var pile = state.Tableau[column];
        int faceUpCount = Math.Max(0, pile.Count - pile.FaceDownCount);
        if (faceUpCount <= 1)
            return FaceUpOffset;

        // Reserve the bottom strip for Undo/Hint and keep every card inside the
        // playable region. Normal columns retain the original 38 px overlap;
        // tall columns are progressively compressed instead of covering HUD.
        int available = TableauBottomY - TableauTopY - CardHeight
            - pile.FaceDownCount * FaceDownOffset;
        int fittedOffset = available / (faceUpCount - 1);
        return Math.Clamp(fittedOffset, 5, FaceUpOffset);
    }

    private Point GetCardPosition(int col, int index, GameState? state = null)
    {
        state ??= _gameState;
        int x = 50 + col * (CardWidth + CardSpacing);
        int y = TableauTopY;
        int faceUpOffset = GetFaceUpOffset(state, col);
        for (int j = 0; j < index; j++)
            y += j < state.Tableau[col].FaceDownCount ? FaceDownOffset : faceUpOffset;
        return new Point(x, y);
    }

    private void DrawLuaStyleHint()
    {
        if (_hintState == HintState.None || _hintTimer > 7f || _gameState == null)
            return;

        float pulse = 0.5f + 0.5f * MathF.Sin(_hintTimer * 7f);
        Color gold = new Color(255, 218, 105);

        if (_isHintStock)
        {
            int dealsLeft = _gameState.Stock.Count / 10;
            if (dealsLeft > 0)
            {
                int spread = Math.Max(0, (dealsLeft - 1) * 3);
                Rectangle stockRect = new Rectangle(
                    StockX - spread, StockY - spread,
                    CardWidth + spread, CardHeight + spread);
                DrawHintGlowRect(stockRect, gold, pulse);
            }
            return;
        }

        if (_hintMove.FromColumn < 0 || _hintMove.FromColumn >= _gameState.Tableau.Count
            || _hintMove.ToColumn < 0 || _hintMove.ToColumn >= _gameState.Tableau.Count)
            return;

        var source = _gameState.Tableau[_hintMove.FromColumn];
        if (_hintMove.FromIndex < 0 || _hintMove.FromIndex >= source.Count)
            return;

        int sourceEnd = GameRules.GetSequenceEnd(source, _hintMove.FromIndex);
        Point sourceFirst = GetCardPosition(_hintMove.FromColumn, _hintMove.FromIndex);
        Point sourceLast = GetCardPosition(_hintMove.FromColumn, sourceEnd);
        int sequenceHeight = sourceLast.Y - sourceFirst.Y + CardHeight;
        Rectangle sourceRect = new Rectangle(sourceFirst.X, sourceFirst.Y, CardWidth, sequenceHeight);
        DrawHintGlowRect(sourceRect, gold, pulse);

        var target = _gameState.Tableau[_hintMove.ToColumn];
        int targetX;
        int targetY;
        if (target.IsEmpty)
        {
            targetX = 50 + _hintMove.ToColumn * (CardWidth + CardSpacing);
            targetY = TableauTopY;
            DrawHintGlowRect(new Rectangle(targetX, targetY, CardWidth, CardHeight), gold, pulse);
        }
        else
        {
            Point targetTop = GetCardPosition(_hintMove.ToColumn, target.Count - 1);
            targetX = targetTop.X;
            targetY = GetCardPosition(_hintMove.ToColumn, target.Count).Y;
            DrawHintGlowRect(new Rectangle(targetTop.X, targetTop.Y, CardWidth, CardHeight), gold, pulse);
        }

        // A translucent outline of the whole movable run glides from source to
        // destination. This deliberately replaces the old arrow with Lua's
        // soft, pulsing movement preview.
        float progress = Math.Min((_hintTimer % 1.8f) / 1.1f, 1f);
        progress = progress * progress * (3f - 2f * progress);
        int ghostX = (int)MathF.Round(MathHelper.Lerp(sourceFirst.X, targetX, progress));
        int ghostY = (int)MathF.Round(MathHelper.Lerp(sourceFirst.Y, targetY, progress));
        Rectangle ghost = new Rectangle(ghostX, ghostY, CardWidth, sequenceHeight);
        float fade = 1f - 0.4f * progress;
        Texture2D ghostMask = GetRoundedHintMask(ghost.Width, ghost.Height);
        _spriteBatch.Draw(ghostMask, ghost, gold * (0.12f * fade));
        DrawRoundedHintOutline(ghost, gold * (0.9f * fade), 2);
    }

    private void DrawHintGlowRect(Rectangle rect, Color color, float pulse)
    {
        // Draw each glow layer from a rounded-rectangle texture. The corner
        // radius stays fixed at the card's radius even when a run is very tall.
        for (int layer = 3; layer >= 1; layer--)
        {
            int thickness = layer * 3;
            float alpha = (0.15f + 0.5f * pulse) / (layer + 0.5f);
            Rectangle expanded = new Rectangle(
                rect.X - 2 - thickness / 2,
                rect.Y - 2 - thickness / 2,
                rect.Width + 4 + thickness,
                rect.Height + 4 + thickness);
            DrawRoundedHintOutline(expanded, color * alpha, thickness);
        }

        DrawRoundedHintOutline(
            new Rectangle(rect.X - 2, rect.Y - 2, rect.Width + 4, rect.Height + 4),
            color * (0.6f + 0.4f * pulse), 2);
    }

    private void DrawRoundedHintOutline(Rectangle destination, Color color, int thickness)
    {
        int width = Math.Max(8, destination.Width);
        int height = Math.Max(8, destination.Height);
        thickness = Math.Clamp(thickness, 1, Math.Min(width, height) / 2);
        var key = (width, height, thickness);

        if (!_hintOutlineCache.TryGetValue(key, out Texture2D? texture) || texture.IsDisposed)
        {
            const int maxCachedTextures = 48;
            while (_hintOutlineCache.Count >= maxCachedTextures && _hintOutlineCacheOrder.Count > 0)
            {
                var expiredKey = _hintOutlineCacheOrder.Dequeue();
                if (_hintOutlineCache.Remove(expiredKey, out Texture2D? expiredTexture))
                    expiredTexture.Dispose();
            }

            int scale = TextureQualityScale;
            texture = CreateRoundedBorder(
                width * scale,
                height * scale,
                Math.Min(10 * scale, Math.Min(width, height) * scale / 2),
                thickness * scale);
            _hintOutlineCache[key] = texture;
            _hintOutlineCacheOrder.Enqueue(key);
        }

        _spriteBatch.Draw(texture, destination, color);
    }

    private Texture2D GetRoundedHintMask(int width, int height)
    {
        width = Math.Max(8, width);
        height = Math.Max(8, height);
        var key = (width, height);

        if (_hintMaskCache.TryGetValue(key, out Texture2D? texture) && !texture.IsDisposed)
            return texture;

        const int maxCachedMasks = 12;
        while (_hintMaskCache.Count >= maxCachedMasks && _hintMaskCacheOrder.Count > 0)
        {
            var expiredKey = _hintMaskCacheOrder.Dequeue();
            if (_hintMaskCache.Remove(expiredKey, out Texture2D? expiredTexture))
                expiredTexture.Dispose();
        }

        int scale = TextureQualityScale;
        texture = CreateRoundedMask(
            width * scale,
            height * scale,
            Math.Min(10 * scale, Math.Min(width, height) * scale / 2));
        _hintMaskCache[key] = texture;
        _hintMaskCacheOrder.Enqueue(key);
        return texture;
    }

    private string GetSuitSymbol(Suit suit) => suit switch { Suit.Hearts => "♥", Suit.Diamonds => "♦", Suit.Clubs => "♣", Suit.Spades => "♠", _ => "?" };
    private string GetRankString(Rank rank) => rank switch { Rank.Ace => "A", Rank.Jack => "J", Rank.Queen => "Q", Rank.King => "K", _ => ((int)rank).ToString() };

    private void DrawCardScaled(Card card, float centerX, int y, bool isFaceDown, float scaleX, float darken, GameTime? gameTime)
    {
        int width = (int)(CardWidth * scaleX); if (width < 2) return; int x = (int)(centerX - width / 2); var rect = new Rectangle(x, y, width, CardHeight);
        ThemePalette palette = GetThemePalette();
        Color backColor = palette.CardBack;
        Color faceColor = palette.CardFace;
        if (width >= 20) DrawCardShadow(rect, 0.58f);
        if (isFaceDown) { 
            Texture2D? backTex = _currentCardBack switch { 
                1 => _cardBack2, 2 => _cardBack3, 3 => _cardBackMinimalist, 4 => _cardBackSpace, 5 => _cardBackGolden, _ => _cardBack1 
            }; 
            if (backTex != null) _spriteBatch.Draw(backTex, rect, Color.White); else _spriteBatch.Draw(_hintMask, rect, backColor); _spriteBatch.Draw(_borderBlack2, rect, new Color((byte)14, (byte)22, (byte)24, (byte)185)); 
        }
        else {
            _spriteBatch.Draw(_hintMask, rect, faceColor);
            if (width > 20) { Color baseTextColor = (card.Suit == Suit.Hearts || card.Suit == Suit.Diamonds) ? (GetThemePalette().IsDark ? Color.Crimson : Color.Red) : Color.Black; string suitStr = GetSuitSymbol(card.Suit); float centerScale = 0.5f * scaleX; var centerSize = _fontSuit.MeasureString(suitStr) * centerScale; var centerPos = new Vector2(x + (width - centerSize.X) / 2, y + (CardHeight - centerSize.Y) / 2); _spriteBatch.DrawString(_fontSuit, suitStr, centerPos, baseTextColor, 0f, Vector2.Zero, centerScale, SpriteEffects.None, 0f); }
            _spriteBatch.Draw(_borderBlack2, rect, new Color((byte)36, (byte)47, (byte)42, (byte)150));
        }
        if (darken < 1.0f) _spriteBatch.Draw(_hintMask, rect, Color.Black * (1.0f - darken));
    }

    private void DrawCard(Card card, int x, int y, bool isFaceDown, float darken = 1.0f, bool highlight = false, float hintBlend = 0f, bool isHovered = false, GameTime? gameTime = null)
    {
        var rect = new Rectangle(x, y, CardWidth, CardHeight);
        ThemePalette palette = GetThemePalette();
        Color faceColor = palette.CardFace;
        DrawCardShadow(rect, highlight ? 0.95f : (isHovered ? 0.88f : 0.68f));
        if (isFaceDown) { 
            Texture2D? backTex = _currentCardBack switch { 
                1 => _cardBack2, 2 => _cardBack3, 3 => _cardBackMinimalist, 4 => _cardBackSpace, 5 => _cardBackGolden, _ => _cardBack1 
            }; 
            if (backTex != null) _spriteBatch.Draw(backTex, rect, Color.White); else _spriteBatch.Draw(_hintMask, rect, palette.CardBack); _spriteBatch.Draw(_borderBlack2, rect, new Color((byte)14, (byte)22, (byte)24, (byte)185)); 
        }
        else {
            _spriteBatch.Draw(_hintMask, rect, faceColor);
            _spriteBatch.Draw(_pixel, new Rectangle(x + 8, y + 4, CardWidth - 16, 1), new Color((byte)255, (byte)255, (byte)255, (byte)85));
            _spriteBatch.Draw(_pixel, new Rectangle(x + 8, y + 5, 1, CardHeight - 10), new Color((byte)255, (byte)255, (byte)255, (byte)42));
            Color baseTextColor = (card.Suit == Suit.Hearts || card.Suit == Suit.Diamonds) ? (GetThemePalette().IsDark ? Color.Crimson : Color.Red) : Color.Black;
            string rankStr = GetRankString(card.Rank); string suitStr = GetSuitSymbol(card.Suit);

            float centerScale = 0.5f; var centerSize = _fontSuit.MeasureString(suitStr) * centerScale; var centerPos = new Vector2(x + (CardWidth - centerSize.X) / 2, y + (CardHeight - centerSize.Y) / 2); _spriteBatch.DrawString(_fontSuit, suitStr, centerPos, baseTextColor, 0f, Vector2.Zero, centerScale, SpriteEffects.None, 0f);

            var tlPos = new Vector2(x + 8, y + 4); var rankSizeTL = _font.MeasureString(rankStr) * _fontScale; _spriteBatch.DrawString(_font, rankStr, tlPos, baseTextColor, 0f, Vector2.Zero, _fontScale, SpriteEffects.None, 0f); var tlSuitPos = new Vector2(tlPos.X, tlPos.Y + rankSizeTL.Y - 2); _spriteBatch.DrawString(_fontSuit, suitStr, tlSuitPos, baseTextColor, 0f, Vector2.Zero, 0.3f, SpriteEffects.None, 0f);

            var brPos = new Vector2(x + CardWidth - 8, y + CardHeight - 4); var rankSizeBR = _font.MeasureString(rankStr) * _fontScale; _spriteBatch.DrawString(_font, rankStr, brPos, baseTextColor, MathF.PI, Vector2.Zero, _fontScale, SpriteEffects.None, 0f); var brSuitPos = new Vector2(brPos.X, brPos.Y - rankSizeBR.Y + 2); _spriteBatch.DrawString(_fontSuit, suitStr, brSuitPos, baseTextColor, MathF.PI, Vector2.Zero, 0.3f, SpriteEffects.None, 0f);

            if (highlight) _spriteBatch.Draw(_borderGold4, rect, Color.Gold);
            else if (isHovered && gameTime != null && !_isDragging) { float pulse = (float)Math.Sin(gameTime.TotalGameTime.TotalSeconds * 6) * 0.5f + 0.5f; _spriteBatch.Draw(_borderYellow3, rect, Color.LightGoldenrodYellow * (0.5f + 0.5f * pulse)); }
            else _spriteBatch.Draw(_borderBlack2, rect, new Color((byte)36, (byte)47, (byte)42, (byte)150));

            if (hintBlend > 0f && gameTime != null) { float pulse = (float)Math.Sin(gameTime.TotalGameTime.TotalSeconds * 10) * 0.5f + 0.5f; Color hintColor = palette.Hint; _spriteBatch.Draw(_hintMask, rect, hintColor * (hintBlend * (0.3f + 0.2f * pulse))); _spriteBatch.Draw(_hintBorderThick, rect, hintColor * (hintBlend * (0.8f + 0.2f * pulse))); }
        }
        if (darken < 1.0f) _spriteBatch.Draw(_hintMask, rect, Color.Black * (1.0f - darken));
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
