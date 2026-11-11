using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace AscendingSpider.Domain;

public enum ProgressReachability
{
    ProgressFound,
    DeadEnd,
    Unknown
}

public readonly record struct Move(int FromColumn, int FromIndex, int ToColumn);

public readonly record struct CollectedSequence(int Column, Suit Suit);

public static class GameEngine
{
    // Deal generation may run several solvers in parallel. Keep this UI event
    // value thread-local so a background solver cannot overwrite the game thread's
    // last collected column.
    [ThreadStatic]
    private static int _lastCollectedColumn = -1;
    [ThreadStatic]
    private static List<CollectedSequence>? _lastCollectedSequences;

    public static int LastCollectedColumn
    {
        get => _lastCollectedColumn;
        private set => _lastCollectedColumn = value;
    }

    // All sequences collected by the most recent player action. More than one
    // sequence can be completed during a stock deal, so preserve every event.
    public static IReadOnlyList<CollectedSequence> LastCollectedSequences
        => _lastCollectedSequences ??= new List<CollectedSequence>();

    private static void ClearLastCollectedSequences()
    {
        LastCollectedColumn = -1;
        _lastCollectedSequences?.Clear();
    }

    public static void Reset() => ClearLastCollectedSequences();

    public static bool TryMove(GameState state, int fromColumn, int fromIndex, int toColumn)
    {
        ClearLastCollectedSequences();
        if (fromColumn == toColumn) return false;
        if (fromColumn < 0 || fromColumn >= 10) return false;
        if (toColumn < 0 || toColumn >= 10) return false;

        var from = state.Tableau[fromColumn];
        var to = state.Tableau[toColumn];

        if (!GameRules.CanPickUp(from, fromIndex, state.Ruleset)) return false;

        var movingCard = from.Cards[fromIndex];
        if (!to.IsEmpty && !GameRules.CanPlaceOn(movingCard, to.TopCard)) return false;

        int endIndex = GameRules.GetSequenceEnd(from, fromIndex, state.Ruleset);
        int count = endIndex - fromIndex + 1;
        var cardsToMove = from.Cards.Skip(fromIndex).Take(count).ToList();

        for (int i = 0; i < count; i++)
            from.RemoveAt(fromIndex);

        foreach (var card in cardsToMove)
            to.AddFaceUp(card);

        int loopsBeforeCollection = state.EndlessLoops;
        TryCollectSequence(state, fromColumn);
        // The eighth sequence in Endless mode starts a completely new deal.
        // Do not inspect the destination column again after that reset.
        if (state.EndlessLoops == loopsBeforeCollection)
            TryCollectSequence(state, toColumn);

        return true;
    }

    public static bool CanDealFromStock(GameState state)
        => state.Stock.Count >= 10 && state.Tableau.All(pile => !pile.IsEmpty);

    public static bool TryDealFromStock(GameState state)
    {
        ClearLastCollectedSequences();
        // Spider rules do not allow a stock deal while any tableau column is empty.
        if (!CanDealFromStock(state)) return false;

        state.DealsMade++;

        int previousEndlessLoops = state.EndlessLoops;
        for (int col = 0; col < 10; col++)
        {
            // TryCollectSequence can begin an entirely new Endless round. In
            // that case the stock and tableau have already been replaced, so
            // do not continue the old ten-card deal against the new round.
            var card = state.Stock[^1];
            state.Stock.RemoveAt(state.Stock.Count - 1);

            state.Tableau[col].AddFaceUp(card);
            TryCollectSequence(state, col);

            if (state.EndlessLoops > previousEndlessLoops)
                break;
        }

        return true;
    }

    /// <summary>
    /// Returns true when at least one legal action exists, including a legal
    /// relocation of a complete face-up pile to an empty column.
    /// </summary>
    public static bool HasAnyMoves(GameState state)
        => EnumerateMoves(state).Any() || CanDealFromStock(state);

    /// <summary>
    /// Returns true only when the player can change the card arrangement in a
    /// way that can help complete sequences, reveal a face-down card, or deal
    /// from stock. Moving an entire face-up pile between two empty columns is
    /// legal, but it only relocates the same pile and cannot resolve a dead end.
    /// </summary>
    public static bool HasAnyProgressMoves(GameState state)
        => EnumerateProgressMoves(state).Any() || CanDealFromStock(state);

    /// <summary>
    /// Checks whether any sequence of legal tableau moves can eventually
    /// reveal a face-down card, collect a sequence, or make a stock deal
    /// available. This catches reversible move cycles that look legal but
    /// cannot advance the game.
    ///
    /// Unknown means the search reached its safety limit; callers must not
    /// treat an inconclusive search as a loss.
    /// </summary>
    public static ProgressReachability CheckReachableProgress(
        GameState initialState,
        int maxStatesToCheck = 50_000,
        CancellationToken cancellationToken = default)
    {
        if (initialState == null) throw new ArgumentNullException(nameof(initialState));
        if (maxStatesToCheck < 1) throw new ArgumentOutOfRangeException(nameof(maxStatesToCheck));

        // Cancellation is a normal control-flow outcome when the player changes
        // the board. Return Unknown instead of throwing OperationCanceledException;
        // this keeps debugger first-chance exception breaks out of normal play.
        if (cancellationToken.IsCancellationRequested)
            return ProgressReachability.Unknown;

        if (initialState.IsWon || CanDealFromStock(initialState))
            return ProgressReachability.ProgressFound;

        var queue = new Queue<GameState>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var initial = initialState.Clone();
        queue.Enqueue(initial);
        visited.Add(GetReachabilityKey(initial));

        while (queue.Count > 0)
        {
            if (cancellationToken.IsCancellationRequested)
                return ProgressReachability.Unknown;

            var current = queue.Dequeue();

            if (current.IsWon || CanDealFromStock(current))
                return ProgressReachability.ProgressFound;

            int faceDownBefore = CountFaceDownCards(current);
            int sequencesBefore = current.TotalCompletedSequences;

            foreach (var move in EnumerateMoves(current))
            {
                if (cancellationToken.IsCancellationRequested)
                    return ProgressReachability.Unknown;

                var next = current.Clone();

                if (!TryMove(next, move.FromColumn, move.FromIndex, move.ToColumn))
                    continue;

                // These are irreversible milestones. Merely moving a run onto
                // another run is not enough: the search continues until it can
                // expose hidden information, collect a sequence, or deal stock.
                if (CountFaceDownCards(next) < faceDownBefore
                    || next.TotalCompletedSequences > sequencesBefore
                    || next.IsWon
                    || CanDealFromStock(next))
                {
                    return ProgressReachability.ProgressFound;
                }

                if (cancellationToken.IsCancellationRequested)
                    return ProgressReachability.Unknown;

                string key = GetReachabilityKey(next);
                if (!visited.Add(key))
                    continue;

                if (visited.Count >= maxStatesToCheck)
                    return ProgressReachability.Unknown;

                queue.Enqueue(next);
            }
        }

        // Every reachable arrangement was exhausted and none could reveal,
        // collect, or deal. The remaining legal moves are only reversible cycles.
        return ProgressReachability.DeadEnd;
    }

    /// <summary>
    /// Canonical key for reachability search. Tableau columns are interchangeable,
    /// but card order inside each pile and stock order are significant.
    /// Card IDs are omitted because they do not affect any game rule.
    /// </summary>
    public static string GetReachabilityKey(GameState state)
    {
        var columns = new string[state.Tableau.Count];
        for (int i = 0; i < state.Tableau.Count; i++)
        {
            var pile = state.Tableau[i];
            var column = new StringBuilder(128);
            column.Append(pile.FaceDownCount).Append(':');
            foreach (var card in pile.Cards)
            {
                column.Append((int)card.Suit).Append(',')
                      .Append((int)card.Rank).Append(';');
            }
            columns[i] = column.ToString();
        }
        Array.Sort(columns, StringComparer.Ordinal);

        var key = new StringBuilder(2048);
        key.Append((int)state.Mode).Append('|')
           .Append((int)state.Ruleset).Append('|')
           .Append(state.SuitCount).Append('|')
           .Append(state.CompletedSequences).Append('|')
           .Append(state.TotalCompletedSequences).Append('|');

        key.Append(state.Stock.Count).Append(':');
        foreach (var card in state.Stock)
        {
            key.Append((int)card.Suit).Append(',')
               .Append((int)card.Rank).Append(';');
        }
        key.Append('|');

        foreach (var column in columns)
            key.Append(column).Append('#');

        return key.ToString();
    }

    private static int CountFaceDownCards(GameState state)
        => state.Tableau.Sum(pile => pile.FaceDownCount);

    public static IEnumerable<Move> EnumerateProgressMoves(GameState state)
    {
        foreach (var move in EnumerateMoves(state))
        {
            var source = state.Tableau[move.FromColumn];
            var target = state.Tableau[move.ToColumn];

            // The whole face-up pile is being moved to another empty column.
            // This leaves the same number of empty columns and changes no card
            // relationships. Do not treat it as a way out of a dead end.
            bool onlyRelocatesWholePile = target.IsEmpty
                && move.FromIndex == 0
                && source.FaceDownCount == 0;

            if (!onlyRelocatesWholePile)
                yield return move;
        }
    }

    public static IEnumerable<Move> EnumerateMoves(GameState state)
    {
        for (int fromCol = 0; fromCol < 10; fromCol++)
        {
            var from = state.Tableau[fromCol];

            for (int start = from.FaceDownCount; start < from.Count; start++)
            {
                if (!GameRules.CanPickUp(from, start, state.Ruleset)) continue;
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

        // Capture the suit before removing the completed 13-card sequence.
        Suit collectedSuit = pile.Cards[pile.Count - 13].Suit;
        for (int i = 0; i < 13; i++)
            pile.RemoveTop();

        state.CompletedSequences++;
        state.TotalCompletedSequences++;
        state.CompletedSequenceSuits.Add(collectedSuit);
        LastCollectedColumn = column;
        (_lastCollectedSequences ??= new List<CollectedSequence>())
            .Add(new CollectedSequence(column, collectedSuit));

        // Endless uses a per-round counter for the eight on-screen piles and a
        // cumulative counter for reliable event detection / save data.
        if (state.CompletedSequences >= 8 && state.Mode == GameMode.Endless)
        {
            state.CompletedSequences = 0;
            state.CompletedSequenceSuits.Clear();
            state.EndlessLoops++;
            DeckFactory.DealFreshRound(state, new Random());
        }
    }
}
