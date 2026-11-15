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
        _currentSuit = (Suit)Math.Clamp(_selectorSuit.SelectedIndex, 0, 3);
        _currentDifficulty = _selectorDifficulty.SelectedIndex;
        // One-suit difficulty is controlled only by the number of hidden cards.
        _faceDownPerColumn = _currentDifficulty switch { 0 => 2, 1 => 3, 2 => 5, _ => 3 };
        _currentMode = (GameMode)_selectorMode.SelectedIndex;
        
        _usedUndoThisGame = false; _usedHintThisGame = false;
        _currentScreen = GameScreen.Generating;
        int faceDownLocal = _faceDownPerColumn; var modeLocal = _currentMode; var suitLocal = _currentSuit;
        _dealGenerationTask = Task.Run(() => GenerateSolvableParallel(faceDownLocal, modeLocal, selectedSuit: suitLocal));
    }

    private GameState GenerateSolvableParallel(
        int faceDown,
        GameMode mode,
        int maxSeconds = 35,
        Suit selectedSuit = Suit.Spades)
    {
        // Endless has no terminal win condition, so its initial deal cannot be
        // certified by the standard Spider solver.
        if (mode == GameMode.Endless)
        {
            return DeckFactory.Deal(faceDown, new Random(), mode, selectedSuit: selectedSuit);
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

                        GameState candidate = DeckFactory.Deal(faceDown, rng, mode, selectedSuit: selectedSuit);

                        SolverSearchResult search;
                        try
                        {
                            search = solver.SolveDetailed(candidate, maxStates, cts.Token);
                        }
                        catch (OperationCanceledException)
                        {
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
                            continue;
                        }

                        // The state/time budget was reached. Keep the strongest
                        // partial candidate, but never label it as solved or
                        // proven impossible.
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
                return solvedDeal;

            if (bestUnknownDeal != null)
                return bestUnknownDeal;
        }

        // No candidate was proven solvable within the time budget. Use a fresh
        // deal rather than blocking the UI or claiming it is unsolvable.
        return DeckFactory.Deal(faceDown, new Random(), mode, selectedSuit: selectedSuit);
    }

    private void ApplyGeneratedDeal(GameState generated)
    {
        _gameState = generated;

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

        _dealDistSoundInstance?.Stop();
        _dealDistSoundStarted = false;

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
            // Продолжение восстанавливает уже разложенную партию без анимации раздачи.
            // Считаем однократный звук раздачи уже использованным для этой сессии.
            _dealDistSoundInstance?.Stop();
            _dealDistSoundStarted = true;

            _gameState = loaded.Value.State;
            _currentSuit = _gameState.SelectedSuit;
            _currentMode = _gameState.Mode;
            _selectorMode.SelectedIndex = (int)_currentMode;
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
            _usedUndoThisGame = loaded.Value.UsedUndoThisGame;
            _usedHintThisGame = loaded.Value.UsedHintThisGame;
            // Старые сохранения без этого поля считаются неподходящими для
            // достижения: по ним невозможно достоверно узнать историю ходов.
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
                _gameTimeSeconds,
                _usedUndoThisGame,
                _usedHintThisGame);
            // Keep the legacy settings field up to date for compatibility with
            // save files created by older versions of the game.
            SaveManager.SaveSettings();
            UpdateContinueButton();
        }
        else if (_gameState?.IsWon == true)
        {
            // Never leave a pre-win checkpoint available to the Continue button.
            SaveManager.DeleteSave();
            UpdateContinueButton();
        }
    }

    // Hint search and result handling live in Game1.Hints.cs.

}
