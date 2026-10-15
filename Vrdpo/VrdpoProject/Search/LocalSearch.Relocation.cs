namespace VrdpoProject
{
    public partial class LocalSearch
    {
        // Moves the stop B (between A and C) behind the stop F (before G) of any route.
        public Relocation FindBestRelocationMove(Relocation rm, Solution sol)
        {
            sol.BeginTimeWindowMemo();
            for (int originRouteIndex = 0; originRouteIndex < sol.Routes.Count; originRouteIndex++)
            {
                Route rt1 = sol.Routes[originRouteIndex];
                for (int targetRouteIndex = 0; targetRouteIndex < sol.Routes.Count; targetRouteIndex++)
                {
                    Route rt2 = sol.Routes[targetRouteIndex];
                    for (int originOptionIndex = 1; originOptionIndex < rt1.SequenceOfOptions.Count - 1; originOptionIndex++)
                    {
                        Option A = rt1.SequenceOfOptions[originOptionIndex - 1];
                        Option B = rt1.SequenceOfOptions[originOptionIndex];
                        Option C = rt1.SequenceOfOptions[originOptionIndex + 1];

                        if (rt1 != rt2 && rt2.Load + B.Cust.Dem > rt2.Capacity)
                        {
                            continue;
                        }
                        int openRoutes = sol.Routes.Count;
                        if (rt1.Load - B.Cust.Dem == 0)
                        {
                            openRoutes--;
                        }
                        double dAB = sol.CalculateDistance(A.Location, B.Location);
                        double dBC = sol.CalculateDistance(B.Location, C.Location);
                        double dAC = sol.CalculateDistance(A.Location, C.Location);
                        double costRemovedBase = dAB + dBC;
                        double costChangeOriginRt = dAC - dAB - dBC;

                        double newUtilizationMetricRoute1 = UtilizationTerm(Convert.ToDouble(rt1.Capacity - (rt1.Load - B.Cust.Dem)));
                        double newUtilizationMetricRoute2 = UtilizationTerm(Convert.ToDouble(rt2.Capacity - (rt2.Load + B.Cust.Dem)));
                        double newSolUtilizationMetric = sol.SolutionUtilizationMetric - rt1.RouteUtilizationMetric - rt2.RouteUtilizationMetric + newUtilizationMetricRoute1 + newUtilizationMetricRoute2;
                        double ratio = (sol.SolutionUtilizationMetric + 1) / (newSolUtilizationMetric + 1);
                        if (sol.Routes.Count == sol.LowerBoundRoutes || rt1 == rt2)
                        {
                            ratio = 1;
                        }

                        for (int targetOptionIndex = 0; targetOptionIndex < rt2.SequenceOfOptions.Count - 1; targetOptionIndex++)
                        {
                            if (originRouteIndex == targetRouteIndex && (targetOptionIndex == originOptionIndex || targetOptionIndex == originOptionIndex - 1))
                            {
                                continue;
                            }
                            Option F = rt2.SequenceOfOptions[targetOptionIndex];
                            Option G = rt2.SequenceOfOptions[targetOptionIndex + 1];

                            double dFB = sol.CalculateDistance(F.Location, B.Location);
                            double dBG = sol.CalculateDistance(B.Location, G.Location);
                            double dFG = sol.CalculateDistance(F.Location, G.Location);
                            double costAdded = dAC + dFB + dBG;
                            double costRemoved = costRemovedBase + dFG;
                            double moveCost = costAdded - costRemoved;
                            double ratioCombinedMoveCost = ratio * moveCost;

                            if (Stats != null)
                            {
                                Stats.Evaluated[MoveStats.Relocation]++;
                                int usedChange = rt1 == rt2 ? 0 : (rt1.Load - B.Cust.Dem == 0 ? -1 : 0) + (rt2.IsEmpty ? 1 : 0);
                                if (MoveStats.Improves(usedChange, moveCost)
                                    && (rt1 == rt2
                                        ? SameRouteRelocateFeasible(sol, rt1, originOptionIndex, targetOptionIndex)
                                        : sol.RespectsTimeWindow2FeasibleMemo(targetRouteIndex, rt2, targetOptionIndex, rt1.SequenceOfLocations[originOptionIndex])))
                                {
                                    Stats.Improving[MoveStats.Relocation]++;
                                    if (PromiseHolds(F.Id, B.Id, moveCost, sol) && PromiseHolds(B.Id, G.Id, moveCost, sol) && PromiseHolds(A.Id, C.Id, moveCost, sol))
                                    {
                                        Stats.Admissible[MoveStats.Relocation]++;
                                    }
                                }
                            }

                            if (ratioCombinedMoveCost + openRoutes * Solver.RoutePenalty < rm.TotalCost + smallDouble & moveCost != 0)
                            {
                                if (rt1 == rt2)
                                {
                                    if (!SameRouteRelocateFeasible(sol, rt1, originOptionIndex, targetOptionIndex)) { continue; }
                                }
                                else if (!sol.RespectsTimeWindow2FeasibleMemo(targetRouteIndex, rt2, targetOptionIndex, rt1.SequenceOfLocations[originOptionIndex]))
                                {
                                    continue;
                                }
                                if (PromiseIsBroken(F.Id, B.Id, moveCost + sol.Cost + smallDouble, sol)) { continue; }
                                if (PromiseIsBroken(B.Id, G.Id, moveCost + sol.Cost + smallDouble, sol)) { continue; }
                                if (PromiseIsBroken(A.Id, C.Id, moveCost + sol.Cost + smallDouble, sol)) { continue; }

                                rm.TotalCost = moveCost + openRoutes * Solver.RoutePenalty;
                                rm.MoveCost = moveCost;
                                rm.OriginRoutePosition = originRouteIndex;
                                rm.TargetRoutePosition = targetRouteIndex;
                                rm.OriginOptionPosition = originOptionIndex;
                                rm.TargetOptionPosition = targetOptionIndex;
                                rm.CostChangeOriginRt = costChangeOriginRt;
                                rm.CostChangeTargetRt = dFB + dBG - dFG;
                            }
                        }
                    }
                }
            }
            return rm;
        }

        // Time-window feasibility of moving the stop at `originIdx` behind the stop at `targetIdx` inside one route.
        private static bool SameRouteRelocateFeasible(Solution sol, Route rt, int originIdx, int targetIdx)
        {
            var locs = rt.SequenceOfLocations;
            var list = new List<Location>(locs.Count);
            for (int i = 0; i < locs.Count; i++)
            {
                if (i == originIdx) { continue; }
                list.Add(locs[i]);
                if (i == targetIdx) { list.Add(locs[originIdx]); }
            }
            return sol.SequenceFeasible(list);
        }

        public void ApplyRelocationMove(Relocation rm, Solution sol)
        {
            if (!rm.IsValid()) { return; }
            sol.LastMove = "relocate";
            Route originRt = sol.Routes[rm.OriginRoutePosition];
            Route targetRt = sol.Routes[rm.TargetRoutePosition];
            AssertRouteFeasible(sol, originRt, "before relocate");
            AssertRouteFeasible(sol, targetRt, "before relocate");
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
                int insertAt = rm.OriginOptionPosition < rm.TargetOptionPosition ? rm.TargetOptionPosition : rm.TargetOptionPosition + 1;
                targetRt.SequenceOfOptions.Insert(insertAt, B);
                targetRt.SequenceOfCustomers.Insert(insertAt, B.Cust);
                targetRt.SequenceOfLocations.Insert(insertAt, B.Location);
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
                originRt.RouteUtilizationMetric = UtilizationTerm(Convert.ToDouble(originRt.Capacity - originRt.Load));
                targetRt.RouteUtilizationMetric = UtilizationTerm(Convert.ToDouble(targetRt.Capacity - targetRt.Load));
                sol.UpdateTimes(originRt);
                sol.UpdateTimes(targetRt);
                UpdateRouteCostAndLoad(originRt, sol);
                UpdateRouteCostAndLoad(targetRt, sol);
            }
            sol.Cost += rm.MoveCost;
            sol.Promises[A.Id, C.Id] = sol.Cost;
            sol.Promises[F.Id, B.Id] = sol.Cost;
            sol.Promises[B.Id, G.Id] = sol.Cost;
            AssertRouteFeasible(sol, originRt, "after relocate");
            AssertRouteFeasible(sol, targetRt, "after relocate");
        }
    }
}
