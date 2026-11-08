using System;
using System.Linq;
using System.Collections.Generic;

namespace AscendingSpider.Domain;

public readonly record struct Move(int FromColumn, int FromIndex, int ToColumn);

public static class GameEngine
{
    public static int LastCollectedColumn { get; private set; } = -1;

    public static void Reset()
    {
        LastCollectedColumn = -1;
    }

    public static bool TryMove(GameState state, int fromColumn, int fromIndex, int toColumn)
    {
        if (fromColumn == toColumn) return false;
        if (fromColumn < 0 || fromColumn >= 10) return false;
        if (toColumn < 0 || toColumn >= 10) return false;

        var from = state.Tableau[fromColumn];
        var to = state.Tableau[toColumn];

        if (!GameRules.CanPickUp(from, fromIndex)) return false;

        var movingCard = from.Cards[fromIndex];
        if (!to.IsEmpty && !GameRules.CanPlaceOn(movingCard, to.TopCard)) return false;

        int endIndex = GameRules.GetSequenceEnd(from, fromIndex);
        int count = endIndex - fromIndex + 1;
        
        var cardsToMove = from.Cards.Skip(fromIndex).Take(count).ToList();
        
        for (int i = 0; i < count; i++)
            from.RemoveAt(fromIndex);

        foreach (var card in cardsToMove)
            to.AddFaceUp(card);

        TryCollectSequence(state, fromColumn);
        TryCollectSequence(state, toColumn);

        return true;
    }

    public static bool TryDealFromStock(GameState state)
    {
        if (state.Stock.Count < 10) return false;
        
        state.DealsMade++;

        for (int col = 0; col < 10; col++)
        {
            var card = state.Stock[^1];
            state.Stock.RemoveAt(state.Stock.Count - 1);

            state.Tableau[col].AddFaceUp(card);
            TryCollectSequence(state, col);
        }
        return true;
    }

    public static bool HasAnyMoves(GameState state)
    {
        if (state.Stock.Count >= 10) return true;

        for (int fromCol = 0; fromCol < 10; fromCol++)
        {
            var from = state.Tableau[fromCol];

            for (int start = from.FaceDownCount; start < from.Count; start++)
            {
                if (!GameRules.CanPickUp(from, start)) continue;
                var card = from.Cards[start];

                for (int toCol = 0; toCol < 10; toCol++)
                {
                    if (fromCol == toCol) continue;
                    var to = state.Tableau[toCol];

                    if (to.IsEmpty) return true;
                    if (GameRules.CanPlaceOn(card, to.TopCard)) return true;
                }
            }
        }
        return false;
    }

    public static IEnumerable<Move> EnumerateMoves(GameState state)
    {
        for (int fromCol = 0; fromCol < 10; fromCol++)
        {
            var from = state.Tableau[fromCol];

            for (int start = from.FaceDownCount; start < from.Count; start++)
            {
                if (!GameRules.CanPickUp(from, start)) continue;
                var card = from.Cards[start];

                for (int toCol = 0; toCol < 10; toCol++)
                {
                    if (fromCol == toCol) continue;
                    var to = state.Tableau[toCol];

                    if (to.IsEmpty || GameRules.CanPlaceOn(card, to.TopCard))
                        yield return new Move(fromCol, start, toCol);
                }
            }
        }
    }

    private static void TryCollectSequence(GameState state, int column)
    {
        var pile = state.Tableau[column];
        if (!GameRules.HasCompleteSequence(pile)) return;

        for (int i = 0; i < 13; i++)
            pile.RemoveTop();
        
        state.CompletedSequences++;
        LastCollectedColumn = column;

        // Логика Бесконечного режима
        if (state.CompletedSequences == 8 && state.Mode == GameMode.Endless)
        {
            state.CompletedSequences = 0;
            state.EndlessLoops++;
            var extraDeck = DeckFactory.CreateDeck(state.SuitCount);
            DeckFactory.Shuffle(extraDeck, new Random());
            state.Stock.AddRange(extraDeck.Take(50)); // Выдаём 5 новых раздач
        }
    }
}