using System;
using System.Collections.Generic;
using System.Linq;

namespace AscendingSpider.Domain;

public static class DeckFactory
{
    public static List<Card> CreateDeck(int suitCount, int firstCardId = 1, Suit selectedSuit = Suit.Spades)
    {
        // Each game uses exactly one suit, selected in the New Game menu.
        // Keep suitCount in the signature for compatibility with existing callers.
        var deck = new List<Card>(104);
        int nextId = firstCardId;

        if (!Enum.IsDefined(typeof(Suit), selectedSuit)) selectedSuit = Suit.Spades;

        for (int set = 0; set < 8; set++)
        {
            for (int rank = 1; rank <= 13; rank++)
                deck.Add(new Card(selectedSuit, (Rank)rank, nextId++));
        }

        return deck;
    }

    public static void Shuffle<T>(IList<T> list, Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    public static GameState Deal(int suitCount, int faceDownPerColumn, Random rng, GameMode mode = GameMode.Standard, SpiderRuleset ruleset = SpiderRuleset.Classic, Suit selectedSuit = Suit.Spades)
    {
        var state = new GameState(suitCount, mode, ruleset, selectedSuit)
        {
            FaceDownPerColumn = faceDownPerColumn
        };

        DealFreshRound(state, rng);
        return state;
    }

    /// <summary>
    /// Deals a fresh 104-card round into an existing state. Used by Endless mode
    /// after all eight sequences from the previous round have been collected.
    /// </summary>
    public static void DealFreshRound(GameState state, Random rng)
    {
        var deck = CreateDeck(state.SuitCount, state.NextCardId, state.SelectedSuit);
        Shuffle(deck, rng);
        state.NextCardId = checked(state.NextCardId + deck.Count);

        foreach (var pile in state.Tableau)
            pile.Clear();
        state.Stock.Clear();

        int index = 0;
        for (int col = 0; col < 10; col++)
        {
            int cardCount = col < 4 ? 6 : 5;
            int faceDown = Math.Min(state.FaceDownPerColumn, cardCount - 1);

            for (int i = 0; i < cardCount; i++)
            {
                var card = deck[index++];
                if (i < faceDown)
                    state.Tableau[col].AddFaceDown(card);
                else
                    state.Tableau[col].AddFaceUp(card);
            }
        }

        for (int i = index; i < deck.Count; i++)
            state.Stock.Add(deck[i]);
    }
}
