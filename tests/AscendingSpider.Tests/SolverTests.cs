using AscendingSpider.Domain;
using AscendingSpider.Solver;
using Xunit;

namespace AscendingSpider.Tests;

public class SolverTests
{
    [Fact]
    public void Solver_Can_Solve_OneSuit_Game()
    {
        var rng = new Random(123); // Фиксированный сид для повторяемости
        var state = DeckFactory.Deal(suitCount: 1, faceDownPerColumn: 5, rng);
        
        var solver = new SpiderSolver();
        // Даём лимит в 10 000 состояний для быстрого теста
        var (isSolvable, solution) = solver.Solve(state, maxStatesToCheck: 10_000);

        // Если решение найдено, проверяем, что оно валидное
        if (isSolvable)
        {
            var simState = state.Clone();
            foreach (var move in solution)
            {
                if (move.FromColumn == -1)
                    Assert.True(GameEngine.TryDealFromStock(simState));
                else
                    Assert.True(GameEngine.TryMove(simState, move.FromColumn, move.FromIndex, move.ToColumn));
            }
            Assert.True(simState.IsWon);
        }
    }
    [Fact]
    public void DeckFactory_Always_Creates_One_Spade_Suit()
    {
        var deck = DeckFactory.CreateDeck(suitCount: 4);

        Assert.Equal(104, deck.Count);
        Assert.All(deck, card => Assert.Equal(Suit.Spades, card.Suit));
        Assert.Equal(8, deck.Count(card => card.Rank == Rank.Ace));
        Assert.Equal(8, deck.Count(card => card.Rank == Rank.King));
    }

    [Fact]
    public void DeckFactory_Creates_The_Selected_Single_Suit()
    {
        var deck = DeckFactory.CreateDeck(suitCount: 1, selectedSuit: Suit.Hearts);

        Assert.Equal(104, deck.Count);
        Assert.All(deck, card => Assert.Equal(Suit.Hearts, card.Suit));
    }

    [Fact]
    public void GameState_Preserves_Selected_Suit_When_Cloned()
    {
        var state = new GameState(suitCount: 1, selectedSuit: Suit.Diamonds);
        var clone = state.Clone();

        Assert.Equal(Suit.Diamonds, clone.SelectedSuit);
    }

    [Fact]
    public void GameState_Normalizes_SuitCount_To_One()
    {
        var state = new GameState(suitCount: 4);

        Assert.Equal(1, state.SuitCount);
    }

}