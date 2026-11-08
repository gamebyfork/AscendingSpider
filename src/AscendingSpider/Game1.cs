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
public enum Theme { Classic, Dark }

public class Game1 : Game
{
    private GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch = null!;
    private RenderTarget2D _renderTarget = null!;
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

    private Task<GameState>? _dealGenerationTask = null;
    private int _generationAttempts = 0;
    private bool _lastDealIsFallback = false;

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
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
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
        _currentTheme = (Theme)SaveManager.Settings.Theme;
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
        
        _selectorSuits = new UISelector(new Rectangle(560, 250, 380, 75), "Suits", new List<string> { "1Suit", "2Suits", "3Suits", "4Suits" });
        _selectorDifficulty = new UISelector(new Rectangle(560, 370, 380, 75), "Difficulty", new List<string> { "Easy", "Medium", "Hard" });
        _selectorMode = new UISelector(new Rectangle(560, 490, 380, 75), "Mode", new List<string> { "Standard", "Blitz", "Endless" });
        _btnStart = new UIButton { Rect = new Rectangle(625, 620, 250, 75), TextKey = "Start", OnClick = () => { StartNewGame(); } };
        _btnBackNewGame = new UIButton { Rect = new Rectangle(625, 720, 250, 75), TextKey = "Back", OnClick = () => { _currentScreen = GameScreen.MainMenu; } };

        _selectorLanguage = new UISelector(new Rectangle(510, 150, 480, 75), "Language", new List<string> { "Русский", "English", "Español", "中文", "日本語" });
        _selectorLanguage.SelectedIndex = Localization.Language switch { "EN" => 1, "ES" => 2, "ZH" => 3, "JA" => 4, _ => 0 };
        _selectorLanguage.OnChanged = (idx) => { 
            Localization.Language = idx switch { 1 => "EN", 2 => "ES", 3 => "ZH", 4 => "JA", _ => "RU" }; 
            SaveManager.Settings.Language = Localization.Language;
            SaveManager.SaveSettings();
            UpdateFonts();
            Window.Title = Localization.Get("Title");
        };

        _sliderVolume = new UISlider(new Rectangle(560, 280, 380, 38), "Volume", _volume);
        _sliderVolume.OnChanged = (val) => { _volume = val; SoundEffect.MasterVolume = _volume; SaveManager.Settings.Volume = _volume; SaveManager.SaveSettings(); };

        _selectorTableStyle = new UISelector(new Rectangle(510, 410, 480, 75), "TableStyle", new List<string> { "Classic", "Dark" });
        _selectorTableStyle.SelectedIndex = (int)_currentTheme;
        _selectorTableStyle.OnChanged = (idx) => { _currentTheme = (Theme)idx; ApplyThemeColors(); SaveManager.Settings.Theme = idx; SaveManager.SaveSettings(); };

        _selectorCardBack = new UISelector(new Rectangle(510, 540, 480, 75), "CardBack", new List<string>());
        _selectorCardBack.OnChanged = (idx) => { 
            if (idx < _selectorCardBack.OptionKeys.Count) {
                _currentCardBack = GetCardBackIdFromSelectorIndex(idx);
                SaveManager.Settings.CardBack = _currentCardBack; 
                SaveManager.SaveSettings(); 
            }
        };
        
        _btnResetStats = new UIButton { Rect = new Rectangle(510, 650, 480, 60), TextKey = "ResetStats", OnClick = () => {
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
        
        _btnBackSettings = new UIButton { Rect = new Rectangle(625, 740, 250, 60), TextKey = "Back", OnClick = () => { _currentScreen = _returnScreen; } };
        _btnBackAchievements = new UIButton { Rect = new Rectangle(625, 750, 250, 60), TextKey = "Back", OnClick = () => { _currentScreen = GameScreen.MainMenu; } };
        _btnBackLeaderboard = new UIButton { Rect = new Rectangle(625, 800, 250, 60), TextKey = "Back", OnClick = () => { _currentScreen = GameScreen.MainMenu; } };

        _btnInGameMenu = new UIButton { Rect = new Rectangle(1325, 50, 125, 62), TextKey = "Menu", OnClick = () => { _currentScreen = GameScreen.Paused; } };
        _btnUndo = new UIButton { Rect = new Rectangle(25, 900, 225, 62), TextKey = "Undo", OnClick = () => PerformUndo() };
        _btnHint = new UIButton { Rect = new Rectangle(275, 900, 225, 62), TextKey = "Hint", OnClick = () => TriggerHint() };

        _btnWinRestart = new UIButton { Rect = new Rectangle(625, 500, 250, 75), TextKey = "NewGame", OnClick = () => { SaveManager.DeleteSave(); UpdateContinueButton(); _currentScreen = GameScreen.NewGameSetup; } };
        _btnWinMenu = new UIButton { Rect = new Rectangle(625, 610, 250, 75), TextKey = "Menu", OnClick = () => { SaveManager.DeleteSave(); UpdateContinueButton(); _currentScreen = GameScreen.MainMenu; } };

        _btnStuckUndo = new UIButton { Rect = new Rectangle(500, 600, 225, 75), TextKey = "Undo", OnClick = () => PerformUndo() };
        _btnStuckRestart = new UIButton { Rect = new Rectangle(775, 600, 225, 75), TextKey = "Restart", OnClick = () => { SaveManager.DeleteSave(); StartNewGame(); } };
        _btnStuckMenu = new UIButton { Rect = new Rectangle(625, 700, 250, 75), TextKey = "Menu", OnClick = () => { _currentScreen = GameScreen.MainMenu; } };

        _btnPauseResume = new UIButton { Rect = new Rectangle(625, 300, 250, 75), TextKey = "Resume", OnClick = () => { _currentScreen = GameScreen.Playing; } };
        _btnPauseRestart = new UIButton { Rect = new Rectangle(625, 400, 250, 75), TextKey = "Restart", OnClick = () => { SaveManager.DeleteSave(); StartNewGame(); } };
        _btnPauseSettings = new UIButton { Rect = new Rectangle(625, 500, 250, 75), TextKey = "Settings", OnClick = () => { _returnScreen = GameScreen.Paused; _currentScreen = GameScreen.Settings; } };
        _btnPauseMainMenu = new UIButton { Rect = new Rectangle(625, 600, 250, 75), TextKey = "MainMenu", OnClick = () => { SaveGame(); _currentScreen = GameScreen.MainMenu; } };

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
        int y = 180; int spacing = 85;
        _btnNewGame.Rect = new Rectangle(625, y, 250, 75); y += spacing;
        _btnContinue.Rect = new Rectangle(625, y, 250, 75); y += spacing;
        if (HasAnyAchievement()) { _btnAchievements.Rect = new Rectangle(625, y, 250, 75); y += spacing; }
        _btnLeaderboard.Rect = new Rectangle(625, y, 250, 75); y += spacing;
        _btnSettings.Rect = new Rectangle(625, y, 250, 75); y += spacing;
        _btnExit.Rect = new Rectangle(625, y, 250, 75);
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

    private void ApplyThemeColors()
    {
        var buttons = new List<UIButton> { 
            _btnNewGame, _btnContinue, _btnAchievements, _btnLeaderboard, _btnSettings, _btnExit, _btnStart, _btnBackNewGame, _btnBackSettings, _btnBackAchievements, _btnBackLeaderboard,
            _btnInGameMenu, _btnUndo, _btnHint, _btnWinRestart, _btnWinMenu, _btnStuckUndo, _btnStuckRestart, _btnStuckMenu,
            _btnPauseResume, _btnPauseRestart, _btnPauseSettings, _btnPauseMainMenu
        };
        foreach (var btn in buttons)
        {
            if (_currentTheme == Theme.Dark) { btn.BaseColor = new Color(50, 50, 70); btn.HoverColor = new Color(70, 70, 100); }
            else { btn.BaseColor = new Color(40, 130, 70); btn.HoverColor = new Color(60, 180, 100); }
        }
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _renderTarget = new RenderTarget2D(GraphicsDevice, 1500, 1000);
        UpdateRenderDestination();

        _pixel = new Texture2D(GraphicsDevice, 1, 1); _pixel.SetData(new[] { Color.White });
        _starTexture = CreateStarTexture(30);
        _fontLatin = Content.Load<SpriteFont>("OpenSans"); _fontLatinItalic = Content.Load<SpriteFont>("OpenSansItalic"); _fontCjk = Content.Load<SpriteFont>("NotoSansSC"); _fontSuit = Content.Load<SpriteFont>("Arial");
        UpdateFonts();

        _clickSound = Content.Load<SoundEffect>("Sounds/audio_click"); _hoverButtonSound = Content.Load<SoundEffect>("Sounds/audio_hover_button"); _hoverCardSound = Content.Load<SoundEffect>("Sounds/audio_hover_card"); _cardMoveSound = Content.Load<SoundEffect>("Sounds/audio_card_move"); _dealCollectSound = Content.Load<SoundEffect>("Sounds/audio_deal_collect"); _winSound = Content.Load<SoundEffect>("Sounds/audio_win");
        try { _stuckSound = Content.Load<SoundEffect>("Sounds/audio_stuck"); } catch (ContentLoadException) { _stuckSound = _winSound; }
        try { _wrongSound = Content.Load<SoundEffect>("Sounds/audio_wrong"); } catch (ContentLoadException) { _wrongSound = _clickSound; }
        try { _dealDistSound = Content.Load<SoundEffect>("Sounds/audio_deal_dist"); } catch (ContentLoadException) { _dealDistSound = _cardMoveSound; }

        _borderBlack2 = CreateRoundedBorder(CardWidth, CardHeight, 10, 2); _borderYellow3 = CreateRoundedBorder(CardWidth, CardHeight, 10, 3); _borderGold4 = CreateRoundedBorder(CardWidth, CardHeight, 10, 4); _hintMask = CreateRoundedMask(CardWidth, CardHeight, 10); _hintBorderThick = CreateRoundedBorder(CardWidth, CardHeight, 10, 5);
        
        _cardBack1 = CreateCardBack(new Color(30, 60, 120), new Color(20, 40, 80), 0); 
        _cardBack2 = CreateCardBack(new Color(150, 30, 30), new Color(100, 20, 20), 0); 
        _cardBack3 = CreateCardBack(new Color(30, 30, 30), new Color(60, 60, 60), 1);
        _cardBackMinimalist = CreateMinimalistCardBack();
        _cardBackSpace = CreateSpaceCardBack();
        _cardBackGolden = CreateGoldenCardBack();

        SoundEffect.MasterVolume = _volume;
        UpdateContinueButton();
        RefreshCardBackSelector();
    }

    private Texture2D CreateMinimalistCardBack()
    {
        var tex = new Texture2D(GraphicsDevice, CardWidth, CardHeight); Color[] data = new Color[CardWidth * CardHeight];
        for (int x = 0; x < CardWidth; x++) for (int y = 0; y < CardHeight; y++) {
            bool inside = IsInsideRoundedRect(x, y, CardWidth, CardHeight, 10); if (!inside) { data[y * CardWidth + x] = Color.Transparent; continue; }
            bool border = !IsInsideRoundedRect(x - 4, y - 4, CardWidth - 8, CardHeight - 8, 6);
            data[y * CardWidth + x] = border ? Color.Black : Color.White;
        }
        tex.SetData(data); return tex;
    }

    private Texture2D CreateSpaceCardBack()
    {
        var tex = new Texture2D(GraphicsDevice, CardWidth, CardHeight); Color[] data = new Color[CardWidth * CardHeight];
        var rng = new Random(42);
        for (int x = 0; x < CardWidth; x++) for (int y = 0; y < CardHeight; y++) {
            bool inside = IsInsideRoundedRect(x, y, CardWidth, CardHeight, 10); if (!inside) { data[y * CardWidth + x] = Color.Transparent; continue; }
            Color c = new Color(20, 10, 40);
            if (rng.Next(100) < 5) c = Color.White;
            data[y * CardWidth + x] = c;
        }
        tex.SetData(data); return tex;
    }

    private Texture2D CreateGoldenCardBack()
    {
        var tex = new Texture2D(GraphicsDevice, CardWidth, CardHeight); Color[] data = new Color[CardWidth * CardHeight];
        for (int x = 0; x < CardWidth; x++) for (int y = 0; y < CardHeight; y++) {
            bool inside = IsInsideRoundedRect(x, y, CardWidth, CardHeight, 10); if (!inside) { data[y * CardWidth + x] = Color.Transparent; continue; }
            bool inner = IsInsideRoundedRect(x - 6, y - 6, CardWidth - 12, CardHeight - 12, 5);
            data[y * CardWidth + x] = inner ? new Color(200, 150, 20) : new Color(255, 215, 0);
        }
        tex.SetData(data); return tex;
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
        var tex = new Texture2D(GraphicsDevice, CardWidth, CardHeight); Color[] data = new Color[CardWidth * CardHeight];
        for (int x = 0; x < CardWidth; x++) for (int y = 0; y < CardHeight; y++) {
            bool inside = IsInsideRoundedRect(x, y, CardWidth, CardHeight, 10); if (!inside) { data[y * CardWidth + x] = Color.Transparent; continue; }
            bool inner = IsInsideRoundedRect(x - 8, y - 8, CardWidth - 16, CardHeight - 16, 5); Color c = inner ? innerColor : baseColor;
            if (inner && patternType == 1) { if (x % 8 == 0 || y % 8 == 0) c = baseColor; }
            data[y * CardWidth + x] = c;
        }
        tex.SetData(data); return tex;
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
        SaveManager.DeleteSave(); UpdateContinueButton();
        int suitCount = _selectorSuits.SelectedIndex + 1; 
        _faceDownPerColumn = _selectorDifficulty.SelectedIndex switch { 0 => 3, 1 => 5, 2 => 7, _ => 5 };
        _currentMode = (GameMode)_selectorMode.SelectedIndex;
        
        _generationAttempts = 0; _lastDealIsFallback = false; _usedUndoThisGame = false; _usedHintThisGame = false; _currentScreen = GameScreen.Generating;
        int suitCountLocal = suitCount; int faceDownLocal = _faceDownPerColumn; var modeLocal = _currentMode;
        _dealGenerationTask = Task.Run(() => GenerateSolvableParallel(suitCountLocal, faceDownLocal, modeLocal));
    }

    private GameState GenerateSolvableParallel(int suitCount, int faceDown, GameMode mode, int maxSeconds = 15)
    {
        var cts = new CancellationTokenSource(maxSeconds * 1000); GameState? result = null; object lockObj = new object(); int threadCount = Math.Min(Environment.ProcessorCount, 6);
        try {
            Parallel.For(0, threadCount, new ParallelOptions { CancellationToken = cts.Token, MaxDegreeOfParallelism = threadCount }, (i) => {
                var solver = new SpiderSolver(); var localRng = new Random(Guid.NewGuid().GetHashCode());
                while (!cts.Token.IsCancellationRequested) {
                    var deal = DeckFactory.Deal(suitCount, faceDown, localRng, mode); Interlocked.Increment(ref _generationAttempts);
                    int maxStates = suitCount switch { 1 => 2000, 2 => 6000, 3 => 15000, 4 => 30000, _ => 15000 };
                    try { if (solver.Solve(deal, maxStates).IsSolvable) { lock (lockObj) { if (result == null) result = deal; } cts.Cancel(); return; } }
                    catch (OperationCanceledException) { return; } catch { }
                }
            });
        } catch (OperationCanceledException) { } catch (AggregateException) { }
        return result ?? DeckFactory.Deal(suitCount, faceDown, new Random(), mode);
    }

    private void ApplyGeneratedDeal(GameState state, bool isFallback)
    {
        _gameState = state; 
        _lastDealIsFallback = isFallback;
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
        
        if (_gameState.Mode == GameMode.Blitz) _gameTimeSeconds = 180.0;
        else _gameTimeSeconds = 0;
        
        _isGameActive = true; 
        _winConfettiTimer = 0f; 
        SaveManager.Settings.CurrentGameTime = 0; 
        _currentScreen = GameScreen.Playing;
        if (!SaveManager.Settings.HasSeenTutorial) _showTutorial = true;
        
        _dealDistSound.Play();

        for (int col = 0; col < 10; col++) {
            var pile = _gameState.Tableau[col];
            for (int i = 0; i < pile.Count; i++) {
                var targetPos = GetCardPosition(col, i, _gameState);
                var startPos = new Vector2(StockX, StockY);
                _animations.Add(new CardAnimation {
                    Card = pile.Cards[i],
                    StartPos = startPos,
                    EndPos = new Vector2(targetPos.X, targetPos.Y),
                    Duration = 0.4f,
                    Time = 0,
                    Delay = (col * 5 + i) * 0.04f,
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
            _gameState = loaded.Value.Item1; _faceDownPerColumn = loaded.Value.Item2;
            GameEngine.Reset(); _undoStack.Clear(); _animations.Clear(); _hiddenCards.Clear(); _flipAnims.Clear(); _particles.Clear();
            _hintState = HintState.None; _isDragging = false; _stuckSoundPlayed = false; _lastHint = new Move(-1, -1, -1); _currentScreen = GameScreen.Playing;
            _gameTimeSeconds = SaveManager.Settings.CurrentGameTime; _isGameActive = true; _winConfettiTimer = 0f; _lastDealIsFallback = false; _usedUndoThisGame = false; _usedHintThisGame = false;
            RefreshCardBackSelector();
        }
    }

    private void SaveGame()
    {
        if (_gameState != null && !_gameState.IsWon) { SaveManager.Settings.CurrentGameTime = _gameTimeSeconds; SaveManager.Save(_gameState, _faceDownPerColumn); UpdateContinueButton(); }
    }

    private void TriggerHint()
    {
        _usedHintThisGame = true;
        var allMoves = GameEngine.EnumerateMoves(_gameState).ToList(); var validMoves = new List<Move>();
        foreach (var m in allMoves) {
            var fromPile = _gameState.Tableau[m.FromColumn]; var toPile = _gameState.Tableau[m.ToColumn]; var card = fromPile.Cards[m.FromIndex];
            bool opensFaceDown = (m.FromIndex == fromPile.FaceDownCount && fromPile.FaceDownCount > 0); bool emptiesColumn = (m.FromIndex == 0);
            if (!toPile.IsEmpty && m.FromIndex > 0 && !opensFaceDown && !emptiesColumn) { var cardUnder = fromPile.Cards[m.FromIndex - 1]; if (cardUnder.Rank == toPile.TopCard.Rank) continue; }
            bool sameSuit = (!toPile.IsEmpty && toPile.TopCard.Suit == card.Suit);
            if (opensFaceDown || emptiesColumn || sameSuit) { validMoves.Add(m); continue; } validMoves.Add(m);
        }
        if (validMoves.Count > 0) {
            Move bestMove = validMoves[0]; int bestScore = int.MinValue;
            foreach (var m in validMoves) { int s = ScoreMove(_gameState, m); if (s > bestScore) { bestScore = s; bestMove = m; } }
            _hintMove = bestMove; _lastHint = bestMove; _isHintStock = false; _hintState = HintState.ShowSource; _hintTimer = 0f;
        } else if (_gameState.Stock.Count >= 10) { _lastHint = new Move(-1, -1, -1); _isHintStock = true; _hintState = HintState.ShowTarget; _hintTimer = 0.5f; } else _hintState = HintState.None;
    }

    private int ScoreMove(GameState state, Move move)
    {
        int score = 0; var fromPile = state.Tableau[move.FromColumn]; var toPile = state.Tableau[move.ToColumn]; var card = fromPile.Cards[move.FromIndex];
        if (move.FromColumn == _lastHint.ToColumn && move.ToColumn == _lastHint.FromColumn) score -= 500;
        if (move.FromIndex == fromPile.FaceDownCount && fromPile.FaceDownCount > 0) score += 100;
        if (move.FromIndex == 0) score += 50; if (!toPile.IsEmpty && toPile.TopCard.Suit == card.Suit) score += 30; return score;
    }

    private void CheckAchievements()
    {
        bool wasEmpty = !HasAnyAchievement();
        bool unlockedNewCardBack = false;

        if (!SaveManager.Settings.AchFirstWin) { SaveManager.Settings.AchFirstWin = true; _achievementQueue.Enqueue("AchFirstWin"); unlockedNewCardBack = true; }
        if (!SaveManager.Settings.AchNoUndo && !_usedUndoThisGame) { SaveManager.Settings.AchNoUndo = true; _achievementQueue.Enqueue("AchNoUndo"); unlockedNewCardBack = true; }
        if (!SaveManager.Settings.AchNoHint && !_usedHintThisGame) { SaveManager.Settings.AchNoHint = true; _achievementQueue.Enqueue("AchNoHint"); }
        if (!SaveManager.Settings.AchSpeedrun && _gameTimeSeconds < 300) { SaveManager.Settings.AchSpeedrun = true; _achievementQueue.Enqueue("AchSpeedrun"); }
        if (!SaveManager.Settings.Ach4Suits && _gameState.SuitCount == 4) { SaveManager.Settings.Ach4Suits = true; _achievementQueue.Enqueue("Ach4Suits"); unlockedNewCardBack = true; }
        if (!SaveManager.Settings.AchHardMode && _selectorDifficulty.SelectedIndex == 2) { SaveManager.Settings.AchHardMode = true; _achievementQueue.Enqueue("AchHardMode"); }
        if (!SaveManager.Settings.AchDedicated && SaveManager.Settings.Wins >= 10) { SaveManager.Settings.AchDedicated = true; _achievementQueue.Enqueue("AchDedicated"); }
        if (!SaveManager.Settings.AchPerfectOrder && _gameState.DealsMade == 0) { SaveManager.Settings.AchPerfectOrder = true; _achievementQueue.Enqueue("AchPerfectOrder"); }
        
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
            case GameScreen.Generating: if (_dealGenerationTask != null && _dealGenerationTask.IsCompleted) { GameState result; bool isFallback = false; try { result = _dealGenerationTask.Result; } catch { result = DeckFactory.Deal(_selectorSuits.SelectedIndex + 1, _faceDownPerColumn, new Random(), _currentMode); isFallback = true; } _dealGenerationTask = null; ApplyGeneratedDeal(result, isFallback); } break;
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
        _btnInGameMenu.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); _btnUndo.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); _btnHint.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound);
        if (_currentScreen != GameScreen.Playing) return;
        if (_hintState != HintState.None) { _hintTimer += deltaTime; if (_hintState == HintState.ShowSource && _hintTimer > 0.5f) _hintState = HintState.ShowTarget; else if (_hintTimer > 1.5f) _hintState = HintState.None; }
        bool isStuck = IsGameStuck();
        
        if (_gameState.IsWon && _animations.Count == 0) {
            if (_isGameActive) { _isGameActive = false; _winSound.Play(); UpdateStats(true); SaveManager.Settings.CurrentGameTime = 0; _winConfettiTimer = 0; CheckAchievements(); }
            if (_winConfettiTimer < 3.0f) { _winConfettiTimer += deltaTime; for (int i = 0; i < 15; i++) SpawnConfetti(); }
            _btnWinRestart.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); _btnWinMenu.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); return;
        } else if (isStuck) {
            if (!_stuckSoundPlayed) { _stuckSound.Play(); _stuckSoundPlayed = true; _isGameActive = false; UpdateStats(false); SaveManager.Settings.CurrentGameTime = 0; }
            _btnStuckUndo.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); _btnStuckRestart.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); _btnStuckMenu.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); return;
        }
        
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
                    _hintState = HintState.None; _lastHint = new Move(-1, -1, -1); var stateBefore = _gameState.Clone(); _undoStack.Add(stateBefore); if (_undoStack.Count > 30) _undoStack.RemoveAt(0); int prevCompleted = _gameState.CompletedSequences; GameEngine.TryDealFromStock(_gameState); _dealCollectSound.Play();
                    for (int col = 0; col < 10; col++) { var pile = _gameState.Tableau[col]; int targetIndex = pile.Count - 1; var endPos = GetCardPosition(col, targetIndex); Card cardToAnimate = pile.Count > 0 ? pile.TopCard : new Card(Suit.Spades, Rank.Ace); _animations.Add(new CardAnimation { Card = cardToAnimate, StartPos = new Vector2(StockX, StockY), EndPos = new Vector2(endPos.X, endPos.Y), Duration = 0.35f, Time = 0, Delay = col * 0.05f, IsArc = true, TargetCol = col, TargetIndex = targetIndex, IsCollecting = false }); _hiddenCards.Add((col, targetIndex)); }
                    if (_gameState.CompletedSequences > prevCompleted) AddCollectAnimation(GameEngine.LastCollectedColumn, _gameState.CompletedSequences - 1, stateBefore); CheckFlips(stateBefore); SaveGame();
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
            int cardsMoved = GameRules.GetSequenceEnd(stateBefore.Tableau[_dragFromColumn], _dragFromIndex) - _dragFromIndex + 1;
            bool success = GameEngine.TryMove(_gameState, _dragFromColumn, _dragFromIndex, bestCol);
            if (success) {
                _undoStack.Add(stateBefore);
                if (_undoStack.Count > 30) _undoStack.RemoveAt(0);
                
                _cardMoveSound.Play(1.0f, cardsMoved > 1 ? -0.2f : 0.0f, 0f);
                
                if (_gameState.CompletedSequences > prevCompleted) { 
                    _dealCollectSound.Play(); 
                    AddCollectAnimation(GameEngine.LastCollectedColumn, _gameState.CompletedSequences - 1, stateBefore); 
                } else {
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
        int diff = _selectorDifficulty.SelectedIndex; 
        if (diff == 0) { if (SaveManager.Settings.BestTimeEasy == 0 || _gameTimeSeconds < SaveManager.Settings.BestTimeEasy) SaveManager.Settings.BestTimeEasy = _gameTimeSeconds; } 
        if (diff == 1) { if (SaveManager.Settings.BestTimeMedium == 0 || _gameTimeSeconds < SaveManager.Settings.BestTimeMedium) SaveManager.Settings.BestTimeMedium = _gameTimeSeconds; } 
        if (diff == 2) { if (SaveManager.Settings.BestTimeHard == 0 || _gameTimeSeconds < SaveManager.Settings.BestTimeHard) SaveManager.Settings.BestTimeHard = _gameTimeSeconds; } 
        SaveManager.SaveSettings(); 
    }

    private void CheckFlips(GameState stateBefore) { for (int col = 0; col < 10; col++) { int oldCount = stateBefore.Tableau[col].FaceDownCount; int newCount = _gameState.Tableau[col].FaceDownCount; if (newCount < oldCount && _gameState.Tableau[col].Count > 0) _flipAnims.Add(new FlipAnimation { Col = col, Index = newCount, Time = 0 }); } }

    protected override void Draw(GameTime gameTime)
    {
        Color bgColor = _currentTheme == Theme.Dark ? new Color(25, 25, 30) : new Color(0, 100, 0); 
        
        GraphicsDevice.SetRenderTarget(_renderTarget);
        GraphicsDevice.Clear(bgColor); 
        _spriteBatch.Begin(samplerState: SamplerState.LinearClamp);
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
                DrawCenteredText("Paused", 180, Color.White, 1.5f);
                _btnPauseResume.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouseState); 
                _btnPauseRestart.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouseState);
                _btnPauseSettings.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouseState); 
                _btnPauseMainMenu.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouseState); 
                break;
        }
        if (_activeAchievementPopup != null) DrawAchievementPopup(_activeAchievementPopup); 
        _spriteBatch.End(); 
        
        GraphicsDevice.SetRenderTarget(null);
        GraphicsDevice.Clear(Color.Black);
        _spriteBatch.Begin(samplerState: SamplerState.LinearClamp);
        _spriteBatch.Draw(_renderTarget, _renderDestination, Color.White);
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

    private void DrawGenerating(GameTime gameTime, MouseState mouse) { DrawCenteredText("Generating", 400, Color.White, 1.0f); string attemptText = $"{Localization.Get("Attempt")}: {_generationAttempts}"; DrawCenteredText(attemptText, 470, new Color(200, 200, 200), 0.6f); int dots = (int)(gameTime.TotalGameTime.TotalSeconds * 2) % 4; DrawCenteredText(new string('.', dots), 520, new Color(200, 200, 200), 0.8f); }

    private void DrawMainMenu(MouseState mouse, GameTime gameTime)
    {
        float pulse = 2.35f + (float)Math.Sin(gameTime.TotalGameTime.TotalSeconds * 2.5f) * 0.15f; string titleText = Localization.Get("Title"); Vector2 size = _font.MeasureString(titleText) * (_fontScale * pulse); _spriteBatch.DrawString(_font, titleText, new Vector2((1500 - size.X) / 2, 50), Color.Gold, 0f, Vector2.Zero, _fontScale * pulse, SpriteEffects.None, 0f);
        _btnNewGame.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse); _btnContinue.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse); if (HasAnyAchievement()) _btnAchievements.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse); 
        _btnLeaderboard.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse);
        _btnSettings.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse); _btnExit.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse);
        string difficultyName = Localization.Get("Easy"); string statsText = $"{Localization.Get("Wins")}: {SaveManager.Settings.Wins}  |  {Localization.Get("BestTime")} ({difficultyName}): {FormatTime(SaveManager.Settings.BestTimeEasy)}"; _spriteBatch.DrawString(_font, statsText, new Vector2(25, 925), Color.White, 0f, Vector2.Zero, _fontScale, SpriteEffects.None, 0f);
    }

    private void DrawNewGameMenu(MouseState mouse) { 
        DrawCenteredText("NewGame", 120, Color.White, 0.9f); 
        _selectorSuits.Draw(_spriteBatch, _pixel, _font, mouse, _currentTheme == Theme.Dark); 
        _selectorDifficulty.Draw(_spriteBatch, _pixel, _font, mouse, _currentTheme == Theme.Dark); 
        _selectorMode.Draw(_spriteBatch, _pixel, _font, mouse, _currentTheme == Theme.Dark);
        _btnStart.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse); 
        _btnBackNewGame.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse); 
    }

    private void DrawSettingsMenu(MouseState mouse)
    {
        DrawCenteredText("Settings", 60, Color.White, 0.9f); _selectorLanguage.Draw(_spriteBatch, _pixel, _font, mouse, _currentTheme == Theme.Dark); _sliderVolume.Draw(_spriteBatch, _pixel, _font, _currentTheme == Theme.Dark); _selectorTableStyle.Draw(_spriteBatch, _pixel, _font, mouse, _currentTheme == Theme.Dark); _selectorCardBack.Draw(_spriteBatch, _pixel, _font, mouse, _currentTheme == Theme.Dark);
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
        _btnInGameMenu.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse); _btnUndo.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse); _btnHint.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse);
        
        string timeText = $"{Localization.Get("Time")}: {FormatTime(_gameTimeSeconds)}"; 
        Color timeColor = Color.White;
        if (_gameState.Mode == GameMode.Blitz && _gameTimeSeconds <= 30) timeColor = Color.Red;
        Vector2 timeSize = _font.MeasureString(timeText) * _fontScale; 
        _spriteBatch.DrawString(_font, timeText, new Vector2((1500 - timeSize.X) / 2, 15), timeColor, 0f, Vector2.Zero, _fontScale, SpriteEffects.None, 0f);

        if (_gameState.Mode == GameMode.Endless) {
            string loopsText = $"{Localization.Get("Loops")}: {_gameState.EndlessLoops}";
            Vector2 loopsSize = _font.MeasureString(loopsText) * _fontScale;
            _spriteBatch.DrawString(_font, loopsText, new Vector2((1500 - loopsSize.X) / 2, 50), Color.Gold, 0f, Vector2.Zero, _fontScale, SpriteEffects.None, 0f);
        }

        if (_lastDealIsFallback) _spriteBatch.DrawString(_font, Localization.Get("FallbackDeal"), new Vector2(1050, 120), Color.OrangeRed, 0f, Vector2.Zero, _fontScale * 0.7f, SpriteEffects.None, 0f);
        
        float sourceBlend = 0f; float targetBlend = 0f; if (_hintState == HintState.ShowSource) sourceBlend = MathHelper.Clamp(_hintTimer / 0.5f, 0f, 1f); if (_hintState == HintState.ShowTarget) targetBlend = MathHelper.Clamp((_hintTimer - 0.5f) / 0.5f, 0f, 1f);
        bool isCollectingActive = _animations.Any(a => a.IsCollecting); int completedToDraw = _gameState.CompletedSequences; if (isCollectingActive) completedToDraw--; for (int i = 0; i < completedToDraw; i++) DrawCard(new Card(Suit.Spades, Rank.Ace), CollectedX + (i * 18), CollectedY, false, 1.0f);
        
        for (int col = 0; col < 10; col++) {
            var pile = _gameState.Tableau[col]; int slotX = 50 + col * (CardWidth + CardSpacing); int slotY = 50;
            if (pile.IsEmpty) { var slotRect = new Rectangle(slotX, slotY, CardWidth, CardHeight); _spriteBatch.Draw(_hintMask, slotRect, Color.Black * 0.2f); _spriteBatch.Draw(_borderBlack2, slotRect, Color.Black * 0.6f); }
            
            int dragEndIndex = _isDragging && col == _dragFromColumn ? GameRules.GetSequenceEnd(pile, _dragFromIndex) : -1;

            for (int i = 0; i < pile.Count; i++) {
                if (_isDragging && col == _dragFromColumn && i >= _dragFromIndex && i <= dragEndIndex) continue; 
                if (_hiddenCards.Contains((col, i))) continue;
                
                bool isFaceDown = i < pile.FaceDownCount;
                
                float darkenFactor = 1.0f;
                if (!isFaceDown && _heldColumn == col && !GameRules.CanPickUp(pile, i)) darkenFactor = 0.5f;

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
            float pulse = (float)Math.Sin(gameTime.TotalGameTime.TotalSeconds * 10) * 0.5f + 0.5f; Color hintColor = _currentTheme == Theme.Dark ? Color.DeepSkyBlue : Color.Gold;
            if (_isHintStock && _gameState.Stock.Count >= 10) { int dealsLeft = _gameState.Stock.Count / 10; int topCardX = StockX - (dealsLeft - 1) * 3; int topCardY = StockY - (dealsLeft - 1) * 3; var rect = new Rectangle(topCardX, topCardY, CardWidth, CardHeight); _spriteBatch.Draw(_hintMask, rect, hintColor * (targetBlend * (0.2f + 0.2f * pulse))); _spriteBatch.Draw(_hintBorderThick, rect, hintColor * (targetBlend * (0.8f + 0.2f * pulse))); }
            else if (!_isHintStock) { var toPile = _gameState.Tableau[_hintMove.ToColumn]; var pos = toPile.IsEmpty ? GetCardPosition(_hintMove.ToColumn, 0) : GetCardPosition(_hintMove.ToColumn, toPile.Count - 1); var rect = new Rectangle(pos.X, pos.Y, CardWidth, CardHeight); _spriteBatch.Draw(_hintMask, rect, hintColor * (targetBlend * (0.3f + 0.2f * pulse))); _spriteBatch.Draw(_hintBorderThick, rect, hintColor * (targetBlend * (0.8f + 0.2f * pulse))); }
        }
        
        foreach (var anim in _animations) {
            if (anim.Delay > 0) continue; float progress = MathHelper.Clamp(anim.Time / anim.Duration, 0f, 1f); Vector2 currentPos; if (anim.IsArc) { Vector2 mid = (anim.StartPos + anim.EndPos) / 2; mid.Y -= 200; currentPos = Vector2.Lerp(Vector2.Lerp(anim.StartPos, mid, progress), Vector2.Lerp(mid, anim.EndPos, progress), progress); } else currentPos = Vector2.Lerp(anim.StartPos, anim.EndPos, progress); DrawCard(anim.Card, (int)currentPos.X, (int)currentPos.Y, false, 1.0f, false, 0f, false, gameTime);
        }
        
        if (_isDragging) {
            var pile = _gameState.Tableau[_dragFromColumn]; 
            int endIndex = GameRules.GetSequenceEnd(pile, _dragFromIndex);
            
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
            DrawCenteredText(stuckText, 400, _currentTheme == Theme.Dark ? Color.IndianRed : Color.Red, 1.0f); 
            _btnStuckUndo.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse); _btnStuckRestart.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse); _btnStuckMenu.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse); 
        }
        
        if (_showTutorial) { _spriteBatch.Draw(_pixel, new Rectangle(0, 0, 1500, 1000), new Color(0, 0, 0, 220)); DrawCenteredText("TutorialTitle", 200, Color.Gold, 1.5f); string[] lines = Localization.Get("TutorialText").Split('\n'); int startY = 320; foreach (var line in lines) { if (string.IsNullOrWhiteSpace(line)) { startY += 20; continue; } DrawCenteredTextRaw(line, startY, Color.White, 0.6f); startY += 40; } float pulse = (float)Math.Sin(gameTime.TotalGameTime.TotalSeconds * 3) * 0.5f + 0.5f; DrawCenteredText("TutorialClick", 850, Color.Gold * (0.5f + 0.5f * pulse), 0.8f); }
        DrawParticles();
    }

    private void DrawParticles() { foreach (var p in _particles) { float alpha = MathHelper.Clamp(p.Life / p.MaxLife, 0f, 1f); _spriteBatch.Draw(_pixel, new Rectangle((int)p.Position.X, (int)p.Position.Y, (int)p.Size, (int)p.Size), p.Color * alpha); } }

    private bool IsGameStuck() { if (_gameState.IsWon || _animations.Count > 0 || _isDragging) return false; if (_gameState.Mode == GameMode.Blitz && _gameTimeSeconds <= 0) return true; if (_gameState.Stock.Count >= 10) return false; var allMoves = GameEngine.EnumerateMoves(_gameState).ToList(); foreach (var m in allMoves) { var fromPile = _gameState.Tableau[m.FromColumn]; var toPile = _gameState.Tableau[m.ToColumn]; bool opensFaceDown = (m.FromIndex == fromPile.FaceDownCount && fromPile.FaceDownCount > 0); bool emptiesColumn = (m.FromIndex == 0); if (!toPile.IsEmpty && m.FromIndex > 0 && !opensFaceDown && !emptiesColumn) { var cardUnder = fromPile.Cards[m.FromIndex - 1]; if (cardUnder.Rank == toPile.TopCard.Rank) continue; } return false; } return true; }

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
            int x = 50 + col * (CardWidth + CardSpacing); if (mousePoint.X >= x - HitboxPaddingX && mousePoint.X <= x + CardWidth + HitboxPaddingX && mousePoint.Y >= 50) {
                var stateBefore = _gameState.Clone(); int prevCompleted = _gameState.CompletedSequences; int cardsMoved = GameRules.GetSequenceEnd(stateBefore.Tableau[_dragFromColumn], _dragFromIndex) - _dragFromIndex + 1; bool success = GameEngine.TryMove(_gameState, _dragFromColumn, _dragFromIndex, col);
                if (success) {
                    _undoStack.Add(stateBefore); 
                    if (_undoStack.Count > 30) _undoStack.RemoveAt(0);
                    _cardMoveSound.Play(1.0f, cardsMoved > 1 ? -0.2f : 0.0f, 0f);
                    if (_gameState.CompletedSequences > prevCompleted) { _dealCollectSound.Play(); AddCollectAnimation(GameEngine.LastCollectedColumn, _gameState.CompletedSequences - 1, stateBefore); }
                    else {
                        var toPile = _gameState.Tableau[col]; int startIndexInTarget = toPile.Count - cardsMoved;
                        for (int i = 0; i < cardsMoved; i++) { var card = toPile.Cards[startIndexInTarget + i]; var endPos = GetCardPosition(col, startIndexInTarget + i); var startPos = _dragPos + new Vector2(0, i * FaceUpOffset); _animations.Add(new CardAnimation { Card = card, StartPos = startPos, EndPos = new Vector2(endPos.X, endPos.Y), Duration = 0.2f, Time = 0, Delay = 0, TargetCol = col, TargetIndex = startIndexInTarget + i, IsBounceBack = false }); _hiddenCards.Add((col, startIndexInTarget + i)); }
                    }
                    CheckFlips(stateBefore); SaveGame(); _dragFromColumn = -1; _dragFromIndex = -1;
                } else BounceBack();
                return;
            }
        }
        BounceBack();
    }

    private void BounceBack()
    {
        _wrongSound.Play(); var pile = _gameState.Tableau[_dragFromColumn];
        int endIndex = GameRules.GetSequenceEnd(pile, _dragFromIndex);
        for (int i = _dragFromIndex; i <= endIndex; i++) {
            var originalPos = GetCardPosition(_dragFromColumn, i); var startPos = _dragPos + new Vector2(0, (i - _dragFromIndex) * FaceUpOffset);
            _animations.Add(new CardAnimation { Card = pile.Cards[i], StartPos = startPos, EndPos = new Vector2(originalPos.X, originalPos.Y), Duration = 0.2f, Time = 0, Delay = 0, TargetCol = _dragFromColumn, TargetIndex = i, IsBounceBack = true }); _hiddenCards.Add((_dragFromColumn, i));
        }
        _dragFromColumn = -1; _dragFromIndex = -1;
    }

    private void AddCollectAnimation(int colIndex, int seqIndex, GameState? stateBefore = null)
    {
        int targetX = CollectedX + (seqIndex * 18); int startIndex = 0; if (stateBefore != null) startIndex = Math.Max(0, stateBefore.Tableau[colIndex].Count - 13);
        for (int i = 0; i < 13; i++) {
            var startPos = stateBefore != null ? GetCardPosition(colIndex, startIndex + i, stateBefore) : new Point(50 + colIndex * (CardWidth + CardSpacing), 50 + i * 19);
            _animations.Add(new CardAnimation { Card = new Card(Suit.Spades, Rank.Ace), StartPos = new Vector2(startPos.X, startPos.Y), EndPos = new Vector2(targetX, CollectedY), Duration = 0.25f, Time = 0, Delay = i * 0.03f, TargetCol = -1, TargetIndex = -1, IsCollecting = true });
        }
        SpawnParticles(new Vector2(targetX + CardWidth/2, CollectedY + CardHeight/2), 30, Color.Gold);
    }

    private void PerformUndo()
    {
        _usedUndoThisGame = true; if (_undoStack.Count == 0) return; _hintState = HintState.None; _lastHint = new Move(-1, -1, -1); _stuckSoundPlayed = false;
        var prevState = _undoStack[^1]; 
        _undoStack.RemoveAt(_undoStack.Count - 1);
        var currentState = _gameState; _gameState = prevState; GameEngine.Reset(); _animations.Clear(); _hiddenCards.Clear(); _flipAnims.Clear();
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
        Color backColor = _currentTheme == Theme.Dark ? new Color(50, 50, 70) : new Color(30, 60, 120); Color faceColor = _currentTheme == Theme.Dark ? new Color(220, 220, 220) : Color.White;
        if (isFaceDown) { 
            Texture2D? backTex = _currentCardBack switch { 
                1 => _cardBack2, 2 => _cardBack3, 3 => _cardBackMinimalist, 4 => _cardBackSpace, 5 => _cardBackGolden, _ => _cardBack1 
            }; 
            if (backTex != null) _spriteBatch.Draw(backTex, rect, Color.White); else _spriteBatch.Draw(_hintMask, rect, backColor); _spriteBatch.Draw(_borderBlack2, rect, Color.Black); 
        }
        else {
            _spriteBatch.Draw(_hintMask, rect, faceColor);
            if (width > 20) { Color baseTextColor = (card.Suit == Suit.Hearts || card.Suit == Suit.Diamonds) ? (_currentTheme == Theme.Dark ? Color.Crimson : Color.Red) : Color.Black; string suitStr = GetSuitSymbol(card.Suit); float centerScale = 0.5f * scaleX; var centerSize = _fontSuit.MeasureString(suitStr) * centerScale; var centerPos = new Vector2(x + (width - centerSize.X) / 2, y + (CardHeight - centerSize.Y) / 2); _spriteBatch.DrawString(_fontSuit, suitStr, centerPos, baseTextColor, 0f, Vector2.Zero, centerScale, SpriteEffects.None, 0f); }
            _spriteBatch.Draw(_borderBlack2, rect, Color.Black);
        }
        if (darken < 1.0f) _spriteBatch.Draw(_hintMask, rect, Color.Black * (1.0f - darken));
    }

    private void DrawCard(Card card, int x, int y, bool isFaceDown, float darken = 1.0f, bool highlight = false, float hintBlend = 0f, bool isHovered = false, GameTime? gameTime = null)
    {
        var rect = new Rectangle(x, y, CardWidth, CardHeight); Color faceColor = _currentTheme == Theme.Dark ? new Color(220, 220, 220) : Color.White;
        if (isFaceDown) { 
            Texture2D? backTex = _currentCardBack switch { 
                1 => _cardBack2, 2 => _cardBack3, 3 => _cardBackMinimalist, 4 => _cardBackSpace, 5 => _cardBackGolden, _ => _cardBack1 
            }; 
            if (backTex != null) _spriteBatch.Draw(backTex, rect, Color.White); else _spriteBatch.Draw(_hintMask, rect, new Color(30,60,120)); _spriteBatch.Draw(_borderBlack2, rect, Color.Black); 
        }
        else {
            _spriteBatch.Draw(_hintMask, rect, faceColor);
            Color baseTextColor = (card.Suit == Suit.Hearts || card.Suit == Suit.Diamonds) ? (_currentTheme == Theme.Dark ? Color.Crimson : Color.Red) : Color.Black;
            string rankStr = GetRankString(card.Rank); string suitStr = GetSuitSymbol(card.Suit);

            float centerScale = 0.5f; var centerSize = _fontSuit.MeasureString(suitStr) * centerScale; var centerPos = new Vector2(x + (CardWidth - centerSize.X) / 2, y + (CardHeight - centerSize.Y) / 2); _spriteBatch.DrawString(_fontSuit, suitStr, centerPos, baseTextColor, 0f, Vector2.Zero, centerScale, SpriteEffects.None, 0f);

            var tlPos = new Vector2(x + 8, y + 4); var rankSizeTL = _font.MeasureString(rankStr) * _fontScale; _spriteBatch.DrawString(_font, rankStr, tlPos, baseTextColor, 0f, Vector2.Zero, _fontScale, SpriteEffects.None, 0f); var tlSuitPos = new Vector2(tlPos.X, tlPos.Y + rankSizeTL.Y - 2); _spriteBatch.DrawString(_fontSuit, suitStr, tlSuitPos, baseTextColor, 0f, Vector2.Zero, 0.3f, SpriteEffects.None, 0f);

            var brPos = new Vector2(x + CardWidth - 8, y + CardHeight - 4); var rankSizeBR = _font.MeasureString(rankStr) * _fontScale; _spriteBatch.DrawString(_font, rankStr, brPos, baseTextColor, MathF.PI, Vector2.Zero, _fontScale, SpriteEffects.None, 0f); var brSuitPos = new Vector2(brPos.X, brPos.Y - rankSizeBR.Y + 2); _spriteBatch.DrawString(_fontSuit, suitStr, brSuitPos, baseTextColor, MathF.PI, Vector2.Zero, 0.3f, SpriteEffects.None, 0f);

            if (highlight) _spriteBatch.Draw(_borderGold4, rect, Color.Gold);
            else if (isHovered && gameTime != null && !_isDragging) { float pulse = (float)Math.Sin(gameTime.TotalGameTime.TotalSeconds * 6) * 0.5f + 0.5f; _spriteBatch.Draw(_borderYellow3, rect, Color.LightGoldenrodYellow * (0.5f + 0.5f * pulse)); }
            else _spriteBatch.Draw(_borderBlack2, rect, Color.Black);

            if (hintBlend > 0f && gameTime != null) { float pulse = (float)Math.Sin(gameTime.TotalGameTime.TotalSeconds * 10) * 0.5f + 0.5f; Color hintColor = _currentTheme == Theme.Dark ? Color.DeepSkyBlue : Color.Gold; _spriteBatch.Draw(_hintMask, rect, hintColor * (hintBlend * (0.3f + 0.2f * pulse))); _spriteBatch.Draw(_hintBorderThick, rect, hintColor * (hintBlend * (0.8f + 0.2f * pulse))); }
        }
        if (darken < 1.0f) _spriteBatch.Draw(_hintMask, rect, Color.Black * (1.0f - darken));
    }
}