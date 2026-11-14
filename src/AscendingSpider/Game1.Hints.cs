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
            _hintMessageKey = string.Empty;
            _hintMessageTimer = 0f;
            return;
        }

        _hintCandidateIndex = (_hintCandidateIndex + 1) % _hintCandidates.Count;
        HintCandidate candidate = _hintCandidates[_hintCandidateIndex];
        _hintMove = candidate.Move;
        _isHintStock = candidate.IsStockDeal;
        _hintTimer = 0f;
        _hintState = HintState.ShowTarget;

        // Hint feedback is purely visual: do not display informational text
        // in the top HUD when suggesting a stock deal or when no move is found.
        _hintMessageKey = string.Empty;
        _hintMessageTimer = 0f;
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
        if (!SaveManager.Settings.AchHardMode && _currentDifficulty == 2) { SaveManager.Settings.AchHardMode = true; _achievementQueue.Enqueue("AchHardMode"); unlockedNewCardBack = true; }
        if (!SaveManager.Settings.AchDedicated && SaveManager.Settings.Wins >= 10) { SaveManager.Settings.AchDedicated = true; _achievementQueue.Enqueue("AchDedicated"); }
        // Perfect Order: complete a game without dealing any cards from the stock.
        if (!SaveManager.Settings.AchPerfectOrder && _gameState.DealsMade == 0)
        {
            SaveManager.Settings.AchPerfectOrder = true;
            _achievementQueue.Enqueue("AchPerfectOrder");
        }
        
        SaveManager.SaveSettings(); 
        if (wasEmpty && HasAnyAchievement()) RecalcMainMenuButtons();
        if (unlockedNewCardBack) RefreshCardBackSelector();
    }


}
