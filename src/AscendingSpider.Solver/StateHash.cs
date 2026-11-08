using System.Text;
using AscendingSpider.Domain;

namespace AscendingSpider.Solver;

public static class StateHash
{
    public static string GetHash(GameState state)
    {
        var sb = new StringBuilder(128);
        sb.Append(state.CompletedSequences).Append('|');
        sb.Append(state.Stock.Count).Append('|');

        for (int i = 0; i < 10; i++)
        {
            var pile = state.Tableau[i];
            sb.Append(pile.FaceDownCount).Append(':');
            foreach (var card in pile.Cards)
            {
                sb.Append((int)card.Suit).Append((int)card.Rank).Append(',');
            }
            sb.Append(';');
        }
        return sb.ToString();
    }
}