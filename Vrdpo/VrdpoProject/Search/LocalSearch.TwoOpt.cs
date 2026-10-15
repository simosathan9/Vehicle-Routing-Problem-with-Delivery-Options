namespace VrdpoProject
{
    public partial class LocalSearch
    {
        // Same route: reverses the segment between the arcs (A,B) and (K,L). Two routes: exchanges the
        // tails after A and after K.
        public TwoOpt FindBestTwoOptMove(TwoOpt top, Solution sol)
        {
            // Per-route tables: prefixLoad[r][k] = demand of stops 0..k, stepDist[r][k] = d(stop k, stop k+1),
            // nuAfterB / nuAfterK = utilization term of the route without stop k+1 / stop k,
            // emptiesB / emptiesK = whether that removal empties the route.
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
                    nuAfterB[r][k] = UtilizationTerm(Convert.ToDouble(rr.Capacity - (rr.Load - ro[k + 1].Cust.Dem)));
                    nuAfterK[r][k] = UtilizationTerm(Convert.ToDouble(rr.Capacity - (rr.Load - ro[k].Cust.Dem)));
                    emptiesB[r][k] = rr.Load - ro[k + 1].Cust.Dem == 0;
                    emptiesK[r][k] = rr.Load - ro[k].Cust.Dem == 0;
                }
            }

            for (int rtInd1 = 0; rtInd1 < sol.Routes.Count; rtInd1++)
            {
                Route rt1 = sol.Routes[rtInd1];
                for (int rtInd2 = 0; rtInd2 < sol.Routes.Count; rtInd2++)
                {
                    Route rt2 = sol.Routes[rtInd2];
                    for (int optInd1 = 0; optInd1 < rt1.SequenceOfOptions.Count - 1; optInd1++)
                    {
                        int start2 = rt1 == rt2 ? optInd1 + 2 : 0;
                        for (int optInd2 = start2; optInd2 < rt2.SequenceOfOptions.Count - 1; optInd2++)
                        {
                            int openRoutes = sol.Routes.Count;
                            Option A = rt1.SequenceOfOptions[optInd1];
                            Option B = rt1.SequenceOfOptions[optInd1 + 1];
                            Option K = rt2.SequenceOfOptions[optInd2];
                            Option L = rt2.SequenceOfOptions[optInd2 + 1];

                            double moveCost;
                            double ratioCombinedMoveCost;
                            if (rt1 == rt2)
                            {
                                if (optInd1 == 0 & optInd2 == rt1.SequenceOfOptions.Count - 2) { continue; }
                                double costAdded = sol.CalculateDistance(A.Location, K.Location) + sol.CalculateDistance(B.Location, L.Location);
                                double costRemoved = sol.CalculateDistance(A.Location, B.Location) + sol.CalculateDistance(K.Location, L.Location);
                                moveCost = costAdded - costRemoved;
                                ratioCombinedMoveCost = moveCost;
                            }
                            else
                            {
                                if (optInd1 == 0 && optInd2 == 0) { continue; }
                                if (optInd1 == rt1.SequenceOfOptions.Count - 2 & optInd2 == rt2.SequenceOfOptions.Count - 2) { continue; }

                                double rt1FirstSegmentLoad = prefixLoad[rtInd1][optInd1];
                                double rt1SecondSegmentLoad = rt1.Load - rt1FirstSegmentLoad;
                                double rt2FirstSegmentLoad = prefixLoad[rtInd2][optInd2];
                                double rt2SecondSegmentLoad = rt2.Load - rt2FirstSegmentLoad;
                                if (rt1FirstSegmentLoad + rt2SecondSegmentLoad > rt1.Capacity
                                    || rt2FirstSegmentLoad + rt1SecondSegmentLoad > rt2.Capacity)
                                {
                                    if (Stats != null) { Stats.Evaluated[MoveStats.TwoOpt]++; }
                                    continue;
                                }

                                double costAdded = sol.CalculateDistance(A.Location, L.Location) + sol.CalculateDistance(B.Location, K.Location);
                                double costRemoved = stepDist[rtInd1][optInd1] + stepDist[rtInd2][optInd2];
                                moveCost = costAdded - costRemoved;
                                if (emptiesB[rtInd1][optInd1] || emptiesK[rtInd2][optInd2])
                                {
                                    openRoutes--;
                                }
                                double newUtilizationMetricRoute1 = nuAfterB[rtInd1][optInd1];
                                double newUtilizationMetricRoute2 = nuAfterK[rtInd2][optInd2];
                                double newSolUtilizationMetric = sol.SolutionUtilizationMetric - rt1.RouteUtilizationMetric - rt2.RouteUtilizationMetric + newUtilizationMetricRoute1 + newUtilizationMetricRoute2;
                                double ratio = (sol.SolutionUtilizationMetric + 1) / (newSolUtilizationMetric + 1);
                                if (sol.Routes.Count == sol.LowerBoundRoutes)
                                {
                                    ratio = 1;
                                }
                                ratioCombinedMoveCost = ratio * moveCost;
                            }

                            if (Stats != null)
                            {
                                Stats.Evaluated[MoveStats.TwoOpt]++;
                                int usedChange = 0;
                                if (rt1 != rt2)
                                {
                                    // Stops of each new route: head of one route plus tail of the other.
                                    int n1 = rt1.SequenceOfOptions.Count, n2 = rt2.SequenceOfOptions.Count;
                                    int newStops1 = optInd1 + (n2 - 2 - optInd2);
                                    int newStops2 = optInd2 + (n1 - 2 - optInd1);
                                    usedChange = (newStops1 > 0 ? 1 : 0) + (newStops2 > 0 ? 1 : 0) - (rt1.IsEmpty ? 0 : 1) - (rt2.IsEmpty ? 0 : 1);
                                }
                                bool feasible = rt1 == rt2
                                    ? SameRouteReversalFeasible(sol, rt1, optInd1, optInd2)
                                    : sol.RespectsTimeWindowFeasible(rt1, optInd1, rt2, optInd2) && sol.RespectsTimeWindowFeasible(rt2, optInd2, rt1, optInd1);
                                if (feasible) { Stats.Feasible[MoveStats.TwoOpt]++; }
                                if (feasible && MoveStats.Improves(usedChange, moveCost))
                                {
                                    Stats.Improving[MoveStats.TwoOpt]++;
                                    if (PromiseHolds(A.Id, L.Id, moveCost, sol) && PromiseHolds(B.Id, K.Id, moveCost, sol))
                                    {
                                        Stats.Admissible[MoveStats.TwoOpt]++;
                                    }
                                }
                            }

                            if (ratioCombinedMoveCost + openRoutes * Solver.RoutePenalty < top.TotalCost + smallDouble & moveCost != 0)
                            {
                                if (rt1 == rt2)
                                {
                                    if (!SameRouteReversalFeasible(sol, rt1, optInd1, optInd2)) { continue; }
                                }
                                else if (!sol.RespectsTimeWindowFeasible(rt1, optInd1, rt2, optInd2)
                                    || !sol.RespectsTimeWindowFeasible(rt2, optInd2, rt1, optInd1))
                                {
                                    continue;
                                }
                                if (PromiseIsBroken(A.Id, L.Id, moveCost + sol.Cost + smallDouble, sol)) { continue; }
                                if (PromiseIsBroken(B.Id, K.Id, moveCost + sol.Cost + smallDouble, sol)) { continue; }

                                top.TotalCost = moveCost + openRoutes * Solver.RoutePenalty;
                                top.PositionOfFirstRoute = rtInd1;
                                top.PositionOfSecondRoute = rtInd2;
                                top.PositionOfFirstOption = optInd1;
                                top.PositionOfSecondOption = optInd2;
                                top.MoveCost = moveCost;
                                if (rt1 != rt2)
                                {
                                    // The resulting routes' time arrays are applied as they are.
                                    var tw1 = sol.RespectsTimeWindow(rt1, optInd1, rt2, optInd2);
                                    var tw2 = sol.RespectsTimeWindow(rt2, optInd2, rt1, optInd1);
                                    top.Ect1 = tw1.Item2;
                                    top.Ect2 = tw2.Item2;
                                    top.Lat1 = tw1.Item3;
                                    top.Lat2 = tw2.Item3;
                                }
                            }
                        }
                    }
                }
            }
            return top;
        }

        // Is the route still time-window feasible after reversing the segment (optInd1+1 .. optInd2)?
        private static bool SameRouteReversalFeasible(Solution sol, Route rt, int optInd1, int optInd2)
        {
            var seq = rt.SequenceOfLocations;
            var list = new List<Location>(seq.Count);
            for (int i = 0; i <= optInd1; i++) { list.Add(seq[i]); }
            for (int i = optInd2; i > optInd1; i--) { list.Add(seq[i]); }
            for (int i = optInd2 + 1; i < seq.Count; i++) { list.Add(seq[i]); }
            return sol.SequenceFeasible(list);
        }

        public void ApplyTwoOptMove(TwoOpt top, Solution sol)
        {
            if (!top.IsValid()) { return; }
            sol.LastMove = "two opt";
            Route rt1 = sol.Routes[top.PositionOfFirstRoute];
            Route rt2 = sol.Routes[top.PositionOfSecondRoute];
            AssertRouteFeasible(sol, rt1, "before two opt");
            AssertRouteFeasible(sol, rt2, "before two opt");
            Option A = rt1.SequenceOfOptions[top.PositionOfFirstOption];
            Option B = rt1.SequenceOfOptions[top.PositionOfFirstOption + 1];
            Option K = rt2.SequenceOfOptions[top.PositionOfSecondOption];
            Option L = rt2.SequenceOfOptions[top.PositionOfSecondOption + 1];
            int frombase = top.PositionOfFirstOption + 1;
            int fromend = top.PositionOfSecondOption + 1;
            if (rt1 == rt2)
            {
                // Reverses the stops in [frombase, fromend).
                int count = fromend - frombase;
                rt1.SequenceOfOptions.Reverse(frombase, count);
                rt1.SequenceOfLocations.Reverse(frombase, count);
                rt1.SequenceOfCustomers.Reverse(frombase, count);
                rt1.Cost += top.MoveCost;
                sol.UpdateTimes(rt1);
                UpdateRouteCostAndLoad(rt1, sol);
                rt1.RouteUtilizationMetric = UtilizationTerm(Convert.ToDouble(rt1.Capacity - rt1.Load));
            }
            else
            {
                // Tails from frombase (rt1) and fromend (rt2) are exchanged.
                List<Option> tailOptions1 = rt1.SequenceOfOptions.GetRange(frombase, rt1.SequenceOfOptions.Count - frombase);
                List<Location> tailLocations1 = rt1.SequenceOfLocations.GetRange(frombase, rt1.SequenceOfLocations.Count - frombase);
                List<Customer> tailCustomers1 = rt1.SequenceOfCustomers.GetRange(frombase, rt1.SequenceOfCustomers.Count - frombase);
                List<Option> tailOptions2 = rt2.SequenceOfOptions.GetRange(fromend, rt2.SequenceOfOptions.Count - fromend);
                List<Location> tailLocations2 = rt2.SequenceOfLocations.GetRange(fromend, rt2.SequenceOfLocations.Count - fromend);
                List<Customer> tailCustomers2 = rt2.SequenceOfCustomers.GetRange(fromend, rt2.SequenceOfCustomers.Count - fromend);

                rt1.SequenceOfOptions.RemoveRange(frombase, rt1.SequenceOfOptions.Count - frombase);
                rt1.SequenceOfLocations.RemoveRange(frombase, rt1.SequenceOfLocations.Count - frombase);
                rt1.SequenceOfCustomers.RemoveRange(frombase, rt1.SequenceOfCustomers.Count - frombase);
                rt2.SequenceOfOptions.RemoveRange(fromend, rt2.SequenceOfOptions.Count - fromend);
                rt2.SequenceOfLocations.RemoveRange(fromend, rt2.SequenceOfLocations.Count - fromend);
                rt2.SequenceOfCustomers.RemoveRange(fromend, rt2.SequenceOfCustomers.Count - fromend);

                rt1.SequenceOfOptions.AddRange(tailOptions2);
                rt2.SequenceOfOptions.AddRange(tailOptions1);
                rt1.SequenceOfLocations.AddRange(tailLocations2);
                rt2.SequenceOfLocations.AddRange(tailLocations1);
                rt1.SequenceOfCustomers.AddRange(tailCustomers2);
                rt2.SequenceOfCustomers.AddRange(tailCustomers1);
                UpdateRouteCostAndLoad(rt1, sol);
                UpdateRouteCostAndLoad(rt2, sol);
                rt1.RouteUtilizationMetric = UtilizationTerm(Convert.ToDouble(rt1.Capacity - rt1.Load));
                rt2.RouteUtilizationMetric = UtilizationTerm(Convert.ToDouble(rt2.Capacity - rt2.Load));
                rt1.SequenceOfEct = top.Ect1.ToList();
                rt2.SequenceOfEct = top.Ect2.ToList();
                rt1.SequenceOfLat = top.Lat1.ToList();
                rt2.SequenceOfLat = top.Lat2.ToList();
            }
            sol.Cost += top.MoveCost;
            sol.Promises[A.Id, L.Id] = sol.Cost;
            sol.Promises[B.Id, K.Id] = sol.Cost;
            AssertRouteFeasible(sol, rt1, "after two opt");
            AssertRouteFeasible(sol, rt2, "after two opt");
        }
    }
}
