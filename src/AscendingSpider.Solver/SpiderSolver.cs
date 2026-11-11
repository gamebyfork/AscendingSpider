using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using AscendingSpider.Domain;

namespace AscendingSpider.Solver;

/// <summary>
/// Result of a bounded Spider search.
/// Unsolvable is returned only after the complete reachable state graph has
/// been exhausted. A budget limit or cancellation always produces Unknown.
/// </summary>
public enum SolvabilityStatus
{
    Solvable,
    Unsolvable,
    Unknown
}

public readonly record struct SolverSearchResult(
    SolvabilityStatus Status,
    List<Move> Solution,
    int StatesChecked,
    int BestHeuristicScore)
{
    public bool IsSolvable => Status == SolvabilityStatus.Solvable;
    public bool IsProvenUnsolvable => Status == SolvabilityStatus.Unsolvable;
    public bool IsUnknown => Status == SolvabilityStatus.Unknown;
}

/// <summary>
/// Recommendation for the in-game hint system. FromColumn == -1 means deal
/// ten cards from the stock.
/// </summary>
public readonly record struct HintSearchResult(
    bool HasHint,
    Move Move,
    int Score,
    int StatesEvaluated)
{
    public bool IsStockDeal => HasHint && Move.FromColumn < 0;
}

public sealed class SpiderSolver
{
    // Keep this identical to the limit used by Game1.IsGameStuck(). A hint is
    // never allowed to recommend a first move that the game's own dead-end
    // detector would classify as DeadEnd immediately afterward.
    private const int HintReachabilityStateLimit = 50_000;
    private sealed record PathLink(PathLink? Previous, Move Move, int Length);

    private sealed record Node(
        GameState State,
        string Hash,
        int Depth,
        int Heuristic,
        PathLink? Path);

    private sealed record HintNode(
        GameState State,
        string Hash,
        int Depth,
        int Heuristic);

    private sealed record HintRoot(
        GameState State,
        string Hash,
        Move FirstMove,
        int Heuristic,
        int FirstMoveBonus,
        int ReversePenalty,
        int InitialScore);

    private sealed record HintCandidate(
        GameState State,
        string Hash,
        Move Move,
        int Heuristic,
        int MoveBonus,
        int ReversePenalty,
        int ImmediateValue,
        bool IsMeaningful,
        bool IsMilestone,
        int TargetRank,
        bool TargetSuitMatches);

    private readonly record struct InstantHintCandidate(
        Move Move,
        int Score,
        bool IsProductive,
        int ReversePenalty,
        AlternativeKey AlternativeKey);

    private readonly record struct AlternativeKey(
        int FromColumn,
        int FromIndex,
        int MovingSuit,
        int MovingRank,
        int TargetRank,
        bool TargetSuitMatches);

    /// <summary>
    /// Backwards-compatible API. A false value means no solution was found
    /// within the search budget; use SolveDetailed() to distinguish Unknown
    /// from a proven Unsolvable result.
    /// </summary>
    public (bool IsSolvable, List<Move> Solution) Solve(
        GameState initialState,
        int maxStatesToCheck = 50_000)
    {
        SolverSearchResult result = SolveDetailed(
            initialState,
            maxStatesToCheck,
            CancellationToken.None);

        return (result.IsSolvable, result.Solution);
    }

    /// <summary>
    /// Weighted best-first search with canonical state hashing and transposition
    /// handling. Useful moves are expanded first; whole-pile relocations between
    /// empty columns are omitted because they only permute interchangeable piles.
    /// maxMilliseconds bounds interactive verification; reaching it returns Unknown.
    /// </summary>
    public SolverSearchResult SolveDetailed(
        GameState initialState,
        int maxStatesToCheck = 50_000,
        CancellationToken cancellationToken = default,
        int maxMilliseconds = 0)
    {
        ArgumentNullException.ThrowIfNull(initialState);

        if (maxStatesToCheck <= 0
            || maxMilliseconds < 0
            || cancellationToken.IsCancellationRequested)
        {
            return new SolverSearchResult(
                SolvabilityStatus.Unknown,
                new List<Move>(),
                0,
                int.MaxValue);
        }

        var stopwatch = Stopwatch.StartNew();
        var open = new PriorityQueue<Node, (int Priority, int Heuristic, int Depth, long Order)>();
        var bestDepthByHash = new Dictionary<string, int>(StringComparer.Ordinal);
        long order = 0;

        GameState initial = initialState.Clone();
        string initialHash = StateHash.GetHash(initial);
        int initialHeuristic = StateHeuristic.Calculate(initial);

        bestDepthByHash[initialHash] = 0;
        open.Enqueue(
            new Node(initial, initialHash, 0, initialHeuristic, null),
            MakePriority(initialHeuristic, 0, 0, order++));

        int statesChecked = 0;
        int bestHeuristic = initialHeuristic;

        try
        {
            while (open.Count > 0 && statesChecked < maxStatesToCheck)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (maxMilliseconds > 0 && stopwatch.ElapsedMilliseconds >= maxMilliseconds)
                {
                    return new SolverSearchResult(
                        SolvabilityStatus.Unknown,
                        new List<Move>(),
                        statesChecked,
                        bestHeuristic);
                }

                Node current = open.Dequeue();
                if (!bestDepthByHash.TryGetValue(current.Hash, out int bestDepth)
                    || current.Depth != bestDepth)
                {
                    continue;
                }

                statesChecked++;
                if (current.Heuristic < bestHeuristic)
                    bestHeuristic = current.Heuristic;

                if (current.State.IsWon)
                {
                    return new SolverSearchResult(
                        SolvabilityStatus.Solvable,
                        ReconstructPath(current.Path),
                        statesChecked,
                        bestHeuristic);
                }

                int nextDepth = current.Depth + 1;

                var orderedMoves = GameEngine.EnumerateProgressMoves(current.State)
                    .Select(move => new
                    {
                        Move = move,
                        Bonus = StateHeuristic.GetMoveOrderBonus(current.State, move)
                    })
                    .OrderByDescending(item => item.Bonus);

                foreach (var item in orderedMoves)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (maxMilliseconds > 0 && stopwatch.ElapsedMilliseconds >= maxMilliseconds)
                    {
                        return new SolverSearchResult(
                            SolvabilityStatus.Unknown,
                            new List<Move>(),
                            statesChecked,
                            bestHeuristic);
                    }

                    GameState next = current.State.Clone();
                    if (!GameEngine.TryMove(
                            next,
                            item.Move.FromColumn,
                            item.Move.FromIndex,
                            item.Move.ToColumn))
                    {
                        continue;
                    }

                    EnqueueIfBetter(next, item.Move, current, nextDepth, item.Bonus);
                }

                if (GameEngine.CanDealFromStock(current.State))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (maxMilliseconds > 0 && stopwatch.ElapsedMilliseconds >= maxMilliseconds)
                    {
                        return new SolverSearchResult(
                            SolvabilityStatus.Unknown,
                            new List<Move>(),
                            statesChecked,
                            bestHeuristic);
                    }

                    GameState next = current.State.Clone();
                    if (GameEngine.TryDealFromStock(next))
                    {
                        Move dealMove = new(-1, -1, -1);
                        int bonus = StateHeuristic.GetMoveOrderBonus(current.State, dealMove);
                        EnqueueIfBetter(next, dealMove, current, nextDepth, bonus);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            return new SolverSearchResult(
                SolvabilityStatus.Unknown,
                new List<Move>(),
                statesChecked,
                bestHeuristic);
        }

        SolvabilityStatus finalStatus = open.Count == 0
            ? SolvabilityStatus.Unsolvable
            : SolvabilityStatus.Unknown;

        return new SolverSearchResult(
            finalStatus,
            new List<Move>(),
            statesChecked,
            bestHeuristic);

        void EnqueueIfBetter(GameState next, Move move, Node parent, int depth, int moveBonus)
        {
            string hash = StateHash.GetHash(next);
            if (bestDepthByHash.TryGetValue(hash, out int oldDepth) && oldDepth <= depth)
                return;

            bestDepthByHash[hash] = depth;
            int heuristic = StateHeuristic.Calculate(next);
            bestHeuristic = Math.Min(bestHeuristic, heuristic);

            var path = new PathLink(parent.Path, move, depth);
            var node = new Node(next, hash, depth, heuristic, path);
            open.Enqueue(node, MakePriority(heuristic, depth, moveBonus, order++));
        }
    }

    /// <summary>
    /// In Standard and Blitz, returns only the first move from a complete
    /// winning path found by SolveDetailed. In Endless, where IsWon is never
    /// true, uses the bounded progress-oriented heuristic.
    /// </summary>
    /// <summary>
    /// Immediate hint evaluation for the UI. It examines each legal next move
    /// exactly once, scores only observable one-move consequences, and never
    /// starts a multi-ply or full solvability search. This keeps button response
    /// effectively immediate while preferring reveals, completed runs, empty
    /// columns, and new same-suit links over neutral card shuffling.
    /// </summary>
    public HintSearchResult FindBestHintInstant(GameState state, Move? previousHint = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.IsWon)
            return NoHint(0);

        int baseFaceDown = CountFaceDownCards(state);
        int baseTableauCards = CountTableauCards(state);
        int baseCompleted = state.TotalCompletedSequences;
        int baseEmptyColumns = state.Tableau.Count(pile => pile.IsEmpty);
        int baseLinks = StateHeuristic.CountSameSuitLinks(state);
        int baseHeuristic = StateHeuristic.Calculate(state);
        bool stockAvailable = GameEngine.CanDealFromStock(state);

        var candidates = new List<InstantHintCandidate>();
        foreach (Move move in GameEngine.EnumerateProgressMoves(state))
        {
            if (move.FromColumn < 0 || move.FromColumn >= state.Tableau.Count
                || move.ToColumn < 0 || move.ToColumn >= state.Tableau.Count)
                continue;

            Pile source = state.Tableau[move.FromColumn];
            Pile target = state.Tableau[move.ToColumn];
            if (move.FromIndex < 0 || move.FromIndex >= source.Count)
                continue;

            Card movingCard = source.Cards[move.FromIndex];
            GameState next = state.Clone();
            if (!GameEngine.TryMove(next, move.FromColumn, move.FromIndex, move.ToColumn))
                continue;

            int nextFaceDown = CountFaceDownCards(next);
            int nextTableauCards = CountTableauCards(next);
            int nextEmptyColumns = next.Tableau.Count(pile => pile.IsEmpty);
            int linkDelta = StateHeuristic.CountSameSuitLinks(next) - baseLinks;
            int heuristicGain = baseHeuristic - StateHeuristic.Calculate(next);

            bool revealsCard = nextFaceDown < baseFaceDown;
            bool collectsSequence = next.TotalCompletedSequences > baseCompleted
                || nextTableauCards < baseTableauCards;
            bool freesColumn = !source.IsEmpty && next.Tableau[move.FromColumn].IsEmpty;
            bool createsEmptyColumn = nextEmptyColumns > baseEmptyColumns;
            bool unlocksDeal = !stockAvailable && GameEngine.CanDealFromStock(next);
            bool sameSuitLinkImproved = linkDelta > 0;

            bool productive = revealsCard || collectsSequence || freesColumn
                || createsEmptyColumn || unlocksDeal || sameSuitLinkImproved
                || heuristicGain >= 20;

            bool targetSuitMatches = !target.IsEmpty && target.TopCard.Suit == movingCard.Suit;
            int targetRank = target.IsEmpty ? 0 : (int)target.TopCard.Rank;
            int score = 0;
            if (revealsCard) score += 20_000;
            if (collectsSequence) score += 25_000;
            if (freesColumn) score += 3_000;
            if (createsEmptyColumn) score += 1_500;
            if (unlocksDeal) score += 900;
            score += linkDelta * 700;
            score += Math.Clamp(heuristicGain, -200, 200) * 8;
            score += StateHeuristic.GetMoveOrderBonus(state, move) * 5;
            if (!targetSuitMatches && !target.IsEmpty) score -= 180;

            int reversePenalty = GetPreviousHintPenalty(previousHint, move);
            score -= reversePenalty * 3;

            var key = new AlternativeKey(
                move.FromColumn,
                move.FromIndex,
                (int)movingCard.Suit,
                (int)movingCard.Rank,
                targetRank,
                targetSuitMatches);

            candidates.Add(new InstantHintCandidate(move, score, productive, reversePenalty, key));
        }

        // Keep only the best destination when the same card/run can be placed
        // on multiple identical ranks with the same suit relationship. This
        // prevents repeated hints such as the same 10 moving between two 9s.
        List<InstantHintCandidate> productiveCandidates = candidates
            .Where(candidate => candidate.IsProductive)
            .GroupBy(candidate => candidate.AlternativeKey)
            .Select(group => group.OrderByDescending(candidate => candidate.Score).First())
            .OrderByDescending(candidate => candidate.Score)
            .ToList();

        // Avoid immediate back-and-forth. If that would remove every useful
        // option, relax the repetition filter rather than inventing a move.
        List<InstantHintCandidate> preferred = productiveCandidates
            .Where(candidate => candidate.ReversePenalty < 450)
            .ToList();
        if (preferred.Count == 0)
            preferred = productiveCandidates;

        if (preferred.Count > 0)
        {
            InstantHintCandidate best = preferred[0];
            return new HintSearchResult(true, best.Move, -best.Score, candidates.Count);
        }

        // If all tableau moves are neutral shuffles, prefer the stock deal.
        if (stockAvailable)
            return new HintSearchResult(true, new Move(-1, -1, -1), 0, candidates.Count + 1);

        // No stock is available. Pick the least bad legal move rather than
        // showing nothing when the player still has to rearrange cards.
        if (candidates.Count > 0)
        {
            InstantHintCandidate best = candidates
                .OrderBy(candidate => candidate.ReversePenalty >= 450 ? 1 : 0)
                .ThenByDescending(candidate => candidate.Score)
                .First();
            return new HintSearchResult(true, best.Move, -best.Score, candidates.Count);
        }

        return NoHint(0);
    }

    public HintSearchResult FindBestHint(
        GameState state,
        Move? previousHint = null,
        int maxStatesToCheck = 350,
        int lookaheadDepth = 3,
        int beamWidth = 8,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        // Endless mode has no terminal win state. Keep its progress-oriented
        // strategy there; in Standard/Blitz, hints must come from a fully found
        // winning continuation. ProgressFound alone is not enough.
        if (state.Mode == GameMode.Endless)
        {
            return FindBestHeuristicHint(
                state,
                previousHint,
                maxStatesToCheck,
                lookaheadDepth,
                beamWidth,
                cancellationToken);
        }

        if (state.IsWon || cancellationToken.IsCancellationRequested)
            return NoHint(0);

        int verificationBudget = state.SuitCount switch
        {
            1 => 500_000,
            2 => 300_000,
            _ => 180_000
        };

        // FindBestHint is called on a worker thread. These are maximum search
        // windows, not a promise that every position can be solved in time.
        // A hint is returned only when SolveDetailed proves a complete path.
        int timeBudgetMs = state.SuitCount switch
        {
            1 => 8_000,
            2 => 12_000,
            _ => 15_000
        };

        SolverSearchResult proof = SolveDetailed(
            state,
            verificationBudget,
            cancellationToken,
            timeBudgetMs);

        // Never recommend a move from a merely promising or Unknown branch.
        // A hint is shown only when the solver has an actual complete winning
        // path and therefore can return its first move.
        if (proof.Status != SolvabilityStatus.Solvable
            || proof.Solution.Count == 0)
        {
            return NoHint(proof.StatesChecked);
        }

        Move winningFirstMove = proof.Solution[0];
        int score = StateHeuristic.Calculate(state);

        return new HintSearchResult(
            true,
            winningFirstMove,
            score,
            proof.StatesChecked);
    }

    private HintSearchResult FindBestHeuristicHint(
        GameState state,
        Move? previousHint,
        int maxStatesToCheck,
        int lookaheadDepth,
        int beamWidth,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (maxStatesToCheck <= 0
            || lookaheadDepth <= 0
            || beamWidth <= 0
            || cancellationToken.IsCancellationRequested)
        {
            return NoHint(0);
        }

        string initialHash = StateHash.GetHash(state);
        int baseHeuristic = StateHeuristic.Calculate(state);
        int baseFaceDown = CountFaceDownCards(state);
        int baseTableauCards = CountTableauCards(state);
        int baseSameSuitLinks = StateHeuristic.CountSameSuitLinks(state);
        bool stockAvailable = GameEngine.CanDealFromStock(state);

        var candidates = new List<HintCandidate>();

        foreach (Move move in GameEngine.EnumerateProgressMoves(state))
        {
            if (cancellationToken.IsCancellationRequested)
                return NoHint(candidates.Count);

            GameState next = state.Clone();
            if (!GameEngine.TryMove(next, move.FromColumn, move.FromIndex, move.ToColumn))
                continue;

            string hash = StateHash.GetHash(next);
            if (hash == initialHash)
                continue;

            var source = state.Tableau[move.FromColumn];
            var target = state.Tableau[move.ToColumn];
            Card movingCard = source.Cards[move.FromIndex];

            bool revealsHidden = CountFaceDownCards(next) < baseFaceDown;
            bool collectsSequence = CountTableauCards(next) < baseTableauCards
                || next.EndlessLoops > state.EndlessLoops
                || next.CompletedSequences > state.CompletedSequences;
            bool emptiesSource = !source.IsEmpty && next.Tableau[move.FromColumn].IsEmpty;
            bool unlocksStockDeal = !stockAvailable && GameEngine.CanDealFromStock(next);

            int linkDelta = StateHeuristic.CountSameSuitLinks(next) - baseSameSuitLinks;
            int heuristicGain = baseHeuristic - StateHeuristic.Calculate(next);

            bool milestone = revealsHidden || collectsSequence || emptiesSource || unlocksStockDeal;

            // A plain rank move is only considered useful when it creates a
            // net same-suit link or materially improves the overall position.
            // Moving a 10 from one 9 onto another 9 usually has linkDelta == 0:
            // it simply transfers the same relationship and is not progress.
            bool usefulMerge = linkDelta > 0 && heuristicGain >= 1;
            bool meaningful = milestone || usefulMerge || heuristicGain >= 24;

            bool targetSuitMatches = !target.IsEmpty && target.TopCard.Suit == movingCard.Suit;
            int targetRank = target.IsEmpty ? 0 : (int)target.TopCard.Rank;

            int immediateValue =
                (revealsHidden ? 2_000 : 0)
                + (collectsSequence ? 2_500 : 0)
                + (emptiesSource ? 650 : 0)
                + (unlocksStockDeal ? 500 : 0)
                + linkDelta * 120
                + Math.Clamp(heuristicGain, -100, 100)
                + (targetSuitMatches ? 12 : -40);

            int moveBonus = StateHeuristic.GetMoveOrderBonus(state, move);
            int reversePenalty = GetPreviousHintPenalty(previousHint, move);

            candidates.Add(new HintCandidate(
                next,
                hash,
                move,
                StateHeuristic.Calculate(next),
                moveBonus,
                reversePenalty,
                immediateValue,
                meaningful,
                milestone,
                targetRank,
                targetSuitMatches));
        }

        // Detect alternatives such as moving the same 10 onto either of two
        // 9s. When both destinations produce practically the same result, they
        // are not two separate strategic ideas: they are interchangeable shuffles.
        HashSet<Move> ambiguousMoves = FindEquivalentRankAlternatives(state, candidates);

        var productiveCandidates = candidates
            .Where(candidate => candidate.IsMeaningful
                && !ambiguousMoves.Contains(candidate.Move)
                && candidate.ReversePenalty < 450)
            .ToList();

        IEnumerable<HintCandidate> selectedCandidates;
        if (productiveCandidates.Count > 0)
        {
            selectedCandidates = productiveCandidates;
        }
        else if (stockAvailable)
        {
            // Do not fall back to neutral tableau shuffles when a stock deal is
            // legal. The stock action is added and reachability-checked below.
            selectedCandidates = Array.Empty<HintCandidate>();
        }
        else
        {
            // With no stock deal available, keep a legal fallback, but it must
            // still pass the same reachability check as every other suggestion.
            selectedCandidates = candidates
                .OrderBy(candidate => candidate.ReversePenalty)
                .ThenByDescending(candidate => candidate.ImmediateValue)
                .Take(Math.Min(candidates.Count, 18));
        }

        var rootsByHash = new Dictionary<string, HintRoot>(StringComparer.Ordinal);

        foreach (HintCandidate candidate in selectedCandidates)
        {
            if (cancellationToken.IsCancellationRequested)
                return NoHint(candidates.Count);

            int initialScore = CalculateHintScore(
                candidate.Heuristic,
                candidate.MoveBonus,
                candidate.ReversePenalty,
                depth: 1);

            var root = new HintRoot(
                candidate.State,
                candidate.Hash,
                candidate.Move,
                candidate.Heuristic,
                candidate.MoveBonus,
                candidate.ReversePenalty,
                initialScore);

            if (!rootsByHash.TryGetValue(candidate.Hash, out HintRoot existing)
                || root.InitialScore < existing.InitialScore)
            {
                rootsByHash[candidate.Hash] = root;
            }
        }

        // Include stock as a normal search candidate when there are other
        // useful moves. The explicit fallback above ensures it wins when the
        // board only offers neutral/equivalent shuffles.
        if (stockAvailable)
        {
            GameState dealt = state.Clone();
            if (GameEngine.TryDealFromStock(dealt))
            {
                string hash = StateHash.GetHash(dealt);
                int heuristic = StateHeuristic.Calculate(dealt);
                int bonus = StateHeuristic.GetMoveOrderBonus(state, new Move(-1, -1, -1));
                int score = CalculateHintScore(heuristic, bonus, 0, 1);
                var root = new HintRoot(
                    dealt,
                    hash,
                    new Move(-1, -1, -1),
                    heuristic,
                    bonus,
                    0,
                    score);

                if (!rootsByHash.TryGetValue(hash, out HintRoot existing)
                    || root.InitialScore < existing.InitialScore)
                {
                    rootsByHash[hash] = root;
                }
            }
        }

        if (rootsByHash.Count == 0)
            return NoHint(candidates.Count);

        int maxRoots = Math.Min(maxStatesToCheck, Math.Min(18, Math.Max(8, beamWidth * 2)));
        List<HintRoot> rankedRoots = rootsByHash.Values
            .OrderBy(root => root.InitialScore)
            .ThenBy(root => root.Heuristic)
            .ToList();

        // Critical safety gate: evaluate the state AFTER the proposed first move
        // with the same reachability algorithm and state budget used by the
        // game's "No moves" screen. DeadEnd roots are never shown as hints.
        // ProgressFound roots are preferred. Unknown roots are retained only as
        // a last resort because the search budget was inconclusive, not because
        // the move is known to be safe.
        var provenRoots = new List<HintRoot>();
        var unknownRoots = new List<HintRoot>();

        foreach (HintRoot root in rankedRoots)
        {
            if (cancellationToken.IsCancellationRequested)
                return NoHint(candidates.Count);

            ProgressReachability reachability = GameEngine.CheckReachableProgress(
                root.State,
                HintReachabilityStateLimit,
                cancellationToken);

            if (reachability == ProgressReachability.DeadEnd)
                continue;

            if (reachability == ProgressReachability.ProgressFound)
                provenRoots.Add(root);
            else
                unknownRoots.Add(root);

            // Once enough confirmed-safe candidates have been found for the
            // bounded lookahead, there is no reason to spend time checking the
            // remaining lower-ranked alternatives.
            if (provenRoots.Count >= maxRoots)
                break;
        }

        List<HintRoot> roots = (provenRoots.Count > 0 ? provenRoots : unknownRoots)
            .Take(maxRoots)
            .ToList();

        if (roots.Count == 0)
            return NoHint(candidates.Count + (stockAvailable ? 1 : 0));

        int statesEvaluated = candidates.Count + (stockAvailable ? 1 : 0);
        Move bestMove = roots[0].FirstMove;
        int bestScore = roots[0].InitialScore;

        foreach (HintRoot root in roots)
        {
            if (statesEvaluated >= maxStatesToCheck || cancellationToken.IsCancellationRequested)
                break;

            int rootBestScore = root.InitialScore;
            var frontier = new List<HintNode>
            {
                new(root.State, root.Hash, 1, root.Heuristic)
            };
            var visited = new HashSet<string>(StringComparer.Ordinal)
            {
                initialHash,
                root.Hash
            };

            for (int depth = 2; depth <= lookaheadDepth; depth++)
            {
                if (statesEvaluated >= maxStatesToCheck || cancellationToken.IsCancellationRequested)
                    break;

                var nextByHash = new Dictionary<string, HintNode>(StringComparer.Ordinal);

                foreach (HintNode node in frontier)
                {
                    foreach (Move move in GameEngine.EnumerateProgressMoves(node.State))
                    {
                        if (statesEvaluated >= maxStatesToCheck || cancellationToken.IsCancellationRequested)
                            break;

                        GameState next = node.State.Clone();
                        if (!GameEngine.TryMove(next, move.FromColumn, move.FromIndex, move.ToColumn))
                            continue;

                        statesEvaluated++;
                        string hash = StateHash.GetHash(next);
                        if (!visited.Add(hash))
                            continue;

                        int heuristic = StateHeuristic.Calculate(next);
                        var candidate = new HintNode(next, hash, depth, heuristic);

                        if (!nextByHash.TryGetValue(hash, out HintNode existing)
                            || candidate.Heuristic < existing.Heuristic)
                        {
                            nextByHash[hash] = candidate;
                        }
                    }

                    if (statesEvaluated >= maxStatesToCheck || cancellationToken.IsCancellationRequested)
                        break;

                    if (GameEngine.CanDealFromStock(node.State))
                    {
                        GameState next = node.State.Clone();
                        if (GameEngine.TryDealFromStock(next))
                        {
                            statesEvaluated++;
                            string hash = StateHash.GetHash(next);
                            if (visited.Add(hash))
                            {
                                int heuristic = StateHeuristic.Calculate(next);
                                var candidate = new HintNode(next, hash, depth, heuristic);

                                if (!nextByHash.TryGetValue(hash, out HintNode existing)
                                    || candidate.Heuristic < existing.Heuristic)
                                {
                                    nextByHash[hash] = candidate;
                                }
                            }
                        }
                    }
                }

                if (nextByHash.Count == 0)
                    break;

                frontier = nextByHash.Values
                    .OrderBy(node => node.Heuristic)
                    .ThenBy(node => node.Depth)
                    .Take(beamWidth)
                    .ToList();

                foreach (HintNode node in frontier)
                {
                    int score = CalculateHintScore(
                        node.Heuristic,
                        root.FirstMoveBonus,
                        root.ReversePenalty,
                        node.Depth);

                    if (score < rootBestScore)
                        rootBestScore = score;
                }
            }

            if (rootBestScore < bestScore)
            {
                bestScore = rootBestScore;
                bestMove = root.FirstMove;
            }
        }

        return new HintSearchResult(true, bestMove, bestScore, statesEvaluated);
    }

    private static HashSet<Move> FindEquivalentRankAlternatives(
        GameState state,
        List<HintCandidate> candidates)
    {
        var ambiguous = new HashSet<Move>();

        var groups = candidates
            .Where(candidate => candidate.Move.FromColumn >= 0
                && !candidate.IsMilestone
                && candidate.TargetRank > 0)
            .GroupBy(candidate =>
            {
                Card movingCard = state.Tableau[candidate.Move.FromColumn]
                    .Cards[candidate.Move.FromIndex];

                return new AlternativeKey(
                    candidate.Move.FromColumn,
                    candidate.Move.FromIndex,
                    (int)movingCard.Suit,
                    (int)movingCard.Rank,
                    candidate.TargetRank,
                    candidate.TargetSuitMatches);
            });

        foreach (var group in groups)
        {
            List<HintCandidate> alternatives = group.ToList();
            if (alternatives.Count < 2)
                continue;

            int minValue = alternatives.Min(candidate => candidate.ImmediateValue);
            int maxValue = alternatives.Max(candidate => candidate.ImmediateValue);

            // Destinations are treated as equivalent only when their immediate
            // tactical impact is close. A clearly better target remains eligible.
            if (maxValue - minValue <= 24)
            {
                foreach (HintCandidate candidate in alternatives)
                    ambiguous.Add(candidate.Move);
            }
        }

        return ambiguous;
    }

    private static int GetPreviousHintPenalty(Move? previousHint, Move current)
    {
        if (previousHint is not Move previous)
            return 0;

        if (previous.FromColumn < 0 || current.FromColumn < 0)
            return 0;

        if (previous.FromColumn == current.FromColumn
            && previous.FromIndex == current.FromIndex
            && previous.ToColumn == current.ToColumn)
        {
            return 800;
        }

        if (previous.FromColumn == current.ToColumn
            && previous.ToColumn == current.FromColumn)
        {
            return 900;
        }

        // When the same card/run was just hinted to one 9, do not immediately
        // suggest moving that same card/run to the other 9 as a cosmetic change.
        if (previous.FromColumn == current.FromColumn
            && previous.FromIndex == current.FromIndex
            && previous.ToColumn != current.ToColumn)
        {
            return 550;
        }

        return 0;
    }

    private static int CalculateHintScore(
        int heuristic,
        int firstMoveBonus,
        int reversePenalty,
        int depth)
    {
        return heuristic * 10
            - firstMoveBonus * 3
            + reversePenalty
            + Math.Max(0, depth - 1) * 2;
    }

    private static (int Priority, int Heuristic, int Depth, long Order) MakePriority(
        int heuristic,
        int depth,
        int moveBonus,
        long order)
    {
        int priority = heuristic * 3 + depth * 5 - moveBonus * 3;
        return (priority, heuristic, depth, order);
    }

    private static List<Move> ReconstructPath(PathLink? path)
    {
        var moves = new List<Move>(path?.Length ?? 0);
        while (path != null)
        {
            moves.Add(path.Move);
            path = path.Previous;
        }

        moves.Reverse();
        return moves;
    }

    private static int CountFaceDownCards(GameState state)
        => state.Tableau.Sum(pile => pile.FaceDownCount);

    private static int CountTableauCards(GameState state)
        => state.Tableau.Sum(pile => pile.Count);

    private static HintSearchResult NoHint(int statesEvaluated)
        => new(false, new Move(-1, -1, -1), int.MaxValue, statesEvaluated);
}
