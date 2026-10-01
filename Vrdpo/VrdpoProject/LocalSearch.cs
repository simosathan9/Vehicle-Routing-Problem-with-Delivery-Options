using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;

namespace VrdpoProject
{

    public class LocalSearch
    {
        public static int power = 2;
        private double smallDouble;
        // NOTE (perf, Phase 1a): added so ApplyPrioritySwapMove's diagnostic Console.WriteLine block
        // below can be gated consistently with Solver's existing `settings.verbal` behavior, instead
        // of printing unconditionally on every accepted priority-swap move regardless of settings.
        private bool verbal;
        private bool fixSameRouteTwoOpt;
        private Route rt1, rt2;
        public LocalSearch()
        {
            string jsonContent = File.ReadAllText("settings.json");
            var settings = JsonSerializer.Deserialize<Settings>(jsonContent);
            if (settings.type == "double")
            {
                this.smallDouble = 0.00001;
            } else if (settings.type == "int")
            {
                this.smallDouble = 0;
            }
            this.verbal = settings.verbal;
            this.fixSameRouteTwoOpt = settings.fixSameRouteTwoOpt;
            Customer.FixCloneBug = settings.fixCloneSideEffect;
        }
        public Relocation FindBestRelocationMove(Relocation rm, Solution sol)
        {
            // NOTE (perf/correctness, Phase 4 prep): every other operator in this class
            // (FindBestSwapMove, FindBestTwoOptMove, FindBestFlipMove, FindBestPrioritySwapMove)
            // declares its own local `Route rt1, rt2;` that shadows the class-level `rt1`/`rt2`
            // fields declared near the top of LocalSearch. This method was the one exception — it
            // read/wrote the shared INSTANCE FIELDS directly. Harmless today because only one
            // restart ever calls into a given `LocalSearch` instance at a time (this class is a
            // single Solver-instance-shared object across all restarts), but it would silently
            // corrupt results if two restarts ever called this method concurrently on the same
            // `ls` instance (Phase 4 parallel-restarts work). Added the same local shadowing
            // declaration every sibling method already has — this is a no-op for the current
            // single-threaded execution (each call still gets its own fresh local, same values,
            // same order) and is required before any parallelization of the restart loop.
            sol.BeginTimeWindowMemo();
            Route rt1, rt2;
            for (int originRouteIndex = 0; originRouteIndex < sol.Routes.Count; originRouteIndex++)
            {
                rt1 = sol.Routes[originRouteIndex];

                for (int targetRouteIndex = 0; targetRouteIndex < sol.Routes.Count; targetRouteIndex++)
                {
                    rt2 = sol.Routes[targetRouteIndex];

                    // NOTE (perf, Phase 4): the improving test below contains `targetRouteIndex != 0`, so no candidate
                    // targeting route 0 can ever be recorded. Everything else done for such a candidate was pure
                    // (the only write, `sol.RatioCombinedMoveCost`, is a scratch value that is assigned and then read on the
                    // very next comparison), so the whole route is skipped.
                    if (targetRouteIndex == 0) { continue; }

                    for (int originOptionIndex = 1; originOptionIndex < rt1.SequenceOfOptions.Count - 1; originOptionIndex++)
                    {
                        Option A = rt1.SequenceOfOptions[originOptionIndex - 1];
                        Option B = rt1.SequenceOfOptions[originOptionIndex];
                        Option C = rt1.SequenceOfOptions[originOptionIndex + 1];

                        // NOTE (perf, Phase 4): loop-invariant work hoisted out of the target-position loop. Each hoisted
                        // value is the very same expression the loop used to recompute per candidate (same operands, same
                        // association, e.g. `(dAB + dBC)` is exactly the left operand of the old `costRemoved` sum), so
                        // every double is bit-for-bit what it was. What depends on the target position (F, G and the
                        // three distances involving them) is still computed per candidate.
                        //  - capacity of the target route: when it fails, every target position used to `continue`.
                        if (rt1 != rt2)
                        {
                            if (rt2.Load + B.Cust.Dem > rt2.Capacity)
                            {
                                continue;
                            }
                        }
                        int openRoutes = sol.Routes.Count;
                        if (rt1.Load - B.Cust.Dem == 0) { // if route becomes empty
                            openRoutes--;
                        }
                        double dAB = sol.CalculateDistance(A.Location, B.Location);
                        double dBC = sol.CalculateDistance(B.Location, C.Location);
                        double dAC = sol.CalculateDistance(A.Location, C.Location);
                        double costRemovedBase = dAB + dBC;
                        double costChangeOriginRt = dAC - dAB - dBC;

                        var newUtilizationMetricRoute1 = Math.Pow(Convert.ToDouble(rt1.Capacity - (rt1.Load - B.Cust.Dem)), power);
                        var newUtilizationMetricRoute2 = Math.Pow(Convert.ToDouble(rt2.Capacity - (rt2.Load + B.Cust.Dem)), power);
                        var newSolUtilizationMetric = sol.SolutionUtilizationMetric - rt1.RouteUtilizationMetric - rt2.RouteUtilizationMetric + newUtilizationMetricRoute1 + newUtilizationMetricRoute2;
                        var ratio = (sol.SolutionUtilizationMetric + 1) / (newSolUtilizationMetric + 1);
                        if (sol.Routes.Count == sol.LowerBoundRoutes)
                        {
                            ratio = 1;
                        }

                        for (int targetOptionIndex = 0; targetOptionIndex < rt2.SequenceOfOptions.Count - 1; targetOptionIndex++)
                        {
                            if (originRouteIndex == targetRouteIndex && (targetOptionIndex == originOptionIndex || targetOptionIndex == originOptionIndex - 1))
                            {
                                continue;
                            }

                            // NOTE (perf, Phase 4): the time-window feasibility check used to run here, first, for every
                            // candidate. It is a pure function of (rt2, targetOptionIndex, the moved location) — no side
                            // effects — and everything between here and the "is this candidate better than the best so
                            // far" test below is pure too (`sol.RatioCombinedMoveCost` is a scratch value: assigned and then
                            // read on the very next comparison, only ever copied by DeepCopy into a field nobody reads).
                            // So the check is deferred to the moment a candidate would actually be recorded (inside the
                            // improving branch); candidates that are infeasible, capacity-violating, or not better than the
                            // incumbent get rejected exactly as before, just without paying for the check. The recorded
                            // move is unchanged.
                            Option F = rt2.SequenceOfOptions[targetOptionIndex];
                            Option G = rt2.SequenceOfOptions[targetOptionIndex + 1];

                            double dFB = sol.CalculateDistance(F.Location, B.Location);
                            double dBG = sol.CalculateDistance(B.Location, G.Location);
                            double dFG = sol.CalculateDistance(F.Location, G.Location);
                            double costAdded = dAC + dFB + dBG;
                            double costRemoved = costRemovedBase + dFG;
                            double moveCost = costAdded - costRemoved;

                            sol.RatioCombinedMoveCost = ratio * moveCost;

                            //favor relocations from very small routes
                            //int bonus = 0;
                            //if (rt1.SequenceOfLocations.Count <= 4)
                            //{
                            //    bonus = -2000;
                            //}
                            ////prevent relocating to empty/small routes
                            //if (rt2.SequenceOfLocations.Count <= 4)
                            //{
                            //    continue;
                            //}
                            if (sol.RatioCombinedMoveCost + openRoutes * 10000 < rm.TotalCost + smallDouble & targetRouteIndex != 0 & moveCost != 0) // + bpnus
                            {
                                // Console.WriteLine("Total cost : " + rm.TotalCost + " Open Routes : " + openRoutes);
                                // Deferred time-window check (see the NOTE near the top of this loop body).
                                if (!sol.RespectsTimeWindow2FeasibleMemo(targetRouteIndex, rt2, targetOptionIndex,
                                                rt1.SequenceOfLocations[originOptionIndex])) { continue; }
                                if (PromiseIsBroken(F.Id,B.Id, moveCost + sol.Cost + smallDouble, sol))
                                {
                                    continue;
                                }
                                if (PromiseIsBroken(B.Id, G.Id, moveCost + sol.Cost + smallDouble, sol))
                                {
                                    continue;
                                }
                                if (PromiseIsBroken(A.Id, C.Id, moveCost + sol.Cost + smallDouble, sol))
                                {
                                    continue;
                                }

                                double costChangeTargetRt = dFB + dBG - dFG;
                                rm.TotalCost = moveCost + openRoutes * 10000;
                                rm.MoveCost = moveCost;
                                rm.OriginRoutePosition = originRouteIndex;
                                rm.TargetRoutePosition = targetRouteIndex;
                                rm.OriginOptionPosition = originOptionIndex;
                                rm.TargetOptionPosition = targetOptionIndex;
                                rm.CostChangeOriginRt = costChangeOriginRt;
                                rm.CostChangeTargetRt = costChangeTargetRt;
                            }
                        }
                    }
                }
            }
            return rm;
        }
        public void ApplyRelocationMove(Relocation rm, Solution sol)
        {
            if (rm.IsValid())
            {
                sol.LastMove = "relocate";
                Route originRt = sol.Routes[rm.OriginRoutePosition];
                Route targetRt = sol.Routes[rm.TargetRoutePosition];

                if (!sol.CheckRouteFeasibility(originRt) || !sol.CheckRouteFeasibility(targetRt))
                {
                    Console.WriteLine("-----");
                }
                Option A = originRt.SequenceOfOptions[rm.OriginOptionPosition - 1];
                Option B = originRt.SequenceOfOptions[rm.OriginOptionPosition];
                Option C = originRt.SequenceOfOptions[rm.OriginOptionPosition + 1];
                Option F = targetRt.SequenceOfOptions[rm.TargetOptionPosition];
                Option G = targetRt.SequenceOfOptions[rm.TargetOptionPosition + 1];

                if (originRt == targetRt)
                {
                    originRt.SequenceOfOptions.RemoveAt(rm.OriginOptionPosition);
                    originRt.SequenceOfCustomers.RemoveAt(rm.OriginOptionPosition);
                    originRt.SequenceOfLocations.RemoveAt(rm.OriginOptionPosition);
                    if (rm.OriginOptionPosition < rm.TargetOptionPosition)
                    {
                        targetRt.SequenceOfOptions.Insert(rm.TargetOptionPosition, B);
                        targetRt.SequenceOfCustomers.Insert(rm.TargetOptionPosition, B.Cust);
                        targetRt.SequenceOfLocations.Insert(rm.TargetOptionPosition, B.Location);
                    }
                    else
                    {
                        targetRt.SequenceOfOptions.Insert(rm.TargetOptionPosition + 1, B);
                        targetRt.SequenceOfCustomers.Insert(rm.TargetOptionPosition + 1, B.Cust);
                        targetRt.SequenceOfLocations.Insert(rm.TargetOptionPosition + 1, B.Location);
                    }
                    sol.UpdateTimes(originRt);
                    originRt.Cost += rm.MoveCost;
                    UpdateRouteCostAndLoad(originRt, sol);
                }
                else
                {
                    originRt.SequenceOfOptions.RemoveAt(rm.OriginOptionPosition);
                    originRt.SequenceOfCustomers.RemoveAt(rm.OriginOptionPosition);
                    originRt.SequenceOfLocations.RemoveAt(rm.OriginOptionPosition);
                    originRt.SequenceOfEct.RemoveAt(rm.OriginOptionPosition);
                    originRt.SequenceOfLat.RemoveAt(rm.OriginOptionPosition);
                    targetRt.SequenceOfOptions.Insert(rm.TargetOptionPosition + 1, B);
                    targetRt.SequenceOfCustomers.Insert(rm.TargetOptionPosition + 1, B.Cust);
                    targetRt.SequenceOfLocations.Insert(rm.TargetOptionPosition + 1, B.Location);
                    targetRt.SequenceOfEct.Insert(rm.TargetOptionPosition + 1, 0);
                    targetRt.SequenceOfLat.Insert(rm.TargetOptionPosition + 1, 0);
                    originRt.Cost += rm.CostChangeOriginRt;
                    targetRt.Cost += rm.CostChangeTargetRt;
                    originRt.Load -= B.Cust.Dem;
                    targetRt.Load += B.Cust.Dem;
                    originRt.RouteUtilizationMetric = Math.Pow(Convert.ToDouble(originRt.Capacity - originRt.Load), 2);
                    targetRt.RouteUtilizationMetric = Math.Pow(Convert.ToDouble(targetRt.Capacity - targetRt.Load), 2);
                    sol.UpdateTimes(originRt);
                    sol.UpdateTimes(targetRt);
                    UpdateRouteCostAndLoad(originRt, sol);
                    UpdateRouteCostAndLoad(targetRt, sol);
                }
                sol.Cost += rm.MoveCost;
                sol.Promises[A.Id, C.Id] = sol.Cost;
                sol.Promises[F.Id, B.Id] = sol.Cost;
                sol.Promises[B.Id, G.Id] = sol.Cost;
                if (!sol.CheckRouteFeasibility(originRt))
                {
                    Console.WriteLine("-----");
                    sol.CheckRouteFeasibility(originRt);
                }
                if (!sol.CheckRouteFeasibility(targetRt))
                {
                    Console.WriteLine("-----");
                }
            }
        }

        public Swap FindBestSwapMove(Swap sm, Solution sol)
        {
            // NOTE (perf, Phase 2): `sol.Options.Select(x => x.Location).ToHashSet().ToList()` used to
            // be recomputed fresh (3 allocations: Select's enumerator state, the HashSet, the List) on
            // every single same-route candidate, purely to hand `getTempCopy` a "distinct locations in
            // this solution" list. That value is invariant for the whole call: FindBestSwapMove never
            // mutates `sol` (only Apply* methods do, called later by the caller), and critically
            // `Solution.Options` (a Solution-level field) is never touched by the reference-mutation bug
            // in Customer.Clone (that bug mutates `Customer.Options`, a *different* field on a *different*
            // class — confirmed via grep, no shared backing field). Computed once here instead. Does NOT
            // touch getTempCopy's cloning internals or the Customer.Clone bug at all — same clone
            // behavior, same call count, same arguments' contents, just computed once instead of per
            // candidate.
            // NOTE (perf, Phase 3): built once per call (was a ToDictionary inside every getTempCopy call).
            var locationLookup = sol.GetLocationLookup();
            // NOTE (perf, Phase 4): see the same-route branch below — one flag per route, per call.
            var routeTainted = new bool[sol.Routes.Count];
            // NOTE (perf, Phase 4): removed[r][k] = distance(option k-1, option k) + distance(option k, option k+1) for
            // route r — exactly the `costRemoved1` / `costRemoved2` expression (`d(a,b) + d(b,c)`, same operands, same
            // order) that used to be recomputed for every candidate although it depends on one side only. Built once per
            // call; routes are never modified inside a Find* call.
            int swapRouteCount = sol.Routes.Count;
            var removed = new double[swapRouteCount][];
            for (int r = 0; r < swapRouteCount; r++)
            {
                var ro = sol.Routes[r].SequenceOfOptions;
                int rn = ro.Count;
                removed[r] = new double[rn];
                for (int k = 1; k < rn - 1; k++)
                {
                    removed[r][k] = sol.CalculateDistance(ro[k - 1].Location, ro[k].Location) + sol.CalculateDistance(ro[k].Location, ro[k + 1].Location);
                }
            }
            sol.BeginTimeWindowMemo();
            Route rt1, rt2;
            int openRoutes;
            int startOfSecondOptionIndex;
            Option a1, b1, c1, a2, b2, c2;
            for (int firstRouteIndex = 0; firstRouteIndex < sol.Routes.Count; firstRouteIndex++)
            {
                rt1 = sol.Routes[firstRouteIndex];
                for (int secondRouteIndex = firstRouteIndex; secondRouteIndex < sol.Routes.Count; secondRouteIndex++)
                {
                    rt2 = sol.Routes[secondRouteIndex];
                    for (int firstOptionIndex = 1; firstOptionIndex < rt1.SequenceOfOptions.Count - 1; firstOptionIndex++)
                    {
                        startOfSecondOptionIndex = 1;
                        if (rt1 == rt2)
                        {
                            startOfSecondOptionIndex = firstOptionIndex + 1;
                        }
                        for (int secondOptionIndex = startOfSecondOptionIndex; secondOptionIndex < rt2.SequenceOfOptions.Count - 1; secondOptionIndex++)
                        {
                            openRoutes = sol.Routes.Count;
                            a1 = rt1.SequenceOfOptions[firstOptionIndex - 1];
                            b1 = rt1.SequenceOfOptions[firstOptionIndex];
                            c1 = rt1.SequenceOfOptions[firstOptionIndex + 1];
                            a2 = rt2.SequenceOfOptions[secondOptionIndex - 1];
                            b2 = rt2.SequenceOfOptions[secondOptionIndex];
                            c2 = rt2.SequenceOfOptions[secondOptionIndex + 1];

                            double moveCost;
                            double costChangeFirstRoute = 0;
                            double costChangeSecondRoute = 0;
                            double ratio = 1;

                            // NOTE (perf, Phase 4): the two time-window checks used to run here for every candidate. They
                            // are pure, and so is everything up to the improving test below, so they are deferred to the
                            // moment a candidate would be recorded. ONE exception must stay eager: for same-route
                            // candidates the first-pair check decides whether getTempCopy's lasting effect (the
                            // Customer.Clone option-list replacement — see Route.ReplaceCustomerOptionsWithClones) fires.
                            // That replacement is idempotent within a call (FindBestSwapMove never mutates sol and never
                            // reads customers' Options, so nothing can observe the intermediate clone lists — only the
                            // final state, which is a fresh clone list either way), so it is performed the first time a
                            // candidate on that route passes the first-pair check — exactly when it first fired before —
                            // and skipped for later candidates on the same route.
                            bool twFirstDone = false;
                            if (rt1 == rt2)
                            {
                                if (!routeTainted[firstRouteIndex])
                                {
                                    if (!sol.RespectsTimeWindow2FeasibleMemo(firstRouteIndex, rt1, firstOptionIndex, b2.Location)
                                        || !sol.RespectsTimeWindow2FeasibleMemo(secondRouteIndex, rt2, secondOptionIndex, b1.Location)) { continue; }
                                    rt1.ReplaceCustomerOptionsWithClones(locationLookup);
                                    routeTainted[firstRouteIndex] = true;
                                    twFirstDone = true;
                                }
                                if (firstOptionIndex == secondOptionIndex - 1)
                                {
                                    double costRemoved = sol.CalculateDistance(a1.Location, b1.Location) + sol.CalculateDistance(b1.Location, b2.Location) + sol.CalculateDistance(b2.Location, c2.Location);
                                    double costAdded = sol.CalculateDistance(a1.Location, b2.Location) + sol.CalculateDistance(b2.Location, b1.Location) + sol.CalculateDistance(b1.Location, c2.Location);
                                    moveCost = costAdded - costRemoved;
                                } else {
                                    double costRemoved1 = removed[firstRouteIndex][firstOptionIndex];
                                    double costAdded1 = sol.CalculateDistance(a1.Location, b2.Location) + sol.CalculateDistance(b2.Location, c1.Location);
                                    double costRemoved2 = removed[secondRouteIndex][secondOptionIndex];
                                    double costAdded2 = sol.CalculateDistance(a2.Location, b1.Location) + sol.CalculateDistance(b1.Location, c2.Location);
                                    moveCost = costAdded1 + costAdded2 - (costRemoved1 + costRemoved2);
                                }
                            } else {
                                if (rt1.Load - b1.Cust.Dem + b2.Cust.Dem > rt1.Capacity) { continue; }
                                if (rt2.Load - b2.Cust.Dem + b1.Cust.Dem > rt2.Capacity) { continue; }
                                double costRemoved1 = removed[firstRouteIndex][firstOptionIndex];
                                double costAdded1 = sol.CalculateDistance(a1.Location, b2.Location) + sol.CalculateDistance(b2.Location, c1.Location);
                                double costRemoved2 = removed[secondRouteIndex][secondOptionIndex];
                                double costAdded2 = sol.CalculateDistance(a2.Location, b1.Location) + sol.CalculateDistance(b1.Location, c2.Location);
                                costChangeFirstRoute = costAdded1 - costRemoved1;
                                costChangeSecondRoute = costAdded2 - costRemoved2;
                                moveCost = costAdded1 + costAdded2 - (costRemoved1 + costRemoved2);
                                var newUtilizationMetricRoute1 = SquareOrPow(rt1.Capacity - (rt1.Load - b1.Cust.Dem + b2.Cust.Dem));
                                var newUtilizationMetricRoute2 = SquareOrPow(rt2.Capacity - (rt2.Load - b2.Cust.Dem + b1.Cust.Dem));
                                var newSolUtilizationMetric = sol.SolutionUtilizationMetric - rt1.RouteUtilizationMetric - rt2.RouteUtilizationMetric + newUtilizationMetricRoute1 + newUtilizationMetricRoute2;
                                ratio = (sol.SolutionUtilizationMetric + 1) / (newSolUtilizationMetric + 1);
                                if (sol.Routes.Count == sol.LowerBoundRoutes)
                                {
                                    ratio = 1;
                                }
                                //else
                                //{
                                //    ratio = Math.Clamp(ratio, 0.8, 1.3);
                                //}
                            }

                            if (ratio * moveCost < sm.MoveCost + smallDouble & moveCost !=0)
                            {
                                // Deferred time-window checks (see the NOTE above): first pair (unless the same-route path
                                // already evaluated it), then — for same-route candidates — the replacement variant that
                                // models b2 sitting at firstOptionIndex.
                                if (!twFirstDone
                                    && (!sol.RespectsTimeWindow2FeasibleMemo(firstRouteIndex, rt1, firstOptionIndex, b2.Location)
                                        || !sol.RespectsTimeWindow2FeasibleMemo(secondRouteIndex, rt2, secondOptionIndex, b1.Location))) { continue; }
                                if (rt1 == rt2
                                    && !sol.RespectsTimeWindow2FeasibleWithReplacement(rt1, secondOptionIndex, b1.Location, firstOptionIndex, b2.Location)) { continue; }
                                if (PromiseIsBroken(a1.Id, b2.Id, moveCost + sol.Cost + smallDouble, sol))
                                {
                                    continue;
                                }
                                if (PromiseIsBroken(b2.Id, c1.Id, moveCost + sol.Cost + smallDouble, sol))
                                {
                                    continue;
                                }
                                if (PromiseIsBroken(a2.Id, b1.Id, moveCost + sol.Cost + smallDouble, sol))
                                {
                                    continue;
                                }                                    
                                if (PromiseIsBroken(b1.Id, c2.Id, moveCost + sol.Cost + smallDouble, sol))
                                {
                                    continue;
                                }

                                sm.TotalCost = moveCost + openRoutes * 10000;
                                sm.PositionOfFirstRoute = firstRouteIndex;
                                sm.PositionOfSecondRoute = secondRouteIndex;
                                sm.PositionOfFirstOption = firstOptionIndex;
                                sm.PositionOfSecondOption = secondOptionIndex;
                                sm.MoveCost = moveCost;

                                if (rt1 != rt2)
                                {
                                    sm.CostChangeFirstRt = costChangeFirstRoute;
                                    sm.CostChangeSecondRt = costChangeSecondRoute;
                                }
                            }
                        }
                    }
                }
            }
            return sm;
        }

        public void ApplySwapMove(Swap sm, Solution sol)
        {
            if (sm.IsValid())
            {
                sol.LastMove = "swap";
                Route rt1 = sol.Routes[sm.PositionOfFirstRoute];
                Route rt2 = sol.Routes[sm.PositionOfSecondRoute];
                if (!sol.CheckRouteFeasibility(rt1) || !sol.CheckRouteFeasibility(rt2))
                {
                    Console.WriteLine("-----");
                }
                Option a1 = rt1.SequenceOfOptions[sm.PositionOfFirstOption - 1];
                Option a2 = rt2.SequenceOfOptions[sm.PositionOfSecondOption - 1];
                Option b1 = rt1.SequenceOfOptions[sm.PositionOfFirstOption];
                Option b2 = rt2.SequenceOfOptions[sm.PositionOfSecondOption];
                Option c1 = rt1.SequenceOfOptions[sm.PositionOfFirstOption + 1];
                Option c2 = rt2.SequenceOfOptions[sm.PositionOfSecondOption + 1];
                rt1.SequenceOfOptions[sm.PositionOfFirstOption] = b2;
                rt1.SequenceOfCustomers[sm.PositionOfFirstOption] = b2.Cust;
                rt1.SequenceOfLocations[sm.PositionOfFirstOption] = b2.Location;
                rt2.SequenceOfOptions[sm.PositionOfSecondOption] = b1;
                rt2.SequenceOfCustomers[sm.PositionOfSecondOption] = b1.Cust;
                rt2.SequenceOfLocations[sm.PositionOfSecondOption] = b1.Location;
                if (rt1 == rt2)
                {
                    rt1.Cost += sm.MoveCost;
                    UpdateRouteCostAndLoad(rt1, sol);
                    sol.UpdateTimes(rt1);
                }
                else
                {
                    rt1.Cost += sm.CostChangeFirstRt;
                    rt2.Cost += sm.CostChangeSecondRt;
                    rt1.Load = rt1.Load - b1.Cust.Dem + b2.Cust.Dem;
                    rt2.Load = rt2.Load + b1.Cust.Dem - b2.Cust.Dem;
                    rt1.RouteUtilizationMetric = Math.Pow(Convert.ToDouble(rt1.Capacity - rt1.Load), 2);
                    rt2.RouteUtilizationMetric = Math.Pow(Convert.ToDouble(rt2.Capacity - rt2.Load), 2);
                    UpdateRouteCostAndLoad(rt1, sol);
                    UpdateRouteCostAndLoad(rt2, sol);
                    sol.UpdateTimes(rt1);
                    sol.UpdateTimes(rt2);
                }
                sol.Cost += sm.MoveCost;
                sol.Promises[a1.Id, b2.Id] = sol.Cost;
                sol.Promises[b2.Id, c1.Id] = sol.Cost;
                sol.Promises[a2.Id, b1.Id] = sol.Cost;
                sol.Promises[b1.Id, c2.Id] = sol.Cost;
                if (!sol.CheckRouteFeasibility(rt1) || !sol.CheckRouteFeasibility(rt2))
                {
                    Console.WriteLine("-----");
                }
            }
        }

        public TwoOpt FindBestTwoOptMove(TwoOpt top, Solution sol) {
            // NOTE (perf, Phase 2): see the identical hoist + rationale in FindBestSwapMove.
            // NOTE (perf, Phase 3): see FindBestSwapMove.
            var locationLookup = sol.GetLocationLookup();
            // NOTE (perf, Phase 4): per-route, per-call state for the same-route branch — see the NOTE there.
            var routeTainted = new bool[sol.Routes.Count];
            var sameRouteCheckFails = new sbyte[sol.Routes.Count]; // 0 = not computed yet, 1 = passes, 2 = fails
            // NOTE (perf, Phase 4): per-route tables, built once per call (routes are never modified inside a Find*
            // call) so the per-candidate work below is table reads instead of recomputation. Every entry is the very same
            // expression the candidate loop used to evaluate, on the same operands in the same order:
            //  * prefixLoad[r][k]: running demand sum of options 0..k, accumulated left to right — exactly the running
            //    sum CapacityIsViolated's two loops built for every candidate (an O(route length) loop each time);
            //  * stepDist[r][k]: distance(option k, option k+1) — the `costRemoved` terms d(A,B) and d(K,L);
            //  * nuAfterB[r][k] / nuAfterK[r][k]: the two per-side utilization terms (they use the demand of option k+1
            //    for the first route's B and of option k for the second route's K, as in the original);
            //  * emptiesB / emptiesK: whether removing that customer empties the route.
            int routeCount = sol.Routes.Count;
            var prefixLoad = new double[routeCount][];
            var stepDist = new double[routeCount][];
            var nuAfterB = new double[routeCount][];
            var nuAfterK = new double[routeCount][];
            var emptiesB = new bool[routeCount][];
            var emptiesK = new bool[routeCount][];
            for (int r = 0; r < routeCount; r++)
            {
                Route rr = sol.Routes[r];
                var ro = rr.SequenceOfOptions;
                int rn = ro.Count;
                prefixLoad[r] = new double[rn];
                stepDist[r] = new double[rn];
                nuAfterB[r] = new double[rn];
                nuAfterK[r] = new double[rn];
                emptiesB[r] = new bool[rn];
                emptiesK[r] = new bool[rn];
                double acc = 0;
                for (int k = 0; k < rn; k++)
                {
                    acc += ro[k].Cust.Dem;
                    prefixLoad[r][k] = acc;
                }
                for (int k = 0; k < rn - 1; k++)
                {
                    stepDist[r][k] = sol.CalculateDistance(ro[k].Location, ro[k + 1].Location);
                    nuAfterB[r][k] = Math.Pow(Convert.ToDouble(rr.Capacity - (rr.Load - ro[k + 1].Cust.Dem)), power);
                    nuAfterK[r][k] = Math.Pow(Convert.ToDouble(rr.Capacity - (rr.Load - ro[k].Cust.Dem)), power);
                    emptiesB[r][k] = rr.Load - ro[k + 1].Cust.Dem == 0;
                    emptiesK[r][k] = rr.Load - ro[k].Cust.Dem == 0;
                }
            }
            int openRoutes;
            for (int rtInd1 = 0; rtInd1 < sol.Routes.Count; rtInd1++) {
                Route rt1 = sol.Routes[rtInd1];
                for (int rtInd2 = 0; rtInd2 < sol.Routes.Count; rtInd2++) {
                    Route rt2 = sol.Routes[rtInd2];
                    for (int optInd1 = 0; optInd1 < rt1.SequenceOfOptions.Count - 1; optInd1++) {
                        int start2 = 0;
                        if (rt1 == rt2) {
                            start2 = optInd1 + 2;
                        }
                        for (int optInd2 = start2; optInd2 < rt2.SequenceOfOptions.Count - 1; optInd2++) {
                            openRoutes = sol.Routes.Count;

                            double moveCost;
                            double costAdded;
                            double costRemoved;

                            Option A = rt1.SequenceOfOptions[optInd1];
                            Option B = rt1.SequenceOfOptions[optInd1 + 1];
                            Option K = rt2.SequenceOfOptions[optInd2];
                            Option L = rt2.SequenceOfOptions[optInd2 + 1];

                            // NOTE (perf, Phase 2): switched to the Route+index overload of
                            // RespectsTimeWindow — avoids both this call's own .GetRange() allocation
                            // and the method's internal List composition. Same composed sequence as
                            // before: rt1's prefix through optInd1, then rt2's suffix from optInd2+1
                            // (and the mirror for tw2) — see the overload's own comment for the index
                            // derivation.
                            // NOTE (perf, Phase 3): feasibility only, no arrays allocated here. The Ect/Lat arrays
                            // are consumed solely when this candidate is recorded as the new best move (below),
                            // where they are recomputed with the array-returning overload — same pure function
                            // of the same unchanged inputs, so identical contents.
                            // NOTE (perf, Phase 4): the two time-window checks used to run here for every candidate.
                            // They are pure, and so is everything up to the improving test below, so they are deferred
                            // to the moment a candidate would be recorded — except for same-route candidates, where the
                            // first pair of checks decides whether getTempCopy's lasting effect (the Customer.Clone
                            // option-list replacement) fires; that stays eager until it has fired once for the route in
                            // this call (idempotent within a call: nothing here mutates sol or reads customers' Options,
                            // so only the final fresh-clone state is observable — see FindBestSwapMove).
                            bool twDone = false;

                            if (rt1 == rt2 && fixSameRouteTwoOpt) {
                                // The check in the branch below rejects EVERY same-route candidate, so same-route 2-opt never moved
                                // anything. Here the real feasibility of the reversed route is tested when (and only when) the
                                // candidate would be recorded - see the improving branch.
                                if (optInd1 == 0 & optInd2 == rt1.SequenceOfOptions.Count - 2) { continue; }
                                twDone = true;
                                costAdded = sol.CalculateDistance(A.Location, K.Location) + sol.CalculateDistance(B.Location, L.Location);
                                costRemoved = sol.CalculateDistance(A.Location, B.Location) + sol.CalculateDistance(K.Location, L.Location);
                                moveCost = costAdded - costRemoved;
                                sol.RatioCombinedMoveCost = moveCost;
                            } else if (rt1 == rt2) {
                                if (optInd1 == 0 & optInd2 == rt1.SequenceOfOptions.Count - 2) { continue; }

                                if (!routeTainted[rtInd1])
                                {
                                    if (!sol.RespectsTimeWindowFeasible(rt1, optInd1, rt2, optInd2)
                                        || !sol.RespectsTimeWindowFeasible(rt2, optInd2, rt1, optInd1)) { continue; }
                                    // (see Route.ReplaceCustomerOptionsWithClones — getTempCopy's one lasting effect; the
                                    // temp route this branch used to build and reverse was never read otherwise)
                                    rt1.ReplaceCustomerOptionsWithClones(locationLookup);
                                    routeTainted[rtInd1] = true;
                                    twDone = true;
                                }
                                // The same-route time check depends only on the route (see SameRouteTwoOptTimeCheckFails:
                                // the reversed segment stays inside [1, Count-2], so the checked set of options is
                                // order-independent), so it is computed once per route per call.
                                if (sameRouteCheckFails[rtInd1] == 0)
                                {
                                    sameRouteCheckFails[rtInd1] = (sbyte)(SameRouteTwoOptTimeCheckFails(rt1) ? 2 : 1);
                                }
                                if (sameRouteCheckFails[rtInd1] == 2)
                                {
                                    continue;
                                }

                                costAdded = sol.CalculateDistance(A.Location, K.Location) + sol.CalculateDistance(B.Location, L.Location);
                                costRemoved = stepDist[rtInd1][optInd1] + stepDist[rtInd2][optInd2];
                                moveCost = costAdded - costRemoved;
                                sol.RatioCombinedMoveCost = moveCost;
                            } else {
                                if (optInd1 == 0 && optInd2 == 0) { continue; }

                                if (optInd1 == rt1.SequenceOfOptions.Count - 2 & optInd2 == rt2.SequenceOfOptions.Count - 2) { continue; }

                                // Same test as CapacityIsViolated(rt1, optInd1, rt2, optInd2), from the prefix tables.
                                double rt1FirstSegmentLoad = prefixLoad[rtInd1][optInd1];
                                double rt1SecondSegmentLoad = rt1.Load - rt1FirstSegmentLoad;
                                double rt2FirstSegmentLoad = prefixLoad[rtInd2][optInd2];
                                double rt2SecondSegmentLoad = rt2.Load - rt2FirstSegmentLoad;
                                if (rt1FirstSegmentLoad + rt2SecondSegmentLoad > rt1.Capacity) { continue; }
                                if (rt2FirstSegmentLoad + rt1SecondSegmentLoad > rt2.Capacity) { continue; }

                                costAdded = sol.CalculateDistance(A.Location, L.Location) + sol.CalculateDistance(B.Location, K.Location);
                                costRemoved = stepDist[rtInd1][optInd1] + stepDist[rtInd2][optInd2];
                                moveCost = costAdded - costRemoved;
                                if (emptiesB[rtInd1][optInd1] || emptiesK[rtInd2][optInd2]) {
                                    //Console.WriteLine("This TWO-OPT move empties a route");
                                    openRoutes--;
                                }
                                var newUtilizationMetricRoute1 = nuAfterB[rtInd1][optInd1];
                                var newUtilizationMetricRoute2 = nuAfterK[rtInd2][optInd2];
                                var newSolUtilizationMetric = sol.SolutionUtilizationMetric - rt1.RouteUtilizationMetric - rt2.RouteUtilizationMetric + newUtilizationMetricRoute1 + newUtilizationMetricRoute2;
                                var ratio = (sol.SolutionUtilizationMetric + 1) / (newSolUtilizationMetric + 1);
                                if (sol.Routes.Count == sol.LowerBoundRoutes)
                                {
                                    ratio = 1;
                                }
                                //else
                                //{
                                //    ratio = Math.Clamp(ratio, 0.8, 1.3);
                                //}
                                sol.RatioCombinedMoveCost = ratio * moveCost;
                            }

                            if (sol.RatioCombinedMoveCost + openRoutes * 10000 < top.TotalCost + smallDouble & moveCost != 0)
                            {
                                // Deferred time-window checks (see the NOTE above).
                                if (!twDone
                                    && (!sol.RespectsTimeWindowFeasible(rt1, optInd1, rt2, optInd2)
                                        || !sol.RespectsTimeWindowFeasible(rt2, optInd2, rt1, optInd1))) { continue; }
                                if (rt1 == rt2 && fixSameRouteTwoOpt && !SameRouteReversalFeasible(sol, rt1, optInd1, optInd2)) { continue; }

                                if (PromiseIsBroken(A.Id, L.Id, moveCost + sol.Cost + smallDouble, sol))
                                {
                                    continue;
                                }
                                if (PromiseIsBroken(B.Id, K.Id, moveCost + sol.Cost + smallDouble, sol))
                                {
                                    continue;
                                }

                                top.TotalCost = moveCost + openRoutes * 10000;
                                top.PositionOfFirstRoute = rtInd1;
                                top.PositionOfSecondRoute = rtInd2;
                                top.PositionOfFirstOption = optInd1;
                                top.PositionOfSecondOption = optInd2;
                                // Arrays are only needed now that this candidate is being recorded: recompute them
                                // (see the NOTE at the feasibility check above). Fresh arrays, as before.
                                var tw1 = sol.RespectsTimeWindow(rt1, optInd1, rt2, optInd2);
                                var tw2 = sol.RespectsTimeWindow(rt2, optInd2, rt1, optInd1);
                                top.Ect1 = tw1.Item2;
                                top.Ect2 = tw2.Item2;
                                top.Lat1 = tw1.Item3;
                                top.Lat2 = tw2.Item3;
                                top.MoveCost = moveCost;
                            }
                        }
                    }
                }
            }
            return top;
        }

        // Direct evaluation of the same-route 2-opt time check that FindBestTwoOptMove used to get by building
        // and reversing a temp route: true iff any option at positions 1..Count-2 has Due > Ready, or the
        // route's last Ect entry exceeds its last Lat entry. See the NOTE at the call site for why this is
        // equivalent for every (optInd1, optInd2) pair.
        private static bool SameRouteTwoOptTimeCheckFails(Route rt)
        {
            var opts = rt.SequenceOfOptions;
            int last = opts.Count - 1;
            for (int j = 1; j < last; j++)
            {
                if (opts[j].Due > opts[j].Ready) { return true; }
            }
            return rt.SequenceOfEct[last] > rt.SequenceOfLat[last];
        }

        // NOTE (perf, Phase 4): `Math.Pow(x, power)` with the default power == 2 is bit-identical to `x * x` for every
        // integral x in +/-2e7 (checked exhaustively on this runtime: 40,000,001 values, 0 mismatches). Every argument
        // passed here is (route capacity - load +/- customer demands), i.e. a small integral double. Anything else — a
        // non-integral value, a huge one, or a changed `power` — still goes through Math.Pow, so results cannot differ.
        private static double SquareOrPow(double x)
        {
            if (power == 2 && x == Math.Floor(x) && Math.Abs(x) <= 20000000.0)
            {
                return x * x;
            }
            return Math.Pow(x, power);
        }

        // Is the route still time-window feasible after reversing the segment (optInd1+1 .. optInd2)?
        private bool SameRouteReversalFeasible(Solution sol, Route rt, int optInd1, int optInd2)
        {
            var seq = rt.SequenceOfLocations;
            var list = new List<Location>(seq.Count);
            for (int i = 0; i <= optInd1; i++) { list.Add(seq[i]); }
            for (int i = optInd2; i > optInd1; i--) { list.Add(seq[i]); }
            for (int i = optInd2 + 1; i < seq.Count; i++) { list.Add(seq[i]); }
            return sol.SequenceFeasible(list);
        }

        public bool CapacityIsViolated(Route rt1, int optionInd1, Route rt2, int optionInd2) {
            double rt1FirstSegmentLoad = 0;
            for (int i = 0; i < optionInd1 + 1; i++) {
                Option n = rt1.SequenceOfOptions[i];
                rt1FirstSegmentLoad += n.Cust.Dem;
            }
            double rt1SecondSegmentLoad = rt1.Load - rt1FirstSegmentLoad;
            double rt2FirstSegmentLoad = 0;
            for (int i = 0; i < optionInd2 + 1; i++) {
                Option n = rt2.SequenceOfOptions[i];
                rt2FirstSegmentLoad += n.Cust.Dem;
            }
            double rt2SecondSegmentLoad = rt2.Load - rt2FirstSegmentLoad;
            if (rt1FirstSegmentLoad + rt2SecondSegmentLoad > rt1.Capacity) {
                return true;
            }
            if (rt2FirstSegmentLoad + rt1SecondSegmentLoad > rt2.Capacity) {
                return true;
            }
            return false;
        }

        public void ApplyTwoOptMove(TwoOpt top, Solution sol) {
            if (!top.IsValid()) { return; }
            sol.LastMove = "two opt";
            Route rt1 = sol.Routes[top.PositionOfFirstRoute];
            Route rt2 = sol.Routes[top.PositionOfSecondRoute];
            if (!sol.CheckRouteFeasibility(rt1) || !sol.CheckRouteFeasibility(rt2))
            {
                Console.WriteLine("-----");
            }
            Option A = rt1.SequenceOfOptions[top.PositionOfFirstOption];
            Option B = rt1.SequenceOfOptions[top.PositionOfFirstOption + 1];
            Option K = rt2.SequenceOfOptions[top.PositionOfSecondOption];
            Option L = rt2.SequenceOfOptions[top.PositionOfSecondOption + 1];
            int frombase = top.PositionOfFirstOption + 1;
            int fromend = top.PositionOfSecondOption + 1;
            if (rt1 == rt2)
            {
                // reverses the nodes in the segment [positionOfFirstNode + 1,  top.positionOfSecondNode]
                List<Option> reversedSegment = Enumerable.Reverse(rt1.SequenceOfOptions.GetRange(frombase, fromend - frombase)).ToList();
                List<Location> reversedLocations = Enumerable.Reverse(rt1.SequenceOfLocations.GetRange(frombase, fromend - frombase)).ToList();
                List<Customer> reversedCustomers = Enumerable.Reverse(rt1.SequenceOfCustomers.GetRange(frombase, fromend - frombase)).ToList();
                rt1.SequenceOfOptions.RemoveRange(frombase, fromend - frombase);
                rt1.SequenceOfOptions.InsertRange(frombase, reversedSegment);
                rt1.SequenceOfLocations.RemoveRange(frombase, fromend - frombase);
                rt1.SequenceOfLocations.InsertRange(frombase, reversedLocations);
                rt1.SequenceOfCustomers.RemoveRange(frombase, fromend - frombase);
                rt1.SequenceOfCustomers.InsertRange(frombase, reversedCustomers);
                rt1.Cost += top.MoveCost;
                sol.UpdateTimes(rt1);
                UpdateRouteCostAndLoad(rt1, sol);
                rt1.RouteUtilizationMetric = Math.Pow(Convert.ToDouble(rt1.Capacity - rt1.Load), 2);
            }
            else
            {
                // slice with the nodes from position top.positionOfFirstNode + 1 onwards
                List<Option> relocatedSegmentOfRt1 = rt1.SequenceOfOptions.GetRange(frombase, rt1.SequenceOfOptions.Count - frombase).ToList();
                List<Location> relocatedLocations1 = rt1.SequenceOfLocations.GetRange(frombase, rt1.SequenceOfLocations.Count - frombase).ToList();
                List<Customer> relocatedCustomers1 = rt1.SequenceOfCustomers.GetRange(frombase, rt1.SequenceOfCustomers.Count - frombase).ToList();
                // slice with the nodes from position top.positionOfFirstNode + 1 onwards
                List<Option> relocatedSegmentOfRt2 = rt2.SequenceOfOptions.GetRange(fromend, rt2.SequenceOfOptions.Count - fromend).ToList();
                List<Location> relocatedLocations2 = rt2.SequenceOfLocations.GetRange(fromend, rt2.SequenceOfLocations.Count - fromend).ToList();
                List<Customer> relocatedCustomers2 = rt2.SequenceOfCustomers.GetRange(fromend, rt2.SequenceOfCustomers.Count - fromend).ToList();

                int length = rt1.SequenceOfOptions.Count - 1;
                for (int i = length; i >= top.PositionOfFirstOption + 1; i--)
                {
                    rt1.SequenceOfOptions.RemoveAt(i);
                    rt1.SequenceOfLocations.RemoveAt(i);
                    rt1.SequenceOfCustomers.RemoveAt(i);
                }
                length = rt2.SequenceOfOptions.Count - 1;
                for (int i = length; i >= top.PositionOfSecondOption + 1; i--)
                {
                    rt2.SequenceOfOptions.RemoveAt(i);
                    rt2.SequenceOfLocations.RemoveAt(i);
                    rt2.SequenceOfCustomers.RemoveAt(i);
                }
                rt1.SequenceOfOptions.AddRange(relocatedSegmentOfRt2);
                rt2.SequenceOfOptions.AddRange(relocatedSegmentOfRt1);
                rt1.SequenceOfLocations.AddRange(relocatedLocations2);
                rt2.SequenceOfLocations.AddRange(relocatedLocations1);
                rt1.SequenceOfCustomers.AddRange(relocatedCustomers2);
                rt2.SequenceOfCustomers.AddRange(relocatedCustomers1);
                UpdateRouteCostAndLoad(rt1, sol);
                UpdateRouteCostAndLoad(rt2, sol);
                rt1.RouteUtilizationMetric = Math.Pow(Convert.ToDouble(rt1.Capacity - rt1.Load), 2);
                rt2.RouteUtilizationMetric = Math.Pow(Convert.ToDouble(rt2.Capacity - rt2.Load), 2);
                rt1.SequenceOfEct = top.Ect1.ToList();
                rt2.SequenceOfEct = top.Ect2.ToList();
                rt1.SequenceOfLat = top.Lat1.ToList();
                rt2.SequenceOfLat = top.Lat2.ToList();

            }
            sol.Cost += top.MoveCost;
            sol.Promises[A.Id, L.Id] = sol.Cost;
            sol.Promises[B.Id, K.Id] = sol.Cost;
            if (!sol.CheckRouteFeasibility(rt1) || !sol.CheckRouteFeasibility(rt2))
            {
                Console.WriteLine("-----");
            }
        }

        // 1) Added mechanisms to allow flips that reduce the overall service level if the bottom levels are not violated
        // 2) Removed rtInd2 != 0 check
        // 3) Added check for selecting customers with >1 options only
        // 4) Added check to avoid target options that are same with the current served option for the examined customer
        public Flip FindBestFlipMove(Flip flip, Solution sol, bool cond = false)
        {
            int openRoutes;
            // NOTE (perf, Phase 2): reads the incrementally-maintained counters directly (no scan at
            // all) instead of the once-per-call ScanServiceLevelCounts this was originally hoisted to
            // — safe under the same conditions Solver.CalculateServiceLevelFast documents: this whole
            // method runs within the multiRestart=false path, where Solution.SeedServiceLevelCounts is
            // called once per restart and every accepted Flip/PrioritySwap move keeps the counts
            // current via AdjustServiceLevelCounts.
            int baseP0 = sol.Po0Count, baseP1 = sol.Po1Count, baseP2 = sol.Po2Count;
            sol.BeginTimeWindowMemo();
            // NOTE (perf, Phase 3): built once per call — was `sol.Options.Select(x => x.Location).ToHashSet().ToList()`
            // plus a ToDictionary inside getTempCopy, once per customer with >= 2 options. FindBestFlipMove never
            // mutates sol, and Solution.Options is never touched by the Customer.Clone bug, so the mapping is identical.
            var flipLocationLookup = sol.GetLocationLookup();
            for (int rtInd1 = 0; rtInd1 < sol.Routes.Count; rtInd1++)
            {
                Route rt1 = sol.Routes[rtInd1];
                // NOTE (perf, Phase 4): see the option-replacement NOTE below.
                bool rt1Tainted = false;

                for (int custInd1 = 1; custInd1 < rt1.SequenceOfCustomers.Count - 1; custInd1++)
                {
                    Customer custA = rt1.SequenceOfCustomers[custInd1 - 1];
                    Customer custB = rt1.SequenceOfCustomers[custInd1];
                    Customer custC = rt1.SequenceOfCustomers[custInd1 + 1];
                    //Route rt1_copy = new Route(rt1);
                    if (custB.Options.Count() < 2) { continue; }
                    // NOTE (perf, Phase 1a — REVERTED, do not remove this block): a first attempt deleted
                    // this as "dead code" because the resulting `rt1_copy` object is never read again
                    // anywhere in this method or file. That's true of the *return value*, but the call is
                    // NOT side-effect-free: getTempCopy() -> Customer.Clone(options) contains a real bug
                    // (Customer.cs: `this.options = options;` mutates the ORIGINAL customer's Options list
                    // instead of the clone's — should be `clone.options = options;`). So every time this
                    // block runs for a given custB, it silently replaces custB.Options with a freshly-cloned
                    // List<Option> (new Option object identities). Downstream in this same method (e.g. the
                    // `custBServedOption == custB.Options[optInd]` and `custB.Options[optInd] ==
                    // rt1.SequenceOfOptions[indCust]` checks) compare Options by reference equality (`==`,
                    // not overridden), so whether this mutation has already happened changes which
                    // candidates get skipped — i.e. the search trajectory depends on this bug as a hidden
                    // side channel. Confirmed empirically: deleting this block reproduced a different (but
                    // equal-cost) route structure on U_25small_1 versus the Phase 0 baseline. Restored
                    // verbatim, unreached-variable warning and all, to preserve exact published behavior —
                    // this is a real latent bug in Customer.Clone, but fixing it is a behavior change to be
                    // decided deliberately by the authors, not something to fix silently in a perf pass
                    // (same principle as the deliberately-preserved Promises aliasing in Solution's clone
                    // constructor — see Solution.cs).
                    // NOTE (perf, Phase 3): this line and the mutations that followed it (RemoveAt x5 on the temp
                    // route's lists, sol.UpdateTimes(rt1_copy), rt1_copy.Load adjustment) only ever touched the
                    // throwaway copy — confirmed by grep, `rt1_copy` has no reader anywhere, and UpdateTimes
                    // writes only to the route it is given. The ONE thing that mattered (see the NOTE above) is
                    // getTempCopy's Customer.Clone side effect, which ReplaceCustomerOptionsWithClones performs
                    // identically for every customer on rt1, without building the discarded route.
                    // NOTE (perf, Phase 4): this used to run once per customer with >= 2 options, re-cloning EVERY
                    // customer on rt1 each time. Within one FindBestFlipMove call nothing mutates sol, and the only
                    // reads of customers' Options (custB.Options below) happen AFTER the replacement, comparing entries
                    // against each other and against route options (never equal to a fresh clone) — so the clone
                    // identities produced by the 2nd, 3rd, ... replacement are indistinguishable from the 1st's, and
                    // the flip record stores only an option INDEX (NewOptionIndex), never an Option reference. One
                    // replacement per route per call therefore leaves exactly the same observable state.
                    if (!rt1Tainted)
                    {
                        rt1.ReplaceCustomerOptionsWithClones(flipLocationLookup);
                        rt1Tainted = true;
                    }

                    // NOTE (perf, Phase 4): everything below is the same computation as before with loop-invariant work
                    // lifted out of the loops it did not depend on. Nothing here mutates sol, custB.Options or the routes.
                    //  * the customer's currently served option, and the customer's index in rt1, were recomputed for every
                    //    option (with a copy of the option list each time) and every target route;
                    //  * A / B1 / C, their three distances, the origin route's utilization term and "does moving B1 empty rt1"
                    //    depend on custInd1 only;
                    //  * the service-level test, the "is this option already in the route" test and the shared-location
                    //    capacity test depend on (custB, option) only — each used to `continue` for every target position of
                    //    every route, so skipping the option outright is the same thing;
                    //  * costChangeOriginRt / costChangeTargetRt are only read when a move is recorded, yet were computed
                    //    (six distance lookups) for every candidate.
                    // Every hoisted value is the very same expression on the same operands (same association), so all doubles
                    // are bit-for-bit what they were.
                    Option custBServedOption = null;
                    foreach (Option opt in custB.Options) {
                        if (sol.Options[opt.Id].IsServed) {
                            custBServedOption = opt;
                            break;
                        }
                    }
                    int indCust = rt1.SequenceOfCustomers.IndexOf(custB);
                    Option A = rt1.SequenceOfOptions[custInd1 - 1];
                    Option B1 = rt1.SequenceOfOptions[custInd1];
                    Option C = rt1.SequenceOfOptions[custInd1 + 1];
                    double dAB1 = sol.CalculateDistance(A.Location, B1.Location);
                    double dB1C = sol.CalculateDistance(B1.Location, C.Location);
                    double dAC = sol.CalculateDistance(A.Location, C.Location);
                    double costRemovedBase = dAB1 + dB1C;
                    double costChangeOriginRt = dAC - dAB1 - dB1C;
                    bool emptiesRt1 = rt1.Load - B1.Cust.Dem == 0;
                    var newUtilizationMetricRoute1 = SquareOrPow(rt1.Capacity - (rt1.Load - B1.Cust.Dem));

                    for (int optInd = 0; optInd < custB.Options.Count; optInd++)
                    {
                        Option B2 = custB.Options[optInd];
                        if (custBServedOption == B2) {
                            continue;
                        }
                        if (B2 == rt1.SequenceOfOptions[indCust])
                        {
                            continue;
                        }
                        if (B2.Location.MaxCap == B2.Location.Cap)
                        {
                            continue;
                        }
                        var (newSl0, newSl1) = TempServiceLevelPair(baseP0, baseP1, baseP2, rt1.SequenceOfOptions[custInd1].Prio, B2.Prio);
                        if (newSl0 < 0.8 || newSl1 < 0.9)
                        {
                            if (rt1.SequenceOfOptions[custInd1].Prio < B2.Prio)
                            {
                                continue;
                            }
                        }
                        for (int rtInd2 = 0; rtInd2 < sol.Routes.Count; rtInd2++)
                        {
                            // NOTE: `openRoutes` is reset here, once per target ROUTE, but decremented once per CANDIDATE below
                            // (when moving B1 empties rt1) — so it keeps decreasing across the target positions of this route.
                            // That is how the original behaved, and flip.TotalCost / the improving test depend on it, so it is
                            // reproduced exactly (not "fixed" into a once-per-route decrement).
                            openRoutes = sol.Routes.Count;
                            Route rt2 = sol.Routes[rtInd2];
                            int targetRouteIndex = 0;

                            if (rt2 == rt1)
                            {
                                targetRouteIndex = custInd1 + 1;
                            }

                            if (rt2.Load + custB.Dem > rt2.Capacity)
                            {
                                continue;
                            }

                            var newUtilizationMetricRoute2 = SquareOrPow(rt2.Capacity - (rt2.Load + B2.Cust.Dem));
                            var newSolUtilizationMetric = sol.SolutionUtilizationMetric - rt1.RouteUtilizationMetric - rt2.RouteUtilizationMetric + newUtilizationMetricRoute1 + newUtilizationMetricRoute2;
                            var ratio = (sol.SolutionUtilizationMetric + 1) / (newSolUtilizationMetric + 1);
                            if (sol.Routes.Count == sol.LowerBoundRoutes)
                            {
                                ratio = 1;
                            }

                            for (int targetOptionIndex = targetRouteIndex; targetOptionIndex < rt2.SequenceOfOptions.Count - 1; targetOptionIndex++) //-1
                            {
                                // NOTE (perf, Phase 4): the time-window check used to run here first; it is pure, so it is
                                // deferred into the improving branch below (everything in between is pure, and
                                // sol.RatioCombinedMoveCost is scratch — see FindBestRelocationMove).
                                Option F = rt2.SequenceOfOptions[targetOptionIndex];
                                Option G = rt2.SequenceOfOptions[targetOptionIndex + 1];

                                // `openRoutes` accumulates across the target positions of this route (see the NOTE where it is reset),
                                // and originally only positions that passed the time-window check reached this decrement. So when
                                // moving B1 empties rt1 the check must stay eager here; every other candidate defers it.
                                bool twChecked = false;
                                if (emptiesRt1)
                                {
                                    if (!sol.RespectsTimeWindow2FeasibleMemo(rtInd2, rt2, targetOptionIndex, B2.Location)) { continue; }
                                    twChecked = true;
                                    openRoutes--;
                                }

                                double dFB2 = sol.CalculateDistance(F.Location, B2.Location);
                                double dB2G = sol.CalculateDistance(B2.Location, G.Location);
                                double dFG = sol.CalculateDistance(F.Location, G.Location);
                                double costAdded = dAC + dFB2 + dB2G;
                                double costRemoved = costRemovedBase + dFG;
                                double moveCost = costAdded - costRemoved;
                                sol.RatioCombinedMoveCost = ratio * moveCost;

                                if (sol.RatioCombinedMoveCost + openRoutes * 10000 < flip.TotalCost + smallDouble) // & rtInd2 != 0)
                                {
                                    // Deferred time-window check (see the NOTE above).
                                    if (!twChecked && !sol.RespectsTimeWindow2FeasibleMemo(rtInd2, rt2, targetOptionIndex, B2.Location)) { continue; }
                                    if (PromiseIsBroken(F.Id, B2.Id, moveCost + sol.Cost + smallDouble, sol))
                                    {
                                        continue;
                                    }
                                    if (PromiseIsBroken(B2.Id, G.Id, moveCost + sol.Cost + smallDouble, sol))
                                    {
                                        continue;
                                    }
                                    if (PromiseIsBroken(A.Id, C.Id, moveCost + sol.Cost + smallDouble, sol))
                                    {
                                        continue;
                                    }
                                    double costChangeTargetRt = dFB2 + dB2G - dFG;
                                    flip.TotalCost = moveCost + openRoutes * 10000;
                                    flip.MoveCost = moveCost;
                                    flip.OriginRoutePosition = rtInd1;
                                    flip.TargetRoutePosition = rtInd2;
                                    flip.TargetOptionPosition = targetOptionIndex;
                                    flip.OriginOptionPosition = custInd1;
                                    flip.CostChangeOriginRt = costChangeOriginRt;
                                    flip.CostChangeTargetRt = costChangeTargetRt;
                                    flip.NewOptionIndex = optInd;
                                }
                            }
                        }
                    }
                    sol.Routes[rtInd1] = rt1;
                }
            }
            return flip;
        }

        public void ApplyFlipMove(Flip flip, Solution sol)
        { 
            if (flip.IsValid())
            {
                sol.LastMove = "flip";
                Route originRt = sol.Routes[flip.OriginRoutePosition];
                Route targetRt = sol.Routes[flip.TargetRoutePosition];
                if (!sol.CheckRouteFeasibility(targetRt) || !sol.CheckRouteFeasibility(originRt))
                {
                    Console.WriteLine("-----");
                }
                Option A = originRt.SequenceOfOptions[flip.OriginOptionPosition - 1];
                Option B1 = originRt.SequenceOfOptions[flip.OriginOptionPosition];
                Option B2 = originRt.SequenceOfCustomers[flip.OriginOptionPosition].Options[flip.NewOptionIndex];//new option to be placed in place of B1
                //Console.WriteLine("Customer ID: " + originRt.SequenceOfCustomers[flip.OriginOptionPosition].Id);
                //Console.WriteLine("B1 ID: " + B1.Id + " B2 ID: " + B2.Id);
                Option C = originRt.SequenceOfOptions[flip.OriginOptionPosition + 1];
                Option F = targetRt.SequenceOfOptions[flip.TargetOptionPosition];
                Option G = targetRt.SequenceOfOptions[flip.TargetOptionPosition + 1];

                originRt.SequenceOfOptions.RemoveAt(flip.OriginOptionPosition);
                originRt.SequenceOfCustomers.RemoveAt(flip.OriginOptionPosition);
                originRt.SequenceOfLocations.RemoveAt(flip.OriginOptionPosition);

                if (originRt == targetRt)
                {
                    if (flip.OriginOptionPosition < flip.TargetOptionPosition)
                    {
                        targetRt.SequenceOfOptions.Insert(flip.TargetOptionPosition, B2);
                        targetRt.SequenceOfCustomers.Insert(flip.TargetOptionPosition, B2.Cust);
                        targetRt.SequenceOfLocations.Insert(flip.TargetOptionPosition, B2.Location);
                    }
                    else
                    {
                        targetRt.SequenceOfOptions.Insert(flip.TargetOptionPosition + 1, B2);
                        targetRt.SequenceOfCustomers.Insert(flip.TargetOptionPosition + 1, B2.Cust);
                        targetRt.SequenceOfLocations.Insert(flip.TargetOptionPosition + 1, B2.Location);
                    }
                    sol.UpdateTimes(originRt);
                    originRt.Cost += flip.MoveCost;
                    UpdateRouteCostAndLoad(originRt, sol);
                    originRt.RouteUtilizationMetric = Math.Pow(Convert.ToDouble(originRt.Capacity - originRt.Load), 2);
                }
                else
                {
                originRt.SequenceOfEct.RemoveAt(flip.OriginOptionPosition);
                originRt.SequenceOfLat.RemoveAt(flip.OriginOptionPosition);

                targetRt.SequenceOfOptions.Insert(flip.TargetOptionPosition + 1, B2);
                targetRt.SequenceOfCustomers.Insert(flip.TargetOptionPosition + 1, B2.Cust);
                targetRt.SequenceOfLocations.Insert(flip.TargetOptionPosition + 1, B2.Location);
                targetRt.SequenceOfEct.Insert(flip.TargetOptionPosition + 1, 0);
                targetRt.SequenceOfLat.Insert(flip.TargetOptionPosition + 1, 0);

                originRt.Cost += flip.CostChangeOriginRt;
                targetRt.Cost += flip.CostChangeTargetRt;
                originRt.Load -= B1.Cust.Dem;
                targetRt.Load += B2.Cust.Dem;
                sol.UpdateTimes(originRt);
                sol.UpdateTimes(targetRt);
                UpdateRouteCostAndLoad(originRt, sol);
                UpdateRouteCostAndLoad(targetRt, sol);

                originRt.RouteUtilizationMetric = Math.Pow(Convert.ToDouble(originRt.Capacity - originRt.Load), 2);
                targetRt.RouteUtilizationMetric = Math.Pow(Convert.ToDouble(targetRt.Capacity - targetRt.Load), 2);
                }
                sol.Cost += flip.MoveCost;
                // NOTE (perf, Phase 2): Flip is one of only two movers that change which option
                // represents a customer (the other is PrioritySwap) — see Solution.AdjustServiceLevelCounts's
                // own comment. B1.Prio/B2.Prio are read correctly here regardless of the Customer.Clone
                // reference-identity bug described below, since Option.Clone() preserves Prio via
                // MemberwiseClone (cloning never changes a priority value, only object identity).
                sol.AdjustServiceLevelCounts(B1.Prio, B2.Prio);
                // NOTE (perf, Phase 1a — REVERTED, do not remove): a first attempt assumed B1/B2 here are
                // always the same object references held in sol.Options (true in the common case — Route/
                // Customer sequences normally hold direct references into the single shared Option list
                // from InstanceReader.BuildModel), and deleted the `sol.Options.Where(x => x.Id ==
                // B1.Id).ToList()[0].IsServed = ...` re-scan lines below as a redundant no-op. That
                // assumption does NOT always hold: Customer.Clone(options) (see Customer.cs) has a bug —
                // `this.options = options;` mutates the ORIGINAL customer's Options list instead of the
                // clone's — which FindBestFlipMove's getTempCopy call (see the NOTE a few lines above this
                // method) triggers as a side effect. Once that's happened for a given customer, B2 resolved
                // via `originRt.SequenceOfCustomers[...].Options[...]` can be a CLONED Option object, not
                // the one actually stored in sol.Options — inserting that clone into the route and setting
                // IsServed on it would silently leave the canonical sol.Options entry with the same Id
                // stuck at IsServed=false. The re-scan below is the (likely unintentional, but load-bearing)
                // defensive re-sync that keeps sol.Options correct regardless of which identity B1/B2 turned
                // out to be. Restored verbatim — confirmed necessary the same way as the getTempCopy revert
                // above: removing it changed the reached route structure on the Phase 0 regression check.
                B1.IsServed = false;
                B2.IsServed = true;
                sol.Options.Where(x => x.Id == B1.Id).ToList()[0].IsServed = false;
                sol.Options.Where(x => x.Id == B2.Id).ToList()[0].IsServed = true;
                //adjust capacity for shared locations
                B2.Location.Cap++;
                B1.Location.Cap--;
                sol.Promises[A.Id, C.Id] = sol.Cost;
                sol.Promises[F.Id, B2.Id] = sol.Cost;
                sol.Promises[B2.Id, G.Id] = sol.Cost;
                if (!sol.CheckRouteFeasibility(targetRt) || !sol.CheckRouteFeasibility(originRt))
                {
                    Console.WriteLine("-----");
                }
            }
            /*
            else {
                Console.WriteLine("Invalid flip move");
            }
            */
        }

        public PrioritySwap FindBestPrioritySwapMove(PrioritySwap psm, Solution sol)
        {
            // NOTE (perf, Phase 2): see the identical hoist + rationale in FindBestSwapMove.
            // NOTE (perf, Phase 4): `optionsPerCustomer` was a Dictionary<int, List<Option>> looked up twice per
            // inner iteration (~15 million inner iterations on a 50-customer run). Customer Ids are small dense
            // integers (the depot's fake customer, Id 1000, has no options and is skipped before every lookup), so
            // an array indexed by Id holds exactly the same lists — built by the same loops in the same order —
            // and the `optionsPerCustomer[id]` reads below are unchanged.
            var locationLookup = sol.GetLocationLookup();
            var routeTainted = new bool[sol.Routes.Count];
            sol.BeginTimeWindowMemo();
            int maxCustomerId = 0;
            foreach (Route rt in sol.Routes)
            {
                for (int i = 0; i < rt.SequenceOfCustomers.Count; i++)
                {
                    int cid = rt.SequenceOfCustomers[i].Id;
                    if (cid != 1000 && cid > maxCustomerId) { maxCustomerId = cid; }
                }
            }
            var optionsPerCustomer = new List<Option>[maxCustomerId + 1];
            foreach (Route rt in sol.Routes)
            {
            // Iterate through Route 1 customers and their corresponding options
                for (int i = 0; i < rt.SequenceOfCustomers.Count; i++)
                {
                    Customer customer = rt.SequenceOfCustomers[i];
                    if (customer.Id > maxCustomerId) { continue; } // only the depot's option-less fake customer (Id 1000)
                    foreach (Option option in customer.Options)
                    {
                        // If the customer has no list yet, create it
                        if (optionsPerCustomer[customer.Id] == null)
                        {
                            optionsPerCustomer[customer.Id] = new List<Option>();
                        }

                        // Add the current option to the customer's option list
                        optionsPerCustomer[customer.Id].Add(option);
                    }
                }
            }
            Option b1, b2;
            int openRoutes = sol.Routes.Count;
            // NOTE (perf, Phase 4): the per-iteration overheads below were replaced with exact equivalents.
            //  * `.First()` / `.Last()` (LINQ calls made on EVERY iteration of both option loops) -> each route's
            //    first/last option read once up front (the lists are never modified during this call).
            //  * foreach -> indexed loops, so the position of b1/b2 in its route is known for free. Every
            //    `Route.SequenceOfOptions.IndexOf(b1)` / `IndexOf(b2)` further down (about 40 linear scans per
            //    candidate that reaches the cost computation) is exactly that position: options are compared by
            //    reference (no Equals override in Option) and an option occurs at most once in a route.
            //  * `RemoveAll(x => x.Id == id)` allocated a closure + delegate on every inner iteration ->
            //    RemoveOptionsWithId, which has the same effect (same entries removed, same order kept) without
            //    allocating. WHEN the removal happens is unchanged on purpose: the `Count <= 1` checks just before
            //    each removal see the already-shrunk lists, so the timing decides which candidates are considered.
            //  * the dictionary was indexed 2-3 times back to back for the same key -> once.
            // NOTE (perf, Phase 4): the two nested option scans below used to visit every interior option of every
            // route for every (b1, alternative-of-b1) pair, and most of those visits do nothing: a visit proceeds only
            // if the customer's option list still has more than one entry, and the list shrinks by the served option
            // the first time the customer is visited (RemoveOptionsWithId) — and a list never grows. So a customer
            // with two options is only ever processed ONCE per call, and every later visit is a bare skip. The scans
            // are therefore flattened into arrays of entries in the original enumeration order (route, position):
            //  * customers with no list, the depot's fake customer (Id 1000) and customers whose list starts with <= 1
            //    entry can never proceed, so they are not entries at all;
            //  * `alive` holds the entries that can still proceed as b2; before each inner scan it is compacted by
            //    dropping entries whose list is already down to <= 1 entry (order of the rest preserved). The visit
            //    body keeps the same `Count <= 1` test, so an entry that dies mid-scan is still skipped exactly as
            //    before, and RemoveOptionsWithId still happens at the same first visit as before.
            var psEntries = new List<PsEntry>();
            for (int rIdx = 0; rIdx < sol.Routes.Count; rIdx++)
            {
                Route er = sol.Routes[rIdx];
                var eseq = er.SequenceOfOptions;
                Option efirst = eseq.Count > 0 ? eseq[0] : null;
                Option elast = eseq.Count > 0 ? eseq[eseq.Count - 1] : null;
                for (int ePos = 0; ePos < eseq.Count; ePos++)
                {
                    Option eopt = eseq[ePos];
                    if (eopt == efirst || eopt == elast) {continue;} //Avoid the first and last option of the route
                    int ecid = eopt.Cust.Id;
                    if (ecid == 1000 || ecid > maxCustomerId) {continue;}
                    List<Option> elist = optionsPerCustomer[ecid];
                    if (elist == null || elist.Count <= 1) {continue;}
                    psEntries.Add(new PsEntry { Opt = eopt, Rt = er, RIdx = rIdx, Pos = ePos, List = elist });
                }
            }
            var alive = new List<PsEntry>(psEntries);
            for (int x = 0; x < psEntries.Count; x++) { // Iterate over each option of each route (b1)
                PsEntry en1 = psEntries[x];
                Option opt1 = en1.Opt;
                b1 = opt1;
                Route rt1 = en1.Rt;
                int rIdx1 = en1.RIdx;
                int pos1 = en1.Pos;
                List<Option> notServedOptionsCustomerB1 = en1.List; //Create a list with the remaining options of customer B1
                if (notServedOptionsCustomerB1.Count <= 1) {continue;} // Avoid creating a list for customers with only one available option
                RemoveOptionsWithId(notServedOptionsCustomerB1, b1.Id);
                foreach (Option notServedOptionB1 in notServedOptionsCustomerB1) { //Start searching to find a match for each not served option of customer B1
                    if (notServedOptionB1.Location.Type == 1 && notServedOptionB1.Location.Cap >= notServedOptionB1.Location.MaxCap) {continue;} //If the location of that option is shared location and there is no available capacity for it continue
                    // Otherwise start searching for match either in the same or other route
                    CompactAlive(alive);
                    for (int y = 0; y < alive.Count; y++) {
                        PsEntry en2 = alive[y];
                        Option opt2 = en2.Opt;
                        if (opt1 == opt2) {continue;} //Avoid searching if it is the same option
                        b2 = opt2;
                        List<Option> notServedOptionsCustomerB2 = en2.List; //Create a list with the remaining options of customer B2
                        if (notServedOptionsCustomerB2.Count <= 1) {continue;} // Avoid creating a list for customers with only one available option
                        RemoveOptionsWithId(notServedOptionsCustomerB2, b2.Id);
                        Route rt2 = en2.Rt;
                        int rIdx2 = en2.RIdx;
                        int pos2 = en2.Pos;
                        foreach (Option notServedOptionB2 in notServedOptionsCustomerB2) {
                                    if (notServedOptionB2.Location.Type == 1 && notServedOptionB2.Location.Cap >= notServedOptionB2.Location.MaxCap) {continue;} //If the location of that option is shared location and there is no available capacity for it continue
                                    if (notServedOptionB1.Location.Id.Equals(notServedOptionB2.Location.Id)) {
                                        if (notServedOptionB1.Location.Cap >= notServedOptionB1.Location.MaxCap - 1) {continue;} //If the location of that option is shared location and there is no available capacity for it continue
                                    }
                                    if (notServedOptionB1.Prio == notServedOptionB2.Prio) {continue;} // No reason to check for options with the same priorities
                                    if (notServedOptionB1.Prio != b2.Prio) {continue;} // If the priority of the cust B1 option that we want to insert is not the same with the priority of the cust B2 option that we want to remove continue
                                    if (notServedOptionB2.Prio != b1.Prio) {continue;} // If the priority of the cust B2 option that we want to insert is not the same with the priority of the cust B1 option that we want to remove continue
                                    // Do Time Window checks and Capacity checks
                                    if (rt1.Load - b1.Cust.Dem + notServedOptionB1.Cust.Dem > rt1.Capacity) {continue;} // If the capacity of the route will be violated from the insertion continue
                                    if (rt2.Load - b2.Cust.Dem + notServedOptionB2.Cust.Dem > rt2.Capacity) {continue;} // If the capacity of the route will be violated from the insertion continue
                                    // NOTE (perf, Phase 4): the time-window checks used to run here for every candidate that got
                                    // this far. They are pure, and so is everything up to the improving test below (cost and
                                    // ratio arithmetic), so they are deferred to the moment a candidate would be recorded.
                                    // Same-route candidates used to build a temp route via getTempCopy right after the first
                                    // check passed; that call's one lasting effect is the Customer.Clone option-list replacement
                                    // (see Route.ReplaceCustomerOptionsWithClones). This method never reads customers' Options
                                    // after building its snapshot at the top, so the intermediate replacements are unobservable
                                    // and one per route per call leaves the same final state; it still fires the first time a
                                    // candidate on that route passes the first check, exactly as before. The temp route's
                                    // time-window check (options[pos1] and locations[pos1] swapped for the alternative option,
                                    // then an insertion after b2's position) is evaluated directly on rt1's locations.
                                    bool twFirstDone = false;
                                    if (rt1 == rt2 && !routeTainted[rIdx1]) {
                                        if (!sol.RespectsTimeWindow2FeasibleMemo(rIdx1, rt1, pos1, notServedOptionB1.Location)) {continue;} // If the insertion of the first option leads to TW violation continue.
                                        rt1.ReplaceCustomerOptionsWithClones(locationLookup);
                                        routeTainted[rIdx1] = true;
                                        twFirstDone = true;
                                    }
                                    double newUtilizationMetricRoute1 = 0;
                                    double newUtilizationMetricRoute2 = 0;
                                    double newSolUtilizationMetric = 0;
                                    double costChangeFirstRoute = 0;
                                    double costChangeSecondRoute = 0;
                                    double moveCost = 0;
                                    double ratio = 1;
                                    // Calculate the cost of the move
                                    if (rt1 != rt2) {
                                        double costRemoved1 = sol.CalculateDistance(rt1.SequenceOfOptions[pos1 - 1].Location, b1.Location) + sol.CalculateDistance(b1.Location, rt1.SequenceOfOptions[pos1 + 1].Location);
                                        double costAdded1 = sol.CalculateDistance(rt1.SequenceOfOptions[pos1 - 1].Location, notServedOptionB1.Location) + sol.CalculateDistance(notServedOptionB1.Location, rt1.SequenceOfOptions[pos1 + 1].Location);
                                        double costRemoved2 = sol.CalculateDistance(rt2.SequenceOfOptions[pos2 - 1].Location, b2.Location) + sol.CalculateDistance(b2.Location, rt2.SequenceOfOptions[pos2 + 1].Location);
                                        double costAdded2 = sol.CalculateDistance(rt2.SequenceOfOptions[pos2 - 1].Location, notServedOptionB2.Location) + sol.CalculateDistance(notServedOptionB2.Location, rt2.SequenceOfOptions[pos2 + 1].Location);
                                        moveCost = costAdded1 + costAdded2 - costRemoved1 - costRemoved2;
                                        costChangeFirstRoute = costAdded1 - costRemoved1;
                                        costChangeSecondRoute = costAdded2 - costRemoved2;
                                        newUtilizationMetricRoute1 = SquareOrPow(rt1.Capacity - (rt1.Load - b1.Cust.Dem + notServedOptionB1.Cust.Dem));
                                        newUtilizationMetricRoute2 = SquareOrPow(rt2.Capacity - (rt2.Load - b2.Cust.Dem + notServedOptionB2.Cust.Dem));
                                        newSolUtilizationMetric = sol.SolutionUtilizationMetric - rt1.RouteUtilizationMetric - rt2.RouteUtilizationMetric + newUtilizationMetricRoute1 + newUtilizationMetricRoute2;
                                    } else {
                                        if (Math.Abs(pos1 - pos2) == 1) { // Calculate cost change if they are next to each other
                                            if (pos1 < pos2) {
                                                double costRemoved = sol.CalculateDistance(rt1.SequenceOfOptions[pos1 - 1].Location, b1.Location) + sol.CalculateDistance(b1.Location, b2.Location) + sol.CalculateDistance(b2.Location, rt1.SequenceOfOptions[pos2 + 1].Location);
                                                double costAdded = sol.CalculateDistance(rt1.SequenceOfOptions[pos1 - 1].Location, notServedOptionB1.Location) + sol.CalculateDistance(notServedOptionB1.Location, notServedOptionB2.Location) + sol.CalculateDistance(notServedOptionB2.Location, rt1.SequenceOfOptions[pos2 + 1].Location);
                                                moveCost = costAdded - costRemoved;
                                            } else {
                                                double costRemoved = sol.CalculateDistance(rt1.SequenceOfOptions[pos2 - 1].Location, b2.Location) + sol.CalculateDistance(b2.Location, b1.Location) + sol.CalculateDistance(b1.Location, rt1.SequenceOfOptions[pos1 + 1].Location);
                                                double costAdded = sol.CalculateDistance(rt1.SequenceOfOptions[pos2 - 1].Location, notServedOptionB2.Location) + sol.CalculateDistance(notServedOptionB2.Location, notServedOptionB1.Location) + sol.CalculateDistance(notServedOptionB1.Location, rt1.SequenceOfOptions[pos1 + 1].Location);
                                                moveCost = costAdded - costRemoved;
                                            }
                                        } else {
                                            double costRemoved = sol.CalculateDistance(rt1.SequenceOfOptions[pos1 - 1].Location, b1.Location) + sol.CalculateDistance(b1.Location, rt1.SequenceOfOptions[pos1 + 1].Location);
                                            costRemoved += sol.CalculateDistance(rt1.SequenceOfOptions[pos2 - 1].Location, b2.Location) + sol.CalculateDistance(b2.Location, rt1.SequenceOfOptions[pos2 + 1].Location);
                                            double costAdded = sol.CalculateDistance(rt1.SequenceOfOptions[pos1 - 1].Location, notServedOptionB1.Location) + sol.CalculateDistance(notServedOptionB1.Location, rt1.SequenceOfOptions[pos1 + 1].Location);
                                            costAdded += sol.CalculateDistance(rt1.SequenceOfOptions[pos2 - 1].Location, notServedOptionB2.Location) + sol.CalculateDistance(notServedOptionB2.Location, rt1.SequenceOfOptions[pos2 + 1].Location);
                                            moveCost = costAdded - costRemoved;
                                        }
                                        newSolUtilizationMetric = SquareOrPow(rt1.Capacity - (rt1.Load - b1.Cust.Dem + notServedOptionB1.Cust.Dem + b2.Cust.Dem - notServedOptionB2.Cust.Dem));
                                    }
                                    ratio = (sol.SolutionUtilizationMetric + 1) / (newSolUtilizationMetric + 1);
                                    if (sol.Routes.Count == sol.LowerBoundRoutes) {
                                        ratio = 1;
                                    }
                                    //else
                                    //{
                                    //    ratio = Math.Clamp(ratio, 0.8, 1.3);
                                    //}
                                    double ratioCombinedMoveCost = ratio * moveCost;
                                    if (ratioCombinedMoveCost + openRoutes * 10000 < psm.TotalCost + smallDouble) {
                                        // Deferred time-window checks (see the NOTE above).
                                        if (!twFirstDone && !sol.RespectsTimeWindow2FeasibleMemo(rIdx1, rt1, pos1, notServedOptionB1.Location)) {continue;}
                                        if (rt1 != rt2) {
                                            if (!sol.RespectsTimeWindow2FeasibleMemo(rIdx2, rt2, pos2, notServedOptionB2.Location)) {continue;}
                                        }
                                        else if (!sol.RespectsTimeWindow2FeasibleWithReplacement(rt1, pos2, notServedOptionB2.Location, pos1, notServedOptionB1.Location)) {continue;}
                                        if (rt1 == rt2) {
                                            if (Math.Abs(pos1 - pos2) == 1) { // Calculate cost change if they are next to each other
                                                if (pos1 < pos2) {
                                                    if (PromiseIsBroken(rt1.SequenceOfOptions[pos1 - 1].Id, notServedOptionB1.Id, moveCost + sol.Cost + smallDouble, sol)) {continue;}
                                                    if (PromiseIsBroken(notServedOptionB1.Id, notServedOptionB2.Id, moveCost + sol.Cost + smallDouble, sol)) {continue;}
                                                    if (PromiseIsBroken(notServedOptionB2.Id, rt1.SequenceOfOptions[pos2 + 1].Id, moveCost + sol.Cost + smallDouble, sol)) {continue;}
                                                } else {
                                                    if (PromiseIsBroken(rt1.SequenceOfOptions[pos2 - 1].Id, notServedOptionB2.Id, moveCost + sol.Cost + smallDouble, sol)) {continue;}
                                                    if (PromiseIsBroken(notServedOptionB2.Id, notServedOptionB1.Id, moveCost + sol.Cost + smallDouble, sol)) {continue;}
                                                    if (PromiseIsBroken(notServedOptionB1.Id, rt1.SequenceOfOptions[pos1 + 1].Id, moveCost + sol.Cost + smallDouble, sol)) {continue;}
                                                }
                                            } else {
                                                if (PromiseIsBroken(rt1.SequenceOfOptions[pos1 - 1].Id, notServedOptionB1.Id, moveCost + sol.Cost + smallDouble, sol)) {continue;}
                                                if (PromiseIsBroken(notServedOptionB1.Id, rt1.SequenceOfOptions[pos1 + 1].Id, moveCost + sol.Cost + smallDouble, sol)) {continue;}
                                                if (PromiseIsBroken(rt1.SequenceOfOptions[pos2 - 1].Id, notServedOptionB2.Id, moveCost + sol.Cost + smallDouble, sol)) {continue;}
                                                if (PromiseIsBroken(notServedOptionB2.Id, rt1.SequenceOfOptions[pos2 + 1].Id, moveCost + sol.Cost + smallDouble, sol)) {continue;}
                                            }
                                        } else {
                                            if (PromiseIsBroken(rt1.SequenceOfOptions[pos1 - 1].Id, notServedOptionB1.Id, moveCost + sol.Cost + smallDouble, sol)) {continue;}
                                            if (PromiseIsBroken(notServedOptionB1.Id, rt1.SequenceOfOptions[pos1 + 1].Id, moveCost + sol.Cost + smallDouble, sol)) {continue;}
                                            if (PromiseIsBroken(rt2.SequenceOfOptions[pos2 - 1].Id, notServedOptionB2.Id, moveCost + sol.Cost + smallDouble, sol)) {continue;}
                                            if (PromiseIsBroken(notServedOptionB2.Id, rt2.SequenceOfOptions[pos2 + 1].Id, moveCost + sol.Cost + smallDouble, sol)) {continue;}
                                        }

                                        psm.TotalCost = moveCost + openRoutes * 10000;
                                        psm.MoveCost = moveCost;
                                        psm.PositionOfFirstRoute = sol.Routes.IndexOf(rt1);
                                        psm.PositionOfSecondRoute = sol.Routes.IndexOf(rt2);
                                        psm.PositionOfFirstOption = pos1;
                                        psm.PositionOfSecondOption = pos2;
                                        psm.CostChangeFirstRt = costChangeFirstRoute;
                                        psm.CostChangeSecondRt = costChangeSecondRoute;
                                        psm.AltOption1 = notServedOptionB1;
                                        psm.AltOption2 = notServedOptionB2;
                                    }
                                }
                    }
                }
            }
            return psm;
        }

        // One interior route option and the (shared, shrinking) list of its customer's other options — see the
        // NOTE at the top of FindBestPrioritySwapMove.
        private struct PsEntry
        {
            public Option Opt;
            public Route Rt;
            public int RIdx;
            public int Pos;
            public List<Option> List;
        }

        // Drops entries whose option list is already down to <= 1 entry. Such an entry can never proceed again (lists
        // only shrink), and the visit it used to get was a bare `continue`, so removing it changes nothing but the
        // cost. The relative order of the remaining entries is preserved.
        private static void CompactAlive(List<PsEntry> alive)
        {
            int write = 0;
            for (int read = 0; read < alive.Count; read++)
            {
                if (alive[read].List.Count > 1)
                {
                    if (write != read) { alive[write] = alive[read]; }
                    write++;
                }
            }
            if (write < alive.Count) { alive.RemoveRange(write, alive.Count - write); }
        }

        // NOTE (perf, Phase 4): same effect as `list.RemoveAll(x => x.Id == id)` — every entry with that Id is
        // removed and the relative order of the rest is kept — without allocating a closure and a delegate.
        private static void RemoveOptionsWithId(List<Option> list, int id)
        {
            int write = 0;
            for (int read = 0; read < list.Count; read++)
            {
                Option o = list[read];
                if (o.Id != id)
                {
                    if (write != read) { list[write] = o; }
                    write++;
                }
            }
            if (write < list.Count) { list.RemoveRange(write, list.Count - write); }
        }

        public void ApplyPrioritySwapMove(PrioritySwap psm, Solution sol)
        {
            if (psm.IsValid()) //&& psm.MoveCost < 0) 
            {
                sol.LastMove = "psm";
                Route rt1 = sol.Routes[psm.PositionOfFirstRoute];
                Route rt2 = sol.Routes[psm.PositionOfSecondRoute];
                if (!sol.CheckRouteFeasibility(rt1) || !sol.CheckRouteFeasibility(rt2))
                {
                    Console.WriteLine("-----");
                }
                Option b1 = rt1.SequenceOfOptions[psm.PositionOfFirstOption];
                // Option B2 = originRt.SequenceOfCustomers[flip.OriginOptionPosition].Options[flip.NewOptionIndex];
                //Option d1 = psm.AltOption1;
                Option d1 = rt1.SequenceOfCustomers[psm.PositionOfFirstOption].Options.FirstOrDefault(opt => opt.Id == psm.AltOption1.Id);
                Option b2 = rt2.SequenceOfOptions[psm.PositionOfSecondOption];
                //Option d2 = psm.AltOption2;
                Option d2 = rt2.SequenceOfCustomers[psm.PositionOfSecondOption].Options.FirstOrDefault(opt => opt.Id == psm.AltOption2.Id);
                //
                // NOTE (perf, Phase 1a): this whole diagnostic block used to print unconditionally on
                // EVERY accepted priority-swap move (10 Console.WriteLine calls, two of them building a
                // fresh string via Select(...).ToList() over each route's full option sequence), never
                // gated by settings.verbal unlike Solver's equivalent per-iteration prints. Gated now for
                // consistency; behavior/output when verbal=true is unchanged, and priority-swap moves are
                // exercised by the regression baselines (they show up in several accepted-move logs).
                if (verbal)
                {
                    Console.WriteLine("Customer 1: " + b1.Cust.Id);
                    Console.WriteLine("B1 ID: " + b1.Id + " D1 ID: " + d1.Id);
                    Console.WriteLine("Customer 2: " + b2.Cust.Id);
                    Console.WriteLine("B2 ID: " + b2.Id + " D2 ID: " + d2.Id);
                    Console.WriteLine("-----");
                    Console.WriteLine("Route 1 Before: " + string.Join(",", rt1.SequenceOfOptions.Select(x => x.Id).ToList()));
                    Console.WriteLine("Route 2 Before: " + string.Join(",", rt2.SequenceOfOptions.Select(x => x.Id).ToList()));
                    Console.WriteLine("-----");
                }
                //
                rt1.SequenceOfOptions[psm.PositionOfFirstOption] = d1;
                rt1.SequenceOfCustomers[psm.PositionOfFirstOption] = d1.Cust;
                rt1.SequenceOfLocations[psm.PositionOfFirstOption] = d1.Location;
                rt2.SequenceOfOptions[psm.PositionOfSecondOption] = d2;
                rt2.SequenceOfCustomers[psm.PositionOfSecondOption] = d2.Cust;
                rt2.SequenceOfLocations[psm.PositionOfSecondOption] = d2.Location;
                if (verbal)
                {
                    Console.WriteLine("---------");
                    Console.WriteLine("Route 1 After: " + string.Join(",", rt1.SequenceOfOptions.Select(x => x.Id).ToList()));
                    Console.WriteLine("Route 2 After: " + string.Join(",", rt2.SequenceOfOptions.Select(x => x.Id).ToList()));
                    Console.WriteLine("---------");
                }
                b1.Location.Cap -= 1;
                b2.Location.Cap -= 1;
                d1.Location.Cap += 1;
                d2.Location.Cap += 1;
                // NOTE (perf, Phase 2): PrioritySwap is a double option-priority change — b1 leaves
                // (replaced by d1), and separately b2 leaves (replaced by d2) — see
                // Solution.AdjustServiceLevelCounts's own comment for why this call site is safe.
                sol.AdjustServiceLevelCounts(b1.Prio, d1.Prio);
                sol.AdjustServiceLevelCounts(b2.Prio, d2.Prio);
                b1.IsServed = false;
                b2.IsServed = false;
                d1.IsServed = true;
                d2.IsServed = true;
                if (rt1 == rt2)
                {
                    rt1.Cost += psm.MoveCost;
                    sol.UpdateTimes(rt1);
                    UpdateRouteCostAndLoad(rt1, sol);
                    rt1.RouteUtilizationMetric = Math.Pow(Convert.ToDouble(rt1.Capacity - rt1.Load), 2);
                    sol.Cost += psm.MoveCost;
                    if (rt1 == rt2 && (psm.PositionOfFirstOption == psm.PositionOfSecondOption - 1 || psm.PositionOfSecondOption == psm.PositionOfFirstOption - 1))
                    {
                        if (psm.PositionOfFirstOption == psm.PositionOfSecondOption - 1)
                        {
                            sol.Promises[rt1.SequenceOfOptions[psm.PositionOfFirstOption - 1].Id, d1.Id] = sol.Cost;
                            sol.Promises[d1.Id, d2.Id] = sol.Cost;
                            sol.Promises[d2.Id, rt1.SequenceOfOptions[psm.PositionOfSecondOption + 1].Id] = sol.Cost;
                        }
                        else if (psm.PositionOfSecondOption == psm.PositionOfFirstOption - 1)
                        {
                            sol.Promises[rt1.SequenceOfOptions[psm.PositionOfSecondOption - 1].Id, d2.Id] = sol.Cost;
                            sol.Promises[d2.Id, d1.Id] = sol.Cost;
                            sol.Promises[d1.Id, rt1.SequenceOfOptions[psm.PositionOfFirstOption + 1].Id] = sol.Cost;
                        }
                    }
                    else
                    {
                        sol.Promises[rt1.SequenceOfOptions[psm.PositionOfFirstOption - 1].Id, d1.Id] = sol.Cost;
                        sol.Promises[d1.Id, rt1.SequenceOfOptions[psm.PositionOfFirstOption + 1].Id] = sol.Cost;
                        sol.Promises[rt1.SequenceOfOptions[psm.PositionOfSecondOption - 1].Id, d2.Id] = sol.Cost;
                        sol.Promises[d2.Id, rt1.SequenceOfOptions[psm.PositionOfSecondOption + 1].Id] = sol.Cost;
                    }
                    if (!sol.CheckRouteFeasibility(rt1) || !sol.CheckRouteFeasibility(rt2))
                    {
                        Console.WriteLine("-----");
                    }
                }
                else
                {
                    rt1.Cost += psm.CostChangeFirstRt;
                    rt2.Cost += psm.CostChangeSecondRt;
                    sol.UpdateTimes(rt1);
                    sol.UpdateTimes(rt2);
                    UpdateRouteCostAndLoad(rt1, sol);
                    UpdateRouteCostAndLoad(rt2, sol);
                    rt1.RouteUtilizationMetric = Math.Pow(Convert.ToDouble(rt1.Capacity - rt1.Load), 2);
                    rt2.RouteUtilizationMetric = Math.Pow(Convert.ToDouble(rt2.Capacity - rt2.Load), 2);
                    sol.Cost += psm.MoveCost;
                    sol.Promises[rt1.SequenceOfOptions[psm.PositionOfFirstOption - 1].Id, d1.Id] = sol.Cost;
                    sol.Promises[d1.Id, rt1.SequenceOfOptions[psm.PositionOfFirstOption + 1].Id] = sol.Cost;
                    sol.Promises[rt2.SequenceOfOptions[psm.PositionOfSecondOption - 1].Id, d2.Id] = sol.Cost;
                    sol.Promises[d2.Id, rt2.SequenceOfOptions[psm.PositionOfSecondOption + 1].Id] = sol.Cost;
                    if (!sol.CheckRouteFeasibility(rt1) || !sol.CheckRouteFeasibility(rt2))
                    {
                        Console.WriteLine("-----");
                    }
                }
            }
        }


        public void UpdateRouteCostAndLoad(Route rt, Solution sol) {
            double tc = 0;
            double tl = 0;
            for (int i = 0; i < rt.SequenceOfOptions.Count - 1; i++) {
                Option A = rt.SequenceOfOptions[i];
                Option B = rt.SequenceOfOptions[i + 1];
                tc += sol.CalculateDistance(A.Location, B.Location);
                tl += A.Cust.Dem;
            }
            rt.Load = tl;
            rt.Cost = tc;
        }

        // NOTE (perf, Phase 2): extracted verbatim from CalculateTempServiceLevel's original body so
        // FindBestFlipMove can compute this ONCE per call instead of once per candidate (see below) —
        // no behavior change, this is the exact same loop that used to run inline.
        (int po0Sum, int po1Sum, int po2Sum) ScanServiceLevelCounts(Solution sol)
        {
            int po0Sum = 0;
            int po1Sum = 0;
            int po2Sum = 0;
            int po = -1;

            for (int r = 0; r < sol.Routes.Count; r++)
            {
                for (int c = 1; c < sol.Routes[r].SequenceOfOptions.Count - 1; c++)
                {
                    po = sol.Routes[r].SequenceOfOptions[c].Prio;
                    switch (po)
                    {
                        case 0:
                            po0Sum++;
                            break;
                        case 1:
                            po1Sum++;
                            break;
                        case 2:
                            po2Sum++;
                            break;
                    }
                }
            }
            return (po0Sum, po1Sum, po2Sum);
        }

        // NOTE (perf, Phase 2): applies the exact same leaving/entering ±1 delta and sl0/sl1 division as
        // the original CalculateTempServiceLevel, but takes the base po0Sum/po1Sum/po2Sum scan as
        // parameters instead of recomputing it. Confirmed safe to hoist the scan out of a caller's loop
        // ONLY where `sol.Routes[*].SequenceOfOptions[*].Prio` is provably unchanged for the caller's
        // entire loop — FindBestFlipMove is a pure candidate-evaluation function (never mutates sol;
        // the only route reassignment in it, `sol.Routes[rtInd1] = rt1;`, is a same-object no-op), so
        // its base scan result is invariant across the whole call. Do NOT reuse this overload from a
        // caller that applies moves (mutates route contents) between calls without re-scanning.
        double[] CalculateTempServiceLevel(int po0Sum, int po1Sum, int po2Sum, int leavingPriority, int enteringPriority, bool verbal = false)
        {
            switch (leavingPriority)
            {
                case 0:
                    po0Sum--;
                    break;
                case 1:
                    po1Sum--;
                    break;
                case 2:
                    po2Sum--;
                    break;
            }
            switch (enteringPriority)
            {
                case 0:
                    po0Sum++;
                    break;
                case 1:
                    po1Sum++;
                    break;
                case 2:
                    po2Sum++;
                    break;
            }
            double sum = po0Sum + po1Sum + po2Sum;
            var sl0 = po0Sum / sum;
            var sl1 = (po0Sum + po1Sum) / sum;
            if (verbal) {
                Console.WriteLine("Priority 1: {0}", sl0);
                Console.WriteLine("Priority 2: {0}", sl1);
            }

            return new double[] {sl0, sl1};
        }

        // NOTE (perf, Phase 4): same arithmetic as the array-returning overload above, returned as a ValueTuple
        // instead of a fresh double[2] (FindBestFlipMove is the only caller and reads just the two values).
        (double, double) TempServiceLevelPair(int po0Sum, int po1Sum, int po2Sum, int leavingPriority, int enteringPriority)
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
            var sl0 = po0Sum / sum;
            var sl1 = (po0Sum + po1Sum) / sum;
            return (sl0, sl1);
        }

        double[] CalculateTempServiceLevel(Solution sol, int leavingPriority, int enteringPriority, bool verbal = false)
        {
            var (po0Sum, po1Sum, po2Sum) = ScanServiceLevelCounts(sol);
            return CalculateTempServiceLevel(po0Sum, po1Sum, po2Sum, leavingPriority, enteringPriority, verbal);
        }

        bool PromiseIsBroken(int a, int b, double newCost, Solution sol)
        {
            if (newCost >= sol.Promises[a, b])
            {
                return true;
            }

            return false;
        }
    }
}
