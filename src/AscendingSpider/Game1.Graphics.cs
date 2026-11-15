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

namespace AscendingSpider;

public partial class Game1
{

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        UpdateRenderDestination();

        _pixel = new Texture2D(GraphicsDevice, 1, 1); _pixel.SetData(new[] { Color.White });
        RefreshTableBackdrop();
        _starTexture = CreateStarTexture(30 * TextureQualityScale);
        _fontLatin = Content.Load<SpriteFont>("OpenSans"); _fontLatinItalic = Content.Load<SpriteFont>("OpenSansItalic"); _fontCjk = Content.Load<SpriteFont>("NotoSansSC"); _fontSuit = Content.Load<SpriteFont>("Arial"); _fontSuitLarge = Content.Load<SpriteFont>("ArialLarge");
        UpdateFonts();

        _clickSound = Content.Load<SoundEffect>("Sounds/audio_click"); _hoverButtonSound = Content.Load<SoundEffect>("Sounds/audio_hover_button"); _hoverCardSound = Content.Load<SoundEffect>("Sounds/audio_hover_card"); _cardMoveSound = Content.Load<SoundEffect>("Sounds/audio_card_move"); _dealCollectSound = Content.Load<SoundEffect>("Sounds/audio_deal_collect"); _winSound = Content.Load<SoundEffect>("Sounds/audio_win");
        try { _stuckSound = Content.Load<SoundEffect>("Sounds/audio_stuck"); } catch (ContentLoadException) { _stuckSound = _winSound; }
        try { _wrongSound = Content.Load<SoundEffect>("Sounds/audio_wrong"); } catch (ContentLoadException) { _wrongSound = _clickSound; }
        try { _dealDistSound = Content.Load<SoundEffect>("Sounds/audio_deal_dist"); } catch (ContentLoadException) { _dealDistSound = _cardMoveSound; }
        _dealDistSoundInstance?.Dispose();
        _dealDistSoundInstance = _dealDistSound.CreateInstance();
        _dealDistSoundInstance.IsLooped = false;

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

                // Fine, low-contrast diagonal lattice adds texture to the felt
                // without competing with cards or reducing text readability.
                int diagonalA = (x + y) % 112;
                int diagonalB = (x - y + 1120) % 112;
                bool patternLine = Math.Abs(diagonalA - 56) <= 1
                    || Math.Abs(diagonalB - 56) <= 1;
                float pattern = patternLine ? 3.2f : 0f;

                pixels[y * width + x] = new Color(
                    ClampColorChannel(palette.Table.R * (1f + light) + grain + pattern),
                    ClampColorChannel(palette.Table.G * (1f + light) + grain + pattern),
                    ClampColorChannel(palette.Table.B * (1f + light) + grain + pattern),
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
        const int x = 625;
        const int spacing = 78;
        const int width = 250;
        const int height = 68;

        int buttonCount = 5 + (HasAnyAchievement() ? 1 : 0);
        int totalHeight = height + (buttonCount - 1) * spacing;

        // Center the complete button column inside the lower panel.
        const int panelY = 305;
        const int panelHeight = 535;

        int firstY = panelY + (panelHeight - totalHeight) / 2;

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
            || SaveManager.Settings.AchSpeedrun || SaveManager.Settings.AchHardMode 
            || SaveManager.Settings.AchDedicated;
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

    private Texture2D CreateClassicCardBack()
    {
        int scale = TextureQualityScale;
        int width = CardWidth * scale;
        int height = CardHeight * scale;
        var tex = new Texture2D(GraphicsDevice, width, height);
        var data = new Color[width * height];
        Color paper = new Color(246, 247, 242);
        Color blue = new Color(42, 104, 166);
        Color red = new Color(190, 55, 68);
        Color border = new Color(30, 67, 105);
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            if (!IsInsideRoundedRect(x, y, width, height, 10 * scale))
            { data[y * width + x] = Color.Transparent; continue; }

            int inset = 5 * scale;
            bool frame = !IsInsideRoundedRect(x - inset, y - inset,
                width - inset * 2, height - inset * 2, 7 * scale);
            // Closely spaced diagonal bands alternate between traditional blue and red.
            int stripeBand = ((x + y) / (5 * scale)) % 2;
            Color stripeColor = stripeBand == 0 ? blue : red;
            bool showStripe = ((x + y) % (10 * scale)) < (4 * scale);
            Color fill = showStripe ? stripeColor : paper;
            if (frame) fill = border;
            data[y * width + x] = fill;
        }
        tex.SetData(data);
        return tex;
    }

    private Texture2D CreateBurgundyCardBack(Color burgundy, Color accent)
    {
        int scale = TextureQualityScale;
        int width = CardWidth * scale;
        int height = CardHeight * scale;
        var tex = new Texture2D(GraphicsDevice, width, height);
        var data = new Color[width * height];
        Color deep = MixColors(burgundy, new Color(36, 12, 25), 0.26f);
        Color rose = MixColors(accent, new Color(205, 139, 151), 0.30f);
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            if (!IsInsideRoundedRect(x, y, width, height, 10 * scale))
            { data[y * width + x] = Color.Transparent; continue; }

            int localX = x % (24 * scale);
            int localY = y % (24 * scale);
            int diamondDistance = Math.Abs(localX - 12 * scale) + Math.Abs(localY - 12 * scale);
            bool diamond = Math.Abs(diamondDistance - 10 * scale) <= scale;
            bool center = Math.Abs(localX - 12 * scale) <= scale
                && Math.Abs(localY - 12 * scale) <= scale;
            bool fineLine = (localX == 0 || localY == 0);
            Color color = deep;
            if (diamond || fineLine) color = MixColors(deep, rose, 0.72f);
            if (center) color = MixColors(rose, Color.White, 0.16f);

            bool innerFrame = !IsInsideRoundedRect(x - 7 * scale, y - 7 * scale,
                width - 14 * scale, height - 14 * scale, 5 * scale);
            if (innerFrame) color = MixColors(deep, rose, 0.58f);
            data[y * width + x] = color;
        }
        tex.SetData(data);
        return tex;
    }

    private Texture2D CreateMinimalistCardBack(Color borderColor, Color faceColor)
    {
        int scale = TextureQualityScale;
        int width = CardWidth * scale;
        int height = CardHeight * scale;
        var tex = new Texture2D(GraphicsDevice, width, height);
        var data = new Color[width * height];
        Color line = MixColors(borderColor, faceColor, 0.48f);
        float cx = width * 0.5f;
        float cy = height * 0.5f;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            if (!IsInsideRoundedRect(x, y, width, height, 10 * scale))
            { data[y * width + x] = Color.Transparent; continue; }
            bool outerFrame = !IsInsideRoundedRect(x - 5 * scale, y - 5 * scale,
                width - 10 * scale, height - 10 * scale, 6 * scale);
            bool innerFrame = !IsInsideRoundedRect(x - 10 * scale, y - 10 * scale,
                width - 20 * scale, height - 20 * scale, 4 * scale);
            int dx = Math.Abs(x - (int)cx);
            int dy = Math.Abs(y - (int)cy);
            bool centerDiamond = Math.Abs(dx - dy) <= scale * 2
                && dx + dy < 17 * scale;
            bool centerDot = dx <= 2 * scale && dy <= 2 * scale;
            bool sideRule = (Math.Abs(x - 18 * scale) <= scale
                || Math.Abs(x - (width - 18 * scale)) <= scale)
                && y > 30 * scale && y < height - 30 * scale;
            Color color = faceColor;
            if (outerFrame || innerFrame || sideRule) color = line;
            if (centerDiamond) color = MixColors(faceColor, borderColor, 0.62f);
            if (centerDot) color = borderColor;
            data[y * width + x] = color;
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
        var data = new Color[width * height];
        float centerX = width * 0.5f;
        float centerY = height * 0.5f;
        float maxRadius = MathF.Sqrt(centerX * centerX + centerY * centerY);
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            if (!IsInsideRoundedRect(x, y, width, height, 10 * scale))
            { data[y * width + x] = Color.Transparent; continue; }
            float dx = x - centerX;
            float dy = y - centerY;
            float radius = MathF.Sqrt(dx * dx + dy * dy) / maxRadius;
            float glow = MathF.Max(0f, 1f - radius * 1.65f);
            glow = glow * glow * 0.58f;
            float grain = ((x * 17 + y * 31) % (7 * scale) == 0) ? 0.10f : 0f;
            bool diagonalEtch = Math.Abs((x + y) % (22 * scale) - 11 * scale) <= scale;
            Color baseTone = MixColors(outerColor, innerColor, 0.42f);
            if (diagonalEtch) baseTone = MixColors(baseTone, Color.White, 0.16f);
            baseTone = MixColors(baseTone, new Color(255, 225, 132), MathHelper.Clamp(glow + grain, 0f, 0.72f));
            bool insetBorder = !IsInsideRoundedRect(x - 7 * scale, y - 7 * scale,
                width - 14 * scale, height - 14 * scale, 5 * scale);
            if (insetBorder) baseTone = MixColors(baseTone, new Color(255, 226, 143), 0.62f);
            data[y * width + x] = baseTone;
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

        Color patternBase = MixColors(palette.Table, palette.Panel, 0.42f);
        Color patternInner = MixColors(palette.PanelAccent, palette.CardBack, 0.30f);

        ReplaceTexture(ref _cardBack1, CreateClassicCardBack());
        Color burgundy = MixColors(new Color(91, 30, 51), palette.CardBack, 0.12f);
        ReplaceTexture(ref _cardBack2, CreateBurgundyCardBack(burgundy, MixColors(palette.Accent, new Color(211, 151, 158), 0.34f)));
        ReplaceTexture(ref _cardBack3, CreateCardBack(patternBase, patternInner, 1));

        // Achievement backs retain their identity (minimal, space, gold), while
        // their colors are gently adapted to the currently selected table theme.
        ReplaceTexture(ref _cardBackMinimalist,
            CreateMinimalistCardBack(
                MixColors(palette.Accent, palette.Panel, 0.42f),
                MixColors(palette.Panel, palette.Table, 0.24f)));
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
        if (SaveManager.Settings.AchHardMode) _selectorCardBack.OptionKeys.Add("Golden");

        int realId = SaveManager.Settings.CardBack;
        int idx = GetSelectorIndexFromCardBackId(realId);
        if (idx == -1)
        {
            // The saved card back is invalid or is no longer unlocked.
            // Fall back to the classic design and persist the correction.
            _currentCardBack = 0;
            SaveManager.Settings.CardBack = 0;
            SaveManager.SaveSettings();
            idx = 0;
        }
        else
        {
            // Restore the actual card-back ID as well as the selector position.
            // The selector index is achievement-dependent and is not the same
            // as the stable card-back ID for unlocked designs.
            _currentCardBack = realId;
        }

        _selectorCardBack.SelectedIndex = idx;
    }

    private int GetCardBackIdFromSelectorIndex(int idx)
    {
        if (idx < 3) return idx;
        int result = 3;
        if (SaveManager.Settings.AchFirstWin) { if (idx == result) return 3; result++; }
        if (SaveManager.Settings.AchNoUndo) { if (idx == result) return 4; result++; }
        if (SaveManager.Settings.AchHardMode) { if (idx == result) return 5; result++; }
        return 0;
    }

    private int GetSelectorIndexFromCardBackId(int id)
    {
        if (id < 3) return id;
        int result = 3;
        if (SaveManager.Settings.AchFirstWin) { if (id == 3) return result; result++; }
        if (SaveManager.Settings.AchNoUndo) { if (id == 4) return result; result++; }
        if (SaveManager.Settings.AchHardMode) { if (id == 5) return result; result++; }
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


}
