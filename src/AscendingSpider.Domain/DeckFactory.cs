using System;
using System.Collections.Generic;

namespace AscendingSpider.Domain;

public static class DeckFactory
{
    public static List<Card> CreateDeck(int suitCount)
    {
        var sets = suitCount switch
        {
            1 => new[] { 8, 0, 0, 0 },
            2 => new[] { 4, 4, 0, 0 },
            3 => new[] { 3, 3, 2, 0 },
            4 => new[] { 2, 2, 2, 2 },
            _ => throw new ArgumentOutOfRangeException(nameof(suitCount))
        };

        var deck = new List<Card>(104);
        int nextId = 1;

        for (int s = 0; s < 4; s++)
        {
            var suit = (Suit)s;
            for (int set = 0; set < sets[s]; set++)
                for (int rank = 1; rank <= 13; rank++)
                    deck.Add(new Card(suit, (Rank)rank, nextId++)); 
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

    public static GameState Deal(int suitCount, int faceDownPerColumn, Random rng, GameMode mode = GameMode.Standard)
    {
        var deck = CreateDeck(suitCount);
        Shuffle(deck, rng);

        var state = new GameState(suitCount, mode);
        int index = 0;

        for (int col = 0; col < 10; col++)
        {
            int cardCount = col < 4 ? 6 : 5;
            int faceDown = Math.Min(faceDownPerColumn, cardCount - 1);

            for (int i = 0; i < cardCount; i++)
            {
                var card = deck[index++];
                if (i < faceDown) state.Tableau[col].AddFaceDown(card);
                else state.Tableau[col].AddFaceUp(card);
            }
        }

        for (int i = index; i < deck.Count; i++)
            state.Stock.Add(deck[i]);

        return state;
    }
}