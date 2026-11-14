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


}
