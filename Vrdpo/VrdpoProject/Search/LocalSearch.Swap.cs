namespace VrdpoProject
{
    public partial class LocalSearch
    {
        // Exchanges the stops b1 (between a1 and c1) and b2 (between a2 and c2), in the same or in two routes.
        // Candidates are compared on ratio * moveCost (the route count cannot change).
        public Swap FindBestSwapMove(Swap sm, Solution sol)
        {
            // removed[r][k] = d(stop k-1, stop k) + d(stop k, stop k+1) of route r.
            int routeCount = sol.Routes.Count;
            var removed = new double[routeCount][];
            for (int r = 0; r < routeCount; r++)
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
            for (int firstRouteIndex = 0; firstRouteIndex < sol.Routes.Count; firstRouteIndex++)
            {
                Route rt1 = sol.Routes[firstRouteIndex];
                for (int secondRouteIndex = firstRouteIndex; secondRouteIndex < sol.Routes.Count; secondRouteIndex++)
                {
                    Route rt2 = sol.Routes[secondRouteIndex];
                    for (int firstOptionIndex = 1; firstOptionIndex < rt1.SequenceOfOptions.Count - 1; firstOptionIndex++)
                    {
                        int startOfSecondOptionIndex = rt1 == rt2 ? firstOptionIndex + 1 : 1;
                        for (int secondOptionIndex = startOfSecondOptionIndex; secondOptionIndex < rt2.SequenceOfOptions.Count - 1; secondOptionIndex++)
                        {
                            int openRoutes = sol.Routes.Count;
                            Option a1 = rt1.SequenceOfOptions[firstOptionIndex - 1];
                            Option b1 = rt1.SequenceOfOptions[firstOptionIndex];
                            Option c1 = rt1.SequenceOfOptions[firstOptionIndex + 1];
                            Option a2 = rt2.SequenceOfOptions[secondOptionIndex - 1];
                            Option b2 = rt2.SequenceOfOptions[secondOptionIndex];
                            Option c2 = rt2.SequenceOfOptions[secondOptionIndex + 1];

                            double moveCost;
                            double costChangeFirstRoute = 0;
                            double costChangeSecondRoute = 0;
                            double ratio = 1;

                            if (rt1 == rt2)
                            {
                                if (firstOptionIndex == secondOptionIndex - 1)
                                {
                                    double costRemoved = sol.CalculateDistance(a1.Location, b1.Location) + sol.CalculateDistance(b1.Location, b2.Location) + sol.CalculateDistance(b2.Location, c2.Location);
                                    double costAdded = sol.CalculateDistance(a1.Location, b2.Location) + sol.CalculateDistance(b2.Location, b1.Location) + sol.CalculateDistance(b1.Location, c2.Location);
                                    moveCost = costAdded - costRemoved;
                                }
                                else
                                {
                                    double costRemoved1 = removed[firstRouteIndex][firstOptionIndex];
                                    double costAdded1 = sol.CalculateDistance(a1.Location, b2.Location) + sol.CalculateDistance(b2.Location, c1.Location);
                                    double costRemoved2 = removed[secondRouteIndex][secondOptionIndex];
                                    double costAdded2 = sol.CalculateDistance(a2.Location, b1.Location) + sol.CalculateDistance(b1.Location, c2.Location);
                                    moveCost = costAdded1 + costAdded2 - (costRemoved1 + costRemoved2);
                                }
                            }
                            else
                            {
                                if (rt1.Load - b1.Cust.Dem + b2.Cust.Dem > rt1.Capacity) { continue; }
                                if (rt2.Load - b2.Cust.Dem + b1.Cust.Dem > rt2.Capacity) { continue; }
                                double costRemoved1 = removed[firstRouteIndex][firstOptionIndex];
                                double costAdded1 = sol.CalculateDistance(a1.Location, b2.Location) + sol.CalculateDistance(b2.Location, c1.Location);
                                double costRemoved2 = removed[secondRouteIndex][secondOptionIndex];
                                double costAdded2 = sol.CalculateDistance(a2.Location, b1.Location) + sol.CalculateDistance(b1.Location, c2.Location);
                                costChangeFirstRoute = costAdded1 - costRemoved1;
                                costChangeSecondRoute = costAdded2 - costRemoved2;
                                moveCost = costAdded1 + costAdded2 - (costRemoved1 + costRemoved2);
                                double newUtilizationMetricRoute1 = SquareOrPow(rt1.Capacity - (rt1.Load - b1.Cust.Dem + b2.Cust.Dem));
                                double newUtilizationMetricRoute2 = SquareOrPow(rt2.Capacity - (rt2.Load - b2.Cust.Dem + b1.Cust.Dem));
                                double newSolUtilizationMetric = sol.SolutionUtilizationMetric - rt1.RouteUtilizationMetric - rt2.RouteUtilizationMetric + newUtilizationMetricRoute1 + newUtilizationMetricRoute2;
                                ratio = (sol.SolutionUtilizationMetric + 1) / (newSolUtilizationMetric + 1);
                                if (sol.Routes.Count == sol.LowerBoundRoutes)
                                {
                                    ratio = 1;
                                }
                            }

                            if (ratio * moveCost < sm.MoveCost + smallDouble & moveCost != 0)
                            {
                                if (!sol.RespectsTimeWindow2FeasibleMemo(firstRouteIndex, rt1, firstOptionIndex, b2.Location)
                                    || !sol.RespectsTimeWindow2FeasibleMemo(secondRouteIndex, rt2, secondOptionIndex, b1.Location)) { continue; }
                                if (rt1 == rt2
                                    && !sol.RespectsTimeWindow2FeasibleWithReplacement(rt1, secondOptionIndex, b1.Location, firstOptionIndex, b2.Location)) { continue; }
                                if (PromiseIsBroken(a1.Id, b2.Id, moveCost + sol.Cost + smallDouble, sol)) { continue; }
                                if (PromiseIsBroken(b2.Id, c1.Id, moveCost + sol.Cost + smallDouble, sol)) { continue; }
                                if (PromiseIsBroken(a2.Id, b1.Id, moveCost + sol.Cost + smallDouble, sol)) { continue; }
                                if (PromiseIsBroken(b1.Id, c2.Id, moveCost + sol.Cost + smallDouble, sol)) { continue; }

                                sm.TotalCost = moveCost + openRoutes * Solver.RoutePenalty;
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
            if (!sm.IsValid()) { return; }
            sol.LastMove = "swap";
            Route rt1 = sol.Routes[sm.PositionOfFirstRoute];
            Route rt2 = sol.Routes[sm.PositionOfSecondRoute];
            AssertRouteFeasible(sol, rt1, "before swap");
            AssertRouteFeasible(sol, rt2, "before swap");
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
                rt1.RouteUtilizationMetric = UtilizationTerm(Convert.ToDouble(rt1.Capacity - rt1.Load));
                rt2.RouteUtilizationMetric = UtilizationTerm(Convert.ToDouble(rt2.Capacity - rt2.Load));
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
            AssertRouteFeasible(sol, rt1, "after swap");
            AssertRouteFeasible(sol, rt2, "after swap");
        }
    }
}
