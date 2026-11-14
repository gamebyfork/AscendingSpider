#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace AscendingSpider;

public class UIButton
{
    public Rectangle Rect;
    public string TextKey = "";
    public Action? OnClick;
    public bool IsEnabled = true;
    public Color BaseColor = new Color(40, 130, 70);
    public Color HoverColor = new Color(60, 180, 100);
    private bool _isHovered = false;

    public void Update(MouseState mouse, MouseState prevMouse, SoundEffect? clickSound, SoundEffect? hoverSound)
    {
        if (!IsEnabled) return;

        bool hover = Rect.Contains(mouse.Position);
        if (hover && !_isHovered && hoverSound != null) hoverSound.Play();
        _isHovered = hover;

        if (mouse.LeftButton == ButtonState.Released && prevMouse.LeftButton == ButtonState.Pressed)
        {
            if (hover)
            {
                clickSound?.Play();
                OnClick?.Invoke();
            }
        }
    }

    public void Draw(SpriteBatch sb, Texture2D pixel, SpriteFont font, SpriteFont italicFont, MouseState mouse)
    {
        bool hover = IsEnabled && Rect.Contains(mouse.Position);
        Color color = IsEnabled ? (hover ? HoverColor : BaseColor) : new Color(75, 78, 76);
        Color edge = IsEnabled ? Color.Lerp(color, new Color(232, 211, 158), hover ? 0.78f : 0.48f) : new Color(105, 108, 105);
        int lift = hover ? 2 : 0;
        Rectangle face = new Rectangle(Rect.X, Rect.Y - lift, Rect.Width, Rect.Height);

        // Soft offset shadow, solid face and restrained brass edge.
        sb.Draw(pixel, new Rectangle(face.X + 3, face.Y + 5, face.Width, face.Height), new Color(0, 0, 0, 72));
        sb.Draw(pixel, face, color);
        sb.Draw(pixel, new Rectangle(face.X + 2, face.Y + 2, face.Width - 4, 2), Color.White * (hover ? 0.20f : 0.10f));
        DrawBorder(sb, pixel, face, edge, 2);

        string text = Localization.Get(TextKey);
        SpriteFont currentFont = IsEnabled ? font : italicFont;
        float textScale = 0.25f;
        Vector2 measured = currentFont.MeasureString(text);
        if (measured.X > 0f)
            textScale = Math.Min(textScale, Math.Max(0.12f, (face.Width - 24f) / measured.X));
        Vector2 size = measured * textScale;
        Vector2 pos = new Vector2(face.X + (face.Width - size.X) / 2f, face.Y + (face.Height - size.Y) / 2f - 2f);
        Color textColor = IsEnabled ? new Color(250, 246, 230) : new Color(165, 165, 158);
        sb.DrawString(currentFont, text, pos + new Vector2(0, 1), Color.Black * 0.35f, 0f, Vector2.Zero, textScale, SpriteEffects.None, 0f);
        sb.DrawString(currentFont, text, pos, textColor, 0f, Vector2.Zero, textScale, SpriteEffects.None, 0f);
    }

    private void DrawBorder(SpriteBatch sb, Texture2D pixel, Rectangle rect, Color color, int thickness)
    {
        sb.Draw(pixel, new Rectangle(rect.Left, rect.Top, rect.Width, thickness), color);
        sb.Draw(pixel, new Rectangle(rect.Left, rect.Bottom - thickness, rect.Width, thickness), color * 0.72f);
        sb.Draw(pixel, new Rectangle(rect.Left, rect.Top, thickness, rect.Height), color * 0.82f);
        sb.Draw(pixel, new Rectangle(rect.Right - thickness, rect.Top, thickness, rect.Height), color * 0.82f);
    }
}

public class UISelector
{
    public Rectangle Rect;
    public string LabelKey = "";
    public List<string> OptionKeys;
    public int SelectedIndex = 0;
    public Action<int>? OnChanged;

    private Rectangle _leftArrowRect;
    private Rectangle _rightArrowRect;
    private bool _isHoveredL = false;
    private bool _isHoveredR = false;

    public UISelector(Rectangle rect, string labelKey, List<string> optionKeys)
    {
        Rect = rect;
        LabelKey = labelKey;
        OptionKeys = optionKeys;
        _leftArrowRect = new Rectangle(Rect.X, Rect.Y, 40, Rect.Height);
        _rightArrowRect = new Rectangle(Rect.Right - 40, Rect.Y, 40, Rect.Height);
    }

    public void Update(MouseState mouse, MouseState prevMouse, SoundEffect? clickSound, SoundEffect? hoverSound)
    {
        bool hoverL = _leftArrowRect.Contains(mouse.Position);
        bool hoverR = _rightArrowRect.Contains(mouse.Position);

        if (hoverL && !_isHoveredL && hoverSound != null) hoverSound.Play();
        if (hoverR && !_isHoveredR && hoverSound != null) hoverSound.Play();
        _isHoveredL = hoverL;
        _isHoveredR = hoverR;

        if (mouse.LeftButton == ButtonState.Released && prevMouse.LeftButton == ButtonState.Pressed)
        {
            if (hoverL)
            {
                SelectedIndex = (SelectedIndex - 1 + OptionKeys.Count) % OptionKeys.Count;
                clickSound?.Play();
                OnChanged?.Invoke(SelectedIndex);
            }
            else if (hoverR)
            {
                SelectedIndex = (SelectedIndex + 1) % OptionKeys.Count;
                clickSound?.Play();
                OnChanged?.Invoke(SelectedIndex);
            }
        }
    }

    public void Draw(SpriteBatch sb, Texture2D pixel, SpriteFont font, MouseState mouse, bool isDarkTheme)
    {
        Draw(sb, pixel, font, mouse,
            isDarkTheme ? new Color(40, 40, 55) : new Color(30, 60, 30),
            isDarkTheme ? new Color(100, 150, 220) : new Color(60, 180, 100));
    }

    public void Draw(SpriteBatch sb, Texture2D pixel, SpriteFont font, MouseState mouse, Color panelColor, Color accentColor)
    {
        string label = Localization.Get(LabelKey);
        float labelScale = 0.25f;
        Vector2 labelMeasured = font.MeasureString(label);
        if (labelMeasured.X > 0f)
            labelScale = Math.Min(labelScale, Math.Max(0.12f, (Rect.Width - 12f) / labelMeasured.X));
        sb.DrawString(font, label, new Vector2(Rect.X, Rect.Y - 43), new Color(232, 224, 203), 0f, Vector2.Zero, labelScale, SpriteEffects.None, 0f);

        Rectangle face = Rect;
        sb.Draw(pixel, new Rectangle(face.X + 3, face.Y + 5, face.Width, face.Height), new Color(0, 0, 0, 58));
        sb.Draw(pixel, face, panelColor);
        DrawBorder(sb, pixel, face, Color.Lerp(accentColor, Color.White, 0.28f), 2);
        sb.Draw(pixel, new Rectangle(face.X + 2, face.Y + 2, face.Width - 4, 2), Color.White * 0.10f);

        bool hoverL = _leftArrowRect.Contains(mouse.Position);
        bool hoverR = _rightArrowRect.Contains(mouse.Position);
        Color arrowLeft = hoverL ? Color.White : accentColor;
        Color arrowRight = hoverR ? Color.White : accentColor;
        sb.Draw(pixel, new Rectangle(_leftArrowRect.Right - 2, face.Y + 12, 1, face.Height - 24), accentColor * 0.35f);
        sb.Draw(pixel, new Rectangle(_rightArrowRect.X + 1, face.Y + 12, 1, face.Height - 24), accentColor * 0.35f);

        var arrowMeasured = font.MeasureString("<");
        float arrowScale = 0.25f;
        float arrowY = Rect.Y + (Rect.Height - arrowMeasured.Y * arrowScale) / 2f - 1f;
        sb.DrawString(font, "<", new Vector2(_leftArrowRect.X + 13, arrowY), arrowLeft, 0f, Vector2.Zero, arrowScale, SpriteEffects.None, 0f);
        sb.DrawString(font, ">", new Vector2(_rightArrowRect.X + 13, arrowY), arrowRight, 0f, Vector2.Zero, arrowScale, SpriteEffects.None, 0f);

        if (OptionKeys.Count == 0) return;
        int safeIndex = Math.Clamp(SelectedIndex, 0, OptionKeys.Count - 1);
        string textKey = OptionKeys[safeIndex];
        string text = Localization.Get(textKey) == textKey ? textKey : Localization.Get(textKey);
        Vector2 measured = font.MeasureString(text);
        float textScale = 0.25f;
        float contentWidth = Math.Max(20f, Rect.Width - 100f);
        if (measured.X > 0f)
            textScale = Math.Min(textScale, Math.Max(0.12f, contentWidth / measured.X));
        Vector2 size = measured * textScale;
        float contentCenterX = Rect.X + Rect.Width / 2f;
        Vector2 textPos = new Vector2(contentCenterX - size.X / 2f, Rect.Y + (Rect.Height - size.Y) / 2f - 1f);
        sb.DrawString(font, text, textPos + new Vector2(0, 1), Color.Black * 0.3f, 0f, Vector2.Zero, textScale, SpriteEffects.None, 0f);
        sb.DrawString(font, text, textPos, new Color(250, 247, 235), 0f, Vector2.Zero, textScale, SpriteEffects.None, 0f);
    }

    private void DrawBorder(SpriteBatch sb, Texture2D pixel, Rectangle rect, Color color, int thickness)
    {
        sb.Draw(pixel, new Rectangle(rect.Left, rect.Top, rect.Width, thickness), color);
        sb.Draw(pixel, new Rectangle(rect.Left, rect.Bottom - thickness, rect.Width, thickness), color);
        sb.Draw(pixel, new Rectangle(rect.Left, rect.Top, thickness, rect.Height), color);
        sb.Draw(pixel, new Rectangle(rect.Right - thickness, rect.Top, thickness, rect.Height), color);
    }
}

public class UISlider
{
    public Rectangle Rect;
    public string LabelKey = "";
    public float Value;
    public Action<float>? OnChanged;
    
    private bool _isDragging = false;

    public UISlider(Rectangle rect, string labelKey, float initialValue)
    {
        Rect = rect;
        LabelKey = labelKey;
        Value = initialValue;
    }

    public void Update(MouseState mouse, MouseState prevMouse)
    {
        if (mouse.LeftButton == ButtonState.Pressed && prevMouse.LeftButton == ButtonState.Released)
        {
            var hitArea = new Rectangle(Rect.X - 10, Rect.Y - 15, Rect.Width + 20, Rect.Height + 30);
            if (hitArea.Contains(mouse.Position)) _isDragging = true;
        }
        
        if (mouse.LeftButton == ButtonState.Released) _isDragging = false;

        if (_isDragging)
        {
            Value = MathHelper.Clamp((float)(mouse.X - Rect.X) / Rect.Width, 0f, 1f);
            OnChanged?.Invoke(Value);
        }
    }

    public void Draw(SpriteBatch sb, Texture2D pixel, SpriteFont font, bool isDarkTheme)
    {
        Draw(sb, pixel, font,
            isDarkTheme ? new Color(40, 40, 55) : new Color(30, 60, 30),
            isDarkTheme ? new Color(100, 150, 220) : new Color(60, 180, 100));
    }

    public void Draw(SpriteBatch sb, Texture2D pixel, SpriteFont font, Color panelColor, Color accentColor)
    {
        string label = Localization.Get(LabelKey);
        string labelText = $"{label}: {(int)(Value * 100)}%";
        float labelScale = 0.25f;
        Vector2 labelSize = font.MeasureString(labelText);
        if (labelSize.X > 0f)
            labelScale = Math.Min(labelScale, Math.Max(0.12f, (Rect.Width - 12f) / labelSize.X));
        sb.DrawString(font, labelText, new Vector2(Rect.X, Rect.Y - 43), new Color(232, 224, 203), 0f, Vector2.Zero, labelScale, SpriteEffects.None, 0f);

        sb.Draw(pixel, new Rectangle(Rect.X + 3, Rect.Y + 5, Rect.Width, Rect.Height), new Color(0, 0, 0, 58));
        sb.Draw(pixel, Rect, panelColor);
        int fillWidth = (int)(Rect.Width * MathHelper.Clamp(Value, 0f, 1f));
        if (fillWidth > 0) sb.Draw(pixel, new Rectangle(Rect.X + 2, Rect.Y + 2, Math.Max(0, fillWidth - 4), Rect.Height - 4), accentColor * 0.88f);
        DrawBorder(sb, pixel, Rect, Color.Lerp(accentColor, Color.White, 0.28f), 2);

        int handleX = Rect.X + fillWidth - 6;
        sb.Draw(pixel, new Rectangle(handleX + 2, Rect.Y - 4, 12, Rect.Height + 8), new Color(0, 0, 0, 65));
        sb.Draw(pixel, new Rectangle(handleX, Rect.Y - 5, 12, Rect.Height + 10), new Color(246, 239, 218));
        sb.Draw(pixel, new Rectangle(handleX + 3, Rect.Y - 2, 6, Rect.Height + 4), accentColor);
    }

    private void DrawBorder(SpriteBatch sb, Texture2D pixel, Rectangle rect, Color color, int thickness)
    {
        sb.Draw(pixel, new Rectangle(rect.Left, rect.Top, rect.Width, thickness), color);
        sb.Draw(pixel, new Rectangle(rect.Left, rect.Bottom - thickness, rect.Width, thickness), color);
        sb.Draw(pixel, new Rectangle(rect.Left, rect.Top, thickness, rect.Height), color);
        sb.Draw(pixel, new Rectangle(rect.Right - thickness, rect.Top, thickness, rect.Height), color);
    }
}