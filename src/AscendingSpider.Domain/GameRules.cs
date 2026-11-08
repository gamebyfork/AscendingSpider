namespace AscendingSpider.Domain;

public static class GameRules
{
    public static bool CanPlaceOn(Card movingCard, Card targetCard)
        => (int)movingCard.Rank == (int)targetCard.Rank + 1;

    /// <summary>
    /// Проверяет, можно ли взять карту (и всё, что над ней).
    /// Карту можно взять только если она является верхней ИЛИ частью валидной последовательности, доходящей до самого верха колонки.
    /// </summary>
    public static bool CanPickUp(Pile pile, int startIndex)
    {
        if (startIndex < 0 || startIndex >= pile.Count) return false;
        if (startIndex < pile.FaceDownCount) return false;
        
        // Проверяем, что последовательность от startIndex до конца колонки валидна
        return GetSequenceEnd(pile, startIndex) == pile.Count - 1;
    }

    /// <summary>Возвращает индекс последней карты в валидной последовательности, начиная с startIndex.</summary>
    public static int GetSequenceEnd(Pile pile, int startIndex)
    {
        if (startIndex < 0 || startIndex >= pile.Count) return startIndex;
        var cards = pile.Cards;
        int endIndex = startIndex;
        while (endIndex < cards.Count - 1)
        {
            var current = cards[endIndex];
            var next = cards[endIndex + 1];
            if (current.Suit == next.Suit && (int)next.Rank == (int)current.Rank + 1)
                endIndex++;
            else
                break;
        }
        return endIndex;
    }

    public static bool HasCompleteSequence(Pile pile)
    {
        if (pile.Count < 13) return false;
        var cards = pile.Cards;
        int start = cards.Count - 13;
        var suit = cards[start].Suit;
        for (int i = 0; i < 13; i++)
        {
            if (cards[start + i].Suit != suit) return false;
            if ((int)cards[start + i].Rank != i + 1) return false;
        }
        return true;
    }
}