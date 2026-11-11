using System.Text;
using AscendingSpider.Domain;

namespace AscendingSpider.Solver;

/// <summary>
/// Creates a position key for the Spider solver.
/// Tableau columns are interchangeable, so their signatures are sorted before
/// hashing. This prevents the solver from exploring the same card arrangement
/// repeatedly just because complete columns were permuted.
/// </summary>
public static class StateHash
{
    public static string GetHash(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var sb = new StringBuilder(1024);
        sb.Append((int)state.Mode).Append('|');
        sb.Append((int)state.Ruleset).Append('|');
        sb.Append(state.SuitCount).Append('|');
        sb.Append(state.CompletedSequences).Append('|');

        // The next card dealt is Stock[^1], therefore stock order matters.
        sb.Append(state.Stock.Count).Append('|');
        foreach (var card in state.Stock)
        {
            sb.Append((int)card.Suit).Append(':')
              .Append((int)card.Rank).Append(',');
        }
        sb.Append('|');

        // A tableau column is fully described by its face-down count and the
        // ordered suit/rank values of its cards. Card IDs are intentionally
        // omitted: duplicate Spider cards of the same suit/rank are equivalent
        // for all game rules and for the solver's win condition.
        var columnKeys = new string[state.Tableau.Count];
        for (int i = 0; i < state.Tableau.Count; i++)
        {
            var pile = state.Tableau[i];
            var column = new StringBuilder(128);
            column.Append(pile.FaceDownCount).Append(':');

            foreach (var card in pile.Cards)
            {
                column.Append((int)card.Suit).Append(':')
                      .Append((int)card.Rank).Append(',');
            }

            columnKeys[i] = column.ToString();
        }

        // The physical column numbers do not affect Spider rules. Sorting
        // gives equivalent positions the same key while keeping the GameState
        // itself untouched, so any returned Move path still uses its own
        // state's original column indexes.
        Array.Sort(columnKeys, StringComparer.Ordinal);

        foreach (var columnKey in columnKeys)
            sb.Append(columnKey).Append(';');

        return sb.ToString();
    }
}
