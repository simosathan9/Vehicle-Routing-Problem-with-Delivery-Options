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
        // Arc memory (settings.memory): promise rule, or tabu tenure on removed arcs.
        private readonly bool tabu;
        private readonly int tabuTenure;
        private readonly double tabuTenureMin, tabuTenureMax;
        private readonly bool tabuAspiration;
        // Set by the solver: the current iteration of the restart, the restart's best cost so far (aspiration),
        // the random generator of the dynamic tenure (seeded per restart) and the number of customers.
        public int Iteration { get; set; }
        public double AspirationCost { get; set; } = double.MaxValue;
        public Random TenureRandom { get; set; }
        public int Customers { get; set; }
        // Move counters of the current iteration (settings.countMoves); null when counting is off.
        public MoveStats Stats { get; }
        // Inter-route coupling counters of the current iteration (settings.countCoupling); null when off.
        public CouplingStats Coupling { get; }

        public LocalSearch(Settings settings)
        {
            smallDouble = settings.IntegerMetric ? 0 : 0.00001;
            exchangeCandidates = settings.ExchangeCandidates;
            tabu = settings.TabuMemory;
            tabuTenure = settings.TabuTenure;
            tabuTenureMin = settings.TabuTenureMin;
            tabuTenureMax = settings.TabuTenureMax;
            tabuAspiration = settings.TabuAspiration;
            Stats = settings.CountMoves ? new MoveStats() : null;
            Coupling = settings.CountCoupling ? new CouplingStats() : null;
        }

        // The promise rule of the scans for one created arc (true = the arc is allowed).
        private bool PromiseHolds(int a, int b, double moveCost, Solution sol)
        {
            return !PromiseIsBroken(a, b, moveCost + sol.Cost + smallDouble, sol);
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

        // Is creating arc (a, b) forbidden by the memory? newCost = the solution cost after the move.
        //   promise: forbidden when the new solution is not better than the one in which the arc was last created.
        //   tabu:    forbidden while the arc is tabu (it was removed less than its tenure ago), unless aspiration:
        //            the new solution beats the restart's best.
        private bool PromiseIsBroken(int a, int b, double newCost, Solution sol)
        {
            if (!tabu)
            {
                return newCost >= sol.Promises[a, b];
            }
            // A move whose distance change is numerically zero does not change the solution's value: the scans mean to
            // exclude it (moveCost != 0) but floating-point noise (1e-13) lets it through, and a chain of such swaps
            // between stops at one location cycles for ever. The promise rule rejects it by its strict improvement test;
            // the tabu rule has to reject it explicitly.
            if (Math.Abs(newCost - sol.Cost - smallDouble) < 1e-7) { return true; }
            if (a > b) { (a, b) = (b, a); }   // the tabu memory is undirected (symmetric distances)
            if (tabuTenure < 0 || Iteration >= sol.Promises[a, b])
            {
                return false;
            }
            return !(tabuAspiration && newCost < AspirationCost);
        }

        // Memory update after a move: the promise rule records the cost on the created arcs; tabu records, on the
        // removed arcs, the iteration from which they may be re-created. A removed arc that the same move re-creates
        // in the other direction (adjacent stops of one route) is still in the solution and is not made tabu.
        private void UpdateMemory(Solution sol, (int a, int b)[] created, (int a, int b)[] removed)
        {
            if (!tabu)
            {
                foreach (var (a, b) in created) { sol.Promises[a, b] = sol.Cost; }
                return;
            }
            if (tabuTenure < 0) { return; }
            foreach (var (a, b) in removed)
            {
                bool stillThere = false;
                foreach (var (c, d) in created)
                {
                    if ((c == a && d == b) || (c == b && d == a)) { stillThere = true; break; }
                }
                if (stillThere) { continue; }
                int tenure = tabuTenure;
                if (tenure == 0)
                {
                    int lo = (int)Math.Ceiling(tabuTenureMin * Customers);
                    int hi = (int)Math.Ceiling(tabuTenureMax * Customers);
                    tenure = TenureRandom.Next(lo, hi + 1);
                }
                sol.Promises[Math.Min(a, b), Math.Max(a, b)] = Iteration + 1 + tenure;
            }
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
