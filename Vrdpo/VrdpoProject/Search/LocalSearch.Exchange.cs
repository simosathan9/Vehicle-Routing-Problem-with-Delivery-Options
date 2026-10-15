namespace VrdpoProject
{
    public partial class LocalSearch
    {
        // Exchange move (settings.exchangeCandidates). At the service-level boundary a flip to a lower
        // priority option is blocked: it would take the share of priority-0 customers below 80% (or of
        // priority 0 and 1 below 90%). The exchange makes two flips together, one customer to a worse
        // option and another to a better one, so the service level still holds. The two stops are taken
        // out and each new option is inserted at its best feasible position of any route.
        private sealed class FlipCand
        {
            public Customer Cust;
            public Option From, To;
            public int Route;
            public double Est;
        }

        public ExchangeMove FindBestExchangeMove(ExchangeMove mv, Solution sol)
        {
            int total = sol.Po0Count + sol.Po1Count + sol.Po2Count;
            var buf = new List<Location>();
            var down = new List<FlipCand>();
            var up = new List<FlipCand>();
            for (int pass = 0; pass < 2; pass++)
            {
                bool wantDown = pass == 0;
                if (!wantDown && down.Count == 0) { break; }
                for (int r1 = 0; r1 < sol.Routes.Count; r1++)
                {
                    Route rt1 = sol.Routes[r1];
                    for (int i = 1; i < rt1.SequenceOfOptions.Count - 1; i++)
                    {
                        Customer cust = rt1.SequenceOfCustomers[i];
                        if (cust.Options.Count < 2) { continue; }
                        Option current = rt1.SequenceOfOptions[i];
                        foreach (Option raw in cust.Options)
                        {
                            Option o = sol.Options[raw.Id];
                            if (!o.Allowed) { continue; }
                            if (wantDown ? o.Prio <= current.Prio : o.Prio >= current.Prio) { continue; }
                            if (wantDown)
                            {
                                var (s0, s1) = TempServiceLevelPair(sol.Po0Count, sol.Po1Count, sol.Po2Count, current.Prio, o.Prio);
                                if (s0 >= Solver.MinServiceLevel0 && s1 >= Solver.MinServiceLevel1) { continue; } // the plain flip move already covers it
                            }
                            double est = EstimateFlipInsertion(sol, rt1, i, o, buf);
                            if (est == double.MaxValue) { continue; }
                            (wantDown ? down : up).Add(new FlipCand { Cust = cust, From = current, To = o, Route = r1, Est = est });
                        }
                    }
                }
            }
            if (down.Count == 0 || up.Count == 0) { return mv; }

            var pairs = new List<(double est, FlipCand d, FlipCand u)>();
            foreach (var d in down)
            {
                foreach (var u in up)
                {
                    if (u.Cust == d.Cust) { continue; }
                    if (!ServiceLevelHolds(sol, total, d, u)) { continue; }
                    if (!ExchangeCapacityOk(d, u)) { continue; }
                    pairs.Add((d.Est + u.Est, d, u));
                }
            }
            pairs.Sort((x, y) => x.est.CompareTo(y.est));
            int take = Math.Min(exchangeCandidates, pairs.Count);
            for (int k = 0; k < take; k++)
            {
                EvaluateExchange(mv, sol, new[] { pairs[k].d, pairs[k].u });
            }
            return mv;
        }

        private static bool ServiceLevelHolds(Solution sol, int total, FlipCand d, FlipCand u)
        {
            int p0 = sol.Po0Count, p1 = sol.Po1Count, p2 = sol.Po2Count;
            foreach (var f in new[] { d, u }) { AdjustPrio(ref p0, ref p1, ref p2, f.From.Prio, f.To.Prio); }
            return (double)p0 / total >= Solver.MinServiceLevel0 && (double)(p0 + p1) / total >= Solver.MinServiceLevel1;
        }

        private static void AdjustPrio(ref int p0, ref int p1, ref int p2, int leaving, int entering)
        {
            if (leaving == 0) { p0--; } else if (leaving == 1) { p1--; } else { p2--; }
            if (entering == 0) { p0++; } else if (entering == 1) { p1++; } else { p2++; }
        }

        // Cheapest cost change of taking the stop at `pos` out of rt1 and inserting option `o` anywhere
        // (capacity and time windows of the target route checked).
        private static double EstimateFlipInsertion(Solution sol, Route rt1, int pos, Option o, List<Location> buf)
        {
            Option A = rt1.SequenceOfOptions[pos - 1], B = rt1.SequenceOfOptions[pos], C = rt1.SequenceOfOptions[pos + 1];
            double dAB = sol.CalculateDistance(A.Location, B.Location);
            double dBC = sol.CalculateDistance(B.Location, C.Location);
            double removal = sol.CalculateDistance(A.Location, C.Location) - dAB - dBC;
            double best = double.MaxValue;
            for (int r2 = 0; r2 < sol.Routes.Count; r2++)
            {
                Route rt2 = sol.Routes[r2];
                bool same = rt2 == rt1;
                if (rt2.SequenceOfOptions.Count <= 2) { continue; }
                if (!same && rt2.Load + B.Cust.Dem > rt2.Capacity) { continue; }
                var ro = rt2.SequenceOfOptions;
                for (int p = 0; p < ro.Count - 1; p++)
                {
                    if (same && p == pos) { continue; }
                    double delta;
                    if (same && p == pos - 1)
                    {
                        delta = sol.CalculateDistance(A.Location, o.Location) + sol.CalculateDistance(o.Location, C.Location) - dAB - dBC;
                    }
                    else
                    {
                        Option F = ro[p], G = ro[p + 1];
                        delta = removal + sol.CalculateDistance(F.Location, o.Location) + sol.CalculateDistance(o.Location, G.Location) - sol.CalculateDistance(F.Location, G.Location);
                    }
                    if (delta >= best) { continue; }
                    buf.Clear();
                    var locs = rt2.SequenceOfLocations;
                    for (int j = 0; j < locs.Count; j++)
                    {
                        if (same && j == pos) { continue; }
                        buf.Add(locs[j]);
                        if (j == p) { buf.Add(o.Location); }
                    }
                    if (!sol.SequenceFeasible(buf)) { continue; }
                    best = delta;
                }
            }
            return best;
        }

        // Home locations have MaxCap -1 (no limit); only the shared lockers are limited.
        private static bool LocationFull(Location l, int extra)
        {
            return l.MaxCap >= 0 && l.Cap + extra > l.MaxCap;
        }

        // The place a flip releases at a shared locker counts for the other flip.
        private static bool ExchangeCapacityOk(FlipCand d, FlipCand u)
        {
            var fs = new[] { d, u };
            foreach (var f in fs)
            {
                foreach (Location l in new[] { f.To.Location, f.From.Location })
                {
                    int net = 0;
                    foreach (var g in fs)
                    {
                        if (g.To.Location == l) { net++; }
                        if (g.From.Location == l) { net--; }
                    }
                    if (net > 0 && LocationFull(l, net)) { return false; }
                }
            }
            return true;
        }

        // Takes both stops out, inserts the two new options at their best feasible positions (both orders
        // tried) and keeps the result if it is the best so far.
        private void EvaluateExchange(ExchangeMove mv, Solution sol, FlipCand[] fs)
        {
            int R = sol.Routes.Count;
            var baseLists = new List<Option>[R];
            var baseLoads = new double[R];
            int nonEmptyBefore = 0;
            for (int r = 0; r < R; r++)
            {
                baseLists[r] = new List<Option>(sol.Routes[r].SequenceOfOptions);
                baseLoads[r] = sol.Routes[r].Load;
                if (baseLists[r].Count > 2) { nonEmptyBefore++; }
            }
            double removalDelta = 0;
            foreach (var f in fs)
            {
                var list = baseLists[f.Route];
                int idx = list.IndexOf(f.From);
                Option a = list[idx - 1], c = list[idx + 1];
                removalDelta += sol.CalculateDistance(a.Location, c.Location) - sol.CalculateDistance(a.Location, f.From.Location) - sol.CalculateDistance(f.From.Location, c.Location);
                list.RemoveAt(idx);
                baseLoads[f.Route] -= f.Cust.Dem;
            }

            bool countedImproving = false, countedAdmissible = false;
            for (int order = 0; order < 2; order++)
            {
                var lists = new List<Option>[R];
                var loads = (double[])baseLoads.Clone();
                for (int r = 0; r < R; r++) { lists[r] = new List<Option>(baseLists[r]); }
                double delta = removalDelta;
                var touched = new SortedSet<int>();
                foreach (var f in fs) { touched.Add(f.Route); }
                bool ok = true;
                for (int k = 0; k < fs.Length && ok; k++)
                {
                    var f = order == 0 ? fs[k] : fs[fs.Length - 1 - k];
                    var (r, p, ins) = BestInsertion(sol, lists, loads, f.To, f.Cust.Dem);
                    if (r < 0) { ok = false; break; }
                    lists[r].Insert(p + 1, f.To);
                    loads[r] += f.Cust.Dem;
                    delta += ins;
                    touched.Add(r);
                }
                if (!ok) { continue; }
                int nonEmptyAfter = 0;
                for (int r = 0; r < R; r++) { if (lists[r].Count > 2) { nonEmptyAfter++; } }
                double totalCost = delta + (R - nonEmptyBefore + nonEmptyAfter) * Solver.RoutePenalty;
                if (Stats != null && MoveStats.Improves(nonEmptyAfter - nonEmptyBefore, delta) && ExchangeRoutesFeasible(sol, touched, lists))
                {
                    countedImproving = true;
                    if (!ExchangePromiseBroken(sol, touched, lists, delta)) { countedAdmissible = true; }
                }
                if (!(totalCost < mv.TotalCost + smallDouble)) { continue; }
                if (ExchangePromiseBroken(sol, touched, lists, delta)) { continue; }
                if (!ExchangeRoutesFeasible(sol, touched, lists)) { continue; }
                mv.TotalCost = totalCost;
                mv.MoveCost = delta;
                mv.Flips = new List<(Option from, Option to)>();
                foreach (var f in fs) { mv.Flips.Add((f.From, f.To)); }
                mv.Changes = new List<(int route, List<Option> stops)>();
                foreach (int r in touched) { mv.Changes.Add((r, lists[r])); }
            }
            if (Stats != null)
            {
                Stats.Evaluated[MoveStats.Exchange]++;
                if (countedImproving) { Stats.Improving[MoveStats.Exchange]++; }
                if (countedAdmissible) { Stats.Admissible[MoveStats.Exchange]++; }
            }
        }

        private static (int route, int pos, double delta) BestInsertion(Solution sol, List<Option>[] lists, double[] loads, Option o, int dem)
        {
            double best = double.MaxValue; int bestR = -1, bestP = -1;
            var buf = new List<Location>();
            for (int r = 0; r < lists.Length; r++)
            {
                if (loads[r] + dem > sol.Routes[r].Capacity) { continue; }
                var l = lists[r];
                if (l.Count <= 2) { continue; }
                for (int p = 0; p < l.Count - 1; p++)
                {
                    Option F = l[p], G = l[p + 1];
                    double delta = sol.CalculateDistance(F.Location, o.Location) + sol.CalculateDistance(o.Location, G.Location) - sol.CalculateDistance(F.Location, G.Location);
                    if (delta >= best) { continue; }
                    buf.Clear();
                    for (int j = 0; j < l.Count; j++)
                    {
                        buf.Add(l[j].Location);
                        if (j == p) { buf.Add(o.Location); }
                    }
                    if (!sol.SequenceFeasible(buf)) { continue; }
                    best = delta; bestR = r; bestP = p;
                }
            }
            return (bestR, bestP, best);
        }

        private static bool ExchangeRoutesFeasible(Solution sol, SortedSet<int> touched, List<Option>[] lists)
        {
            var buf = new List<Location>();
            foreach (int r in touched)
            {
                buf.Clear();
                foreach (Option o in lists[r]) { buf.Add(o.Location); }
                if (lists[r].Count > 2 && !sol.SequenceFeasible(buf)) { return false; }
            }
            return true;
        }

        // Same promise rule as the other moves, applied to every arc the exchange creates.
        private bool ExchangePromiseBroken(Solution sol, SortedSet<int> touched, List<Option>[] lists, double delta)
        {
            foreach (int r in touched)
            {
                var old = new HashSet<(int, int)>();
                var oldList = sol.Routes[r].SequenceOfOptions;
                for (int j = 0; j < oldList.Count - 1; j++) { old.Add((oldList[j].Id, oldList[j + 1].Id)); }
                var nl = lists[r];
                for (int j = 0; j < nl.Count - 1; j++)
                {
                    if (old.Contains((nl[j].Id, nl[j + 1].Id))) { continue; }
                    if (PromiseIsBroken(nl[j].Id, nl[j + 1].Id, delta + sol.Cost + smallDouble, sol)) { return true; }
                }
            }
            return false;
        }

        public void ApplyExchangeMove(ExchangeMove mv, Solution sol)
        {
            if (!mv.IsValid()) { return; }
            sol.LastMove = "exchange";
            var newArcs = new List<(int, int)>();
            foreach (var (r, stops) in mv.Changes)
            {
                var oldList = sol.Routes[r].SequenceOfOptions;
                var old = new HashSet<(int, int)>();
                for (int j = 0; j < oldList.Count - 1; j++) { old.Add((oldList[j].Id, oldList[j + 1].Id)); }
                for (int j = 0; j < stops.Count - 1; j++)
                {
                    if (!old.Contains((stops[j].Id, stops[j + 1].Id))) { newArcs.Add((stops[j].Id, stops[j + 1].Id)); }
                }
                RebuildRoute(sol.Routes[r], stops, sol);
            }
            sol.Cost += mv.MoveCost;
            foreach (var (from, to) in mv.Flips)
            {
                sol.AdjustServiceLevelCounts(from.Prio, to.Prio);
                from.IsServed = false;
                to.IsServed = true;
                to.Location.Cap++;
                from.Location.Cap--;
            }
            foreach (var (a, b) in newArcs) { sol.Promises[a, b] = sol.Cost; }
        }

        // Replaces the stop sequence of a route (depots included) and refreshes everything derived from it.
        private static void RebuildRoute(Route rt, List<Option> opts, Solution sol)
        {
            Customer custStart = rt.SequenceOfCustomers[0];
            Customer custEnd = rt.SequenceOfCustomers[rt.SequenceOfCustomers.Count - 1];
            double ect0 = rt.SequenceOfEct[0];
            double latEnd = rt.SequenceOfLat[rt.SequenceOfLat.Count - 1];
            var custs = new List<Customer>(opts.Count);
            var locs = new List<Location>(opts.Count);
            for (int i = 0; i < opts.Count; i++)
            {
                custs.Add(i == 0 ? custStart : (i == opts.Count - 1 ? custEnd : opts[i].Cust));
                locs.Add(opts[i].Location);
            }
            rt.SequenceOfOptions = new List<Option>(opts);
            rt.SequenceOfCustomers = custs;
            rt.SequenceOfLocations = locs;
            rt.SequenceOfEct = new List<double>(new double[opts.Count]);
            rt.SequenceOfLat = new List<double>(new double[opts.Count]);
            rt.SequenceOfEct[0] = ect0;
            rt.SequenceOfLat[opts.Count - 1] = latEnd;
            sol.UpdateTimes(rt);
            UpdateRouteCostAndLoad(rt, sol);
            rt.RouteUtilizationMetric = UtilizationTerm(Convert.ToDouble(rt.Capacity - rt.Load));
        }
    }
}
