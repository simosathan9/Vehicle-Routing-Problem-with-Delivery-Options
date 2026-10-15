namespace VrdpoProject
{
    // Per-iteration move counts of the neighborhood scans (settings.countMoves).
    //
    //   evaluated   candidate moves whose cost the scan computed (capacity-feasible candidates; the
    //               exchange counts the pairs it evaluates exactly, at most exchangeCandidates)
    //   improving   evaluated candidates that improve the solution lexicographically (fewer used vehicles,
    //               or the same and a lower distance) and are feasible (capacity, time windows,
    //               shared-location capacity, service levels). Used vehicles = non-empty routes: unlike the
    //               scans' own route penalty, a move that fills the spare empty route counts as one more.
    //   admissible  improving candidates that the promise memory does not forbid, i.e. the ones the
    //               search could apply in this iteration
    //
    // Counting removes the scans' cost-first pruning only for the counters: every improving candidate is
    // checked for feasibility, not just the ones better than the best found so far. The move that is
    // applied is the same as without counting.
    public sealed class MoveStats
    {
        public const int Swap = 0, Relocation = 1, TwoOpt = 2, Flip = 3, Exchange = 4;
        public static readonly string[] Names = { "swap", "relocate", "twoopt", "flip", "exchange" };

        // Below this distance change a move counts as improving (move costs are distances, x1 or x10).
        public const double ImprovementTolerance = 1e-6;

        public readonly long[] Evaluated = new long[Names.Length];
        public readonly long[] Improving = new long[Names.Length];
        public readonly long[] Admissible = new long[Names.Length];

        public void Reset()
        {
            Array.Clear(Evaluated);
            Array.Clear(Improving);
            Array.Clear(Admissible);
        }

        // Lexicographic improvement: fewer open routes first, then distance.
        public static bool Improves(int routeChange, double moveCost)
        {
            return routeChange < 0 || (routeChange == 0 && moveCost < -ImprovementTolerance);
        }

    }
}
