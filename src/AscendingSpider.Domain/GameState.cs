namespace AscendingSpider.Domain;

public enum GameMode { Standard, Blitz, Endless }

public class GameState
{
    public List<Pile> Tableau { get; } = new();
    public List<Card> Stock { get; } = new();
    public int CompletedSequences { get; set; }
    public int SuitCount { get; init; }
    
    public GameMode Mode { get; }
    public int EndlessLoops { get; set; }
    public int DealsMade { get; set; }

    public GameState(int suitCount, GameMode mode = GameMode.Standard)
    {
        SuitCount = suitCount;
        Mode = mode;
        for (int i = 0; i < 10; i++)
            Tableau.Add(new Pile());
    }

    public bool IsWon => CompletedSequences == 8 && Mode != GameMode.Endless;

    public GameState Clone()
    {
        var copy = new GameState(SuitCount, Mode) 
        { 
            CompletedSequences = CompletedSequences, 
            EndlessLoops = EndlessLoops, 
            DealsMade = DealsMade 
        };
        for (int i = 0; i < 10; i++)
            copy.Tableau[i] = Tableau[i].Clone();
        copy.Stock.AddRange(Stock);
        return copy;
    }
}
