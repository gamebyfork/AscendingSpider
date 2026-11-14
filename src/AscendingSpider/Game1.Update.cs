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
    protected override void Update(GameTime gameTime)
    {
        if (!IsActive) {
            if (_hintState != HintState.None || _hintMessageTimer > 0f) ClearHintState();
            _previousMouseState = Mouse.GetState(); 
            _previousKeyboardState = Keyboard.GetState(); 
            _isDragging = false; 
            _dragFromColumn = -1; 
            _dragFromIndex = -1; 
            _dragHoldTime = 0;
            base.Update(gameTime); 
            return; 
        }
        var kState = Keyboard.GetState(); var mouseState = GetScaledMouseState(); float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;

        // The help tooltip is shown only while the pointer is over the info icon.
        _infoIconHovered = _currentScreen == GameScreen.MainMenu
            && new Rectangle(30, 957, 40, 40).Contains(mouseState.Position);
        float tooltipTarget = _infoIconHovered ? 1f : 0f;
        float tooltipBlend = 1f - MathF.Exp(-deltaTime * 14f);
        _infoTooltipProgress = MathHelper.Lerp(_infoTooltipProgress, tooltipTarget, tooltipBlend);
        if (!_infoIconHovered && _infoTooltipProgress < 0.005f)
            _infoTooltipProgress = 0f;

        if (_activeAchievementPopup != null) { _activeAchievementPopup.Timer -= deltaTime; if (_activeAchievementPopup.Timer <= 0) _activeAchievementPopup = null; }
        else if (_achievementQueue.Count > 0) _activeAchievementPopup = new AchievementPopup { NameKey = _achievementQueue.Dequeue() };
        if (_confirmReset) { _resetConfirmTimer -= deltaTime; if (_resetConfirmTimer <= 0) _confirmReset = false; }
        if (_currentScreen != GameScreen.Settings) _confirmReset = false;
        if (_currentScreen != GameScreen.Playing
            && (_hintState != HintState.None || _hintMessageTimer > 0f))
            ClearHintState();

        if (kState.IsKeyDown(Keys.F) && !_previousKeyboardState.IsKeyDown(Keys.F)) {
            _graphics.ToggleFullScreen();
            UpdateRenderDestination();
        }

        if (kState.IsKeyDown(Keys.Escape) && !_previousKeyboardState.IsKeyDown(Keys.Escape)) {
            if (_currentScreen == GameScreen.Playing && !_gameState.IsWon && !IsGameStuck()) { 
                _currentScreen = GameScreen.Paused; 
            }
            else if (_currentScreen == GameScreen.Paused) {
                _currentScreen = GameScreen.Playing;
            }
            else if (_currentScreen == GameScreen.MainMenu) Exit();
            else if (_currentScreen != GameScreen.Generating) _currentScreen = GameScreen.MainMenu;
        }
        if (_currentScreen == GameScreen.Playing && _isGameActive && !_showTutorial) {
            if (kState.IsKeyDown(Keys.Z) && !_previousKeyboardState.IsKeyDown(Keys.Z)) PerformUndo();
            if (kState.IsKeyDown(Keys.H) && !_previousKeyboardState.IsKeyDown(Keys.H)) TriggerHint();
        }

        _mouseVelocity = mouseState.Position.ToVector2() - _previousMousePos; _previousMousePos = mouseState.Position.ToVector2();
        UpdateParticles(deltaTime); UpdateFlipAnimations(deltaTime);

        switch (_currentScreen) {
            case GameScreen.MainMenu: 
                _btnNewGame.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); 
                _btnContinue.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); 
                if (HasAnyAchievement()) _btnAchievements.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); 
                _btnLeaderboard.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); 
                _btnSettings.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); 
                _btnExit.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); 
                break;
            case GameScreen.NewGameSetup: _selectorSuit.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); _selectorDifficulty.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); _selectorMode.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); _btnStart.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); _btnBackNewGame.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); break;
            case GameScreen.Settings: _selectorLanguage.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); _sliderVolume.Update(mouseState, _previousMouseState); _selectorTableStyle.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); _selectorCardBack.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); _btnResetStats.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); _btnBackSettings.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); break;
            case GameScreen.Achievements: _btnBackAchievements.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); break;
            case GameScreen.Leaderboard: _btnBackLeaderboard.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); break;
            case GameScreen.Generating:
                if (_dealGenerationTask != null && _dealGenerationTask.IsCompleted) {
                    GeneratedDeal generated;
                    try
                    {
                        generated = _dealGenerationTask.Result;
                    }
                    catch
                    {
                        generated = new GeneratedDeal(
                            DeckFactory.Deal(1, _faceDownPerColumn, new Random(), _currentMode, selectedSuit: _currentSuit),
                            SolvabilityStatus.Unknown);
                    }
                    _dealGenerationTask = null;
                    ApplyGeneratedDeal(generated);
                }
                break;
            case GameScreen.Playing: UpdatePlaying(gameTime, mouseState); break;
            case GameScreen.Paused: 
                _btnPauseResume.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); 
                _btnPauseRestart.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound);
                _btnPauseSettings.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); 
                _btnPauseMainMenu.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound); 
                break;
        }

        _previousMouseState = mouseState; _previousKeyboardState = kState; base.Update(gameTime);
    }

    private void UpdateParticles(float dt) { for (int i = _particles.Count - 1; i >= 0; i--) { var p = _particles[i]; p.Position += p.Velocity * dt; p.Velocity.Y += 500f * dt; p.Life -= dt; if (p.Life <= 0) _particles.RemoveAt(i); } }
    private void UpdateFlipAnimations(float dt) { for (int i = _flipAnims.Count - 1; i >= 0; i--) { _flipAnims[i].Time += dt; if (_flipAnims[i].Time >= _flipAnims[i].Duration) _flipAnims.RemoveAt(i); } }

    private void SpawnParticles(Vector2 pos, int count, Color color)
    {
        for (int i = 0; i < count; i++) { float angle = (float)(_rng.NextDouble() * Math.PI * 2); float speed = (float)(_rng.NextDouble() * 200 + 100); _particles.Add(new Particle { Position = pos, Velocity = new Vector2((float)Math.Cos(angle) * speed, (float)Math.Sin(angle) * speed - 100), Life = (float)(_rng.NextDouble() * 0.8 + 0.4), MaxLife = 1.2f, Color = color, Size = (float)(_rng.NextDouble() * 4 + 2) }); }
    }

    private void SpawnConfetti()
    {
        _particles.Add(new Particle { Position = new Vector2(_rng.Next(1500), -20), Velocity = new Vector2((float)(_rng.NextDouble() * 100 - 50), (float)(_rng.NextDouble() * 100 + 50)), Life = 3.0f, MaxLife = 3.0f, Color = _rng.Next(4) switch { 0 => Color.Gold, 1 => Color.Red, 2 => Color.DeepSkyBlue, _ => Color.White }, Size = (float)(_rng.NextDouble() * 6 + 4) });
    }

    private void UpdatePlaying(GameTime gameTime, MouseState mouseState)
    {
        if (_showTutorial) { if (mouseState.LeftButton == ButtonState.Pressed && _previousMouseState.LeftButton == ButtonState.Released) { _showTutorial = false; SaveManager.Settings.HasSeenTutorial = true; SaveManager.SaveSettings(); } return; }
        float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (_gameState.IsWon)
            _isGameActive = false;

        if (_isGameActive) {
            if (_gameState.Mode == GameMode.Blitz) {
                _gameTimeSeconds -= deltaTime;
                if (_gameTimeSeconds <= 0) {
                    _gameTimeSeconds = 0;
                    _isGameActive = false;
                    _stuckSoundPlayed = true;
                    UpdateStats(false);
                    SaveManager.Settings.CurrentGameTime = 0;
                }
            } else {
                _gameTimeSeconds += deltaTime;
            }
        }

        for (int i = _animations.Count - 1; i >= 0; i--) { var anim = _animations[i]; if (anim.Delay > 0) anim.Delay -= deltaTime; else { anim.Time += deltaTime; if (anim.Time >= anim.Duration) { _animations.RemoveAt(i); _hiddenCards.Remove((anim.TargetCol, anim.TargetIndex)); } } }
        if (_currentScreen != GameScreen.Playing) return;
        if (_hintMessageTimer > 0f)
            _hintMessageTimer = Math.Max(0f, _hintMessageTimer - deltaTime);
        if (_hintState != HintState.None)
        {
            _hintTimer += deltaTime;
            if (_hintTimer > 7.0f)
                _hintState = HintState.None;
        }
        bool isStuck = IsGameStuck();
        
        if (_gameState.IsWon) {
            // Freeze the clock and input as soon as the eighth sequence is
            // collected. Let its animations finish before showing the result.
            _isGameActive = false;
            if (_animations.Count > 0)
                return;

            // Process a win exactly once, independent of _isGameActive. A player
            // may undo out of a previous stuck state, which previously left this
            // flag false and silently skipped every achievement check.
            if (!_winHandled) {
                _winHandled = true;
                _winSound.Play();
                UpdateStats(true);
                SaveManager.Settings.CurrentGameTime = 0;
                _winConfettiTimer = 0;
                CheckAchievements();
            }
            if (_winConfettiTimer < 3.0f) { _winConfettiTimer += deltaTime; for (int i = 0; i < 15; i++) SpawnConfetti(); }
            _btnWinRestart.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound);
            _btnWinMenu.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound);
            return;
        } else if (isStuck) {
            if (!_stuckSoundPlayed) { _stuckSound.Play(); _stuckSoundPlayed = true; _isGameActive = false; UpdateStats(false); SaveManager.Settings.CurrentGameTime = 0; }
            bool timeExpired = _gameState.Mode == GameMode.Blitz && _gameTimeSeconds <= 0;
            LayoutStuckButtons(timeExpired);
            if (!timeExpired)
                _btnStuckUndo.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound);
            _btnStuckRestart.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound);
            _btnStuckMenu.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound);
            return;
        }

        _btnInGameMenu.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound);
        _btnUndo.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound);
        _btnHint.Update(mouseState, _previousMouseState, _clickSound, _hoverButtonSound);

        var mousePoint = new Point(mouseState.X, mouseState.Y);
        
        if (!_isDragging && !_animations.Any(a => a.IsBounceBack)) {
            int oldCol = _hoveredCol; int oldIdx = _hoveredIndex; _hoveredCol = -1; _hoveredIndex = -1; _isStockHovered = false;
            if (new Rectangle(StockX - HitboxPaddingX, StockY - HitboxPaddingY, CardWidth + HitboxPaddingX * 2, CardHeight + HitboxPaddingY * 2).Contains(mousePoint)) 
                _isStockHovered = true;
            else 
            { 
                for (int col = 0; col < 10; col++) 
                { 
                    var pile = _gameState.Tableau[col]; 
                    for (int i = pile.Count - 1; i >= 0; i--) 
                    { 
                        if (_hiddenCards.Contains((col, i))) continue; 
                        var pos = GetCardPosition(col, i); 
                        if (new Rectangle(pos.X - HitboxPaddingX, pos.Y - HitboxPaddingY, CardWidth + HitboxPaddingX * 2, CardHeight + HitboxPaddingY * 2).Contains(mousePoint)) 
                        { 
                            _hoveredCol = col; 
                            _hoveredIndex = i; 
                            break; 
                        } 
                    } 
                    if (_hoveredCol != -1) break; 
                } 
            }
            if ((oldCol != _hoveredCol || oldIdx != _hoveredIndex) && _hoveredCol != -1) _hoverCardSound.Play();
        }

        if (_isDragging) { 
            _dragHoldTime += deltaTime;
            Vector2 targetPos = new Vector2(mouseState.X - _dragOffset.X, mouseState.Y - _dragOffset.Y); 
            _dragPos = Vector2.Lerp(_dragPos, targetPos, 0.3f); 
            _dragPos += _mouseVelocity * 0.15f; 
        }

        if (mouseState.LeftButton == ButtonState.Pressed && _previousMouseState.LeftButton == ButtonState.Released && !_isDragging) {
            _heldColumn = -1;
            _heldIndex = -1;
            if (new Rectangle(StockX - HitboxPaddingX, StockY - HitboxPaddingY, CardWidth + HitboxPaddingX * 2, CardHeight + HitboxPaddingY * 2).Contains(mousePoint)) {
                bool hasEmptyColumns = _gameState.Tableau.Any(p => p.IsEmpty);
                if (!hasEmptyColumns && _gameState.Stock.Count >= 10) {
                    _hintState = HintState.None;
                   
                    var stateBefore = _gameState.Clone();
                    _undoStack.Add(stateBefore);
                    if (_undoStack.Count > 30) _undoStack.RemoveAt(0);
                    int previousCompletedInRound = _gameState.CompletedSequences;
                    int previousTotalCompleted = _gameState.TotalCompletedSequences;
                    int previousEndlessLoops = _gameState.EndlessLoops;
                    ClearHintState();
                    GameEngine.TryDealFromStock(_gameState);
                    _dealCollectSound.Play();

                    if (_gameState.TotalCompletedSequences > previousTotalCompleted)
                    {
                        int sequenceIndex = previousCompletedInRound;
                        foreach (var collected in GameEngine.LastCollectedSequences)
                            AddCollectAnimation(collected.Column, sequenceIndex++, collected.Suit, stateBefore);
                    }

                    if (_gameState.EndlessLoops > previousEndlessLoops) {
                        AddTableauDealAnimations();
                    }
                    else {
                        for (int col = 0; col < 10; col++) {
                            var pile = _gameState.Tableau[col];
                            int targetIndex = pile.Count - 1;
                            var endPos = GetCardPosition(col, targetIndex);
                            Card cardToAnimate = pile.Count > 0 ? pile.TopCard : new Card(_gameState.SelectedSuit, Rank.Ace);
                            _animations.Add(new CardAnimation { Card = cardToAnimate, StartPos = new Vector2(StockX, StockY), EndPos = new Vector2(endPos.X, endPos.Y), Duration = 0.35f, Time = 0, Delay = col * 0.05f, IsArc = true, TargetCol = col, TargetIndex = targetIndex, IsCollecting = false });
                            _hiddenCards.Add((col, targetIndex));
                        }
                        CheckFlips(stateBefore);
                    }
                    SaveGame();
                }
            } else TryPickUpCard(mousePoint);
        } else if (mouseState.LeftButton == ButtonState.Released && _previousMouseState.LeftButton == ButtonState.Pressed) { 
            if (_isDragging) { 
                float dist = Vector2.Distance(mousePoint.ToVector2(), _dragStartMousePos.ToVector2());
                if (dist < 10f && _dragHoldTime < 0.25f) {
                    TryAutoMove();
                    _isDragging = false;
                } else {
                    TryDropCard(mousePoint); 
                    _isDragging = false; 
                }
            } 
            _heldColumn = -1;
            _heldIndex = -1;
        }
    }

    // Достижение «Идеальный порядок» доступно в режимах на 2, 3 и 4 масти.
    private static bool IsPerfectOrderEligibleSuitCount(int suitCount)
        => suitCount >= 2 && suitCount <= 4;

    // Проверяет именно ручное действие игрока по исходному состоянию поля.
    // Перемещение на пустую колонку не считается наложением одной масти на другую.
    private static bool IsCrossSuitPlacement(GameState state, int fromColumn, int fromIndex, int toColumn)
    {
        var sourcePile = state.Tableau[fromColumn];
        var targetPile = state.Tableau[toColumn];
        if (targetPile.IsEmpty || fromIndex < 0 || fromIndex >= sourcePile.Count)
            return false;

        return sourcePile.Cards[fromIndex].Suit != targetPile.TopCard.Suit;
    }

    private void TryAutoMove()
    {
        var pile = _gameState.Tableau[_dragFromColumn];
        var card = pile.Cards[_dragFromIndex];
        int bestCol = -1;
        int bestScore = int.MinValue;
        
        for (int col = 0; col < 10; col++) {
            if (col == _dragFromColumn) continue;
            var targetPile = _gameState.Tableau[col];
            if (!targetPile.IsEmpty && GameRules.CanPlaceOn(card, targetPile.TopCard)) {
                int score = 0;
                if (targetPile.TopCard.Suit == card.Suit) score += 100;
                if (score > bestScore) {
                    bestScore = score;
                    bestCol = col;
                }
            }
        }
        
        if (bestCol == -1) {
            for (int col = 0; col < 10; col++) {
                if (col == _dragFromColumn) continue;
                if (_gameState.Tableau[col].IsEmpty) {
                    bestCol = col;
                    break;
                }
            }
        }
        
        if (bestCol != -1) {
            var stateBefore = _gameState.Clone();
            int prevCompleted = _gameState.CompletedSequences;
            int prevTotalCompleted = _gameState.TotalCompletedSequences;
            int prevEndlessLoops = _gameState.EndlessLoops;
            int cardsMoved = GameRules.GetSequenceEnd(stateBefore.Tableau[_dragFromColumn], _dragFromIndex) - _dragFromIndex + 1;
            bool crossSuitPlacement = IsCrossSuitPlacement(stateBefore, _dragFromColumn, _dragFromIndex, bestCol);
            bool success = GameEngine.TryMove(_gameState, _dragFromColumn, _dragFromIndex, bestCol);
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
                    var toPile = _gameState.Tableau[bestCol];
                    int startIndexInTarget = toPile.Count - cardsMoved;
                    for (int i = 0; i < cardsMoved; i++) {
                        var cardToAnim = toPile.Cards[startIndexInTarget + i];
                        var endPos = GetCardPosition(bestCol, startIndexInTarget + i);
                        var startPos = GetCardPosition(_dragFromColumn, _dragFromIndex + i, stateBefore);
                        _animations.Add(new CardAnimation { Card = cardToAnim, StartPos = new Vector2(startPos.X, startPos.Y), EndPos = new Vector2(endPos.X, endPos.Y), Duration = 0.2f, Time = 0, Delay = 0, TargetCol = bestCol, TargetIndex = startIndexInTarget + i, IsBounceBack = false });
                        _hiddenCards.Add((bestCol, startIndexInTarget + i));
                    }
                }

                if (_gameState.EndlessLoops == prevEndlessLoops)
                    CheckFlips(stateBefore); 
                SaveGame();
                _dragFromColumn = -1; 
                _dragFromIndex = -1;
            } else {
                BounceBack();
            }
        } else {
            BounceBack();
        }
    }

    private void UpdateStats(bool won) { 
        if (won) {
            SaveManager.Settings.Wins++; 
            SaveManager.AddScore(_gameState.Mode, _gameState.Mode == GameMode.Blitz ? (180 - _gameTimeSeconds) : _gameTimeSeconds);
        } else {
            SaveManager.Settings.Losses++; 
        }
        int diff = _currentDifficulty; 
        if (diff == 0) { if (SaveManager.Settings.BestTimeEasy == 0 || _gameTimeSeconds < SaveManager.Settings.BestTimeEasy) SaveManager.Settings.BestTimeEasy = _gameTimeSeconds; } 
        if (diff == 1) { if (SaveManager.Settings.BestTimeMedium == 0 || _gameTimeSeconds < SaveManager.Settings.BestTimeMedium) SaveManager.Settings.BestTimeMedium = _gameTimeSeconds; } 
        if (diff == 2) { if (SaveManager.Settings.BestTimeHard == 0 || _gameTimeSeconds < SaveManager.Settings.BestTimeHard) SaveManager.Settings.BestTimeHard = _gameTimeSeconds; } 
        SaveManager.SaveSettings(); 
    }

    private void CheckFlips(GameState stateBefore) { for (int col = 0; col < 10; col++) { int oldCount = stateBefore.Tableau[col].FaceDownCount; int newCount = _gameState.Tableau[col].FaceDownCount; if (newCount < oldCount && _gameState.Tableau[col].Count > 0) _flipAnims.Add(new FlipAnimation { Col = col, Index = newCount, Time = 0 }); } }


}
