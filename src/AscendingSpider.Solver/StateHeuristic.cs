using System;
using AscendingSpider.Domain;

namespace AscendingSpider.Solver;

/// <summary>
/// Lower values represent positions closer to a completed Spider game.
/// This is an ordering heuristic, not a proof of solvability.
/// </summary>
public static class StateHeuristic
{
    private const int RemainingSequenceWeight = 900;
    private const int FaceDownCardWeight = 105;

    public static int Calculate(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        int remainingSequences = Math.Max(0, 8 - state.CompletedSequences);
        int score = remainingSequences * RemainingSequenceWeight;
        int emptyColumns = 0;

        foreach (Pile pile in state.Tableau)
        {
            if (pile.IsEmpty)
            {
                emptyColumns++;
                continue;
            }

            score += pile.FaceDownCount * FaceDownCardWeight;
            var cards = pile.Cards;
            int visibleStart = Math.Clamp(pile.FaceDownCount, 0, cards.Count);

            int currentSameSuitRun = 1;
            int longestSameSuitRun = 1;

            for (int i = visibleStart; i < cards.Count - 1; i++)
            {
                Card lower = cards[i];
                Card upper = cards[i + 1];
                bool rankContinues = (int)upper.Rank == (int)lower.Rank + 1;
                bool sameSuit = lower.Suit == upper.Suit;

                if (rankContinues && sameSuit)
                {
                    score -= 24;
                    currentSameSuitRun++;
                    longestSameSuitRun = Math.Max(longestSameSuitRun, currentSameSuitRun);
                }
                else
                {
                    currentSameSuitRun = 1;
                    score += rankContinues ? 17 : 27;
                }
            }

            int topRunLength = GetTopSameSuitRunLength(pile);
            score -= Math.Max(0, topRunLength - 1) * 15;
            score -= Math.Max(0, longestSameSuitRun - topRunLength) * 5;
        }

        score -= emptyColumns switch
        {
            0 => 0,
            1 => 38,
            2 => 64,
            3 => 82,
            4 => 94,
            _ => 94 + (emptyColumns - 4) * 8
        };

        return Math.Max(0, score);
    }

    /// <summary>
    /// Counts visible adjacent pairs that form a descending same-suit run.
    /// A pure 10-to-9 transfer from one such pair to another has zero net gain,
    /// so the hint system can recognise it as a shuffle rather than progress.
    /// </summary>
    public static int CountSameSuitLinks(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        int links = 0;

        foreach (Pile pile in state.Tableau)
        {
            int start = Math.Clamp(pile.FaceDownCount, 0, pile.Count);
            for (int i = start; i < pile.Count - 1; i++)
            {
                Card lower = pile.Cards[i];
                Card upper = pile.Cards[i + 1];
                if (lower.Suit == upper.Suit
                    && (int)upper.Rank == (int)lower.Rank + 1)
                {
                    links++;
                }
            }
        }

        return links;
    }

    /// <summary>
    /// Immediate move-order preference. Large bonuses are reserved for
    /// irreversible progress; rank-compatible same-suit merges are useful but
    /// should not outrank revealing a hidden card or freeing a column.
    /// </summary>
    public static int GetMoveOrderBonus(GameState state, Move move)
    {
        if (move.FromColumn < 0)
            return -18;

        if (move.FromColumn >= state.Tableau.Count
            || move.ToColumn < 0
            || move.ToColumn >= state.Tableau.Count)
        {
            return int.MinValue / 4;
        }

        Pile source = state.Tableau[move.FromColumn];
        Pile target = state.Tableau[move.ToColumn];

        if (move.FromIndex < source.FaceDownCount
            || move.FromIndex < 0
            || move.FromIndex >= source.Count)
        {
            return int.MinValue / 4;
        }

        Card movingCard = source.Cards[move.FromIndex];
        bool revealsHidden = move.FromIndex == source.FaceDownCount
            && source.FaceDownCount > 0;
        bool emptiesSource = move.FromIndex == 0;

        int bonus = 0;
        if (revealsHidden)
            bonus += 420;
        if (emptiesSource)
            bonus += 180;

        if (target.IsEmpty)
        {
            // Empty columns are useful temporary workspace, but this bonus is
            // intentionally modest so empty-column shuffling is not overvalued.
            bonus += 12;
        }
        else if (target.TopCard.Suit == movingCard.Suit)
        {
            bonus += 76 + Math.Min(GetTopSameSuitRunLength(target), 8) * 5;
        }
        else
        {
            // Mixed-suit stacks are legal, but harder to move as a single run.
            bonus -= 65;
        }

        // Splitting an existing same-suit sequence requires a concrete payoff.
        if (!revealsHidden && move.FromIndex > source.FaceDownCount)
        {
            Card below = source.Cards[move.FromIndex - 1];
            bool breaksSameSuitRun = below.Suit == movingCard.Suit
                && (int)movingCard.Rank == (int)below.Rank + 1;
            if (breaksSameSuitRun)
                bonus -= 75;
        }

        return bonus;
    }

    private static int GetTopSameSuitRunLength(Pile pile)
    {
        if (pile.Count == 0)
            return 0;

        int length = 1;
        for (int i = pile.Count - 1; i > pile.FaceDownCount; i--)
        {
            Card lower = pile.Cards[i - 1];
            Card upper = pile.Cards[i];
            if (lower.Suit == upper.Suit
                && (int)upper.Rank == (int)lower.Rank + 1)
            {
                length++;
            }
            else
            {
                break;
            }
        }

        return length;
    }
}
