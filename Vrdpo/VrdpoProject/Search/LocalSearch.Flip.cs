namespace VrdpoProject
{
    public partial class LocalSearch
    {
        // Replaces the served option B1 of a customer (between A and C) by another option B2 of the same
        // customer, inserted behind the stop F (before G) of any route. A flip to a lower priority is
        // allowed only while the service level stays above its thresholds.
        // Number of insertion positions the flip of the stop at custInd1 of rt1 has over all routes.
        private static int FlipPositions(Solution sol, Route rt1, int custInd1)
        {
            int n = 0;
            foreach (Route rt2 in sol.Routes)
            {
                int first = rt2 == rt1 ? custInd1 + 1 : 0;
                n += Math.Max(0, rt2.SequenceOfOptions.Count - 1 - first);
            }
            return n;
        }

        public Flip FindBestFlipMove(Flip flip, Solution sol)
        {
            int baseP0 = sol.Po0Count, baseP1 = sol.Po1Count, baseP2 = sol.Po2Count;
            sol.BeginTimeWindowMemo();
            for (int rtInd1 = 0; rtInd1 < sol.Routes.Count; rtInd1++)
            {
                Route rt1 = sol.Routes[rtInd1];
                for (int custInd1 = 1; custInd1 < rt1.SequenceOfCustomers.Count - 1; custInd1++)
                {
                    Customer custB = rt1.SequenceOfCustomers[custInd1];
                    if (custB.Options.Count < 2) { continue; }

                    Option custBServedOption = null;
                    foreach (Option opt in custB.Options)
                    {
                        if (sol.Options[opt.Id].IsServed)
                        {
                            custBServedOption = opt;
                            break;
                        }
                    }
                    Option A = rt1.SequenceOfOptions[custInd1 - 1];
                    Option B1 = rt1.SequenceOfOptions[custInd1];
                    Option C = rt1.SequenceOfOptions[custInd1 + 1];
                    double dAB1 = sol.CalculateDistance(A.Location, B1.Location);
                    double dB1C = sol.CalculateDistance(B1.Location, C.Location);
                    double dAC = sol.CalculateDistance(A.Location, C.Location);
                    double costRemovedBase = dAB1 + dB1C;
                    double costChangeOriginRt = dAC - dAB1 - dB1C;
                    bool emptiesRt1 = rt1.Load - B1.Cust.Dem == 0;
                    double newUtilizationMetricRoute1 = SquareOrPow(rt1.Capacity - (rt1.Load - B1.Cust.Dem));

                    for (int optInd = 0; optInd < custB.Options.Count; optInd++)
                    {
                        Option B2 = custB.Options[optInd];
                        if (custBServedOption == B2) { continue; }
                        if (B2 == B1) { continue; }
                        if (!B2.Allowed) { continue; }
                        // Shared location full, or the service level would fall below its threshold: every
                        // insertion position of B2 is an infeasible candidate.
                        if (B2.Location.MaxCap == B2.Location.Cap)
                        {
                            if (Stats != null) { Stats.Evaluated[MoveStats.Flip] += FlipPositions(sol, rt1, custInd1); }
                            continue;
                        }
                        var (newSl0, newSl1) = TempServiceLevelPair(baseP0, baseP1, baseP2, B1.Prio, B2.Prio);
                        if (newSl0 < Solver.MinServiceLevel0 || newSl1 < Solver.MinServiceLevel1)
                        {
                            if (B1.Prio < B2.Prio)
                            {
                                if (Stats != null) { Stats.Evaluated[MoveStats.Flip] += FlipPositions(sol, rt1, custInd1); }
                                continue;
                            }
                        }
                        for (int rtInd2 = 0; rtInd2 < sol.Routes.Count; rtInd2++)
                        {
                            // openRoutes is reset per target route but decremented per candidate position that passes
                            // the time check when the move empties rt1, so it keeps decreasing along the route.
                            // The recorded TotalCost depends on this, so it is kept as is.
                            int openRoutes = sol.Routes.Count;
                            Route rt2 = sol.Routes[rtInd2];
                            int firstTargetIndex = rt2 == rt1 ? custInd1 + 1 : 0;

                            if (rt2.Load + custB.Dem > rt2.Capacity)
                            {
                                if (Stats != null) { Stats.Evaluated[MoveStats.Flip] += Math.Max(0, rt2.SequenceOfOptions.Count - 1 - firstTargetIndex); }
                                continue;
                            }

                            double newUtilizationMetricRoute2 = SquareOrPow(rt2.Capacity - (rt2.Load + B2.Cust.Dem));
                            double newSolUtilizationMetric = sol.SolutionUtilizationMetric - rt1.RouteUtilizationMetric - rt2.RouteUtilizationMetric + newUtilizationMetricRoute1 + newUtilizationMetricRoute2;
                            double ratio = (sol.SolutionUtilizationMetric + 1) / (newSolUtilizationMetric + 1);
                            if (sol.Routes.Count == sol.LowerBoundRoutes)
                            {
                                ratio = 1;
                            }

                            for (int targetOptionIndex = firstTargetIndex; targetOptionIndex < rt2.SequenceOfOptions.Count - 1; targetOptionIndex++)
                            {
                                Option F = rt2.SequenceOfOptions[targetOptionIndex];
                                Option G = rt2.SequenceOfOptions[targetOptionIndex + 1];

                                if (Stats != null)
                                {
                                    // Same arithmetic as moveCost below.
                                    double countedMoveCost = (dAC + sol.CalculateDistance(F.Location, B2.Location) + sol.CalculateDistance(B2.Location, G.Location))
                                        - (costRemovedBase + sol.CalculateDistance(F.Location, G.Location));
                                    Stats.Evaluated[MoveStats.Flip]++;
                                    int usedChange = (emptiesRt1 ? -1 : 0) + (rt2.IsEmpty ? 1 : 0);
                                    bool feasible = sol.RespectsTimeWindow2FeasibleMemo(rtInd2, rt2, targetOptionIndex, B2.Location);
                                    if (feasible) { Stats.Feasible[MoveStats.Flip]++; }
                                    if (feasible && MoveStats.Improves(usedChange, countedMoveCost))
                                    {
                                        Stats.Improving[MoveStats.Flip]++;
                                        if (PromiseHolds(F.Id, B2.Id, countedMoveCost, sol) && PromiseHolds(B2.Id, G.Id, countedMoveCost, sol) && PromiseHolds(A.Id, C.Id, countedMoveCost, sol))
                                        {
                                            Stats.Admissible[MoveStats.Flip]++;
                                        }
                                    }
                                }

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
                                double ratioCombinedMoveCost = ratio * moveCost;

                                if (ratioCombinedMoveCost + openRoutes * Solver.RoutePenalty < flip.TotalCost + smallDouble)
                                {
                                    if (!twChecked && !sol.RespectsTimeWindow2FeasibleMemo(rtInd2, rt2, targetOptionIndex, B2.Location)) { continue; }
                                    if (PromiseIsBroken(F.Id, B2.Id, moveCost + sol.Cost + smallDouble, sol)) { continue; }
                                    if (PromiseIsBroken(B2.Id, G.Id, moveCost + sol.Cost + smallDouble, sol)) { continue; }
                                    if (PromiseIsBroken(A.Id, C.Id, moveCost + sol.Cost + smallDouble, sol)) { continue; }
                                    flip.TotalCost = moveCost + openRoutes * Solver.RoutePenalty;
                                    flip.MoveCost = moveCost;
                                    flip.OriginRoutePosition = rtInd1;
                                    flip.TargetRoutePosition = rtInd2;
                                    flip.TargetOptionPosition = targetOptionIndex;
                                    flip.OriginOptionPosition = custInd1;
                                    flip.CostChangeOriginRt = costChangeOriginRt;
                                    flip.CostChangeTargetRt = dFB2 + dB2G - dFG;
                                    flip.NewOptionIndex = optInd;
                                }
                            }
                        }
                    }
                }
            }
            return flip;
        }

        public void ApplyFlipMove(Flip flip, Solution sol)
        {
            if (!flip.IsValid()) { return; }
            sol.LastMove = "flip";
            Route originRt = sol.Routes[flip.OriginRoutePosition];
            Route targetRt = sol.Routes[flip.TargetRoutePosition];
            AssertRouteFeasible(sol, targetRt, "before flip");
            AssertRouteFeasible(sol, originRt, "before flip");
            Option A = originRt.SequenceOfOptions[flip.OriginOptionPosition - 1];
            Option B1 = originRt.SequenceOfOptions[flip.OriginOptionPosition];
            Option B2 = originRt.SequenceOfCustomers[flip.OriginOptionPosition].Options[flip.NewOptionIndex];
            Option C = originRt.SequenceOfOptions[flip.OriginOptionPosition + 1];
            Option F = targetRt.SequenceOfOptions[flip.TargetOptionPosition];
            Option G = targetRt.SequenceOfOptions[flip.TargetOptionPosition + 1];

            originRt.SequenceOfOptions.RemoveAt(flip.OriginOptionPosition);
            originRt.SequenceOfCustomers.RemoveAt(flip.OriginOptionPosition);
            originRt.SequenceOfLocations.RemoveAt(flip.OriginOptionPosition);

            if (originRt == targetRt)
            {
                int insertAt = flip.OriginOptionPosition < flip.TargetOptionPosition ? flip.TargetOptionPosition : flip.TargetOptionPosition + 1;
                targetRt.SequenceOfOptions.Insert(insertAt, B2);
                targetRt.SequenceOfCustomers.Insert(insertAt, B2.Cust);
                targetRt.SequenceOfLocations.Insert(insertAt, B2.Location);
                sol.UpdateTimes(originRt);
                originRt.Cost += flip.MoveCost;
                UpdateRouteCostAndLoad(originRt, sol);
                originRt.RouteUtilizationMetric = UtilizationTerm(Convert.ToDouble(originRt.Capacity - originRt.Load));
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
                originRt.RouteUtilizationMetric = UtilizationTerm(Convert.ToDouble(originRt.Capacity - originRt.Load));
                targetRt.RouteUtilizationMetric = UtilizationTerm(Convert.ToDouble(targetRt.Capacity - targetRt.Load));
            }
            sol.Cost += flip.MoveCost;
            sol.AdjustServiceLevelCounts(B1.Prio, B2.Prio);
            B1.IsServed = false;
            B2.IsServed = true;
            B2.Location.Cap++;
            B1.Location.Cap--;
            sol.Promises[A.Id, C.Id] = sol.Cost;
            sol.Promises[F.Id, B2.Id] = sol.Cost;
            sol.Promises[B2.Id, G.Id] = sol.Cost;
            AssertRouteFeasible(sol, targetRt, "after flip");
            AssertRouteFeasible(sol, originRt, "after flip");
        }
    }
}
