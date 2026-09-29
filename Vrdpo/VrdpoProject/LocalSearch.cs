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
            Route rt1, rt2;
            int openRoutes;
            for (int originRouteIndex = 0; originRouteIndex < sol.Routes.Count; originRouteIndex++)
            {
                rt1 = sol.Routes[originRouteIndex];

                for (int targetRouteIndex = 0; targetRouteIndex < sol.Routes.Count; targetRouteIndex++)
                {
                    rt2 = sol.Routes[targetRouteIndex];

                    for (int originOptionIndex = 1; originOptionIndex < rt1.SequenceOfOptions.Count - 1; originOptionIndex++)
                    {
                        for (int targetOptionIndex = 0; targetOptionIndex < rt2.SequenceOfOptions.Count - 1; targetOptionIndex++)
                        {
                            openRoutes = sol.Routes.Count;
                            if (originRouteIndex == targetRouteIndex && (targetOptionIndex == originOptionIndex || targetOptionIndex == originOptionIndex - 1))
                            {
                                continue;
                            }

                            // NOTE (perf, Phase 3): bool-only, allocation-free variant — only `.Item1` was ever read.
                            if (!sol.RespectsTimeWindow2Feasible(rt2, targetOptionIndex,
                                            rt1.SequenceOfLocations[originOptionIndex])) { continue; };

                            Option A = rt1.SequenceOfOptions[originOptionIndex - 1];
                            Option B = rt1.SequenceOfOptions[originOptionIndex];
                            Option C = rt1.SequenceOfOptions[originOptionIndex + 1];

                            Option F = rt2.SequenceOfOptions[targetOptionIndex];
                            Option G = rt2.SequenceOfOptions[targetOptionIndex + 1];
                            if (rt1 != rt2)
                            {
                                if (rt2.Load + B.Cust.Dem > rt2.Capacity)
                                {
                                    continue;
                                }
                            }
                            if (rt1.Load - B.Cust.Dem == 0) { // if route becomes empty
                                //Console.WriteLine("This RELOCATION move empties a route");
                                //Console.WriteLine("Routes before : " + openRoutes);
                                openRoutes--;
                                //Console.WriteLine("Routes after : " + openRoutes);
                            }

                            double costAdded = sol.CalculateDistance(A.Location, C.Location) + sol.CalculateDistance(F.Location, B.Location)
                                                + sol.CalculateDistance(B.Location, G.Location);
                            double costRemoved = sol.CalculateDistance(A.Location, B.Location) + sol.CalculateDistance(B.Location, C.Location)
                                                + sol.CalculateDistance(F.Location, G.Location);
                            double moveCost = costAdded - costRemoved;

                            double costChangeOriginRt = sol.CalculateDistance(A.Location, C.Location) - sol.CalculateDistance(A.Location, B.Location)
                                                - sol.CalculateDistance(B.Location, C.Location);
                            double costChangeTargetRt = sol.CalculateDistance(F.Location, B.Location) + sol.CalculateDistance(B.Location, G.Location)
                                                - sol.CalculateDistance(F.Location, G.Location);

                            var newUtilizationMetricRoute1 = Math.Pow(Convert.ToDouble(rt1.Capacity - (rt1.Load - B.Cust.Dem)), power);
                            var newUtilizationMetricRoute2 = Math.Pow(Convert.ToDouble(rt2.Capacity - (rt2.Load + B.Cust.Dem)), power);
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
            var distinctLocationsInSolution = sol.Options.Select(x => x.Location).ToHashSet().ToList();
            // NOTE (perf, Phase 3): built once per call (was a ToDictionary inside every getTempCopy call).
            var locationLookup = Route.BuildLocationLookup(distinctLocationsInSolution);
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

                            // NOTE (perf, Phase 3): bool-only variants (only `.Item1` was ever read); the second is
                            // skipped when the first already fails, which is safe — the checks are pure.
                            if (!sol.RespectsTimeWindow2Feasible(rt1, firstOptionIndex, b2.Location)
                                || !sol.RespectsTimeWindow2Feasible(rt2, secondOptionIndex, b1.Location)) { continue; }

                            if (rt1 == rt2)
                            {
                                // NOTE (perf, Phase 3): this used to be `Route rtTemp = rt1.getTempCopy(...)`, then
                                // overwrite rtTemp's options/customers/locations at firstOptionIndex with b2's, then
                                // RespectsTimeWindow2(rtTemp, secondOptionIndex, b1.Location). rtTemp was never used
                                // for anything else, so: (1) ReplaceCustomerOptionsWithClones performs getTempCopy's
                                // one lasting effect (the Customer.Clone option-list replacement the search trajectory
                                // depends on — see its comment) exactly as before, and (2) the time-window check runs
                                // directly on rt1's locations with firstOptionIndex swapped for b2.Location, which is
                                // the sequence rtTemp held (its Location clones differ from rt1's only by object
                                // identity; the check reads only value fields).
                                rt1.ReplaceCustomerOptionsWithClones(locationLookup);
                                if (!sol.RespectsTimeWindow2FeasibleWithReplacement(rt1, secondOptionIndex, b1.Location, firstOptionIndex, b2.Location))
                                {
                                    continue;
                                }
                                if (firstOptionIndex == secondOptionIndex - 1)
                                {
                                    double costRemoved = sol.CalculateDistance(a1.Location, b1.Location) + sol.CalculateDistance(b1.Location, b2.Location) + sol.CalculateDistance(b2.Location, c2.Location);
                                    double costAdded = sol.CalculateDistance(a1.Location, b2.Location) + sol.CalculateDistance(b2.Location, b1.Location) + sol.CalculateDistance(b1.Location, c2.Location);
                                    moveCost = costAdded - costRemoved;
                                } else {
                                    double costRemoved1 = sol.CalculateDistance(a1.Location, b1.Location) + sol.CalculateDistance(b1.Location, c1.Location);
                                    double costAdded1 = sol.CalculateDistance(a1.Location, b2.Location) + sol.CalculateDistance(b2.Location, c1.Location);
                                    double costRemoved2 = sol.CalculateDistance(a2.Location, b2.Location) + sol.CalculateDistance(b2.Location, c2.Location);
                                    double costAdded2 = sol.CalculateDistance(a2.Location, b1.Location) + sol.CalculateDistance(b1.Location, c2.Location);
                                    moveCost = costAdded1 + costAdded2 - (costRemoved1 + costRemoved2);
                                }
                            } else {
                                if (rt1.Load - b1.Cust.Dem + b2.Cust.Dem > rt1.Capacity) { continue; }
                                if (rt2.Load - b2.Cust.Dem + b1.Cust.Dem > rt2.Capacity) { continue; }
                                double costRemoved1 = sol.CalculateDistance(a1.Location, b1.Location) + sol.CalculateDistance(b1.Location, c1.Location);
                                double costAdded1 = sol.CalculateDistance(a1.Location, b2.Location) + sol.CalculateDistance(b2.Location, c1.Location);
                                double costRemoved2 = sol.CalculateDistance(a2.Location, b2.Location) + sol.CalculateDistance(b2.Location, c2.Location);
                                double costAdded2 = sol.CalculateDistance(a2.Location, b1.Location) + sol.CalculateDistance(b1.Location, c2.Location);
                                costChangeFirstRoute = costAdded1 - costRemoved1;
                                costChangeSecondRoute = costAdded2 - costRemoved2;
                                moveCost = costAdded1 + costAdded2 - (costRemoved1 + costRemoved2);
                                var newUtilizationMetricRoute1 = Math.Pow(Convert.ToDouble(rt1.Capacity - (rt1.Load - b1.Cust.Dem + b2.Cust.Dem)), power);
                                var newUtilizationMetricRoute2 = Math.Pow(Convert.ToDouble(rt2.Capacity - (rt2.Load - b2.Cust.Dem + b1.Cust.Dem)), power);
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
            var distinctLocationsInSolution = sol.Options.Select(x => x.Location).ToHashSet().ToList();
            // NOTE (perf, Phase 3): see FindBestSwapMove.
            var locationLookup = Route.BuildLocationLookup(distinctLocationsInSolution);
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
                            if (!sol.RespectsTimeWindowFeasible(rt1, optInd1, rt2, optInd2)
                                || !sol.RespectsTimeWindowFeasible(rt2, optInd2, rt1, optInd1)) { continue; }

                            if (rt1 == rt2) {
                                if (optInd1 == 0 & optInd2 == rt1.SequenceOfOptions.Count - 2) { continue; }

                                //tw2 = sol.RespectsTimeWindow(rt1, optInd2, rt1.SequenceOfLocations.GetRange(optInd1 + 1, rt1.SequenceOfLocations.Count - (optInd1 + 1)));
                                //respectsTw1 = tw1.Item1;
                                //respectsTw2 = tw2.Item1;
                                //if (!respectsTw1 || !respectsTw2) { continue; }

                                // NOTE (perf, Phase 3): this used to build a temp route via getTempCopy, reverse the
                                // segment [optInd1+1, optInd2] in its option/location/customer lists, overwrite its
                                // Ect/Lat lists with each option's Due/Ready for positions 0..Count-2, and then run
                                // CheckTimeWindowsFeasibility (which tests Ect[i+1] > Lat[i+1] for i in 0..Count-2,
                                // i.e. positions 1..Count-1). Nothing else ever read that temp route, so:
                                //  (1) ReplaceCustomerOptionsWithClones performs getTempCopy's one lasting effect (the
                                //      Customer.Clone option-list replacement; see its comment) exactly as before;
                                //  (2) the check is evaluated directly. Positions 1..Count-2 hold the SAME set of
                                //      options before and after the reversal (the reversed segment lies entirely inside
                                //      [1, Count-2] because optInd1 >= 0 and optInd2 <= Count-2), so "some option there
                                //      has Due > Ready" doesn't depend on the ordering; position Count-1 kept the
                                //      copied rt1.SequenceOfEct/Lat value (the loop only wrote 0..Count-2). Cloned
                                //      options carry identical Due/Ready. See SameRouteTwoOptTimeCheckFails.
                                rt1.ReplaceCustomerOptionsWithClones(locationLookup);
                                if (SameRouteTwoOptTimeCheckFails(rt1))
                                {
                                    continue;
                                }

                                costAdded = sol.CalculateDistance(A.Location, K.Location) + sol.CalculateDistance(B.Location, L.Location);
                                costRemoved = sol.CalculateDistance(A.Location, B.Location) + sol.CalculateDistance(K.Location, L.Location);
                                moveCost = costAdded - costRemoved;
                                sol.RatioCombinedMoveCost = moveCost;
                            } else {
                                if (optInd1 == 0 && optInd2 == 0) { continue; }

                                if (optInd1 == rt1.SequenceOfOptions.Count - 2 & optInd2 == rt2.SequenceOfOptions.Count - 2) { continue; }

                                if (CapacityIsViolated(rt1, optInd1, rt2, optInd2)) { continue; }

                                costAdded = sol.CalculateDistance(A.Location, L.Location) + sol.CalculateDistance(B.Location, K.Location);
                                costRemoved = sol.CalculateDistance(A.Location, B.Location) + sol.CalculateDistance(K.Location, L.Location);
                                moveCost = costAdded - costRemoved;
                                if (rt1.Load - B.Cust.Dem == 0 || rt2.Load - K.Cust.Dem == 0) {
                                    //Console.WriteLine("This TWO-OPT move empties a route");
                                    openRoutes--;
                                }
                                var newUtilizationMetricRoute1 = Math.Pow(Convert.ToDouble(rt1.Capacity - (rt1.Load - B.Cust.Dem)), power);
                                var newUtilizationMetricRoute2 = Math.Pow(Convert.ToDouble(rt2.Capacity - (rt2.Load - K.Cust.Dem)), power);
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
            // NOTE (perf, Phase 3): built once per call — was `sol.Options.Select(x => x.Location).ToHashSet().ToList()`
            // plus a ToDictionary inside getTempCopy, once per customer with >= 2 options. FindBestFlipMove never
            // mutates sol, and Solution.Options is never touched by the Customer.Clone bug, so the mapping is identical.
            var flipLocationLookup = Route.BuildLocationLookup(sol.Options.Select(x => x.Location).ToHashSet().ToList());
            for (int rtInd1 = 0; rtInd1 < sol.Routes.Count; rtInd1++)
            {
                Route rt1 = sol.Routes[rtInd1];

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
                    rt1.ReplaceCustomerOptionsWithClones(flipLocationLookup);

                    for (int optInd = 0; optInd < custB.Options.Count; optInd++)
                    {
                        Option custBServedOption = null;
                        List<Option> options = new List<Option>(custB.Options);
                        foreach (Option opt in options) {
                            if (sol.Options[opt.Id].IsServed) {
                                custBServedOption = opt;
                                break;
                            }
                        }
                        if (custBServedOption == custB.Options[optInd]) {
                            continue;
                        }
                        for (int rtInd2 = 0; rtInd2 < sol.Routes.Count; rtInd2++)
                        {
                            openRoutes = sol.Routes.Count;
                            Route rt2 = sol.Routes[rtInd2];
                            int indCust = rt1.SequenceOfCustomers.IndexOf(custB);
                            int targetRouteIndex = 0;

                            if (rt2 == rt1)
                            {
                                targetRouteIndex = custInd1 + 1;
                            }

                            if (custB.Options[optInd] == rt1.SequenceOfOptions[indCust])
                            {
                                continue;
                            }
                            if (rt2.Load + custB.Dem > rt2.Capacity)
                            {
                                continue;
                            }
                            if (custB.Options[optInd].Location.MaxCap == custB.Options[optInd].Location.Cap)
                            {
                                continue;
                            }

                            for (int targetOptionIndex = targetRouteIndex; targetOptionIndex < rt2.SequenceOfOptions.Count - 1; targetOptionIndex++) //-1
                            {

                                // NOTE (perf, Phase 3): bool-only variant — only `.Item1` was ever read.
                                if (!sol.RespectsTimeWindow2Feasible(rt2, targetOptionIndex, custB.Options[optInd].Location)) { continue; }

                                var newServiceLevel = CalculateTempServiceLevel(baseP0, baseP1, baseP2, rt1.SequenceOfOptions[custInd1].Prio, custB.Options[optInd].Prio);
                                if (newServiceLevel[0] < 0.8 || newServiceLevel[1] < 0.9)
                                {
                                    if (rt1.SequenceOfOptions[custInd1].Prio < custB.Options[optInd].Prio)
                                    {
                                        continue;
                                    }
                                }
                                
                                Option A = rt1.SequenceOfOptions[custInd1 - 1];
                                Option B1 = rt1.SequenceOfOptions[custInd1];
                                Option C = rt1.SequenceOfOptions[custInd1 + 1];

                                Option F = rt2.SequenceOfOptions[targetOptionIndex];
                                Option B2 = custB.Options[optInd];
                                Option G = rt2.SequenceOfOptions[targetOptionIndex + 1];

                                if (rt1 != rt2)
                                {
                                    if (rt2.Load + custB.Dem > rt2.Capacity)
                                    {
                                        continue;
                                    }
                                }

                                if (rt1.Load - B1.Cust.Dem == 0)
                                {
                                    openRoutes--;
                                }

                                double costAdded = sol.CalculateDistance(A.Location, C.Location) + sol.CalculateDistance(F.Location, B2.Location)
                                                    + sol.CalculateDistance(B2.Location, G.Location);
                                double costRemoved = sol.CalculateDistance(A.Location, B1.Location) + sol.CalculateDistance(B1.Location, C.Location)
                                                    + sol.CalculateDistance(F.Location, G.Location);
                                double moveCost = costAdded - costRemoved;
                                var newUtilizationMetricRoute1 = Math.Pow(Convert.ToDouble(rt1.Capacity - (rt1.Load - B1.Cust.Dem)), power);
                                var newUtilizationMetricRoute2 = Math.Pow(Convert.ToDouble(rt2.Capacity - (rt2.Load + B2.Cust.Dem)), power);
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

                                double costChangeOriginRt = sol.CalculateDistance(A.Location, C.Location) - sol.CalculateDistance(A.Location, B1.Location)
                                                    - sol.CalculateDistance(B1.Location, C.Location);
                                double costChangeTargetRt = sol.CalculateDistance(F.Location, B2.Location) + sol.CalculateDistance(B2.Location, G.Location)
                                                    - sol.CalculateDistance(F.Location, G.Location);


                                if (sol.RatioCombinedMoveCost + openRoutes * 10000 < flip.TotalCost + smallDouble) // & rtInd2 != 0)
                                {
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
            var distinctLocationsInSolution = sol.Options.Select(x => x.Location).ToHashSet().ToList();
            Dictionary<int, List<Option>> optionsPerCustomer = new Dictionary<int, List<Option>>();
            foreach (Route rt in sol.Routes)
            {
            // Iterate through Route 1 customers and their corresponding options
                for (int i = 0; i < rt.SequenceOfCustomers.Count; i++)
                {
                    Customer customer = rt.SequenceOfCustomers[i];
                    foreach (Option option in customer.Options)
                    {
                        // If the customer is not already in the dictionary, add them
                        if (!optionsPerCustomer.ContainsKey(customer.Id))
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
            foreach (Route rt1 in sol.Routes) { //Start iterating over the available Routes
                foreach (Option opt1 in rt1.SequenceOfOptions) { // Iterate over each option of the current Route rt1
                    if (opt1 == rt1.SequenceOfOptions.First() || opt1 == rt1.SequenceOfOptions.Last()) {continue;} //Avoid the first and last option of the route
                    b1 = opt1;
                    Customer customerB1 = b1.Cust;
                    if (customerB1.Id == 1000) {continue;} //Probably unnecessary because there are no options corresponding to the warehouse but add it to be safe
                    if (optionsPerCustomer[customerB1.Id].Count <= 1) {continue;} // Avoid creating a list for customers with only one available option
                    List<Option> notServedOptionsCustomerB1 = optionsPerCustomer[customerB1.Id]; //Create a list with the remaining options of customer B1
                    notServedOptionsCustomerB1.RemoveAll(x => x.Id == b1.Id);
                    foreach (Option notServedOptionB1 in notServedOptionsCustomerB1) { //Start searching to find a match for each not served option of customer B1
                        if (notServedOptionB1.Location.Type == 1 && notServedOptionB1.Location.Cap >= notServedOptionB1.Location.MaxCap) {continue;} //If the location of that option is shared location and there is no available capacity for it continue
                        // Otherwise start searching for match either in the same or other route
                        foreach (Route rt2 in sol.Routes) {
                            foreach (Option opt2 in rt2.SequenceOfOptions) {
                                if (opt2 == rt2.SequenceOfOptions.First() || opt2 == rt2.SequenceOfOptions.Last()) {continue;} //Avoid the first and last option of the route
                                if (opt1 == opt2) {continue;} //Avoid searching if it is the same option
                                b2 = opt2;
                                Customer customerB2 = b2.Cust;
                                if (customerB2.Id == 1000) {continue;} //Probably unnecessary because there are no options corresponding to the warehouse but add it to be safe
                                if (optionsPerCustomer[customerB2.Id].Count <= 1) {continue;} // Avoid creating a list for customers with only one available option
                                List<Option> notServedOptionsCustomerB2 = optionsPerCustomer[customerB2.Id]; //Create a list with the remaining options of customer B2
                                notServedOptionsCustomerB2.RemoveAll(x => x.Id == b2.Id);
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
                                    // Check if the time windows are respected for the insertion of the new options in different routes
                                    if (rt1 != rt2) {
                                        // NOTE (perf, Phase 3): bool-only variants — only `.Item1` was ever read.
                                        if (!sol.RespectsTimeWindow2Feasible(rt1, rt1.SequenceOfOptions.IndexOf(b1), notServedOptionB1.Location)
                                            || !sol.RespectsTimeWindow2Feasible(rt2, rt2.SequenceOfOptions.IndexOf(b2), notServedOptionB2.Location)) {continue;}
                                    }
                                    else {
                                        if (!sol.RespectsTimeWindow2Feasible(rt1, rt1.SequenceOfOptions.IndexOf(b1), notServedOptionB1.Location)) {continue;} // If the insertion of the first option leads to TW violation continue.
                                        // If no TW window violation then insert the new option in the temp route and check for the second option
                                        Route rtTemp = rt1.getTempCopy(rt1, distinctLocationsInSolution);
                                        // NOTE (perf, Phase 2): four `list.Select(x => x.Id).ToList()` + `.IndexOf(...)`
                                        // pairs used to live here (each materializing a fresh List<int> just to
                                        // search it once). Confirmed via grep that `indexB1`, `indexB1Location`, and
                                        // `indexB2Location` (and their backing lists) were computed and never read
                                        // again anywhere in this file — genuinely dead, and safe to delete outright
                                        // unlike the Flip/getTempCopy case earlier in this file: this Select just reads
                                        // the `.Id` int property, no Clone() call hides inside it, so there's no
                                        // side-effect-through-a-bug risk here. Only `indexB2` (used immediately below)
                                        // was live; replaced its list-then-IndexOf with FindIndex, which searches the
                                        // existing List<Option> in place — same result, zero allocation.
                                        rtTemp.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b1)] = notServedOptionB1;
                                        rtTemp.SequenceOfLocations[rt1.SequenceOfOptions.IndexOf(b1)] = notServedOptionB1.Location;
                                        int indexB2 = rtTemp.SequenceOfOptions.FindIndex(x => x.Id == b2.Id);
                                        var tw2 = sol.RespectsTimeWindow2(rtTemp, indexB2, notServedOptionB2.Location);
                                        if (!tw2.Item1) {continue;} // If the insertion of the second option leads to TW violation continue.
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
                                        double costRemoved1 = sol.CalculateDistance(rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b1) - 1].Location, b1.Location) + sol.CalculateDistance(b1.Location, rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b1) + 1].Location);
                                        double costAdded1 = sol.CalculateDistance(rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b1) - 1].Location, notServedOptionB1.Location) + sol.CalculateDistance(notServedOptionB1.Location, rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b1) + 1].Location);
                                        double costRemoved2 = sol.CalculateDistance(rt2.SequenceOfOptions[rt2.SequenceOfOptions.IndexOf(b2) - 1].Location, b2.Location) + sol.CalculateDistance(b2.Location, rt2.SequenceOfOptions[rt2.SequenceOfOptions.IndexOf(b2) + 1].Location);
                                        double costAdded2 = sol.CalculateDistance(rt2.SequenceOfOptions[rt2.SequenceOfOptions.IndexOf(b2) - 1].Location, notServedOptionB2.Location) + sol.CalculateDistance(notServedOptionB2.Location, rt2.SequenceOfOptions[rt2.SequenceOfOptions.IndexOf(b2) + 1].Location);
                                        moveCost = costAdded1 + costAdded2 - costRemoved1 - costRemoved2;
                                        costChangeFirstRoute = costAdded1 - costRemoved1;
                                        costChangeSecondRoute = costAdded2 - costRemoved2;
                                        newUtilizationMetricRoute1 = Math.Pow(Convert.ToDouble(rt1.Capacity - (rt1.Load - b1.Cust.Dem + notServedOptionB1.Cust.Dem)), power);
                                        newUtilizationMetricRoute2 = Math.Pow(Convert.ToDouble(rt2.Capacity - (rt2.Load - b2.Cust.Dem + notServedOptionB2.Cust.Dem)), power);
                                        newSolUtilizationMetric = sol.SolutionUtilizationMetric - rt1.RouteUtilizationMetric - rt2.RouteUtilizationMetric + newUtilizationMetricRoute1 + newUtilizationMetricRoute2;
                                    } else {
                                        if (Math.Abs(rt1.SequenceOfOptions.IndexOf(b1) - rt1.SequenceOfOptions.IndexOf(b2)) == 1) { // Calculate cost change if they are next to each other
                                            if (rt1.SequenceOfOptions.IndexOf(b1) < rt1.SequenceOfOptions.IndexOf(b2)) {
                                                double costRemoved = sol.CalculateDistance(rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b1) - 1].Location, b1.Location) + sol.CalculateDistance(b1.Location, b2.Location) + sol.CalculateDistance(b2.Location, rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b2) + 1].Location);
                                                double costAdded = sol.CalculateDistance(rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b1) - 1].Location, notServedOptionB1.Location) + sol.CalculateDistance(notServedOptionB1.Location, notServedOptionB2.Location) + sol.CalculateDistance(notServedOptionB2.Location, rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b2) + 1].Location);
                                                moveCost = costAdded - costRemoved;
                                            } else {
                                                double costRemoved = sol.CalculateDistance(rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b2) - 1].Location, b2.Location) + sol.CalculateDistance(b2.Location, b1.Location) + sol.CalculateDistance(b1.Location, rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b1) + 1].Location);
                                                double costAdded = sol.CalculateDistance(rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b2) - 1].Location, notServedOptionB2.Location) + sol.CalculateDistance(notServedOptionB2.Location, notServedOptionB1.Location) + sol.CalculateDistance(notServedOptionB1.Location, rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b1) + 1].Location);
                                                moveCost = costAdded - costRemoved;
                                            }
                                        } else {
                                            double costRemoved = sol.CalculateDistance(rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b1) - 1].Location, b1.Location) + sol.CalculateDistance(b1.Location, rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b1) + 1].Location);
                                            costRemoved += sol.CalculateDistance(rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b2) - 1].Location, b2.Location) + sol.CalculateDistance(b2.Location, rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b2) + 1].Location);
                                            double costAdded = sol.CalculateDistance(rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b1) - 1].Location, notServedOptionB1.Location) + sol.CalculateDistance(notServedOptionB1.Location, rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b1) + 1].Location);
                                            costAdded += sol.CalculateDistance(rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b2) - 1].Location, notServedOptionB2.Location) + sol.CalculateDistance(notServedOptionB2.Location, rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b2) + 1].Location);
                                            moveCost = costAdded - costRemoved;
                                        }
                                        newSolUtilizationMetric = Math.Pow(Convert.ToDouble(rt1.Capacity - (rt1.Load - b1.Cust.Dem + notServedOptionB1.Cust.Dem + b2.Cust.Dem - notServedOptionB2.Cust.Dem)), power);
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
                                        if (rt1 == rt2) {
                                            if (Math.Abs(rt1.SequenceOfOptions.IndexOf(b1) - rt1.SequenceOfOptions.IndexOf(b2)) == 1) { // Calculate cost change if they are next to each other
                                                if (rt1.SequenceOfOptions.IndexOf(b1) < rt1.SequenceOfOptions.IndexOf(b2)) {
                                                    if (PromiseIsBroken(rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b1) - 1].Id, notServedOptionB1.Id, moveCost + sol.Cost + smallDouble, sol)) {continue;}
                                                    if (PromiseIsBroken(notServedOptionB1.Id, notServedOptionB2.Id, moveCost + sol.Cost + smallDouble, sol)) {continue;}
                                                    if (PromiseIsBroken(notServedOptionB2.Id, rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b2) + 1].Id, moveCost + sol.Cost + smallDouble, sol)) {continue;}
                                                } else {
                                                    if (PromiseIsBroken(rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b2) - 1].Id, notServedOptionB2.Id, moveCost + sol.Cost + smallDouble, sol)) {continue;}
                                                    if (PromiseIsBroken(notServedOptionB2.Id, notServedOptionB1.Id, moveCost + sol.Cost + smallDouble, sol)) {continue;}
                                                    if (PromiseIsBroken(notServedOptionB1.Id, rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b1) + 1].Id, moveCost + sol.Cost + smallDouble, sol)) {continue;}
                                                }
                                            } else {
                                                if (PromiseIsBroken(rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b1) - 1].Id, notServedOptionB1.Id, moveCost + sol.Cost + smallDouble, sol)) {continue;}
                                                if (PromiseIsBroken(notServedOptionB1.Id, rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b1) + 1].Id, moveCost + sol.Cost + smallDouble, sol)) {continue;}
                                                if (PromiseIsBroken(rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b2) - 1].Id, notServedOptionB2.Id, moveCost + sol.Cost + smallDouble, sol)) {continue;}
                                                if (PromiseIsBroken(notServedOptionB2.Id, rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b2) + 1].Id, moveCost + sol.Cost + smallDouble, sol)) {continue;}
                                            }
                                        } else {
                                            if (PromiseIsBroken(rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b1) - 1].Id, notServedOptionB1.Id, moveCost + sol.Cost + smallDouble, sol)) {continue;}
                                            if (PromiseIsBroken(notServedOptionB1.Id, rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b1) + 1].Id, moveCost + sol.Cost + smallDouble, sol)) {continue;}
                                            if (PromiseIsBroken(rt2.SequenceOfOptions[rt2.SequenceOfOptions.IndexOf(b2) - 1].Id, notServedOptionB2.Id, moveCost + sol.Cost + smallDouble, sol)) {continue;}
                                            if (PromiseIsBroken(notServedOptionB2.Id, rt2.SequenceOfOptions[rt2.SequenceOfOptions.IndexOf(b2) + 1].Id, moveCost + sol.Cost + smallDouble, sol)) {continue;}
                                        }

                                        psm.TotalCost = moveCost + openRoutes * 10000;
                                        psm.MoveCost = moveCost;
                                        psm.PositionOfFirstRoute = sol.Routes.IndexOf(rt1);
                                        psm.PositionOfSecondRoute = sol.Routes.IndexOf(rt2);
                                        psm.PositionOfFirstOption = rt1.SequenceOfOptions.IndexOf(b1);
                                        psm.PositionOfSecondOption = rt2.SequenceOfOptions.IndexOf(b2);
                                        psm.CostChangeFirstRt = costChangeFirstRoute;
                                        psm.CostChangeSecondRt = costChangeSecondRoute;
                                        psm.AltOption1 = notServedOptionB1;
                                        psm.AltOption2 = notServedOptionB2;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            return psm;
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
