namespace VrdpoProject
{
    // Neighborhood scans (FindBest*Move) and move application (Apply*Move); one file per operator.
    //
    // Every scan evaluates a move as ratio * moveCost + openRoutes * RoutePenalty, where moveCost is the
    // distance change, openRoutes the number of non-empty routes after the move, and ratio
    // (old utilization metric + 1) / (new utilization metric + 1) favours consolidated loads; the ratio is
    // 1 once the fleet is at its lower bound. Arcs the move creates must beat their promise (the solution
    // cost when the arc was last created, see PromiseIsBroken), and the route(s) must stay time-window
    // and capacity feasible.
    public partial class LocalSearch
    {
        // Tolerance when comparing move values ("int" metric: exact).
        private readonly double smallDouble;
        private readonly int exchangeCandidates;

        public LocalSearch(Settings settings)
        {
            smallDouble = settings.IntegerMetric ? 0 : 0.00001;
            exchangeCandidates = settings.ExchangeCandidates;
        }

        // (Capacity - Load)^2 of a route, the per-route utilization term.
        private static double UtilizationTerm(double freeCapacity)
        {
            return Math.Pow(freeCapacity, 2);
        }

        // Same value as UtilizationTerm for the integral arguments the scans pass, without the Math.Pow call.
        private static double SquareOrPow(double x)
        {
            if (x == Math.Floor(x) && Math.Abs(x) <= 20000000.0)
            {
                return x * x;
            }
            return Math.Pow(x, 2);
        }

        private static bool PromiseIsBroken(int a, int b, double newCost, Solution sol)
        {
            return newCost >= sol.Promises[a, b];
        }

        // Service level after one option of priority `leavingPriority` is replaced by one of `enteringPriority`.
        private static (double sl0, double sl1) TempServiceLevelPair(int po0Sum, int po1Sum, int po2Sum, int leavingPriority, int enteringPriority)
        {
            switch (leavingPriority)
            {
                case 0: po0Sum--; break;
                case 1: po1Sum--; break;
                case 2: po2Sum--; break;
            }
            switch (enteringPriority)
            {
                case 0: po0Sum++; break;
                case 1: po1Sum++; break;
                case 2: po2Sum++; break;
            }
            double sum = po0Sum + po1Sum + po2Sum;
            return (po0Sum / sum, (po0Sum + po1Sum) / sum);
        }

        public static void UpdateRouteCostAndLoad(Route rt, Solution sol)
        {
            double tc = 0;
            double tl = 0;
            for (int i = 0; i < rt.SequenceOfOptions.Count - 1; i++)
            {
                Option a = rt.SequenceOfOptions[i];
                Option b = rt.SequenceOfOptions[i + 1];
                tc += sol.CalculateDistance(a.Location, b.Location);
                tl += a.Cust.Dem;
            }
            rt.Load = tl;
            rt.Cost = tc;
        }

        // Runtime assertion run before and after every applied move; prints the violation.
        private static void AssertRouteFeasible(Solution sol, Route rt, string when)
        {
            if (!sol.CheckRouteFeasibility(rt))
            {
                Console.WriteLine($"[check] route {rt.Id} infeasible {when}");
            }
        }
    }
}
