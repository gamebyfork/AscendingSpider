using System;
#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

public partial class Game1
{
    protected override void Draw(GameTime gameTime)
    {
        // Recalculate from the actual back buffer after fullscreen/resolution changes.
        UpdateRenderDestination();

        ThemePalette palette = GetThemePalette();
        Color bgColor = palette.Table;

        // Fill the physical back buffer, then cover it with an aspect-preserving
        // copy of the felt texture. This extends the table artwork into the
        // widescreen margins instead of leaving flat-color side bars.
        GraphicsDevice.Clear(bgColor);
        Rectangle backdropSource = new Rectangle(0, 0, 1500, 1000);
        if (_tableBackdrop != null)
        {
            int backBufferWidth = GraphicsDevice.PresentationParameters.BackBufferWidth;
            int backBufferHeight = GraphicsDevice.PresentationParameters.BackBufferHeight;
            float coverScale = Math.Max(backBufferWidth / 1500f, backBufferHeight / 1000f);
            int coverWidth = (int)MathF.Ceiling(1500f * coverScale);
            int coverHeight = (int)MathF.Ceiling(1000f * coverScale);
            var coverRect = new Rectangle(
                (backBufferWidth - coverWidth) / 2,
                (backBufferHeight - coverHeight) / 2,
                coverWidth,
                coverHeight);

            // Draw the same cover crop inside the 3:2 game viewport so the
            // texture joins seamlessly at the edges of the widescreen margins.
            int sourceX = Math.Clamp(
                (int)MathF.Round((_renderDestination.X - coverRect.X) / coverScale), 0, 1499);
            int sourceY = Math.Clamp(
                (int)MathF.Round((_renderDestination.Y - coverRect.Y) / coverScale), 0, 999);
            int sourceWidth = Math.Clamp(
                (int)MathF.Round(_renderDestination.Width / coverScale), 1, 1500 - sourceX);
            int sourceHeight = Math.Clamp(
                (int)MathF.Round(_renderDestination.Height / coverScale), 1, 1000 - sourceY);
            backdropSource = new Rectangle(sourceX, sourceY, sourceWidth, sourceHeight);

            _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            _spriteBatch.Draw(_tableBackdrop, coverRect, Color.White);
            _spriteBatch.End();
        }

        float scaleX = _renderDestination.Width / 1500f;
        float scaleY = _renderDestination.Height / 1000f;
        Matrix screenTransform = Matrix.CreateScale(scaleX, scaleY, 1f)
            * Matrix.CreateTranslation(_renderDestination.X, _renderDestination.Y, 0f);

        // PointClamp disables texture interpolation. Pixels are sampled directly
        // instead of blended with neighboring texels, keeping cards and UI crisp.
        // MSAA is already disabled in the GraphicsDeviceManager configuration.
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: screenTransform);
        if (_tableBackdrop != null)
            _spriteBatch.Draw(_tableBackdrop, new Rectangle(0, 0, 1500, 1000), backdropSource, Color.White);
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

        // Modal screens shade the logical 3:2 game area while they are drawn.
        // Extend that same shade into the physical back-buffer margins so the
        // dimmed background covers the entire monitor in fullscreen/widescreen.
        bool fullscreenShade = _currentScreen == GameScreen.Paused
            || (_currentScreen == GameScreen.Playing
                && ((_gameState.IsWon && _animations.Count == 0) || IsGameStuck() || _showTutorial));

        if (fullscreenShade)
        {
            int backBufferWidth = GraphicsDevice.PresentationParameters.BackBufferWidth;
            int backBufferHeight = GraphicsDevice.PresentationParameters.BackBufferHeight;
            byte shadeAlpha = _currentScreen == GameScreen.Paused
                ? (byte)176
                : (_showTutorial && _currentScreen == GameScreen.Playing ? (byte)220 : (byte)184);

            _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            var shade = new Color(0, 0, 0, (int)shadeAlpha);
            if (_renderDestination.X > 0)
                _spriteBatch.Draw(_pixel, new Rectangle(0, 0, _renderDestination.X, backBufferHeight), shade);
            int rightStart = _renderDestination.Right;
            if (rightStart < backBufferWidth)
                _spriteBatch.Draw(_pixel, new Rectangle(rightStart, 0, backBufferWidth - rightStart, backBufferHeight), shade);
            if (_renderDestination.Y > 0)
                _spriteBatch.Draw(_pixel, new Rectangle(0, 0, backBufferWidth, _renderDestination.Y), shade);
            int bottomStart = _renderDestination.Bottom;
            if (bottomStart < backBufferHeight)
                _spriteBatch.Draw(_pixel, new Rectangle(0, bottomStart, backBufferWidth, backBufferHeight - bottomStart), shade);
            _spriteBatch.End();
        }

        base.Draw(gameTime);
    }

    private void DrawLeaderboard(MouseState mouse)
    {
        ThemePalette palette = GetThemePalette();
        DrawPanel(new Rectangle(390, 75, 720, 850), palette.Panel, palette.Accent, 0.94f);
        DrawScreenHeading("Leaderboard", 90, 0.9f);

        // Both lists can contain ten entries. Fixed row heights keep the second
        // list and the Back button from colliding, even when both lists are full.
        DrawCenteredText("Standard", 170, Color.Gold, 1.0f);
        DrawLeaderboardRows(SaveManager.Settings.LeaderboardStandard, 205);

        DrawCenteredText("Blitz", 475, Color.Gold, 1.0f);
        DrawLeaderboardRows(SaveManager.Settings.LeaderboardBlitz, 510);

        _btnBackLeaderboard.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse);
    }

    private void DrawLeaderboardRows(IReadOnlyList<double> scores, int startY)
{
    if (scores.Count == 0)
    {
        DrawCenteredText("NoScores", startY, Color.Gray, 0.7f);
        return;
    }

    int visibleCount = Math.Min(scores.Count, 10);
    for (int i = 0; i < visibleCount; i++)
    {
        string text = $"{i + 1}. {FormatTime(scores[i])}";
        DrawCenteredTextRaw(text, startY + i * 27, Color.White, 0.7f);
    }
}

    private void DrawAchievementPopup(AchievementPopup popup) { float alpha = MathHelper.Clamp(popup.Timer / 0.5f, 0f, 1f); int y = 130; var bgRect = new Rectangle(1050, y, 420, 80); _spriteBatch.Draw(_pixel, bgRect, new Color(0, 0, 0) * (0.85f * alpha)); _spriteBatch.Draw(_pixel, new Rectangle(bgRect.X, bgRect.Y, bgRect.Width, 3), Color.Gold * alpha); _spriteBatch.Draw(_pixel, new Rectangle(bgRect.X, bgRect.Bottom - 3, bgRect.Width, 3), Color.Gold * alpha); _spriteBatch.Draw(_pixel, new Rectangle(bgRect.X, bgRect.Y, 3, bgRect.Height), Color.Gold * alpha); _spriteBatch.Draw(_pixel, new Rectangle(bgRect.Right - 3, bgRect.Y, 3, bgRect.Height), Color.Gold * alpha); var starRect = new Rectangle(bgRect.X + 15, bgRect.Y + 25, 30, 30); _spriteBatch.Draw(_starTexture, starRect, Color.Gold * alpha); string name = Localization.Get(popup.NameKey); string unlocked = Localization.Get("AchUnlocked"); _spriteBatch.DrawString(_font, name, new Vector2(bgRect.X + 60, bgRect.Y + 12), Color.Gold * alpha, 0f, Vector2.Zero, _fontScale, SpriteEffects.None, 0f); _spriteBatch.DrawString(_font, unlocked, new Vector2(bgRect.X + 60, bgRect.Y + 45), Color.White * alpha, 0f, Vector2.Zero, _fontScale * 0.8f, SpriteEffects.None, 0f); }


    private void DrawGenerating(GameTime gameTime, MouseState mouse)
    {
        string label = Localization.Get("Loading");
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
        DrawPanel(new Rectangle(480, 310, 540, 535), palette.Panel, palette.Accent, 0.90f);
        DrawPanel(new Rectangle(480, 145, 540, 125), palette.Panel, palette.Accent, 0.82f);

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
        Vector2 titlePos = new Vector2(
            480f + (540f - titleSize.X) / 2f,
            145f + (125f - titleSize.Y) / 2f
        );
        _spriteBatch.DrawString(_font, titleText, titlePos + new Vector2(2, 3), Color.Black * 0.35f, 0f, Vector2.Zero, titleScale, SpriteEffects.None, 0f);
        _spriteBatch.DrawString(_font, titleText, titlePos, new Color(246, 222, 166), 0f, Vector2.Zero, titleScale, SpriteEffects.None, 0f);

        _btnNewGame.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse);
        _btnContinue.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse);
        if (HasAnyAchievement()) _btnAchievements.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse);
        _btnLeaderboard.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse);
        _btnSettings.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse);
        _btnExit.Draw(_spriteBatch, _pixel, _font, _fontItalic, mouse);

        string winsText = $"{Localization.Get("Wins")}: {SaveManager.Settings.Wins}";
        string bestText = $"{Localization.Get("BestTime")} ({Localization.Get("Easy")}): {FormatTime(SaveManager.Settings.BestTimeEasy)}";
        // Use the regular UI font for the separator. The suit font atlas
        // contains card symbols, but may not contain the bullet glyph.
        const string statsSeparator = "|";
        float statsScale = _fontScale * 0.78f;
        float separatorScale = statsScale;
        float winsWidth = _font.MeasureString(winsText).X * statsScale;
        float separatorWidth = _font.MeasureString(statsSeparator).X * separatorScale;
        float bestWidth = _font.MeasureString(bestText).X * statsScale;
        float spacing = 22f;
        float totalWidth = winsWidth + spacing + separatorWidth + spacing + bestWidth;
        if (totalWidth > 1400f)
        {
            float fit = 1400f / totalWidth;
            statsScale *= fit;
            separatorScale *= fit;
            winsWidth *= fit;
            separatorWidth *= fit;
            bestWidth *= fit;
            spacing *= fit;
            totalWidth = winsWidth + spacing + separatorWidth + spacing + bestWidth;
        }
        float statsX = (1500f - totalWidth) / 2f;
        Color statsColor = new Color(230, 225, 208) * 0.88f;
        _spriteBatch.DrawString(_font, winsText, new Vector2(statsX, 930), statsColor, 0f, Vector2.Zero, statsScale, SpriteEffects.None, 0f);
        float separatorX = statsX + winsWidth + spacing;
        _spriteBatch.DrawString(_font, statsSeparator, new Vector2(separatorX, 930), statsColor, 0f, Vector2.Zero, separatorScale, SpriteEffects.None, 0f);
        float bestX = separatorX + separatorWidth + spacing;
        _spriteBatch.DrawString(_font, bestText, new Vector2(bestX, 930), statsColor, 0f, Vector2.Zero, statsScale, SpriteEffects.None, 0f);

        DrawMainMenuFooter();
    }

    private void DrawMainMenuFooter()
    {
        ThemePalette palette = GetThemePalette();
        float progress = MathHelper.Clamp(_infoTooltipProgress, 0f, 1f);

        // Version comes from the assembly metadata, which is populated from the
        // project's Version/InformationalVersion properties during build/publish.
        string version = typeof(Game1).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(Game1).Assembly.GetName().Version?.ToString(3)
            ?? "0.0.0";
        int metadataSuffix = version.IndexOf('+');
        if (metadataSuffix >= 0)
            version = version[..metadataSuffix];
        version = version.TrimStart('v', 'V');

        string footerText = $"v{version} | game by fork";
        float footerScale = _fontScale * 0.64f;
        Vector2 footerSize = _font.MeasureString(footerText) * footerScale;
        Vector2 footerPosition = new Vector2(1460f - footerSize.X, 966f);
        Color footerColor = new Color(230, 225, 208) * 0.76f;
        _spriteBatch.DrawString(_font, footerText, footerPosition + new Vector2(1, 2), Color.Black * 0.28f,
            0f, Vector2.Zero, footerScale, SpriteEffects.None, 0f);
        _spriteBatch.DrawString(_font, footerText, footerPosition, footerColor,
            0f, Vector2.Zero, footerScale, SpriteEffects.None, 0f);

        // Circular information mark in the lower-left corner.
        Vector2 iconCenter = new Vector2(50f, 977f);
        float iconScale = 1f + progress * 0.07f;
        float radius = 14.5f * iconScale;
        Color iconColor = Color.Lerp(new Color(230, 225, 208), palette.Accent, 0.48f + progress * 0.42f);
        const int segments = 40;
        for (int i = 0; i < segments; i++)
        {
            float a0 = MathHelper.TwoPi * i / segments;
            float a1 = MathHelper.TwoPi * (i + 1) / segments;
            Vector2 start = iconCenter + new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * radius;
            Vector2 end = iconCenter + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * radius;
            Vector2 delta = end - start;
            _spriteBatch.Draw(_pixel, start, null, iconColor, MathF.Atan2(delta.Y, delta.X),
                Vector2.Zero, new Vector2(delta.Length(), 2.1f), SpriteEffects.None, 0f);
        }

        string infoGlyph = "i";
        float glyphScale = _fontScale * 0.94f * iconScale;
        Vector2 glyphSize = _font.MeasureString(infoGlyph) * glyphScale;
        Vector2 glyphPosition = iconCenter - glyphSize * 0.5f + new Vector2(0f, -1f);
        _spriteBatch.DrawString(_font, infoGlyph, glyphPosition, iconColor,
            0f, Vector2.Zero, glyphScale, SpriteEffects.None, 0f);

        if (progress <= 0.001f)
            return;

        string helpHeading;
        (string Key, string Action)[] helpControls;

        switch (Localization.Language)
        {
            case "EN":
                helpHeading = "Controls";
                helpControls = new[]
                {
                    ("F", "Fullscreen"),
                    ("Esc", "Pause / Back"),
                    ("Z", "Undo move"),
                    ("H", "Hint")
                };
                break;
            case "ES":
                helpHeading = "Controles";
                helpControls = new[]
                {
                    ("F", "Pantalla completa"),
                    ("Esc", "Pausa / Volver"),
                    ("Z", "Deshacer"),
                    ("H", "Pista")
                };
                break;
            case "ZH":
                helpHeading = "操作";
                helpControls = new[]
                {
                    ("F", "全屏"),
                    ("Esc", "暂停 / 返回"),
                    ("Z", "撤销"),
                    ("H", "提示")
                };
                break;
            case "JA":
                helpHeading = "操作方法";
                helpControls = new[]
                {
                    ("F", "全画面"),
                    ("Esc", "一時停止 / 戻る"),
                    ("Z", "手を戻す"),
                    ("H", "ヒント")
                };
                break;
            default:
                helpHeading = "Управление";
                helpControls = new[]
                {
                    ("F", "Полный экран"),
                    ("Esc", "Пауза / Назад"),
                    ("Z", "Отменить ход"),
                    ("H", "Подсказка")
                };
                break;
        }

        const float baseWidth = 350f;
        const float baseHeight = 166f;
        float panelScale = 0.93f + 0.07f * progress;
        int panelWidth = (int)(baseWidth * panelScale);
        int panelHeight = (int)(baseHeight * panelScale);
        int panelX = 72;
        int panelY = 950 - panelHeight;
        byte shadowAlpha = (byte)(68f * progress);
        _spriteBatch.Draw(_pixel, new Rectangle(panelX + 7, panelY + 8, panelWidth, panelHeight),
            new Color(0, 0, 0, (int)shadowAlpha));
        _spriteBatch.Draw(_pixel, new Rectangle(panelX, panelY, panelWidth, panelHeight),
            palette.Panel * (0.94f * progress));
        _spriteBatch.Draw(_pixel, new Rectangle(panelX, panelY, panelWidth, 3),
            palette.Accent * (0.9f * progress));
        _spriteBatch.Draw(_pixel, new Rectangle(panelX, panelY + panelHeight - 2, panelWidth, 2),
            palette.Accent * (0.5f * progress));
        _spriteBatch.Draw(_pixel, new Rectangle(panelX, panelY, 2, panelHeight),
            palette.Accent * (0.45f * progress));
        _spriteBatch.Draw(_pixel, new Rectangle(panelX + panelWidth - 2, panelY, 2, panelHeight),
            palette.Accent * (0.45f * progress));

        float textScale = _fontScale * 0.67f * panelScale;
        float headingScale = textScale * 1.12f;
        float textX = panelX + 20f * panelScale;
        float textY = panelY + 13f * panelScale;
        Color headingColor = new Color(246, 222, 166) * progress;
        Color lineColor = new Color(235, 232, 220) * (0.92f * progress);
        _spriteBatch.DrawString(_font, helpHeading, new Vector2(textX, textY), headingColor,
            0f, Vector2.Zero, headingScale, SpriteEffects.None, 0f);
        float separatorY = textY + 27f * panelScale;
        _spriteBatch.Draw(_pixel, new Rectangle((int)textX, (int)separatorY,
            panelWidth - (int)(40f * panelScale), 1), palette.Accent * (0.42f * progress));

        // Two fixed, left-aligned columns: keyboard key and its action.
        // Measuring "Esc" gives the key column enough room in every language.
        float keyColumnWidth = _font.MeasureString("Esc").X * textScale + 16f * panelScale;
        float keyX = textX;
        float actionX = keyX + keyColumnWidth;
        for (int i = 0; i < helpControls.Length; i++)
        {
            float rowY = separatorY + (8f + i * 23f) * panelScale;
            var control = helpControls[i];

            _spriteBatch.DrawString(_font, control.Key, new Vector2(keyX, rowY), lineColor,
                0f, Vector2.Zero, textScale, SpriteEffects.None, 0f);
            _spriteBatch.DrawString(_font, control.Action, new Vector2(actionX, rowY), lineColor,
                0f, Vector2.Zero, textScale, SpriteEffects.None, 0f);
        }
    }

    private void DrawNewGameMenu(MouseState mouse)
    {
        ThemePalette palette = GetThemePalette();
        DrawPanel(new Rectangle(420, 125, 660, 735), palette.Panel, palette.Accent, 0.94f);
        DrawScreenHeading("NewGame", 145, 0.9f);
        _selectorSuit.Draw(_spriteBatch, _pixel, _font, mouse, palette.Panel, palette.PanelAccent);
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
            _spriteBatch.DrawString(_font, Localization.Get(nameKey), new Vector2(430, startY + 2), nameColor, 0f, Vector2.Zero, _fontScale, SpriteEffects.None, 0f);
            _spriteBatch.DrawString(_font, Localization.Get(descKey), new Vector2(430, startY + 29), descColor, 0f, Vector2.Zero, _fontScale * 0.72f, SpriteEffects.None, 0f);
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

            // Draw the rounded column slot before the cards in every column.
            // Cards are rendered afterwards, so the placeholder remains beneath
            // occupied piles and is visible as soon as a column becomes empty.
            // Keep the slot clean: no inner horizontal stripe.
            var slotRect = new Rectangle(slotX, slotY, CardWidth, CardHeight);
            _spriteBatch.Draw(_hintMask, slotRect, hudPalette.CardBack * 0.12f);
            _spriteBatch.Draw(_borderBlack2, slotRect, hudPalette.Accent * 0.48f);
            
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


}
