namespace AscendingSpider.Domain;

/// <summary>
/// Rules for AscendingSpider. Cards are stored bottom-to-top in a
/// pile, so a complete run is Ace, Two, Three, ... King.
/// </summary>
public static class GameRules
{
    /// <summary>Returns true when the moving card can be placed on the target.</summary>
    public static bool CanPlaceOn(Card movingCard, Card targetCard)
        => (int)movingCard.Rank == (int)targetCard.Rank + 1;

    /// <summary>
    /// A card and the cards above it can be picked up together only when they
    /// form an ascending run. In Classic mode the run must also be one suit.
    /// </summary>
    public static bool CanPickUp(
        Pile pile,
        int startIndex,
        SpiderRuleset ruleset = SpiderRuleset.Classic)
    {
        if (startIndex < 0 || startIndex >= pile.Count)
            return false;

        if (startIndex < pile.FaceDownCount)
            return false;

        return GetSequenceEnd(pile, startIndex, ruleset) == pile.Count - 1;
    }

    /// <summary>
    /// Returns the final index of a valid ascending run beginning at
    /// <paramref name="startIndex"/>. Pile indices increase from bottom to top.
    /// </summary>
    public static int GetSequenceEnd(
        Pile pile,
        int startIndex,
        SpiderRuleset ruleset = SpiderRuleset.Classic)
    {
        if (startIndex < 0 || startIndex >= pile.Count)
            return startIndex;

        var cards = pile.Cards;
        int endIndex = startIndex;

        while (endIndex < cards.Count - 1)
        {
            Card current = cards[endIndex];
            Card next = cards[endIndex + 1];

            bool ascendingRank = (int)next.Rank == (int)current.Rank + 1;
            bool suitAllowed = ruleset == SpiderRuleset.Relaxed || current.Suit == next.Suit;

            if (!ascendingRank || !suitAllowed)
                break;

            endIndex++;
        }

        return endIndex;
    }

    /// <summary>
    /// A completed AscendingSpider sequence is Ace through King, all in one suit,
    /// occupying the top thirteen cards of a tableau column.
    /// </summary>
    public static bool HasCompleteSequence(Pile pile)
    {
        if (pile.Count < 13)
            return false;

        var cards = pile.Cards;
        int start = cards.Count - 13;
        Suit suit = cards[start].Suit;

        for (int i = 0; i < 13; i++)
        {
            Card card = cards[start + i];
            if (card.Suit != suit || (int)card.Rank != i + 1)
                return false;
        }

        return true;
    }
}
