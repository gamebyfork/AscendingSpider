namespace AscendingSpider.Domain;
public enum GameMode { Standard, Blitz, Endless }

/// <summary>Controls which descending face-up runs may be moved together.</summary>
public enum SpiderRuleset { Classic = 0, Relaxed = 1 }

public class GameState
{
    public List<Pile> Tableau { get; } = new();
    public List<Card> Stock { get; } = new();
    
    // CompletedSequences is the number of sequences collected in the current round.
    // In Endless mode it resets after eight, while TotalCompletedSequences never resets.
    public int CompletedSequences { get; set; }
    public int TotalCompletedSequences { get; set; }
    
    // Suits of completed sequences, in the same order as the collected piles shown by the UI.
    public List<Suit> CompletedSequenceSuits { get; } = new();
    
    public int SuitCount { get; init; }
    public Suit SelectedSuit { get; }
    
    // Keeps card identities unique when Endless mode creates another deck.
    public int NextCardId { get; set; } = 1;
    
    // Retained so Endless mode can deal the next round at the selected difficulty.
    public int FaceDownPerColumn { get; set; } = 5;
    
    public GameMode Mode { get; }
    public SpiderRuleset Ruleset { get; }
    public int EndlessLoops { get; set; }
    public int DealsMade { get; set; }
    
    // Для таймера в режиме "Блиц"
    public bool IsBlitzTimerActive { get; set; } = false;
    public int BlitzTimeLeftSeconds { get; set; } = 0;
    
    public GameState(int suitCount, GameMode mode = GameMode.Standard, SpiderRuleset ruleset = SpiderRuleset.Classic, Suit selectedSuit = Suit.Spades)
    {
        // One suit is used per game; the player chooses which suit.
        SuitCount = 1;
        SelectedSuit = Enum.IsDefined(typeof(Suit), selectedSuit) ? selectedSuit : Suit.Spades;
        Mode = mode;
        Ruleset = Enum.IsDefined(typeof(SpiderRuleset), ruleset) ? ruleset : SpiderRuleset.Classic;
        
        for (int i = 0; i < 10; i++)
            Tableau.Add(new Pile());
    }
    
    public bool IsWon => CompletedSequences >= 8 && Mode != GameMode.Endless;
    
    public GameState Clone()
    {
        var copy = new GameState(SuitCount, Mode, Ruleset, SelectedSuit)
        {
            CompletedSequences = CompletedSequences,
            TotalCompletedSequences = TotalCompletedSequences,
            NextCardId = NextCardId,
            FaceDownPerColumn = FaceDownPerColumn,
            EndlessLoops = EndlessLoops,
            DealsMade = DealsMade,
            IsBlitzTimerActive = IsBlitzTimerActive,
            BlitzTimeLeftSeconds = BlitzTimeLeftSeconds
        };
        
        copy.CompletedSequenceSuits.AddRange(CompletedSequenceSuits);
        
        for (int i = 0; i < 10; i++)
            copy.Tableau[i] = Tableau[i].Clone();
            
        copy.Stock.AddRange(Stock);
        
        return copy;
    }
}