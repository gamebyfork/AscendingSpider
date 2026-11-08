using AscendingSpider.Domain;

namespace AscendingSpider.Solver;

public class SpiderSolver
{
    private record Node(GameState State, int Score, List<Move> Path);

    public (bool IsSolvable, List<Move> Solution) Solve(GameState initialState, int maxStatesToCheck = 50_000)
    {
        // Очередь с приоритетом: сначала состояния с наименьшим Score (наиболее перспективные)
        var openList = new PriorityQueue<Node, int>();
        var visited = new HashSet<string>();

        openList.Enqueue(new Node(initialState, StateHeuristic.Calculate(initialState), new List<Move>()), StateHeuristic.Calculate(initialState));
        
        int statesChecked = 0;

        while (openList.Count > 0 && statesChecked < maxStatesToCheck)
        {
            var current = openList.Dequeue();
            statesChecked++;

            if (current.State.IsWon)
                return (true, current.Path);

            var hash = StateHash.GetHash(current.State);
            if (visited.Contains(hash)) continue;
            visited.Add(hash);

            // 1. Варианты ходов по полю
            var moves = GameEngine.EnumerateMoves(current.State).ToList();
            foreach (var move in moves)
            {
                var nextState = current.State.Clone();
                GameEngine.TryMove(nextState, move.FromColumn, move.FromIndex, move.ToColumn);
                
                var nextPath = new List<Move>(current.Path) { move };
                int score = StateHeuristic.Calculate(nextState);
                
                openList.Enqueue(new Node(nextState, score, nextPath), score);
            }

            // 2. Ход из стока (раздача)
            if (current.State.Stock.Count >= 10)
            {
                var nextState = current.State.Clone();
                GameEngine.TryDealFromStock(nextState);
                
                // Используем специальный маркер для раздачи (FromColumn = -1)
                var nextPath = new List<Move>(current.Path) { new Move(-1, -1, -1) };
                int score = StateHeuristic.Calculate(nextState);
                
                openList.Enqueue(new Node(nextState, score, nextPath), score);
            }
        }

        // Если превысили лимит или очередь опустела — решение не найдено
        return (false, new List<Move>());
    }
}