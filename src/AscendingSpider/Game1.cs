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
    private SpiderRuleset _currentRuleset = SpiderRuleset.Classic;

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
    private float _generationFailureNoticeTimer = 0f;

    private float _hintUnavailableNoticeTimer = 0f;
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

    private sealed record GeneratedDeal(GameState? State, SolvabilityStatus Status);

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
    private Move _lastHint = new Move(-1, -1, -1);

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
        int savedRuleset = SaveManager.Settings.LastRuleset;
        _currentRuleset = Enum.IsDefined(typeof(SpiderRuleset), savedRuleset)
            ? (SpiderRuleset)savedRuleset
            : SpiderRuleset.Classic;
        SaveManager.Settings.LastRuleset = (int)_currentRuleset;
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
        
        _selectorSuits = new UISelector(new Rectangle(560, 220, 380, 75), "Suits", new List<string> { "1Suit", "2Suits" });
        _selectorDifficulty = new UISelector(new Rectangle(560, 360, 380, 75), "Difficulty", new List<string> { "Easy", "Medium", "Hard" });
        _selectorMode = new UISelector(new Rectangle(560, 500, 380, 75), "Mode", new List<string> { "Standard", "Blitz", "Endless" });
        _btnStart = new UIButton { Rect = new Rectangle(625, 650, 250, 75), TextKey = "Start", OnClick = () => { StartNewGame(); } };
        _btnBackNewGame = new UIButton { Rect = new Rectangle(625, 745, 250, 75), TextKey = "Back", OnClick = () => { _currentScreen = GameScreen.MainMenu; } };

        _selectorLanguage = new UISelector(new Rectangle(510, 165, 480, 75), "Language", new List<string> { "Русский", "English", "Español", "中文", "日本語" });
        _selectorLanguage.SelectedIndex = Localization.Language switch { "EN" => 1, "ES" => 2, "ZH" => 3, "JA" => 4, _ => 0 };
        _selectorLanguage.OnChanged = (idx) => { 
            Localization.Language = idx switch { 1 => "EN", 2 => "ES", 3 => "ZH", 4 => "JA", _ => "RU" }; 
            SaveManager.Settings.Language = Localization.Language;
            SaveManager.SaveSettings();
            UpdateFonts();
            Window.Title = Localization.Get("Title");
        };

        _sliderVolume = new UISlider(new Rectangle(560, 290, 380, 38), "Volume", _volume);
        _sliderVolume.OnChanged = (val) => { _volume = val; SoundEffect.MasterVolume = _volume; SaveManager.Settings.Volume = _volume; SaveManager.SaveSettings(); };

        _selectorTableStyle = new UISelector(new Rectangle(510, 410, 480, 75), "TableStyle",
            new List<string> { "Classic", "Dark", "Ocean", "Burgundy", "Sand" });
        _selectorTableStyle.SelectedIndex = (int)_currentTheme;
        _selectorTableStyle.OnChanged = (idx) => { _currentTheme = (Theme)idx; ApplyThemeColors(); RefreshThemeCardBacks(); SaveManager.Settings.Theme = idx; SaveManager.SaveSettings(); };

        _selectorCardBack = new UISelector(new Rectangle(510, 535, 480, 75), "CardBack", new List<string>());
        _selectorCardBack.OnChanged = (idx) => { 
            if (idx < _selectorCardBack.OptionKeys.Count) {
                _currentCardBack = GetCardBackIdFromSelectorIndex(idx);
                SaveManager.Settings.CardBack = _currentCardBack; 
                SaveManager.SaveSettings(); 
            }
        };
        
        _btnResetStats = new UIButton { Rect = new Rectangle(510, 655, 480, 60), TextKey = "ResetStats", OnClick = () => {
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
        
        _btnBackSettings = new UIButton { Rect = new Rectangle(625, 820, 250, 60), TextKey = "Back", OnClick = () => { _currentScreen = _returnScreen; } };
        _btnBackAchievements = new UIButton { Rect = new Rectangle(625, 820, 250, 60), TextKey = "Back", OnClick = () => { _currentScreen = GameScreen.MainMenu; } };
        _btnBackLeaderboard = new UIButton { Rect = new Rectangle(625, 850, 250, 60), TextKey = "Back", OnClick = () => { _currentScreen = GameScreen.MainMenu; } };

        _btnInGameMenu = new UIButton { Rect = new Rectangle(1325, 50, 125, 62), TextKey = "Menu", OnClick = () => { _currentScreen = GameScreen.Paused; } };
        _btnUndo = new UIButton { Rect = new Rectangle(25, 900, 225, 62), TextKey = "Undo", OnClick = () => PerformUndo() };
        _btnHint = new UIButton { Rect = new Rectangle(275, 900, 225, 62), TextKey = "Hint", OnClick = () => TriggerHint() };

        _btnWinRestart = new UIButton { Rect = new Rectangle(625, 535, 250, 75), TextKey = "NewGame", OnClick = () => { SaveManager.DeleteSave(); UpdateContinueButton(); _currentScreen = GameScreen.NewGameSetup; } };
        _btnWinMenu = new UIButton { Rect = new Rectangle(625, 635, 250, 75), TextKey = "Menu", OnClick = () => { SaveManager.DeleteSave(); UpdateContinueButton(); _currentScreen = GameScreen.MainMenu; } };

        _btnStuckUndo = new UIButton { Rect = new Rectangle(625, 535, 250, 75), TextKey = "Undo", OnClick = () => PerformUndo() };
        _btnStuckRestart = new UIButton { Rect = new Rectangle(625, 625, 250, 75), TextKey = "Restart", OnClick = () => { StartNewGame(); } };
        _btnStuckMenu = new UIButton { Rect = new Rectangle(625, 715, 250, 75), TextKey = "Menu", OnClick = () => { _currentScreen = GameScreen.MainMenu; } };

        _btnPauseResume = new UIButton { Rect = new Rectangle(625, 325, 250, 75), TextKey = "Resume", OnClick = () => { _currentScreen = GameScreen.Playing; } };
        _btnPauseRestart = new UIButton { Rect = new Rectangle(625, 415, 250, 75), TextKey = "Restart", OnClick = () => { StartNewGame(); } };
        _btnPauseSettings = new UIButton { Rect = new Rectangle(625, 505, 250, 75), TextKey = "Settings", OnClick = () => { _returnScreen = GameScreen.Paused; _currentScreen = GameScreen.Settings; } };
        _btnPauseMainMenu = new UIButton { Rect = new Rectangle(625, 595, 250, 75), TextKey = "MainMenu", OnClick = () => { SaveGame(); _currentScreen = GameScreen.MainMenu; } };

        RecalcMainMenuButtons();
        ApplyThemeColors();
        base.Initialize();
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
        const int buttonWidth = 250;
        const int buttonHeight = 75;
        const int spacing = 14;
        int centerX = (1500 - buttonWidth) / 2;

        var buttons = new List<UIButton>
        {
            _btnNewGame,
            _btnContinue
        };
        if (HasAnyAchievement())
            buttons.Add(_btnAchievements);
        buttons.Add(_btnLeaderboard);
        buttons.Add(_btnSettings);
        buttons.Add(_btnExit);

        int totalHeight = buttons.Count * buttonHeight + (buttons.Count - 1) * spacing;
        int y = (1000 - totalHeight) / 2;
        foreach (UIButton button in buttons)
        {
            button.Rect = new Rectangle(centerX, y, buttonWidth, buttonHeight);
            y += buttonHeight + spacing;
        }
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
        
        // Build every card back from the active table theme palette.
        RefreshThemeCardBacks();

        SoundEffect.MasterVolume = _volume;
        UpdateContinueButton();
        RefreshCardBackSelector();
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
        if (idx == -1) { // Card back is locked or invalid; reset to the default.
            SaveManager.Settings.CardBack = 0;
            SaveManager.SaveSettings();
            idx = 0;
        }

        // The selector index is only the visible position in the unlocked list.
        // Rendering uses the persistent card-back ID (0..5), so restore it too.
        _currentCardBack = SaveManager.Settings.CardBack;
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
        CancelHintSearch();
        _hintUnavailableNoticeTimer = 0f;
        int suitCount = Math.Clamp(_selectorSuits.SelectedIndex + 1, 1, 2);
        _currentDifficulty = Math.Clamp(_selectorDifficulty.SelectedIndex, 0, 2);
        _currentMode = (GameMode)_selectorMode.SelectedIndex;
        // Keep the setup menu simple: one-suit games use standard rules;
        // two-suit games automatically use relaxed stack movement.
        _currentRuleset = suitCount == 2 ? SpiderRuleset.Relaxed : SpiderRuleset.Classic;
        SaveManager.Settings.LastRuleset = (int)_currentRuleset;
        SaveManager.SaveSettings();
        _faceDownPerColumn = GetFaceDownPerColumn(suitCount, _currentDifficulty, _currentRuleset);

        _generationFailureNoticeTimer = 0f;
        _generationAttempts = 0;
        _generationUnsolvableAttempts = 0;
        _generationUnknownAttempts = 0;
        _showDealSolvabilityStatus = false;
        _usedUndoThisGame = false; _usedHintThisGame = false;
        _mixedSuitPlacementUsedThisGame = false;
        _currentScreen = GameScreen.Generating;
        int suitCountLocal = suitCount;
        int faceDownLocal = _faceDownPerColumn;
        GameMode modeLocal = _currentMode;
        SpiderRuleset rulesetLocal = _currentRuleset;
        _dealGenerationTask = Task.Run(() =>
            GenerateSolvableParallel(suitCountLocal, faceDownLocal, modeLocal, rulesetLocal));
    }

    private static int GetFaceDownPerColumn(int suitCount, int difficulty, SpiderRuleset ruleset)
    {
        difficulty = Math.Clamp(difficulty, 0, 2);
        if (suitCount <= 1)
            return difficulty switch { 0 => 3, 1 => 5, _ => 7 };

        // Two-suit games use fewer hidden cards so the player can build suit
        // sequences earlier. Relaxed rules get a slightly denser Hard deal.
        if (ruleset == SpiderRuleset.Relaxed)
            return difficulty switch { 0 => 2, 1 => 3, _ => 4 };

        return difficulty switch { 0 => 2, 1 => 3, _ => 5 };
    }

    private GeneratedDeal GenerateSolvableParallel(
        int suitCount,
        int faceDown,
        GameMode mode,
        SpiderRuleset ruleset,
        int maxSeconds = 45)
    {
        // Endless has no terminal win state, so it cannot be certified by the
        // standard solver. It still receives a normal deal and heuristic hints.
        if (mode == GameMode.Endless)
        {
            return new GeneratedDeal(
                DeckFactory.Deal(suitCount, faceDown, new Random(), mode, ruleset),
                SolvabilityStatus.Unknown);
        }

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(maxSeconds));

        GameState? solvedDeal = null;
        object resultLock = new();
        int threadCount = Math.Max(1, Math.Min(Environment.ProcessorCount, 2));
        int maxStates = suitCount == 1 ? 250_000 : 160_000;
        int candidateTimeMs = suitCount == 1 ? 5_000 : 6_500;

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
                        GameState candidate = DeckFactory.Deal(suitCount, faceDown, rng, mode, ruleset);
                        Interlocked.Increment(ref _generationAttempts);

                        SolverSearchResult search = solver.SolveDetailed(
                            candidate,
                            maxStates,
                            cts.Token,
                            candidateTimeMs);

                        if (search.Status == SolvabilityStatus.Solvable
                            && search.Solution.Count > 0)
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
                            Interlocked.Increment(ref _generationUnsolvableAttempts);
                        else
                            Interlocked.Increment(ref _generationUnknownAttempts);
                        // Unknown candidates are discarded, never shown as if
                        // they were verified. Continue with a fresh seed.
                    }
                });
        }
        catch (OperationCanceledException)
        {
            // Expected when a solved deal is found or the total search window ends.
        }
        catch (AggregateException ex) when (ex.Flatten().InnerExceptions.All(
            inner => inner is OperationCanceledException))
        {
            // Parallel.For wraps expected cancellation in AggregateException.
        }

        lock (resultLock)
        {
            return solvedDeal != null
                ? new GeneratedDeal(solvedDeal, SolvabilityStatus.Solvable)
                : new GeneratedDeal(null, SolvabilityStatus.Unknown);
        }
    }

    private void ApplyGeneratedDeal(GeneratedDeal generated)
    {
        GameState generatedState = generated.State
            ?? throw new InvalidOperationException("A generated deal must be verified before it is applied.");

        CancelHintSearch();
        _gameState = generatedState;
        _currentRuleset = generatedState.Ruleset;
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
        _lastHint = new Move(-1, -1, -1);

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
        CancelHintSearch();
        var loaded = SaveManager.Load();
        if (loaded.HasValue) {
            _gameState = loaded.Value.State;
            _faceDownPerColumn = loaded.Value.FaceDownPerColumn;
            _currentRuleset = _gameState.Ruleset;
            SaveManager.Settings.LastRuleset = (int)_currentRuleset;
            SaveManager.SaveSettings();
            _currentDifficulty = _gameState.SuitCount == 1
                ? _faceDownPerColumn switch { 3 => 0, 7 => 2, _ => 1 }
                : _faceDownPerColumn switch { 2 => 0, 5 => 2, 4 => 2, _ => 1 };
            _selectorDifficulty.SelectedIndex = _currentDifficulty;
            GameEngine.Reset(); _undoStack.Clear(); _animations.Clear(); _hiddenCards.Clear(); _flipAnims.Clear(); _particles.Clear();
            _hintState = HintState.None; _isDragging = false; _stuckSoundPlayed = false; _lastHint = new Move(-1, -1, -1); _currentScreen = GameScreen.Playing;
            _gameTimeSeconds = SaveManager.Settings.CurrentGameTime;
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
            SaveManager.Settings.CurrentGameTime = _gameTimeSeconds;
            SaveManager.Save(_gameState, _faceDownPerColumn, _mixedSuitPlacementUsedThisGame);
            UpdateContinueButton();
        }
    }

    private void TriggerHint()
    {
        if (_gameState == null || _gameState.IsWon || !_isGameActive
            || _currentScreen != GameScreen.Playing || _isDragging || _animations.Count > 0)
            return;

        _usedHintThisGame = true;
        _hintState = HintState.None;
        _hintUnavailableNoticeTimer = 0f;

        // Instant, single-position evaluation. No background task, full-game
        // solve, reachability proof, or loading spinner is started on hint press.
        Move? previousHint = _lastHint.FromColumn >= 0 ? _lastHint : null;
        HintSearchResult result;
        try
        {
            result = new SpiderSolver().FindBestHintInstant(_gameState, previousHint);
        }
        catch
        {
            result = default;
        }

        if (result.HasHint)
        {
            if (result.IsStockDeal && GameEngine.CanDealFromStock(_gameState))
            {
                _lastHint = new Move(-1, -1, -1);
                _isHintStock = true;
                _hintState = HintState.ShowTarget;
                _hintTimer = 0.5f;
                return;
            }

            bool legalMove = result.Move.FromColumn >= 0
                && GameEngine.EnumerateProgressMoves(_gameState).Contains(result.Move);
            if (legalMove)
            {
                _hintMove = result.Move;
                _lastHint = result.Move;
                _isHintStock = false;
                _hintState = HintState.ShowSource;
                _hintTimer = 0f;
                return;
            }
        }

        _hintState = HintState.None;
        _hintUnavailableNoticeTimer = 2.5f;
    }

    private void CancelHintSearch()
    {
        // Kept as a common board-change hook so existing move/load paths can
        // clear a stale visual hint. Hint calculation itself is synchronous.
        _hintState = HintState.None;
        _isHintStock = false;
    }

    private static int GetTopSameSuitRunLength(Pile pile)
    {
        if (pile.Count == 0) return 0;
        int length = 1;
        for (int i = pile.Count - 1; i > 0; i--) {
            var lower = pile.Cards[i - 1];
            var upper = pile.Cards[i];
            if (lower.Suit == upper.Suit && (int)upper.Rank == (int)lower.Rank + 1)
                length++;
            else
                break;
        }
        return length;
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
        if (!SaveManager.Settings.Ach4Suits && _gameState.SuitCount == 2) { SaveManager.Settings.Ach4Suits = true; _achievementQueue.Enqueue("Ach4Suits"); unlockedNewCardBack = true; }
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
        if (_generationFailureNoticeTimer > 0f)
            _generationFailureNoticeTimer = Math.Max(0f, _generationFailureNoticeTimer - deltaTime);
        if (_hintUnavailableNoticeTimer > 0f)
            _hintUnavailableNoticeTimer = Math.Max(0f, _hintUnavailableNoticeTimer - deltaTime);

        if (_activeAchievementPopup != null) { _activeAchievementPopup.Timer -= deltaTime; if (_activeAchievementPopup.Timer <= 0) _activeAchievementPopup = null; }
        else if (_achievementQueue.Count > 0) _activeAchievementPopup = new AchievementPopup { NameKey = _achievementQueue.Dequeue() };
        if (_confirmReset) { _resetConfirmTimer -= deltaTime; if (_resetConfirmTimer <= 0) _confirmReset = false; }
        if (_currentScreen != GameScreen.Settings) _confirmReset = false;

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
                if (_dealGenerationTask != null && _dealGenerationTask.IsCompleted)
                {
                    GeneratedDeal generated;
                    try
                    {
                        generated = _dealGenerationTask.GetAwaiter().GetResult();
                    }
                    catch
                    {
                        generated = new GeneratedDeal(null, SolvabilityStatus.Unknown);
                    }

                    _dealGenerationTask = null;
                    if (generated.State == null)
                    {
                        // Do not start an unverified Standard/Blitz deal and do
                        // not destroy the previous save when certification fails.
                        _currentScreen = GameScreen.NewGameSetup;
                        _generationFailureNoticeTimer = 7.0f;
                        UpdateContinueButton();
                    }
                    else
                    {
                        SaveManager.DeleteSave();
                        UpdateContinueButton();
                        ApplyGeneratedDeal(generated);
                    }
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
        if (_hintState != HintState.None) { _hintTimer += deltaTime; if (_hintState == HintState.ShowSource && _hintTimer > 0.5f) _hintState = HintState.ShowTarget; else if (_hintTimer > 1.5f) _hintState = HintState.None; }
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
            PositionStuckButtons(includeUndo: !timeExpired);
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
                    _lastHint = new Move(-1, -1, -1);
                    var stateBefore = _gameState.Clone();
                    _undoStack.Add(stateBefore);
                    if (_undoStack.Count > 30) _undoStack.RemoveAt(0);
                    int previousCompletedInRound = _gameState.CompletedSequences;
                    int previousTotalCompleted = _gameState.TotalCompletedSequences;
                    int previousEndlessLoops = _gameState.EndlessLoops;
                    CancelHintSearch();
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
        var sourcePile = _gameState.Tableau[_dragFromColumn];
        var movingCard = sourcePile.Cards[_dragFromIndex];
        var candidates = new List<(int Column, int Score)>();

        // Prefer a same-suit destination, but keep every legal destination as a
        // fallback. A preferred destination may be a proven dead end while a
        // second destination is safe.
        for (int col = 0; col < 10; col++)
        {
            if (col == _dragFromColumn)
                continue;

            var targetPile = _gameState.Tableau[col];
            if (!targetPile.IsEmpty && GameRules.CanPlaceOn(movingCard, targetPile.TopCard))
            {
                int score = targetPile.TopCard.Suit == movingCard.Suit ? 100 : 0;
                if (targetPile.TopCard.Suit == movingCard.Suit)
                    score += GetTopSameSuitRunLength(targetPile) * 5;
                candidates.Add((col, score));
            }
        }

        // Empty columns are considered only when no ranked destination exists,
        // matching the previous quick-move behavior.
        if (candidates.Count == 0)
        {
            for (int col = 0; col < 10; col++)
            {
                if (col != _dragFromColumn && _gameState.Tableau[col].IsEmpty)
                    candidates.Add((col, 0));
            }
        }

        // Auto-move is a convenience action, not a safety gate. Never cancel a
        // legal player move because a bounded prediction suspects a dead end.
        // The normal no-moves dialog handles an actual dead-end position.
        int bestCol = candidates
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Column)
            .Select(item => item.Column)
            .DefaultIfEmpty(-1)
            .First();

        if (bestCol < 0)
        {
            BounceBack(playWrongSound: false);
            return;
        }

        var stateBefore = _gameState.Clone();
        int prevCompleted = _gameState.CompletedSequences;
        int prevTotalCompleted = _gameState.TotalCompletedSequences;
        int prevEndlessLoops = _gameState.EndlessLoops;
        int cardsMoved = GameRules.GetSequenceEnd(stateBefore.Tableau[_dragFromColumn], _dragFromIndex, stateBefore.Ruleset) - _dragFromIndex + 1;
        bool crossSuitPlacement = IsCrossSuitPlacement(stateBefore, _dragFromColumn, _dragFromIndex, bestCol);
        bool success = GameEngine.TryMove(_gameState, _dragFromColumn, _dragFromIndex, bestCol);
        if (success)
        {
            CancelHintSearch();
            if (IsPerfectOrderEligibleSuitCount(_gameState.SuitCount) && crossSuitPlacement)
                _mixedSuitPlacementUsedThisGame = true;

            _undoStack.Add(stateBefore);
            if (_undoStack.Count > 30)
                _undoStack.RemoveAt(0);

            _cardMoveSound.Play(1.0f, cardsMoved > 1 ? -0.2f : 0.0f, 0f);

            if (_gameState.TotalCompletedSequences > prevTotalCompleted)
            {
                _dealCollectSound.Play();
                int sequenceIndex = prevCompleted;
                foreach (var collected in GameEngine.LastCollectedSequences)
                    AddCollectAnimation(collected.Column, sequenceIndex++, collected.Suit, stateBefore);
            }

            if (_gameState.EndlessLoops > prevEndlessLoops)
            {
                AddTableauDealAnimations();
            }
            else if (_gameState.TotalCompletedSequences == prevTotalCompleted)
            {
                var toPile = _gameState.Tableau[bestCol];
                int startIndexInTarget = toPile.Count - cardsMoved;
                for (int i = 0; i < cardsMoved; i++)
                {
                    var cardToAnim = toPile.Cards[startIndexInTarget + i];
                    var endPos = GetCardPosition(bestCol, startIndexInTarget + i);
                    var startPos = GetCardPosition(_dragFromColumn, _dragFromIndex + i, stateBefore);
                    _animations.Add(new CardAnimation
                    {
                        Card = cardToAnim,
                        StartPos = new Vector2(startPos.X, startPos.Y),
                        EndPos = new Vector2(endPos.X, endPos.Y),
                        Duration = 0.2f,
                        Time = 0,
                        Delay = 0,
                        TargetCol = bestCol,
                        TargetIndex = startIndexInTarget + i,
                        IsBounceBack = false
                    });
                    _hiddenCards.Add((bestCol, startIndexInTarget + i));
                }
            }

            if (_gameState.EndlessLoops == prevEndlessLoops)
                CheckFlips(stateBefore);

            SaveGame();
            _dragFromColumn = -1;
            _dragFromIndex = -1;
        }
        else
        {
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
                _spriteBatch.Draw(_pixel, new Rectangle(0, 0, 1500, 1000), new Color(0, 0, 0, 180));
                DrawCenteredText("Paused", 220, Color.White, 1.5f);
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
        DrawCenteredText("Leaderboard", 60, Color.White, 0.9f);
        
        int yOffset = 180;
        DrawCenteredText("Standard", yOffset, Color.Gold, 0.8f);
        yOffset += 40;
        var standard = SaveManager.Settings.LeaderboardStandard;
        if (standard.Count == 0) { DrawCenteredText("NoScores", yOffset, Color.Gray, 0.6f); yOffset += 40; }
        else {
            for (int i = 0; i < standard.Count; i++) {
                string text = $"{i + 1}. {FormatTime(standard[i])}";
                DrawCenteredTextRaw(text, yOffset, Color.White, 0.6f);
                yOffset += 30;
            }
        }

        yOffset += 30;
        DrawCenteredText("Blitz", yOffset, Color.Gold, 0.8f);
        yOffset += 40;
        var blitz = SaveManager.Settings.LeaderboardBlitz;
        if (blitz.Count == 0) { DrawCenteredText("NoScores", yOffset, Color.Gray, 0.6f); yOffset += 40; }
        else {
            for (int i = 0; i < blitz.Count; i++) {
                string text = $"{i + 1}. {FormatTime(blitz[i])}";
                DrawCenteredTextRaw(text, yOffset, Color.White, 0.6f);
                yOffset += 30;
            }
        }

        _btnBackLeaderboard.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse);
    }

    private void DrawAchievementPopup(AchievementPopup popup) { float alpha = MathHelper.Clamp(popup.Timer / 0.5f, 0f, 1f); int y = 130; var bgRect = new Rectangle(1050, y, 420, 80); _spriteBatch.Draw(_pixel, bgRect, new Color(0, 0, 0) * (0.85f * alpha)); _spriteBatch.Draw(_pixel, new Rectangle(bgRect.X, bgRect.Y, bgRect.Width, 3), Color.Gold * alpha); _spriteBatch.Draw(_pixel, new Rectangle(bgRect.X, bgRect.Bottom - 3, bgRect.Width, 3), Color.Gold * alpha); _spriteBatch.Draw(_pixel, new Rectangle(bgRect.X, bgRect.Y, 3, bgRect.Height), Color.Gold * alpha); _spriteBatch.Draw(_pixel, new Rectangle(bgRect.Right - 3, bgRect.Y, 3, bgRect.Height), Color.Gold * alpha); var starRect = new Rectangle(bgRect.X + 15, bgRect.Y + 25, 30, 30); _spriteBatch.Draw(_starTexture, starRect, Color.Gold * alpha); string name = Localization.Get(popup.NameKey); string unlocked = Localization.Get("AchUnlocked"); _spriteBatch.DrawString(_font, name, new Vector2(bgRect.X + 60, bgRect.Y + 12), Color.Gold * alpha, 0f, Vector2.Zero, _fontScale, SpriteEffects.None, 0f); _spriteBatch.DrawString(_font, unlocked, new Vector2(bgRect.X + 60, bgRect.Y + 45), Color.White * alpha, 0f, Vector2.Zero, _fontScale * 0.8f, SpriteEffects.None, 0f); }

    private void DrawGenerating(GameTime gameTime, MouseState mouse)
    {
        DrawCenteredText("Generating", 370, Color.White, 1.0f);
        string attemptText = $"{Localization.Get("Attempt")}: {_generationAttempts}";
        DrawCenteredText(attemptText, 440, new Color(220, 220, 220), 0.6f);
        string rejectedText = $"{Localization.Get("DealRejected")}: {_generationUnsolvableAttempts}";
        DrawCenteredText(rejectedText, 485, new Color(220, 160, 160), 0.5f);
        string unknownText = $"{Localization.Get("DealUnverified")}: {_generationUnknownAttempts}";
        DrawCenteredText(unknownText, 525, new Color(220, 200, 140), 0.5f);
        int dots = (int)(gameTime.TotalGameTime.TotalSeconds * 2) % 4;
        DrawCenteredText(new string('.', dots), 575, new Color(200, 200, 200), 0.8f);
    }

    private void DrawMainMenu(MouseState mouse, GameTime gameTime)
    {
        float pulse = 2.35f + (float)Math.Sin(gameTime.TotalGameTime.TotalSeconds * 2.5f) * 0.15f; string titleText = Localization.Get("Title"); Vector2 size = _font.MeasureString(titleText) * (_fontScale * pulse); _spriteBatch.DrawString(_font, titleText, new Vector2((1500 - size.X) / 2, 50), Color.Gold, 0f, Vector2.Zero, _fontScale * pulse, SpriteEffects.None, 0f);
        _btnNewGame.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse); _btnContinue.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse); if (HasAnyAchievement()) _btnAchievements.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse); 
        _btnLeaderboard.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse);
        _btnSettings.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse); _btnExit.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse);
        string difficultyName = Localization.Get("Easy"); string statsText = $"{Localization.Get("Wins")}: {SaveManager.Settings.Wins}  |  {Localization.Get("BestTime")} ({difficultyName}): {FormatTime(SaveManager.Settings.BestTimeEasy)}"; _spriteBatch.DrawString(_font, statsText, new Vector2(25, 925), Color.White, 0f, Vector2.Zero, _fontScale, SpriteEffects.None, 0f);
    }

    private void DrawNewGameMenu(MouseState mouse)
    {
        DrawCenteredText("NewGame", 85, Color.White, 0.9f);
        ThemePalette palette = GetThemePalette();
        _selectorSuits.Draw(_spriteBatch, _pixel, _font, mouse, palette.Panel, palette.PanelAccent);
        _selectorDifficulty.Draw(_spriteBatch, _pixel, _font, mouse, palette.Panel, palette.PanelAccent);
        _selectorMode.Draw(_spriteBatch, _pixel, _font, mouse, palette.Panel, palette.PanelAccent);
        if (_generationFailureNoticeTimer > 0f)
            DrawCenteredText("NoVerifiedDeal", 585, new Color(255, 190, 110), 0.45f);
        _btnStart.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse);
        _btnBackNewGame.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse);
    }

    private void DrawSettingsMenu(MouseState mouse)
    {
        ThemePalette palette = GetThemePalette();
        DrawCenteredText("Settings", 60, Color.White, 0.9f);
        _selectorLanguage.Draw(_spriteBatch, _pixel, _font, mouse, palette.Panel, palette.PanelAccent);
        _sliderVolume.Draw(_spriteBatch, _pixel, _font, palette.Panel, palette.PanelAccent);
        _selectorTableStyle.Draw(_spriteBatch, _pixel, _font, mouse, palette.Panel, palette.PanelAccent);
        _selectorCardBack.Draw(_spriteBatch, _pixel, _font, mouse, palette.Panel, palette.PanelAccent);
        string originalKey = _btnResetStats.TextKey; _btnResetStats.TextKey = _confirmReset ? "ConfirmReset" : "ResetStats"; _btnResetStats.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse); _btnResetStats.TextKey = originalKey; _btnBackSettings.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse);
    }

    private void DrawAchievementsMenu(MouseState mouse, GameTime gameTime)
    {
        DrawCenteredText("Achievements", 60, Color.White, 0.9f); 
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
        int startY = 130; int rowHeight = 70;
        foreach (var (nameKey, descKey, unlocked) in achievements) {
            var iconRect = new Rectangle(370, startY + 15, 40, 40); _spriteBatch.Draw(_pixel, iconRect, unlocked ? new Color(60, 180, 100) : new Color(80, 80, 80)); _spriteBatch.Draw(_pixel, new Rectangle(iconRect.Left, iconRect.Top, iconRect.Width, 2), Color.White); _spriteBatch.Draw(_pixel, new Rectangle(iconRect.Left, iconRect.Bottom - 2, iconRect.Width, 2), Color.White); _spriteBatch.Draw(_pixel, new Rectangle(iconRect.Left, iconRect.Top, 2, iconRect.Height), Color.White); _spriteBatch.Draw(_pixel, new Rectangle(iconRect.Right - 2, iconRect.Top, 2, iconRect.Height), Color.White);
            if (unlocked) { var starRect = new Rectangle(iconRect.X + 5, iconRect.Y + 5, 30, 30); _spriteBatch.Draw(_starTexture, starRect, Color.Gold); } else { string icon = "?"; Vector2 iconSize = _font.MeasureString(icon) * _fontScale; _spriteBatch.DrawString(_font, icon, new Vector2(iconRect.X + (40 - iconSize.X) / 2, iconRect.Y + (40 - iconSize.Y) / 2), new Color(150, 150, 150), 0f, Vector2.Zero, _fontScale, SpriteEffects.None, 0f); }
            Color nameColor = unlocked ? Color.Gold : new Color(150, 150, 150); Color descColor = unlocked ? Color.White : new Color(100, 100, 100); _spriteBatch.DrawString(_font, Localization.Get(nameKey), new Vector2(440, startY + 5), nameColor, 0f, Vector2.Zero, _fontScale, SpriteEffects.None, 0f); _spriteBatch.DrawString(_font, Localization.Get(descKey), new Vector2(440, startY + 35), descColor, 0f, Vector2.Zero, _fontScale * 0.75f, SpriteEffects.None, 0f); startY += rowHeight;
        } _btnBackAchievements.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse);
    }

    private void DrawCenteredText(string key, int y, Color color, float scale = 1f) { string text = Localization.Get(key); Vector2 size = _font.MeasureString(text) * (_fontScale * scale); _spriteBatch.DrawString(_font, text, new Vector2((1500 - size.X) / 2, y), color, 0f, Vector2.Zero, _fontScale * scale, SpriteEffects.None, 0f); }
    private void DrawCenteredTextRaw(string text, int y, Color color, float scale = 1f) { Vector2 size = _font.MeasureString(text) * (_fontScale * scale); _spriteBatch.DrawString(_font, text, new Vector2((1500 - size.X) / 2, y), color, 0f, Vector2.Zero, _fontScale * scale, SpriteEffects.None, 0f); }
    private string FormatTime(double seconds) { if (seconds <= 0 && _gameState?.Mode != GameMode.Blitz) return "--:--"; int m = (int)(seconds / 60); int s = (int)(seconds % 60); return $"{m:00}:{s:00}"; }

    private void DrawPlaying(GameTime gameTime, MouseState mouse)
    {
        // Permanent tableau placeholders: draw every column at the very back,
        // even when it contains cards. Cards, hints, and animations are rendered
        // afterwards, so they cover the placeholder naturally. This avoids any
        // temporary redraw/overlay and prevents doubled visuals during movement.
        for (int col = 0; col < 10; col++)
        {
            int slotX = 50 + col * (CardWidth + CardSpacing);
            var slotRect = new Rectangle(slotX, 50, CardWidth, CardHeight);
            _spriteBatch.Draw(_hintMask, slotRect, Color.Black * 0.2f);
            _spriteBatch.Draw(_borderBlack2, slotRect, Color.Black * 0.6f);
        }

        bool terminalScreen = _gameState.IsWon || IsGameStuck();
        if (!terminalScreen) {
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

        if (_showDealSolvabilityStatus && _lastDealSolvability == SolvabilityStatus.Unknown)
            _spriteBatch.DrawString(_font, Localization.Get("FallbackDeal"), new Vector2(930, 120), GetThemePalette().Accent, 0f, Vector2.Zero, _fontScale * 0.7f, SpriteEffects.None, 0f);

        if (_hintUnavailableNoticeTimer > 0f)
            DrawCenteredText("HintUnavailable", 865, GetThemePalette().Accent, 0.45f);


        float sourceBlend = 0f; float targetBlend = 0f; if (_hintState == HintState.ShowSource) sourceBlend = MathHelper.Clamp(_hintTimer / 0.5f, 0f, 1f); if (_hintState == HintState.ShowTarget) targetBlend = MathHelper.Clamp((_hintTimer - 0.5f) / 0.5f, 0f, 1f);
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
            var pile = _gameState.Tableau[col];
            int dragEndIndex = _isDragging && col == _dragFromColumn ? GameRules.GetSequenceEnd(pile, _dragFromIndex, _gameState.Ruleset) : -1;

            for (int i = 0; i < pile.Count; i++) {
                if (_isDragging && col == _dragFromColumn && i >= _dragFromIndex && i <= dragEndIndex) continue; 
                if (_hiddenCards.Contains((col, i))) continue;
                
                bool isFaceDown = i < pile.FaceDownCount;
                
                float darkenFactor = 1.0f;
                if (!isFaceDown && _heldColumn == col && !GameRules.CanPickUp(pile, i, _gameState.Ruleset)) darkenFactor = 0.5f;

                var pos = GetCardPosition(col, i); float hint = 0f; if (_hintState == HintState.ShowSource && !_isHintStock && col == _hintMove.FromColumn && i >= _hintMove.FromIndex) hint = sourceBlend; bool isHovered = (col == _hoveredCol && i == _hoveredIndex);
                var flip = _flipAnims.FirstOrDefault(f => f.Col == col && f.Index == i);
                if (flip != null) { float progress = flip.Time / flip.Duration; float scaleX = 1f; if (progress < 0.5f) scaleX = 1f - (progress * 2); else { scaleX = (progress - 0.5f) * 2; isFaceDown = false; } DrawCardScaled(pile.Cards[i], pos.X + (CardWidth / 2), pos.Y, isFaceDown, scaleX, darkenFactor, gameTime); }
                else DrawCard(pile.Cards[i], pos.X, pos.Y, isFaceDown, darkenFactor, false, hint, isHovered, gameTime);
            }
        }
        
        int dealsLeftGlobal = _gameState.Stock.Count / 10;
        if (dealsLeftGlobal > 0) {
            for (int i = 0; i < dealsLeftGlobal; i++) DrawCard(new Card(Suit.Hearts, Rank.Ace), StockX - (i * 3), StockY - (i * 3), true, 1.0f, false, 0f, false, gameTime); string stockText = $"{Localization.Get("Deals")}: {dealsLeftGlobal}"; var textSize = _font.MeasureString(stockText) * _fontScale; _spriteBatch.DrawString(_font, stockText, new Vector2(StockX + (CardWidth - textSize.X) / 2, StockY + CardHeight + 15), Color.White, 0f, Vector2.Zero, _fontScale, SpriteEffects.None, 0f);
            if (_isStockHovered && !_isDragging) { float pulse = (float)Math.Sin(gameTime.TotalGameTime.TotalSeconds * 6) * 0.5f + 0.5f; int topCardX = StockX - (dealsLeftGlobal - 1) * 3; int topCardY = StockY - (dealsLeftGlobal - 1) * 3; _spriteBatch.Draw(_borderYellow3, new Rectangle(topCardX, topCardY, CardWidth, CardHeight), Color.LightGoldenrodYellow * (0.5f + 0.5f * pulse)); }
        } else { var stockRect = new Rectangle(StockX, StockY, CardWidth, CardHeight); _spriteBatch.Draw(_hintMask, stockRect, Color.Black * 0.2f); _spriteBatch.Draw(_borderBlack2, stockRect, Color.Black * 0.6f); }
        
        if (_hintState == HintState.ShowTarget) {
            float pulse = (float)Math.Sin(gameTime.TotalGameTime.TotalSeconds * 10) * 0.5f + 0.5f; Color hintColor = GetThemePalette().Hint;
            if (_isHintStock && _gameState.Stock.Count >= 10) { int dealsLeft = _gameState.Stock.Count / 10; int topCardX = StockX - (dealsLeft - 1) * 3; int topCardY = StockY - (dealsLeft - 1) * 3; var rect = new Rectangle(topCardX, topCardY, CardWidth, CardHeight); _spriteBatch.Draw(_hintMask, rect, hintColor * (targetBlend * (0.2f + 0.2f * pulse))); _spriteBatch.Draw(_hintBorderThick, rect, hintColor * (targetBlend * (0.8f + 0.2f * pulse))); }
            else if (!_isHintStock) { var toPile = _gameState.Tableau[_hintMove.ToColumn]; var pos = toPile.IsEmpty ? GetCardPosition(_hintMove.ToColumn, 0) : GetCardPosition(_hintMove.ToColumn, toPile.Count - 1); var rect = new Rectangle(pos.X, pos.Y, CardWidth, CardHeight); _spriteBatch.Draw(_hintMask, rect, hintColor * (targetBlend * (0.3f + 0.2f * pulse))); _spriteBatch.Draw(_hintBorderThick, rect, hintColor * (targetBlend * (0.8f + 0.2f * pulse))); }
        }
        
        foreach (var anim in _animations) {
            if (anim.Delay > 0) continue; float progress = MathHelper.Clamp(anim.Time / anim.Duration, 0f, 1f); Vector2 currentPos; if (anim.IsArc) { Vector2 mid = (anim.StartPos + anim.EndPos) / 2; mid.Y -= 200; currentPos = Vector2.Lerp(Vector2.Lerp(anim.StartPos, mid, progress), Vector2.Lerp(mid, anim.EndPos, progress), progress); } else currentPos = Vector2.Lerp(anim.StartPos, anim.EndPos, progress); DrawCard(anim.Card, (int)currentPos.X, (int)currentPos.Y, anim.IsFaceDown, 1.0f, false, 0f, false, gameTime);
        }

        if (_isDragging) {
            var pile = _gameState.Tableau[_dragFromColumn]; 
            int endIndex = GameRules.GetSequenceEnd(pile, _dragFromIndex, _gameState.Ruleset);
            
            for (int i = _dragFromIndex; i <= endIndex; i++) {
                int y = (int)_dragPos.Y + (i - _dragFromIndex) * FaceUpOffset;
                _spriteBatch.Draw(_hintMask, new Rectangle((int)_dragPos.X + 6, y + 6, CardWidth, CardHeight), Color.Black * 0.4f);
            }
            
            int currentY = (int)_dragPos.Y; int currentX = (int)_dragPos.X;
            for (int i = _dragFromIndex; i <= endIndex; i++) { DrawCard(pile.Cards[i], currentX, currentY, false, 1.0f, true, 0f, false, gameTime); currentY += FaceUpOffset; }
        }
        
        bool isStuck = IsGameStuck();
        if (_gameState.IsWon && _animations.Count == 0) { _spriteBatch.Draw(_pixel, new Rectangle(0, 0, 1500, 1000), new Color(0, 0, 0, 180)); DrawCenteredText("Win", 300, Color.Gold, 3.2f); DrawCenteredText($"{Localization.Get("Time")}: {FormatTime(_gameTimeSeconds)}", 450, Color.White, 1.0f); _btnWinRestart.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse); _btnWinMenu.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse); }
        else if (isStuck) { 
            _spriteBatch.Draw(_pixel, new Rectangle(0, 0, 1500, 1000), new Color(0, 0, 0, 180)); 
            string stuckText = (_gameState.Mode == GameMode.Blitz && _gameTimeSeconds <= 0) ? "TimeUp" : "Stuck";
            DrawCenteredText(stuckText, 400, GetThemePalette().IsDark ? Color.IndianRed : Color.Red, 1.0f);
            bool timeExpired = _gameState.Mode == GameMode.Blitz && _gameTimeSeconds <= 0;
            PositionStuckButtons(includeUndo: !timeExpired);
            if (!timeExpired)
                _btnStuckUndo.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse);
            _btnStuckRestart.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse);
            _btnStuckMenu.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse); 
        }
        
        if (_showTutorial) { _spriteBatch.Draw(_pixel, new Rectangle(0, 0, 1500, 1000), new Color(0, 0, 0, 220)); DrawCenteredText("TutorialTitle", 200, Color.Gold, 1.5f); string[] lines = Localization.Get("TutorialText").Split('\n'); int startY = 320; foreach (var line in lines) { if (string.IsNullOrWhiteSpace(line)) { startY += 20; continue; } DrawCenteredTextRaw(line, startY, Color.White, 0.6f); startY += 40; } float pulse = (float)Math.Sin(gameTime.TotalGameTime.TotalSeconds * 3) * 0.5f + 0.5f; DrawCenteredText("TutorialClick", 850, Color.Gold * (0.5f + 0.5f * pulse), 0.8f); }
        DrawParticles();
    }

    private void PositionStuckButtons(bool includeUndo)
    {
        const int x = 625;
        const int width = 250;
        const int height = 75;
        if (includeUndo)
        {
            _btnStuckUndo.Rect = new Rectangle(x, 535, width, height);
            _btnStuckRestart.Rect = new Rectangle(x, 625, width, height);
            _btnStuckMenu.Rect = new Rectangle(x, 715, width, height);
        }
        else
        {
            _btnStuckRestart.Rect = new Rectangle(x, 585, width, height);
            _btnStuckMenu.Rect = new Rectangle(x, 675, width, height);
        }
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
            if (GameRules.CanPickUp(pile, i, _gameState.Ruleset)) { 
                _isDragging = true; _dragFromColumn = col; _dragFromIndex = i; _dragOffset = new Vector2(mousePoint.X - pos.X, mousePoint.Y - pos.Y); _dragPos = new Vector2(pos.X, pos.Y); _dragStartMousePos = mousePoint; _dragHoldTime = 0f; _cardMoveSound.Play(0.8f, 0f, 0f); _hintState = HintState.None; 
            } return; } } }
    }

    private void TryDropCard(Point mousePoint)
    {
        for (int col = 0; col < 10; col++) {
            int x = 50 + col * (CardWidth + CardSpacing); if (mousePoint.X >= x - HitboxPaddingX && mousePoint.X <= x + CardWidth + HitboxPaddingX && mousePoint.Y >= 50) {
                var stateBefore = _gameState.Clone();
                    int prevCompleted = _gameState.CompletedSequences;
                int prevTotalCompleted = _gameState.TotalCompletedSequences;
                int prevEndlessLoops = _gameState.EndlessLoops;
                int cardsMoved = GameRules.GetSequenceEnd(stateBefore.Tableau[_dragFromColumn], _dragFromIndex, stateBefore.Ruleset) - _dragFromIndex + 1;
                bool crossSuitPlacement = IsCrossSuitPlacement(stateBefore, _dragFromColumn, _dragFromIndex, col);
                bool success = GameEngine.TryMove(_gameState, _dragFromColumn, _dragFromIndex, col);
                if (success) {
                    CancelHintSearch();
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
                            var startPos = _dragPos + new Vector2(0, i * FaceUpOffset);
                            _animations.Add(new CardAnimation { Card = card, StartPos = startPos, EndPos = new Vector2(endPos.X, endPos.Y), Duration = 0.2f, Time = 0, Delay = 0, TargetCol = col, TargetIndex = startIndexInTarget + i, IsBounceBack = false });
                            _hiddenCards.Add((col, startIndexInTarget + i));
                        }
                    }
                    if (_gameState.EndlessLoops == prevEndlessLoops)
                        CheckFlips(stateBefore);
                    SaveGame(); _dragFromColumn = -1; _dragFromIndex = -1;
                } else BounceBack();
                return;
            }
        }
        BounceBack();
    }

    private void BounceBack(bool playWrongSound = true)
    {
        if (playWrongSound) _wrongSound.Play();
        var pile = _gameState.Tableau[_dragFromColumn];
        int endIndex = GameRules.GetSequenceEnd(pile, _dragFromIndex, _gameState.Ruleset);
        for (int i = _dragFromIndex; i <= endIndex; i++) {
            var originalPos = GetCardPosition(_dragFromColumn, i); var startPos = _dragPos + new Vector2(0, (i - _dragFromIndex) * FaceUpOffset);
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
        CancelHintSearch();
        _usedUndoThisGame = true; if (_undoStack.Count == 0) return; _hintState = HintState.None; _lastHint = new Move(-1, -1, -1); _stuckSoundPlayed = false;
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

    private Point GetCardPosition(int col, int index, GameState? state = null) { state ??= _gameState; int x = 50 + col * (CardWidth + CardSpacing); int y = 50; for (int j = 0; j < index; j++) y += j < state.Tableau[col].FaceDownCount ? FaceDownOffset : FaceUpOffset; return new Point(x, y); }

    private string GetSuitSymbol(Suit suit) => suit switch { Suit.Hearts => "♥", Suit.Diamonds => "♦", Suit.Clubs => "♣", Suit.Spades => "♠", _ => "?" };
    private string GetRankString(Rank rank) => rank switch { Rank.Ace => "A", Rank.Jack => "J", Rank.Queen => "Q", Rank.King => "K", _ => ((int)rank).ToString() };

    private void DrawCardScaled(Card card, float centerX, int y, bool isFaceDown, float scaleX, float darken, GameTime? gameTime)
    {
        int width = (int)(CardWidth * scaleX); if (width < 2) return; int x = (int)(centerX - width / 2); var rect = new Rectangle(x, y, width, CardHeight);
        ThemePalette palette = GetThemePalette();
        Color backColor = palette.CardBack;
        Color faceColor = palette.CardFace;
        if (isFaceDown) { 
            Texture2D? backTex = _currentCardBack switch { 
                1 => _cardBack2, 2 => _cardBack3, 3 => _cardBackMinimalist, 4 => _cardBackSpace, 5 => _cardBackGolden, _ => _cardBack1 
            }; 
            if (backTex != null) _spriteBatch.Draw(backTex, rect, Color.White); else _spriteBatch.Draw(_hintMask, rect, backColor); _spriteBatch.Draw(_borderBlack2, rect, Color.Black); 
        }
        else {
            _spriteBatch.Draw(_hintMask, rect, faceColor);
            if (width > 20) { Color baseTextColor = (card.Suit == Suit.Hearts || card.Suit == Suit.Diamonds) ? (GetThemePalette().IsDark ? Color.Crimson : Color.Red) : Color.Black; string suitStr = GetSuitSymbol(card.Suit); float centerScale = 0.5f * scaleX; var centerSize = _fontSuit.MeasureString(suitStr) * centerScale; var centerPos = new Vector2(x + (width - centerSize.X) / 2, y + (CardHeight - centerSize.Y) / 2); _spriteBatch.DrawString(_fontSuit, suitStr, centerPos, baseTextColor, 0f, Vector2.Zero, centerScale, SpriteEffects.None, 0f); }
            _spriteBatch.Draw(_borderBlack2, rect, Color.Black);
        }
        if (darken < 1.0f) _spriteBatch.Draw(_hintMask, rect, Color.Black * (1.0f - darken));
    }

    private void DrawCard(Card card, int x, int y, bool isFaceDown, float darken = 1.0f, bool highlight = false, float hintBlend = 0f, bool isHovered = false, GameTime? gameTime = null)
    {
        var rect = new Rectangle(x, y, CardWidth, CardHeight);
        ThemePalette palette = GetThemePalette();
        Color faceColor = palette.CardFace;
        if (isFaceDown) { 
            Texture2D? backTex = _currentCardBack switch { 
                1 => _cardBack2, 2 => _cardBack3, 3 => _cardBackMinimalist, 4 => _cardBackSpace, 5 => _cardBackGolden, _ => _cardBack1 
            }; 
            if (backTex != null) _spriteBatch.Draw(backTex, rect, Color.White); else _spriteBatch.Draw(_hintMask, rect, palette.CardBack); _spriteBatch.Draw(_borderBlack2, rect, Color.Black); 
        }
        else {
            _spriteBatch.Draw(_hintMask, rect, faceColor);
            Color baseTextColor = (card.Suit == Suit.Hearts || card.Suit == Suit.Diamonds) ? (GetThemePalette().IsDark ? Color.Crimson : Color.Red) : Color.Black;
            string rankStr = GetRankString(card.Rank); string suitStr = GetSuitSymbol(card.Suit);

            float centerScale = 0.5f; var centerSize = _fontSuit.MeasureString(suitStr) * centerScale; var centerPos = new Vector2(x + (CardWidth - centerSize.X) / 2, y + (CardHeight - centerSize.Y) / 2); _spriteBatch.DrawString(_fontSuit, suitStr, centerPos, baseTextColor, 0f, Vector2.Zero, centerScale, SpriteEffects.None, 0f);

            var tlPos = new Vector2(x + 8, y + 4); var rankSizeTL = _font.MeasureString(rankStr) * _fontScale; _spriteBatch.DrawString(_font, rankStr, tlPos, baseTextColor, 0f, Vector2.Zero, _fontScale, SpriteEffects.None, 0f); var tlSuitPos = new Vector2(tlPos.X, tlPos.Y + rankSizeTL.Y - 2); _spriteBatch.DrawString(_fontSuit, suitStr, tlSuitPos, baseTextColor, 0f, Vector2.Zero, 0.3f, SpriteEffects.None, 0f);

            var brPos = new Vector2(x + CardWidth - 8, y + CardHeight - 4); var rankSizeBR = _font.MeasureString(rankStr) * _fontScale; _spriteBatch.DrawString(_font, rankStr, brPos, baseTextColor, MathF.PI, Vector2.Zero, _fontScale, SpriteEffects.None, 0f); var brSuitPos = new Vector2(brPos.X, brPos.Y - rankSizeBR.Y + 2); _spriteBatch.DrawString(_fontSuit, suitStr, brSuitPos, baseTextColor, MathF.PI, Vector2.Zero, 0.3f, SpriteEffects.None, 0f);

            if (highlight) _spriteBatch.Draw(_borderGold4, rect, Color.Gold);
            else if (isHovered && gameTime != null && !_isDragging) { float pulse = (float)Math.Sin(gameTime.TotalGameTime.TotalSeconds * 6) * 0.5f + 0.5f; _spriteBatch.Draw(_borderYellow3, rect, Color.LightGoldenrodYellow * (0.5f + 0.5f * pulse)); }
            else _spriteBatch.Draw(_borderBlack2, rect, Color.Black);

            if (hintBlend > 0f && gameTime != null) { float pulse = (float)Math.Sin(gameTime.TotalGameTime.TotalSeconds * 10) * 0.5f + 0.5f; Color hintColor = palette.Hint; _spriteBatch.Draw(_hintMask, rect, hintColor * (hintBlend * (0.3f + 0.2f * pulse))); _spriteBatch.Draw(_hintBorderThick, rect, hintColor * (hintBlend * (0.8f + 0.2f * pulse))); }
        }
        if (darken < 1.0f) _spriteBatch.Draw(_hintMask, rect, Color.Black * (1.0f - darken));
    }
}