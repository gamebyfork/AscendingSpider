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
}