namespace AscendingSpider.Domain;
public enum GameMode { Standard, Blitz, Endless }

public class GameState
{
    public List<Pile> Tableau { get; } = new();
    public List<Card> Stock { get; } = new();
    
    // CompletedSequences is the number of sequences collected in the current round.
    // In Endless mode it resets after eight, while TotalCompletedSequences never resets.
    public int CompletedSequences { get; set; }
    public int TotalCompletedSequences { get; set; }

    public Suit SelectedSuit { get; }
    
    // Keeps card identities unique when Endless mode creates another deck.
    public int NextCardId { get; set; } = 1;
    
    // Retained so Endless mode can deal the next round at the selected difficulty.
    public int FaceDownPerColumn { get; set; } = 5;
    
    public GameMode Mode { get; }
    public int EndlessLoops { get; set; }

    public GameState(GameMode mode = GameMode.Standard, Suit selectedSuit = Suit.Spades)
    {
        // Every game uses one selected suit.
        SelectedSuit = Enum.IsDefined(typeof(Suit), selectedSuit) ? selectedSuit : Suit.Spades;
        Mode = mode;
        
        for (int i = 0; i < 10; i++)
            Tableau.Add(new Pile());
    }
    
    public bool IsWon => CompletedSequences >= 8 && Mode != GameMode.Endless;
    
    public GameState Clone()
    {
        var copy = new GameState(Mode, SelectedSuit)
        {
            CompletedSequences = CompletedSequences,
            TotalCompletedSequences = TotalCompletedSequences,
            NextCardId = NextCardId,
            FaceDownPerColumn = FaceDownPerColumn,
            EndlessLoops = EndlessLoops,
        };

        for (int i = 0; i < 10; i++)
            copy.Tableau[i] = Tableau[i].Clone();
            
        copy.Stock.AddRange(Stock);
        
        return copy;
    }
}
