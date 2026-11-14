using AscendingSpider.Domain;
using AscendingSpider.Solver;

namespace AscendingSpider.Tests;

public sealed class SpiderSolverRegressionTests
{
    [Fact]
    public void SolverFindsAscendingAceToKingTableauWin()
    {
        var state = new GameState(1)
        {
            CompletedSequences = 7,
            TotalCompletedSequences = 7
        };

        for (int rank = 1; rank <= 12; rank++)
            state.Tableau[0].AddFaceUp(new Card(Suit.Spades, (Rank)rank, rank));

        state.Tableau[1].AddFaceUp(new Card(Suit.Spades, Rank.King, 13));

        SolverSearchResult result = new SpiderSolver().SolveDetailed(
            state,
            maxStatesToCheck: 10_000,
            allowStockDeals: true);

        Assert.Equal(SolvabilityStatus.Solvable, result.Status);
        Assert.Single(result.Solution);
        Assert.Equal(new Move(1, 0, 0), result.Solution[0]);
    }

    [Fact]
    public void WinningHintCanRecommendStockDealWhenItCompletesAceToKing()
    {
        var state = new GameState(1)
        {
            CompletedSequences = 7,
            TotalCompletedSequences = 7
        };

        for (int rank = 1; rank <= 12; rank++)
            state.Tableau[0].AddFaceUp(new Card(Suit.Spades, (Rank)rank, rank));

        for (int column = 1; column < 10; column++)
            state.Tableau[column].AddFaceUp(new Card(Suit.Spades, (Rank)(column + 1), 100 + column));

        for (int i = 0; i < 9; i++)
            state.Stock.Add(new Card(Suit.Spades, (Rank)(i + 2), 200 + i));
        // Stock[^1] is dealt to column zero first and completes A..K.
        state.Stock.Add(new Card(Suit.Spades, Rank.King, 299));

        HintSearchResult hint = new SpiderSolver().FindWinningHintOrStock(state);

        Assert.True(hint.HasHint);
        Assert.True(hint.IsStockDeal);
    }
}
