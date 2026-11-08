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
        Color color = IsEnabled ? (hover ? HoverColor : BaseColor) : new Color(100, 100, 100);

        sb.Draw(pixel, Rect, color);
        DrawBorder(sb, pixel, Rect, Color.White, 2);
        
        string text = Localization.Get(TextKey);
        SpriteFont currentFont = IsEnabled ? font : italicFont;
        
        Vector2 size = currentFont.MeasureString(text) * 0.25f;
        Vector2 pos = new Vector2(Rect.X + (Rect.Width - size.X) / 2, Rect.Y + (Rect.Height - size.Y) / 2 - 2);
        sb.DrawString(currentFont, text, pos, Color.White, 0f, Vector2.Zero, 0.25f, SpriteEffects.None, 0f);
    }

    private void DrawBorder(SpriteBatch sb, Texture2D pixel, Rectangle rect, Color color, int thickness)
    {
        sb.Draw(pixel, new Rectangle(rect.Left, rect.Top, rect.Width, thickness), color);
        sb.Draw(pixel, new Rectangle(rect.Left, rect.Bottom - thickness, rect.Width, thickness), color);
        sb.Draw(pixel, new Rectangle(rect.Left, rect.Top, thickness, rect.Height), color);
        sb.Draw(pixel, new Rectangle(rect.Right - thickness, rect.Top, thickness, rect.Height), color);
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
        string label = Localization.Get(LabelKey);
        sb.DrawString(font, label, new Vector2(Rect.X, Rect.Y - 45), Color.White, 0f, Vector2.Zero, 0.25f, SpriteEffects.None, 0f);

        Color bgColor = isDarkTheme ? new Color(40, 40, 55) : new Color(30, 60, 30);
        sb.Draw(pixel, Rect, bgColor);
        DrawBorder(sb, pixel, Rect, Color.White, 2);

        bool hoverL = _leftArrowRect.Contains(mouse.Position);
        bool hoverR = _rightArrowRect.Contains(mouse.Position);
        
        var arrowSize = font.MeasureString("<") * 0.25f;
        float arrowY = Rect.Y + (Rect.Height - arrowSize.Y) / 2 - 2;
        
        sb.DrawString(font, "<", new Vector2(_leftArrowRect.X + 15, arrowY), hoverL ? Color.Gold : Color.White, 0f, Vector2.Zero, 0.25f, SpriteEffects.None, 0f);
        sb.DrawString(font, ">", new Vector2(_rightArrowRect.X + 15, arrowY), hoverR ? Color.Gold : Color.White, 0f, Vector2.Zero, 0.25f, SpriteEffects.None, 0f);

        string textKey = OptionKeys[SelectedIndex];
        string text = Localization.Get(textKey) == textKey ? textKey : Localization.Get(textKey);

        Vector2 size = font.MeasureString(text) * 0.25f;
        sb.DrawString(font, text, new Vector2(Rect.X + (Rect.Width - size.X) / 2, Rect.Y + (Rect.Height - size.Y) / 2 - 2), Color.White, 0f, Vector2.Zero, 0.25f, SpriteEffects.None, 0f);
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
        string label = Localization.Get(LabelKey);
        sb.DrawString(font, $"{label}: {(int)(Value * 100)}%", new Vector2(Rect.X, Rect.Y - 45), Color.White, 0f, Vector2.Zero, 0.25f, SpriteEffects.None, 0f);

        Color bgColor = isDarkTheme ? new Color(40, 40, 55) : new Color(30, 60, 30);
        sb.Draw(pixel, Rect, bgColor);
        DrawBorder(sb, pixel, Rect, Color.White, 2);

        Color fillColor = isDarkTheme ? new Color(100, 150, 220) : new Color(60, 180, 100);
        int fillWidth = (int)(Rect.Width * Value);
        sb.Draw(pixel, new Rectangle(Rect.X, Rect.Y, fillWidth, Rect.Height), fillColor);

        int handleX = Rect.X + fillWidth - 5;
        sb.Draw(pixel, new Rectangle(handleX, Rect.Y - 5, 10, Rect.Height + 10), Color.White);
    }

    private void DrawBorder(SpriteBatch sb, Texture2D pixel, Rectangle rect, Color color, int thickness)
    {
        sb.Draw(pixel, new Rectangle(rect.Left, rect.Top, rect.Width, thickness), color);
        sb.Draw(pixel, new Rectangle(rect.Left, rect.Bottom - thickness, rect.Width, thickness), color);
        sb.Draw(pixel, new Rectangle(rect.Left, rect.Top, thickness, rect.Height), color);
        sb.Draw(pixel, new Rectangle(rect.Right - thickness, rect.Top, thickness, rect.Height), color);
    }
}