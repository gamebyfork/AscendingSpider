namespace AscendingSpider.Domain;

public static class RandomBot
{
    public readonly record struct Result(bool Won, int Moves, int CompletedSequences, bool Stuck);

    public static Result PlayGame(GameState state, Random rng, int maxMoves = 10_000)
    {
        int moves = 0;

        while (!state.IsWon && moves < maxMoves)
        {
            if (!GameEngine.HasAnyMoves(state))
                return new Result(false, moves, state.CompletedSequences, Stuck: true);

            var options = GameEngine.EnumerateMoves(state).ToList();
            bool canDeal = GameEngine.CanDealFromStock(state);

            bool takeStock = options.Count == 0
                ? canDeal
                : (canDeal && rng.NextDouble() < 0.2);

            if (takeStock)
            {
                if (!GameEngine.TryDealFromStock(state)) break;
            }
            else
            {
                if (options.Count == 0) break;
                var move = options[rng.Next(options.Count)];
                GameEngine.TryMove(state, move.FromColumn, move.FromIndex, move.ToColumn);
            }

            moves++;

            if (!GameEngine.HasAnyMoves(state))
                return new Result(false, moves, state.CompletedSequences, Stuck: true);
        }

        return new Result(state.IsWon, moves, state.CompletedSequences, Stuck: false);
    }
}