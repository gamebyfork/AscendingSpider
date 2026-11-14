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


}
