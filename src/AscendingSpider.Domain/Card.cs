namespace AscendingSpider.Domain;

public readonly record struct Card(Suit Suit, Rank Rank, int Id = 0)
{
    public override string ToString() => $"{Rank} of {Suit}";
}