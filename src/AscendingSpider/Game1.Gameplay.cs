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
    private void UpdateContinueButton() => _btnContinue.IsEnabled = SaveManager.HasSave();

    private void StartNewGame()
    {
        ClearHintState();
        SaveManager.DeleteSave(); UpdateContinueButton();
        const int suitCount = 1;
        _currentSuit = (Suit)Math.Clamp(_selectorSuit.SelectedIndex, 0, 3);
        _currentDifficulty = _selectorDifficulty.SelectedIndex;
        // One-suit difficulty is controlled only by the number of hidden cards.
        _faceDownPerColumn = _currentDifficulty switch { 0 => 2, 1 => 3, 2 => 5, _ => 3 };
        _currentMode = (GameMode)_selectorMode.SelectedIndex;
        
        _generationAttempts = 0;
        _generationUnsolvableAttempts = 0;
        _generationUnknownAttempts = 0;
        _showDealSolvabilityStatus = false;
        _usedUndoThisGame = false; _usedHintThisGame = false;
        _mixedSuitPlacementUsedThisGame = false;
        _currentScreen = GameScreen.Generating;
        int suitCountLocal = suitCount; int faceDownLocal = _faceDownPerColumn; var modeLocal = _currentMode; var suitLocal = _currentSuit;
        _dealGenerationTask = Task.Run(() => GenerateSolvableParallel(suitCountLocal, faceDownLocal, modeLocal, selectedSuit: suitLocal));
    }

    private GeneratedDeal GenerateSolvableParallel(
        int suitCount,
        int faceDown,
        GameMode mode,
        int maxSeconds = 35,
        Suit selectedSuit = Suit.Spades)
    {
        // Endless has no terminal win condition, so its initial deal cannot be
        // certified by the standard Spider solver.
        if (mode == GameMode.Endless)
        {
            return new GeneratedDeal(
                DeckFactory.Deal(suitCount, faceDown, new Random(), mode, selectedSuit: selectedSuit),
                SolvabilityStatus.Unknown);
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(maxSeconds));

        GameState? solvedDeal = null;
        GameState? bestUnknownDeal = null;
        int bestUnknownScore = int.MaxValue;
        int bestUnknownStates = -1;
        object resultLock = new();

        // Keep CPU and memory use bounded for older integrated-GPU/CPU systems.
        int threadCount = Math.Max(1, Math.Min(Environment.ProcessorCount, 2));
        const int maxStates = 150_000;

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
                        cts.Token.ThrowIfCancellationRequested();

                        GameState candidate = DeckFactory.Deal(suitCount, faceDown, rng, mode, selectedSuit: selectedSuit);
                        Interlocked.Increment(ref _generationAttempts);

                        SolverSearchResult search;
                        try
                        {
                            search = solver.SolveDetailed(candidate, maxStates, cts.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            // If the global timer expired while this candidate
                            // was being searched (rather than another worker
                            // finding a solution), this attempt is Unknown.
                            lock (resultLock)
                            {
                                if (solvedDeal == null)
                                    Interlocked.Increment(ref _generationUnknownAttempts);
                            }
                            return;
                        }

                        if (search.Status == SolvabilityStatus.Solvable)
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
                        {
                            // This candidate was exhaustively searched. It is
                            // safe to discard; unlike Unknown, this is a proof.
                            Interlocked.Increment(ref _generationUnsolvableAttempts);
                            continue;
                        }

                        // The state/time budget was reached. Keep the strongest
                        // partial candidate, but never label it as solved or
                        // proven impossible.
                        Interlocked.Increment(ref _generationUnknownAttempts);
                        lock (resultLock)
                        {
                            bool isBetter =
                                search.BestHeuristicScore < bestUnknownScore
                                || (search.BestHeuristicScore == bestUnknownScore
                                    && search.StatesChecked > bestUnknownStates);

                            if (isBetter)
                            {
                                bestUnknownDeal = candidate.Clone();
                                bestUnknownScore = search.BestHeuristicScore;
                                bestUnknownStates = search.StatesChecked;
                            }
                        }
                    }
                });
        }
        catch (OperationCanceledException)
        {
            // Expected when a certified deal is found or the overall time limit expires.
        }
        catch (AggregateException)
        {
            // Parallel.For may wrap cancellation in AggregateException.
        }

        lock (resultLock)
        {
            if (solvedDeal != null)
                return new GeneratedDeal(solvedDeal, SolvabilityStatus.Solvable);

            if (bestUnknownDeal != null)
                return new GeneratedDeal(bestUnknownDeal, SolvabilityStatus.Unknown);
        }

        // Every candidate may have been proven impossible before the global
        // time limit. A final fresh candidate has not been searched, so its
        // status is explicitly Unknown rather than Unsolvable.
        return new GeneratedDeal(
            DeckFactory.Deal(suitCount, faceDown, new Random(), mode, selectedSuit: selectedSuit),
            SolvabilityStatus.Unknown);
    }

    private void ApplyGeneratedDeal(GeneratedDeal generated)
    {
        _gameState = generated.State;
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
        var loaded = SaveManager.Load();
        if (loaded.HasValue) {
            _gameState = loaded.Value.State;
            _currentSuit = _gameState.SelectedSuit;
            _selectorSuit.SelectedIndex = (int)_currentSuit;
            _faceDownPerColumn = loaded.Value.FaceDownPerColumn;
            _currentDifficulty = _faceDownPerColumn switch { 2 => 0, 3 => 1, _ => 2 };
            _selectorDifficulty.SelectedIndex = _currentDifficulty;
            GameEngine.Reset(); _undoStack.Clear(); _animations.Clear(); _hiddenCards.Clear(); _flipAnims.Clear(); _particles.Clear();
            _hintState = HintState.None; _isDragging = false; _stuckSoundPlayed = false; _currentScreen = GameScreen.Playing;
            double savedGameTime = loaded.Value.CurrentGameTime
                ?? SaveManager.Settings.CurrentGameTime;

            if (_gameState.Mode == GameMode.Blitz)
            {
                // New saves preserve the exact remaining time in save.json.
                // Older saves fall back to settings; when that value was never
                // written, avoid incorrectly resuming an in-progress game at zero.
                _gameTimeSeconds = loaded.Value.CurrentGameTime.HasValue
                    ? Math.Clamp(savedGameTime, 0.0, 180.0)
                    : (savedGameTime > 0.0
                        ? Math.Clamp(savedGameTime, 0.0, 180.0)
                        : 180.0);
            }
            else
            {
                _gameTimeSeconds = Math.Max(0.0, savedGameTime);
            }

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
            SaveManager.Settings.CurrentGameTime = Math.Max(0.0, _gameTimeSeconds);
            SaveManager.Save(
                _gameState,
                _faceDownPerColumn,
                _mixedSuitPlacementUsedThisGame,
                _gameTimeSeconds);
            // Keep the legacy settings field up to date for compatibility with
            // save files created by older versions of the game.
            SaveManager.SaveSettings();
            UpdateContinueButton();
        }
    }

    // Hint search and result handling live in Game1.Hints.cs.

}
