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

public partial class Game1
{
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
            if (width > 20)
            {
                Color baseTextColor = (card.Suit == Suit.Hearts || card.Suit == Suit.Diamonds)
                    ? (palette.IsDark ? Color.Crimson : Color.Red)
                    : Color.Black;
                string rankStr = GetRankString(card.Rank);
                string suitStr = GetSuitSymbol(card.Suit);
                Vector2 suitMeasure = _fontSuitLarge.MeasureString(suitStr);
                float centerScale = Math.Min(1.15f,
                    Math.Min((width - 18f) / Math.Max(1f, suitMeasure.X),
                             (CardHeight - 28f) / Math.Max(1f, suitMeasure.Y)));
                Vector2 centerSize = suitMeasure * centerScale;
                Vector2 centerPos = new Vector2(
                    x + (width - centerSize.X) / 2f,
                    y + (CardHeight - centerSize.Y) / 2f);
                _spriteBatch.DrawString(_fontSuitLarge, suitStr, centerPos, baseTextColor,
                    0f, Vector2.Zero, centerScale, SpriteEffects.None, 0f);

                if (width >= 55)
                {
                    _spriteBatch.DrawString(_font, rankStr, new Vector2(x + 7, y + 4),
                        baseTextColor, 0f, Vector2.Zero, _fontScale, SpriteEffects.None, 0f);
                    float cornerSuitScale = 0.25f;
                    Vector2 cornerSuitSize = suitMeasure * cornerSuitScale;
                    _spriteBatch.DrawString(_fontSuit, suitStr,
                        new Vector2(x + width - 7 - cornerSuitSize.X, y + 4),
                        baseTextColor, 0f, Vector2.Zero, cornerSuitScale, SpriteEffects.None, 0f);
                }
            }
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
            Color baseTextColor = (card.Suit == Suit.Hearts || card.Suit == Suit.Diamonds) ? (GetThemePalette().IsDark ? Color.Crimson : Color.Red) : Color.Black;
            string rankStr = GetRankString(card.Rank); string suitStr = GetSuitSymbol(card.Suit);

            Vector2 suitMeasure = _fontSuitLarge.MeasureString(suitStr);
            float centerScale = Math.Min(1.15f,
                Math.Min((CardWidth - 18f) / Math.Max(1f, suitMeasure.X),
                         (CardHeight - 28f) / Math.Max(1f, suitMeasure.Y)));
            Vector2 centerSize = suitMeasure * centerScale;
            Vector2 centerPos = new Vector2(
                x + (CardWidth - centerSize.X) / 2f,
                y + (CardHeight - centerSize.Y) / 2f);
            _spriteBatch.DrawString(_fontSuitLarge, suitStr, centerPos, baseTextColor,
                0f, Vector2.Zero, centerScale, SpriteEffects.None, 0f);

            _spriteBatch.DrawString(_font, rankStr, new Vector2(x + 8, y + 4),
                baseTextColor, 0f, Vector2.Zero, _fontScale, SpriteEffects.None, 0f);
            const float cornerSuitScale = 0.25f;
            Vector2 cornerSuitSize = suitMeasure * cornerSuitScale;
            _spriteBatch.DrawString(_fontSuit, suitStr,
                new Vector2(x + CardWidth - 8 - cornerSuitSize.X, y + 4),
                baseTextColor, 0f, Vector2.Zero, cornerSuitScale, SpriteEffects.None, 0f);

            if (highlight) _spriteBatch.Draw(_borderGold4, rect, Color.Gold);
            else if (isHovered && gameTime != null && !_isDragging) { float pulse = (float)Math.Sin(gameTime.TotalGameTime.TotalSeconds * 6) * 0.5f + 0.5f; _spriteBatch.Draw(_borderYellow3, rect, Color.LightGoldenrodYellow * (0.5f + 0.5f * pulse)); }
            else _spriteBatch.Draw(_borderBlack2, rect, new Color((byte)36, (byte)47, (byte)42, (byte)150));

            if (hintBlend > 0f && gameTime != null) { float pulse = (float)Math.Sin(gameTime.TotalGameTime.TotalSeconds * 10) * 0.5f + 0.5f; Color hintColor = palette.Hint; _spriteBatch.Draw(_hintMask, rect, hintColor * (hintBlend * (0.3f + 0.2f * pulse))); _spriteBatch.Draw(_hintBorderThick, rect, hintColor * (hintBlend * (0.8f + 0.2f * pulse))); }
        }
        if (darken < 1.0f) _spriteBatch.Draw(_hintMask, rect, Color.Black * (1.0f - darken));
    }

}
