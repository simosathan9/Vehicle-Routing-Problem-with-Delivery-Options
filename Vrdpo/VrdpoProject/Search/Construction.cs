namespace VrdpoProject
{
    // Construction: cheapest insertion of one option per customer, randomized among the three cheapest
    // (option, route, position) candidates, with a spare empty route always available.
    public partial class Solver
    {
        private const int MaxRoutes = 100;
        private const int InsertionCandidates = 3;

        // Routes every customer; false when some customer has no feasible insertion left.
        private bool MinimumInsertions(Solution sol, Random rnd)
        {
            while (sol.Customers.Any(x => !x.IsRouted))
            {
                AlwaysKeepAnEmptyRoute(sol);
                List<Insertion> topThree = IdentifyMinimumCostInsertion(new Insertion(), sol);
                Insertion chosen = topThree[rnd.Next(topThree.Count)];
                if (chosen.Customer == null)
                {
                    return false;
                }
                ApplyInsertion(chosen, sol);
            }
            return true;
        }

        private void AlwaysKeepAnEmptyRoute(Solution sol)
        {
            if (sol.Routes.Count >= MaxRoutes)
            {
                return;
            }
            if (sol.Routes.Count == 0 || !sol.Routes.Last().IsEmpty)
            {
                Route newRoute = sol.CreateEmptyRoute(sol.Routes.Count);
                sol.Routes.Add(newRoute);
                sol.Cost += newRoute.Cost;
            }
        }

        // The cheapest feasible insertions of the unrouted options. `bestInsertion` is the first entry of
        // the returned list and is overwritten with every recorded candidate (so it ends up holding the
        // last one recorded); the other entries are copies. The randomized choice in MinimumInsertions
        // relies on exactly this list.
        private List<Insertion> IdentifyMinimumCostInsertion(Insertion bestInsertion, Solution sol)
        {
            var topThree = new List<Insertion> { bestInsertion };
            List<Option> candidates = sol.Options;

            // Distance of each arc of each route, computed once per call.
            var routesList = sol.Routes;
            var stepCost = new double[routesList.Count][];
            for (int r = 0; r < routesList.Count; r++)
            {
                var locsOfRoute = routesList[r].SequenceOfLocations;
                var sc = new double[Math.Max(locsOfRoute.Count - 1, 0)];
                for (int k = 0; k < sc.Length; k++)
                {
                    sc[k] = sol.CalculateDistance(locsOfRoute[k], locsOfRoute[k + 1]);
                }
                stepCost[r] = sc;
            }

            double lastCost = topThree.Last().Cost;
            int topThreeCount = topThree.Count;
            for (int i = 0; i < candidates.Count; i++)
            {
                Option candidateOpt = candidates[i];
                if (candidateOpt.Cust.IsRouted || candidateOpt.IsServed || !candidateOpt.Allowed)
                {
                    continue;
                }
                Location candLoc = candidateOpt.Location;
                if (!(candLoc.Type == 2 | candLoc.Cap < candLoc.MaxCap))
                {
                    continue;
                }
                for (int r = 0; r < routesList.Count; r++)
                {
                    Route rt = routesList[r];
                    if (rt.Load + candidateOpt.Cust.Dem > rt.Capacity)
                    {
                        continue;
                    }
                    var locsOfRt = rt.SequenceOfLocations;
                    if (locsOfRt.Count < 2) { continue; }
                    double[] stepCostOfRt = stepCost[r];
                    double dToPrev = sol.CalculateDistance(locsOfRt[0], candLoc);
                    for (int j = 0; j < locsOfRt.Count - 1; j++)
                    {
                        double dToNext = sol.CalculateDistance(candLoc, locsOfRt[j + 1]);
                        double trialCost = (dToPrev + dToNext) - stepCostOfRt[j];
                        dToPrev = dToNext;
                        if (trialCost <= lastCost || topThreeCount < InsertionCandidates)
                        {
                            if (sol.RespectsTimeWindow2Feasible(rt, j, candLoc))
                            {
                                bestInsertion.Option = candidateOpt;
                                bestInsertion.Customer = candidateOpt.Cust;
                                bestInsertion.Location = candidateOpt.Location;
                                bestInsertion.Route = rt;
                                bestInsertion.InsertionPosition = j + 1;
                                bestInsertion.Cost = trialCost;
                                topThree.Add(new Insertion(bestInsertion));
                                topThree = topThree.OrderBy(o => o.Cost).ToList();
                                topThree = topThree.Take(InsertionCandidates).ToList();
                                lastCost = topThree.Last().Cost;
                                topThreeCount = topThree.Count;
                            }
                        }
                    }
                }
            }
            return topThree;
        }

        private static void ApplyInsertion(Insertion insertion, Solution sol)
        {
            Route rt = insertion.Route;
            rt.SequenceOfLocations.Insert(insertion.InsertionPosition, insertion.Location);
            rt.SequenceOfCustomers.Insert(insertion.InsertionPosition, insertion.Customer);
            rt.SequenceOfOptions.Insert(insertion.InsertionPosition, insertion.Option);
            rt.Cost += insertion.Cost;
            rt.Load += insertion.Customer.Dem;
            rt.RouteUtilizationMetric = Math.Pow(Convert.ToDouble(rt.Capacity - rt.Load), 2);
            insertion.Customer.IsRouted = true;
            insertion.Option.IsServed = true;
            sol.Cost += insertion.Cost;
            insertion.Location.Cap += 1;
            // Placeholders; UpdateTimes recomputes every interior entry.
            rt.SequenceOfEct.Insert(insertion.InsertionPosition, 0);
            rt.SequenceOfLat.Insert(insertion.InsertionPosition, 0);
            sol.UpdateTimes(rt);
        }
    }
}
