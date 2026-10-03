using System;
using System.Collections.Generic;

namespace VrdpoProject
{
    // ---- Route elimination (settings.routeEliminationEvery) ----
    // The other moves change the fleet only by relocating the stops of a route out one at a time; every intermediate step is a
    // worsening detour that has to win the iteration on its own, and the 10000 bonus comes only with the last stop, so in
    // practice the number of routes is whatever construction produced. Every instance where the solver uses one vehicle more
    // than the best known solution sits at the demand lower bound with well under 1% of total capacity to spare, so removing
    // the last route is a bin-packing problem as much as a routing one.
    //
    // This move takes a small route apart and puts its customers back with ANY of their options (an ejected customer that
    // does not fit at home usually fits at a locker, whose window is much wider). A customer that fits nowhere is placed by an
    // ejection chain built for tight capacity:
    //   1. direct insertion, any option, any route;
    //   2. depth-2 chain: insert the customer where a stop of strictly smaller demand makes room, and relocate that stop
    //      directly somewhere else (the pool does not grow);
    //   3. eject one stop of strictly smaller demand into the pool (the pool gets easier to pack);
    //   4. eject two stops of smaller demand from one route (creates slack at the price of a larger pool).
    // Demands strictly decrease along a chain, so chains are finite; a customer is ejected at most three times and the number of
    // steps is bounded. The service level may drop inside the chain; at the end the cheapest up-flips restore it to at least
    // where it was. Everything is computed on copies of the stop lists; the solution is only touched when the whole chain
    // succeeds, so a failed attempt leaves it exactly as it was.
    public partial class LocalSearch
    {
        private const double EliminationThresholdSl0 = 0.8;
        private const double EliminationThresholdSl1 = 0.9;
        private const int EliminationMaxEjectionsPerCustomer = 3;
        private const int EliminationCandidateRoutes = 3;

        // Tries to remove one non-empty route (the smallest ones first, up to EliminationCandidateRoutes of them).
        // Returns true (and mutates sol) on success; sol is unchanged on failure. `info` describes what happened.
        public bool TryEliminateRoute(Solution sol, out string info)
        {
            var candidates = new List<int>();
            int open = 0;
            for (int r = 0; r < sol.Routes.Count; r++)
            {
                if (sol.Routes[r].SequenceOfOptions.Count > 2) { open++; candidates.Add(r); }
            }
            if (open <= sol.LowerBoundRoutes)
            {
                info = "at lower bound";
                return false;
            }
            candidates.Sort((a, b) =>
            {
                int sa = sol.Routes[a].SequenceOfOptions.Count, sb = sol.Routes[b].SequenceOfOptions.Count;
                if (sa != sb) { return sa.CompareTo(sb); }
                int c = sol.Routes[a].Load.CompareTo(sol.Routes[b].Load);
                return c != 0 ? c : a.CompareTo(b);
            });
            var reasons = new List<string>();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int k = 0; k < Math.Min(EliminationCandidateRoutes, candidates.Count); k++)
            {
                if (TryEliminateRouteAt(sol, candidates[k], out string attempt))
                {
                    info = $"{attempt} [{watch.ElapsedMilliseconds} ms]";
                    return true;
                }
                reasons.Add($"route {candidates[k]}: {attempt}");
            }
            info = string.Join(" | ", reasons) + $" [{watch.ElapsedMilliseconds} ms]";
            return false;
        }

        private bool TryEliminateRouteAt(Solution sol, int target, out string info)
        {
            int R = sol.Routes.Count;
            int targetStops = sol.Routes[target].SequenceOfOptions.Count - 2;

            // Working copies.
            var lists = new List<Option>[R];
            var loads = new double[R];
            var caps = new double[R];
            for (int r = 0; r < R; r++)
            {
                lists[r] = new List<Option>(sol.Routes[r].SequenceOfOptions);
                loads[r] = sol.Routes[r].Load;
                caps[r] = sol.Routes[r].Capacity;
            }
            var pool = new List<Option>();                       // the option each pooled customer was served with when ejected
            for (int i = 1; i < lists[target].Count - 1; i++) { pool.Add(lists[target][i]); }
            lists[target] = new List<Option> { lists[target][0], lists[target][lists[target].Count - 1] };
            loads[target] = 0;

            int p0 = sol.Po0Count, p1 = sol.Po1Count, p2 = sol.Po2Count;
            int total = p0 + p1 + p2;
            var net = new Dictionary<Location, int>();          // net change of the number of customers at a shared location
            var ejectedTimes = new Dictionary<int, int>();       // customer id -> how often it has been ejected
            var flipOf = new Dictionary<int, (Option from, Option to)>(); // customer id -> served option before / after the move
            var touched = new SortedSet<int> { target };
            int maxSteps = 8 * pool.Count + 20;
            int steps = 0, ejections = 0, chains = 0, swaps = 0;

            int Ejected(Customer c) => ejectedTimes.TryGetValue(c.Id, out int t) ? t : 0;
            void MarkEjected(Customer c) => ejectedTimes[c.Id] = Ejected(c) + 1;
            double Dist(Option a, Option b) => sol.CalculateDistance(a.Location, b.Location);
            double RemovalDelta(List<Option> l, int q) => Dist(l[q - 1], l[q + 1]) - Dist(l[q - 1], l[q]) - Dist(l[q], l[q + 1]);
            // Would placing `o` (instead of `leaving`) overfill its shared location, given `freed` places released in the same step?
            bool Full(Option o, Option leaving, int freed) => o != leaving && LocationFull(o.Location, NetAt(net, o.Location) + 1 - freed);

            // 1) Cheapest feasible insertion of any option of c, anywhere.
            bool Direct(Customer c, Option cur)
            {
                double best = double.MaxValue; int bestR = -1, bestP = -1; Option bestO = null;
                foreach (Option raw in c.Options)
                {
                    Option o = sol.Options[raw.Id];
                    if (Full(o, cur, 0)) { continue; }
                    var (r, p, d) = BestInsertion(sol, lists, loads, o, c.Dem);
                    if (r >= 0 && d < best) { best = d; bestR = r; bestP = p; bestO = o; }
                }
                if (bestR < 0) { return false; }
                lists[bestR].Insert(bestP + 1, bestO);
                loads[bestR] += c.Dem;
                RecordPlacement(c, cur, bestO, net, flipOf, ref p0, ref p1, ref p2);
                touched.Add(bestR);
                return true;
            }

            // 2) Depth-2 chain: c goes where a stop v of strictly smaller demand leaves, and v is inserted directly elsewhere.
            bool Chain2(Customer c, Option cur)
            {
                double best = double.MaxValue;
                int bR = -1, bQ = -1, bP = -1, b2R = -1, b2P = -1; Option bO = null, bV = null, bOv = null;
                for (int r = 0; r < R; r++)
                {
                    var l = lists[r];
                    if (l.Count <= 2) { continue; }
                    for (int q = 1; q < l.Count - 1; q++)
                    {
                        Option v = l[q];
                        if (v.Cust == c || v.Cust.Dem > c.Dem || Ejected(v.Cust) >= EliminationMaxEjectionsPerCustomer) { continue; }
                        double remV = RemovalDelta(l, q);
                        if (remV >= best) { continue; }        // insertion deltas are non-negative (Euclidean), so this is a bound
                        l.RemoveAt(q);
                        loads[r] -= v.Cust.Dem;
                        foreach (Option raw in c.Options)
                        {
                            Option o = sol.Options[raw.Id];
                            if (Full(o, cur, v.Location == o.Location ? 1 : 0)) { continue; }
                            var (p, d1) = BestInsertionIn(sol, l, loads[r], caps[r], o, c.Dem);
                            if (p < 0 || remV + d1 >= best) { continue; }
                            l.Insert(p + 1, o);
                            loads[r] += c.Dem;
                            foreach (Option rawV in v.Cust.Options)
                            {
                                Option ov = sol.Options[rawV.Id];
                                int freed = (o != cur && cur.Location == ov.Location ? 1 : 0) - (o != cur && o.Location == ov.Location ? 1 : 0);
                                if (Full(ov, v, freed)) { continue; }
                                var (r2, p2, d2) = BestInsertion(sol, lists, loads, ov, v.Cust.Dem);
                                if (r2 < 0) { continue; }
                                double tot = remV + d1 + d2;
                                if (tot < best) { best = tot; bR = r; bQ = q; bP = p; bO = o; bV = v; b2R = r2; b2P = p2; bOv = ov; }
                            }
                            l.RemoveAt(p + 1);
                            loads[r] -= c.Dem;
                        }
                        l.Insert(q, v);
                        loads[r] += v.Cust.Dem;
                    }
                }
                if (bR < 0) { return false; }
                lists[bR].RemoveAt(bQ);
                loads[bR] -= bV.Cust.Dem;
                lists[bR].Insert(bP + 1, bO);
                loads[bR] += c.Dem;
                RecordPlacement(c, cur, bO, net, flipOf, ref p0, ref p1, ref p2);
                lists[b2R].Insert(b2P + 1, bOv);
                loads[b2R] += bV.Cust.Dem;
                RecordPlacement(bV.Cust, bV, bOv, net, flipOf, ref p0, ref p1, ref p2);
                MarkEjected(bV.Cust);
                touched.Add(bR);
                touched.Add(b2R);
                return true;
            }

            // 3) Eject one stop of strictly smaller demand to make room for c; the victim joins the pool.
            bool Eject1(Customer c, Option cur)
            {
                double best = double.MaxValue;
                int vR = -1, vQ = -1, bP = -1; Option victim = null, bO = null;
                for (int r = 0; r < R; r++)
                {
                    var l = lists[r];
                    if (l.Count <= 2) { continue; }
                    for (int q = 1; q < l.Count - 1; q++)
                    {
                        Option v = l[q];
                        if (v.Cust == c || Ejected(v.Cust) >= EliminationMaxEjectionsPerCustomer) { continue; }
                        // The pool must get easier: a smaller victim, or one of equal demand with more options than c.
                        if (v.Cust.Dem > c.Dem || (v.Cust.Dem == c.Dem && v.Cust.Options.Count <= c.Options.Count)) { continue; }
                        double removal = RemovalDelta(l, q);
                        if (removal >= best) { continue; }
                        l.RemoveAt(q);
                        loads[r] -= v.Cust.Dem;
                        foreach (Option raw in c.Options)
                        {
                            Option o = sol.Options[raw.Id];
                            if (Full(o, cur, v.Location == o.Location ? 1 : 0)) { continue; }
                            var (p, d) = BestInsertionIn(sol, l, loads[r], caps[r], o, c.Dem);
                            if (p >= 0 && removal + d < best) { best = removal + d; vR = r; vQ = q; victim = v; bO = o; bP = p; }
                        }
                        l.Insert(q, v);
                        loads[r] += v.Cust.Dem;
                    }
                }
                if (vR < 0) { return false; }
                lists[vR].RemoveAt(vQ);
                loads[vR] -= victim.Cust.Dem;
                lists[vR].Insert(bP + 1, bO);
                loads[vR] += c.Dem;
                pool.Add(victim);
                MarkEjected(victim.Cust);
                RecordPlacement(c, cur, bO, net, flipOf, ref p0, ref p1, ref p2);
                touched.Add(vR);
                return true;
            }

            // 4) Eject two stops of smaller demand from one route; creates slack at the price of a larger pool.
            bool Eject2(Customer c, Option cur)
            {
                double best = double.MaxValue;
                int vR = -1, q1b = -1, q2b = -1, bP = -1; Option bO = null;
                for (int r = 0; r < R; r++)
                {
                    var l = lists[r];
                    if (l.Count <= 3) { continue; }
                    double baseCost = SequenceCost(sol, l);
                    for (int q1 = 1; q1 < l.Count - 2; q1++)
                    {
                        Option v1 = l[q1];
                        if (v1.Cust == c || v1.Cust.Dem >= c.Dem || Ejected(v1.Cust) >= EliminationMaxEjectionsPerCustomer) { continue; }
                        for (int q2 = q1 + 1; q2 < l.Count - 1; q2++)
                        {
                            Option v2 = l[q2];
                            if (v2.Cust == c || v2.Cust.Dem >= c.Dem || Ejected(v2.Cust) >= EliminationMaxEjectionsPerCustomer) { continue; }
                            if (loads[r] - v1.Cust.Dem - v2.Cust.Dem + c.Dem > caps[r]) { continue; }
                            l.RemoveAt(q2);
                            l.RemoveAt(q1);
                            double load = loads[r] - v1.Cust.Dem - v2.Cust.Dem;
                            double removal = SequenceCost(sol, l) - baseCost;
                            if (removal < best)
                            {
                                foreach (Option raw in c.Options)
                                {
                                    Option o = sol.Options[raw.Id];
                                    int freed = (v1.Location == o.Location ? 1 : 0) + (v2.Location == o.Location ? 1 : 0);
                                    if (Full(o, cur, freed)) { continue; }
                                    var (p, d) = BestInsertionIn(sol, l, load, caps[r], o, c.Dem);
                                    if (p >= 0 && removal + d < best) { best = removal + d; vR = r; q1b = q1; q2b = q2; bO = o; bP = p; }
                                }
                            }
                            l.Insert(q1, v1);
                            l.Insert(q2, v2);
                        }
                    }
                }
                if (vR < 0) { return false; }
                var lv = lists[vR];
                Option w1 = lv[q1b], w2 = lv[q2b];
                lv.RemoveAt(q2b);
                lv.RemoveAt(q1b);
                loads[vR] -= w1.Cust.Dem + w2.Cust.Dem;
                lv.Insert(bP + 1, bO);
                loads[vR] += c.Dem;
                pool.Add(w1);
                pool.Add(w2);
                MarkEjected(w1.Cust);
                MarkEjected(w2.Cust);
                RecordPlacement(c, cur, bO, net, flipOf, ref p0, ref p1, ref p2);
                touched.Add(vR);
                return true;
            }

            // 5) Capacity rebalancing: when c fits nowhere, swap stops of unequal demand between the route with the most free
            //    capacity and the others (both stops may change option) until that route has room for c, then insert c.
            bool ConcentrateSlack(Customer c, Option cur)
            {
                for (int round = 0; round < 12; round++)
                {
                    int rStar = -1; double slackStar = -1;
                    for (int r = 0; r < R; r++)
                    {
                        if (lists[r].Count <= 2) { continue; }
                        double sl = caps[r] - loads[r];
                        if (sl > slackStar) { slackStar = sl; rStar = r; }
                    }
                    if (rStar < 0) { return false; }
                    double needed = c.Dem - slackStar;
                    double bestCost = double.MaxValue; int bestGain = 0;
                    int bQ1 = -1, bR2 = -1, bQ2 = -1, bP1 = -1, bP2 = -1; Option bO1 = null, bO2 = null, bB1 = null, bB2 = null;
                    var l1 = lists[rStar];
                    for (int q1 = 1; q1 < l1.Count - 1; q1++)
                    {
                        Option b1 = l1[q1];
                        if (b1.Cust == c || Ejected(b1.Cust) >= EliminationMaxEjectionsPerCustomer) { continue; }
                        double rem1 = RemovalDelta(l1, q1);
                        l1.RemoveAt(q1);
                        loads[rStar] -= b1.Cust.Dem;
                        for (int r2 = 0; r2 < R; r2++)
                        {
                            if (r2 == rStar || lists[r2].Count <= 2) { continue; }
                            var l2 = lists[r2];
                            for (int q2 = 1; q2 < l2.Count - 1; q2++)
                            {
                                Option b2 = l2[q2];
                                int gain = b1.Cust.Dem - b2.Cust.Dem;
                                if (gain <= 0 || b2.Cust == c || Ejected(b2.Cust) >= EliminationMaxEjectionsPerCustomer) { continue; }
                                if (loads[r2] - b2.Cust.Dem + b1.Cust.Dem > caps[r2]) { continue; }
                                // Prefer a swap that creates the room needed in one go; otherwise the largest gain, then the cheapest.
                                bool enough = gain >= needed, bestEnough = bestGain >= needed && bQ1 >= 0;
                                if (bQ1 >= 0 && bestEnough && !enough) { continue; }
                                double rem2 = RemovalDelta(l2, q2);
                                l2.RemoveAt(q2);
                                loads[r2] -= b2.Cust.Dem;
                                foreach (Option raw2 in b2.Cust.Options)
                                {
                                    Option o2 = sol.Options[raw2.Id];
                                    if (Full(o2, b2, b1.Location == o2.Location ? 1 : 0)) { continue; }
                                    var (p1, d1) = BestInsertionIn(sol, l1, loads[rStar], caps[rStar], o2, b2.Cust.Dem);
                                    if (p1 < 0) { continue; }
                                    foreach (Option raw1 in b1.Cust.Options)
                                    {
                                        Option o1 = sol.Options[raw1.Id];
                                        int freed = (b2.Location == o1.Location ? 1 : 0) - (o2 != b2 && o2.Location == o1.Location ? 1 : 0) + (o2 != b2 && b2.Location == o1.Location ? 0 : 0);
                                        if (Full(o1, b1, freed)) { continue; }
                                        var (p2, d2) = BestInsertionIn(sol, l2, loads[r2], caps[r2], o1, b1.Cust.Dem);
                                        if (p2 < 0) { continue; }
                                        double cost = rem1 + rem2 + d1 + d2;
                                        bool better = bQ1 < 0 || (enough && !bestEnough) || (enough == bestEnough && (enough ? cost < bestCost : (gain > bestGain || (gain == bestGain && cost < bestCost))));
                                        if (better) { bestCost = cost; bestGain = gain; bQ1 = q1; bR2 = r2; bQ2 = q2; bP1 = p1; bP2 = p2; bO1 = o1; bO2 = o2; bB1 = b1; bB2 = b2; }
                                    }
                                }
                                l2.Insert(q2, b2);
                                loads[r2] += b2.Cust.Dem;
                            }
                        }
                        l1.Insert(q1, b1);
                        loads[rStar] += b1.Cust.Dem;
                    }
                    if (bQ1 < 0) { return false; }
                    l1.RemoveAt(bQ1);
                    loads[rStar] -= bB1.Cust.Dem;
                    lists[bR2].RemoveAt(bQ2);
                    loads[bR2] -= bB2.Cust.Dem;
                    l1.Insert(bP1 + 1, bO2);
                    loads[rStar] += bB2.Cust.Dem;
                    lists[bR2].Insert(bP2 + 1, bO1);
                    loads[bR2] += bB1.Cust.Dem;
                    RecordPlacement(bB1.Cust, bB1, bO1, net, flipOf, ref p0, ref p1, ref p2);
                    RecordPlacement(bB2.Cust, bB2, bO2, net, flipOf, ref p0, ref p1, ref p2);
                    MarkEjected(bB1.Cust);
                    MarkEjected(bB2.Cust);
                    touched.Add(rStar);
                    touched.Add(bR2);
                    swaps++;
                    if (Direct(c, cur)) { return true; }
                }
                return false;
            }

            while (pool.Count > 0)
            {
                if (++steps > maxSteps)
                {
                    info = $"step limit ({steps - 1} steps, {ejections} ejections, {chains} chains, {pool.Count} customers left in the pool, route of {targetStops} stops)";
                    return false;
                }
                // Hardest customer first: largest demand, then fewest options.
                int pick = 0;
                for (int k = 1; k < pool.Count; k++)
                {
                    Customer a = pool[k].Cust, b = pool[pick].Cust;
                    if (a.Dem > b.Dem || (a.Dem == b.Dem && a.Options.Count < b.Options.Count)) { pick = k; }
                }
                Option cur = pool[pick];
                pool.RemoveAt(pick);
                Customer c = cur.Cust;

                if (Direct(c, cur)) { continue; }
                if (Chain2(c, cur)) { chains++; continue; }
                if (Eject1(c, cur)) { ejections++; continue; }
                if (Eject2(c, cur)) { ejections += 2; continue; }
                if (ConcentrateSlack(c, cur)) { continue; }
                int roomy = 0;
                for (int r = 0; r < R; r++) { if (lists[r].Count > 2 && caps[r] - loads[r] >= c.Dem) { roomy++; } }
                info = $"customer {c.Id} (demand {c.Dem}, {c.Options.Count} options) cannot be placed: {roomy} routes have the capacity; after {ejections} ejections, {chains} chains, {swaps} swaps; {pool.Count + 1} in the pool, route of {targetStops} stops";
                return false;
            }

            // Service level: the thresholds must hold at the end (the caller only starts an attempt on a solution that meets them).
            int upFlips = 0;
            while (!ServiceLevelAcceptable(p0, p1, total))
            {
                double best = double.MaxValue; int fR = -1, fI = -1, toR = -1, toP = -1; Option fromO = null, toO = null;
                for (int r = 0; r < R; r++)
                {
                    var l = lists[r];
                    for (int i = 1; i < l.Count - 1; i++)
                    {
                        Option cur = l[i];
                        if (cur.Prio == 0) { continue; }
                        Customer c = cur.Cust;
                        double removal = RemovalDelta(l, i);
                        if (removal >= best) { continue; }
                        l.RemoveAt(i);
                        loads[r] -= c.Dem;
                        foreach (Option raw in c.Options)
                        {
                            Option o = sol.Options[raw.Id];
                            if (o.Prio >= cur.Prio) { continue; }
                            if (Full(o, cur, 0)) { continue; }
                            var (rr, pp, dd) = BestInsertion(sol, lists, loads, o, c.Dem);
                            if (rr >= 0 && removal + dd < best) { best = removal + dd; fR = r; fI = i; toR = rr; toP = pp; fromO = cur; toO = o; }
                        }
                        l.Insert(i, cur);
                        loads[r] += c.Dem;
                    }
                }
                if (fR < 0)
                {
                    info = $"service level not restorable (priority-0 customers {p0}/{total}; {ejections} ejections, {chains} chains, {upFlips} up-flips)";
                    return false;
                }
                lists[fR].RemoveAt(fI);
                loads[fR] -= fromO.Cust.Dem;
                lists[toR].Insert(toP + 1, toO);
                loads[toR] += toO.Cust.Dem;
                RecordPlacement(fromO.Cust, fromO, toO, net, flipOf, ref p0, ref p1, ref p2);
                touched.Add(fR);
                touched.Add(toR);
                upFlips++;
            }

            // Final safety checks on the touched routes: time windows, vehicle capacity, shared-location capacity.
            if (!ExchangeRoutesFeasible(sol, touched, lists)) { info = "final time-window check failed"; return false; }
            foreach (int r in touched)
            {
                if (loads[r] > caps[r] + 1e-9) { info = "final capacity check failed"; return false; }
            }
            if (!LocationCapacitiesOk(lists)) { info = "final shared-location check failed"; return false; }

            // Apply.
            double oldCost = 0, newCost = 0;
            var newArcs = new List<(int, int)>();
            foreach (int r in touched)
            {
                var oldList = sol.Routes[r].SequenceOfOptions;
                oldCost += SequenceCost(sol, oldList);
                newCost += SequenceCost(sol, lists[r]);
                var old = new HashSet<(int, int)>();
                for (int j = 0; j < oldList.Count - 1; j++) { old.Add((oldList[j].Id, oldList[j + 1].Id)); }
                for (int j = 0; j < lists[r].Count - 1; j++)
                {
                    if (!old.Contains((lists[r][j].Id, lists[r][j + 1].Id))) { newArcs.Add((lists[r][j].Id, lists[r][j + 1].Id)); }
                }
            }
            var emptied = new List<int>();
            foreach (int r in touched)
            {
                if (lists[r].Count > 2) { RebuildRoute(sol.Routes[r], lists[r], sol); }
                else { emptied.Add(r); }
            }
            sol.Cost += newCost - oldCost;
            int optionChanges = 0;
            foreach (var kv in flipOf)
            {
                var (from, to) = kv.Value;
                if (from == to) { continue; }
                optionChanges++;
                sol.AdjustServiceLevelCounts(from.Prio, to.Prio);
                from.IsServed = false;
                to.IsServed = true;
                sol.Options[from.Id].IsServed = false;
                sol.Options[to.Id].IsServed = true;
                to.Location.Cap++;
                from.Location.Cap--;
            }
            for (int k = emptied.Count - 1; k >= 0; k--) { sol.Routes.RemoveAt(emptied[k]); }
            for (int r = 0; r < sol.Routes.Count; r++) { sol.Routes[r].Id = r; }
            foreach (var (a, b) in newArcs) { sol.Promises[a, b] = sol.Cost; }
            sol.SolutionUtilizationMetric = sol.CalculateUtilizationMetric();
            sol.LastMove = "eliminate";
            info = $"removed route {target} ({targetStops} stops): {emptied.Count} route(s) closed, cost {oldCost:F3} -> {newCost:F3} on {touched.Count} touched routes, {optionChanges} option change(s), {chains} chains, {ejections} ejections, {swaps} swaps, {upFlips} up-flips";
            return true;
        }

        private static bool ServiceLevelAcceptable(int p0, int p1, int total)
        {
            return (double)p0 / total >= EliminationThresholdSl0 && (double)(p0 + p1) / total >= EliminationThresholdSl1;
        }

        private static int NetAt(Dictionary<Location, int> net, Location l)
        {
            return net.TryGetValue(l, out int v) ? v : 0;
        }

        // Customer c, served with `cur` until now, is placed with option `o`: shared-location counts, service-level counters and the
        // per-customer before/after record are updated. `cur` may itself be the result of an earlier placement in this chain.
        private static void RecordPlacement(Customer c, Option cur, Option o, Dictionary<Location, int> net,
            Dictionary<int, (Option from, Option to)> flipOf, ref int p0, ref int p1, ref int p2)
        {
            if (o == cur) { return; }
            net[o.Location] = NetAt(net, o.Location) + 1;
            net[cur.Location] = NetAt(net, cur.Location) - 1;
            AdjustPrio(ref p0, ref p1, ref p2, cur.Prio, o.Prio);
            Option from = flipOf.TryGetValue(c.Id, out var rec) ? rec.from : cur;
            flipOf[c.Id] = (from, o);
        }

        // Cheapest feasible position for option `o` inside one stop list (capacity and time windows checked).
        private (int pos, double delta) BestInsertionIn(Solution sol, List<Option> l, double load, double cap, Option o, int dem)
        {
            if (load + dem > cap || l.Count <= 2) { return (-1, double.MaxValue); }
            double best = double.MaxValue; int bestP = -1;
            var buf = new List<Location>(l.Count + 1);
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
                best = delta; bestP = p;
            }
            return (bestP, best);
        }

        // Counts the stops per shared location over all lists (the lists hold every served customer) against MaxCap.
        private static bool LocationCapacitiesOk(List<Option>[] lists)
        {
            var count = new Dictionary<Location, int>();
            foreach (var l in lists)
            {
                for (int i = 1; i < l.Count - 1; i++)
                {
                    Location loc = l[i].Location;
                    if (loc.MaxCap < 0) { continue; }
                    int n = (count.TryGetValue(loc, out int v) ? v : 0) + 1;
                    if (n > loc.MaxCap) { return false; }
                    count[loc] = n;
                }
            }
            return true;
        }

        private static double SequenceCost(Solution sol, List<Option> l)
        {
            double c = 0;
            for (int j = 0; j < l.Count - 1; j++) { c += sol.CalculateDistance(l[j].Location, l[j + 1].Location); }
            return c;
        }
    }
}
