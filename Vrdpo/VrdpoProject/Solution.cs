using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Schema;

namespace VrdpoProject
{

    public class Solution
    {
        private double duration;
        private double cost;
        private int repetition;
        private int restart;
        private List<Route> routes;
        private double[,] timeMatrix;
        private double[,] distanceMatrix;
        private int cap;
        private Location depot;
        private List<Option> options = new();
        private double[,] promises;
        private List<Customer> customers = new();
        private Dictionary<int, List<Option>> optionsPerCustomer;
        private Dictionary<int, List<int>> optionsPrioritiesPerCustomer;
        private string lastMove;
        private double solutionUtilizationMetric;
        private double ratioCombinedMoveCost;
        private int lowerBoundRoutes;
        private List<bool> solutionOptionsList = new List<bool>();
        // NOTE (perf, Phase 2): incrementally-maintained service-level counters — see
        // SeedServiceLevelCounts/AdjustServiceLevelCounts below. Plain ints, so they value-copy
        // automatically through the cloning constructor below (the opposite situation from
        // `Promises`, which is deliberately aliased — don't confuse the two).
        private int po0Count;
        private int po1Count;
        private int po2Count;

        public Solution()
        {
            InstanceReader model = new();
            model.BuildModel();
            //model.ExportToJson("./vrpdo_data.json");

            this.duration = 0;
            this.cost = 0;
            this.routes = new List<Route>();
            this.DistanceMatrix = model.DistanceMatrix;
            this.TimeMatrix = model.TimeMatrix;
            this.Cap = model.Cap;
            this.Depot = model.Depot;
            this.Options = model.Options;
            this.Customers = model.AllCustomers;
            this.Promises = new double[Options.Count + 1, Options.Count + 1];
            this.optionsPerCustomer = model.OptionsPerCustomer;
            this.optionsPrioritiesPerCustomer = model.OptionsPrioritiesPerCustomer;
            this.Repetition = repetition;
            this.SolutionUtilizationMetric = 0;
            this.ratioCombinedMoveCost = 0;
            this.solutionOptionsList = new List<bool>();
            this.lowerBoundRoutes = (int)Math.Ceiling((double)model.AllCustomers.Sum(customer => customer.Dem) / model.Cap);
            for (int i = 0; i < Math.Pow(Options.Count + 1, 2); i++) promises[i % (Options.Count + 1), i / (Options.Count + 1)] = double.MaxValue;
        }

        public Solution(double duration, double cost, List<Route> routes, double[,] distanceMatrix,
            double[,] timeMatrix, int cap, Location depot, List<Option> options, double[,] promises, List<Customer> customers, Dictionary<int, List<Option>> optionsPerCustomer,
            Dictionary<int, List<int>> optionsPrioritiesPerCustomer, int repetition, double solutionUtilizationMetric, double ratioCombinedMoveCost, int lowerBoundRoutes,
            int po0Count, int po1Count, int po2Count)
        {
            this.Duration = duration;
            this.Cost = cost;
            this.RatioCombinedMoveCost = ratioCombinedMoveCost;
            this.SolutionUtilizationMetric = solutionUtilizationMetric;
            this.LowerBoundRoutes = lowerBoundRoutes;
            this.po0Count = po0Count;
            this.po1Count = po1Count;
            this.po2Count = po2Count;
            List<Location> clonedLocations = new List<Location>();
            List<Option> clonedOptions = new List<Option>();
            List<Customer> clonedCustomers = new List<Customer>();
            List<Route> clonedRoutes = new List<Route>();
            foreach (Option option in options)
            {
                if (!(clonedLocations.Select(x => x.Id).ToList()).Contains(option.Location.Id))
                {
                    clonedLocations.Add((Location)option.Location.Clone());
                }
                var clonedloc = clonedLocations.SingleOrDefault(x => x.Id == option.Location.Id);
                clonedOptions.Add((Option)option.Clone(clonedloc));
            }
            foreach (Customer customer in customers)
            {
                List<Option> customersOptions = clonedOptions.Where(x => x.Cust.Id == customer.Id).ToList();
                clonedCustomers.Add((Customer)customer.Clone(customersOptions));
            }
            foreach (Option option in clonedOptions)
            {
                option.Cust = clonedCustomers.SingleOrDefault(x => x.Id == option.Cust.Id);
            }
            
            
            foreach (Route rt in routes)
            {
                Route clonedRoute = new Route(rt);
                for (int i = 1; i < rt.SequenceOfCustomers.Count-1; i++)
                {
                    if (i == 0 || i == rt.SequenceOfCustomers.Count - 1)
                    {
                        clonedRoute.SequenceOfLocations[i] = (Location)depot.Clone();
                        clonedRoute.SequenceOfCustomers[i] = new Customer(1000, 0, true);
                        clonedRoute.SequenceOfOptions[i] = (Option)rt.SequenceOfOptions[0].Clone(depot);
                    } else
                    {
                        clonedRoute.SequenceOfLocations[i] = (Location)clonedLocations.Where(x => x.Id == rt.SequenceOfLocations[i].Id).ToList()[0];
                        clonedRoute.SequenceOfCustomers[i] = (Customer)clonedCustomers.Where(x => x.Id == rt.SequenceOfCustomers[i].Id).ToList()[0];
                        clonedRoute.SequenceOfOptions[i] = (Option)clonedOptions.Where(x => x.Id == rt.SequenceOfOptions[i].Id).ToList()[0];
                    }
                }

                clonedRoutes.Add(clonedRoute);
            }

            this.DistanceMatrix = distanceMatrix;
            this.TimeMatrix = timeMatrix;
            this.Cap = cap;
            this.Depot = depot;
            this.Options = clonedOptions;
            this.Customers = clonedCustomers;
            this.Routes = clonedRoutes;
            this.Promises = promises;
            this.optionsPerCustomer = optionsPerCustomer;
            this.optionsPrioritiesPerCustomer = optionsPrioritiesPerCustomer;
            this.Repetition = repetition;
        }


        public Solution DeepCopy(Solution sol)
        {
            Solution deepCopySol = new Solution(sol.Duration, sol.Cost, sol.Routes,
                sol.DistanceMatrix, sol.TimeMatrix, sol.Cap, sol.Depot, sol.Options,
                sol.Promises, sol.Customers, sol.optionsPerCustomer,
                sol.optionsPrioritiesPerCustomer, sol.Repetition, sol.SolutionUtilizationMetric, sol.RatioCombinedMoveCost, sol.LowerBoundRoutes,
                sol.po0Count, sol.po1Count, sol.po2Count);
            return deepCopySol;
        }

        // NOTE (perf, Phase 2): full O(routes×options) scan — same shape as the old
        // CalculateTempServiceLevel/CalculateServiceLevel scans. Call exactly once to establish the
        // correct baseline (right after a solution's routes are first built, e.g. after
        // MinimumInsertions succeeds each restart); every move that changes which priority an option
        // is served at must call AdjustServiceLevelCounts with the exact leaving/entering priorities
        // instead of re-seeding. Relocation/Swap/TwoOpt never change which option represents a
        // customer (they only reposition/reorder existing stops), so they don't need to call either
        // method — only Flip and PrioritySwap do (confirmed by reading every Apply* method).
        public void SeedServiceLevelCounts()
        {
            po0Count = 0;
            po1Count = 0;
            po2Count = 0;
            for (int r = 0; r < routes.Count; r++)
            {
                for (int c = 1; c < routes[r].SequenceOfOptions.Count - 1; c++)
                {
                    switch (routes[r].SequenceOfOptions[c].Prio)
                    {
                        case 0: po0Count++; break;
                        case 1: po1Count++; break;
                        case 2: po2Count++; break;
                    }
                }
            }
        }

        // NOTE (perf, Phase 2): applies the same ±1 delta CalculateTempServiceLevel's original body
        // used to compute inline. Call at the exact point an accepted move commits an option-priority
        // change (i.e. where IsServed flags flip) — see ApplyFlipMove and ApplyPrioritySwapMove.
        public void AdjustServiceLevelCounts(int leavingPriority, int enteringPriority)
        {
            switch (leavingPriority)
            {
                case 0: po0Count--; break;
                case 1: po1Count--; break;
                case 2: po2Count--; break;
            }
            switch (enteringPriority)
            {
                case 0: po0Count++; break;
                case 1: po1Count++; break;
                case 2: po2Count++; break;
            }
        }

        public int Po0Count { get => po0Count; }
        public int Po1Count { get => po1Count; }
        public int Po2Count { get => po2Count; }

        public ulong getSolutionOptionsHashCode()
        {
            var solutionOptionsList = new bool[Options.Count + 1];
            foreach (Option option in Options)
            {
                solutionOptionsList[option.Id] = option.IsServed;
            }
            /*
            foreach (bool value in solutionOptionsList)
            {
                hashCode = hashCode * 31 + value.GetHashCode();
            }
            */
            ulong hash = 0;     
            for (int i = 0; i < solutionOptionsList.Length && i < 64; i++){
                if (solutionOptionsList[i]) {
                    hash |= (ulong) 1 << i; 
                } 
            } 
            return hash;
        }

        public double Duration { get => duration; set => duration = value; }
        public double Cost { get => cost; set => cost = value; }
        internal List<Route> Routes { get => routes; set => routes = value; }
        public double[,] DistanceMatrix { get => distanceMatrix; set => distanceMatrix = value; }
        public double[,] TimeMatrix { get => timeMatrix; set => timeMatrix = value; }
        public List<Option> Options { get => options; set => options = value; }
        public double[,] Promises { get => promises; set => promises = value; }
        public List<Customer> Customers { get => customers; set => customers = value; }
        public Location Depot { get => depot; set => depot = value; }
        public int Cap { get => cap; set => cap = value; }
        public Dictionary<int, List<Option>> OptionsPerCustomer { get => optionsPerCustomer; set => optionsPerCustomer = value; }
        public Dictionary<int, List<int>> OptionsPrioritiesPerCustomer { get => optionsPrioritiesPerCustomer; set => optionsPrioritiesPerCustomer = value; }
        public int Repetition { get => repetition; set => repetition = value; }
        public int Restart { get => restart; set => restart = value; }
        public string LastMove { get => lastMove; set => lastMove = value; }
        public double SolutionUtilizationMetric { get => solutionUtilizationMetric; set => solutionUtilizationMetric = value; }
        public double RatioCombinedMoveCost { get => ratioCombinedMoveCost; set => ratioCombinedMoveCost = value; }
        public int LowerBoundRoutes { get => lowerBoundRoutes; set => lowerBoundRoutes = value; }
        //public List<bool> SolutionOptionsList { get => solutionOptionsList; set => solutionOptionsList = value; }

        public double CalculateDistance(Location n1, Location n2)
        {
            if (n1.Id > n2.Id)
            {
                return DistanceMatrix[n2.Id, n1.Id - n2.Id];
            }
            else
            {
                return DistanceMatrix[n1.Id, n2.Id - n1.Id];
            }
        }

        public double CalculateTime(Location n1, Location n2)
        {
            if (n1.Id > n2.Id)
            {
                return TimeMatrix[n2.Id, n1.Id - n2.Id];
            }
            else
            {
                return TimeMatrix[n1.Id, n2.Id - n1.Id];
            }
        }

        public void InitPromises()
        {
            for (int i = 0; i < Math.Pow(Options.Count + 1, 2); i++) Promises[i % (Options.Count + 1), i / (Options.Count + 1)] = double.MaxValue;
        }


        //public double[] RespectsTimeWindow(Route rt, int loc, Location l)
        //{
        //    /// loc: the position to be placed after
        //    double lat = Math.Min(rt.SequenceOfLat[loc + 1] - CalculateTime(l, rt.SequenceOfLocations[loc + 1]) - l.ServiceTime, l.Due - l.ServiceTime);
        //    if (l.Id == rt.SequenceOfLocations[loc].Id)
        //    {
        //        lat += l.ServiceTime;
        //    }

        //    if (l.Id == rt.SequenceOfLocations[loc + 1].Id)
        //    {
        //        lat = Math.Min(rt.SequenceOfLat[loc + 1] - CalculateTime(l, rt.SequenceOfLocations[loc + 1]), l.Due - l.ServiceTime);

        //       if (l.Id == rt.SequenceOfLocations[loc].Id)
        //        {
        //            lat = Math.Min(rt.SequenceOfLat[loc + 1] - CalculateTime(l, rt.SequenceOfLocations[loc + 1]), l.Due);
        //        }
        //    }

        //    double ect = Math.Max(rt.SequenceOfEct[loc] + CalculateTime(rt.SequenceOfLocations[loc], l) + l.ServiceTime, l.Ready + l.ServiceTime);
        //    if (l.Id == rt.SequenceOfLocations[loc].Id)
        //    {
        //        ect = ect - l.ServiceTime;
        //    }
        //    double lat2 = 0;
        //    for (int j = loc; j > 0; j--)
        //    {
        //        if (loc == 1) { continue; };
        //        if (j == loc)
        //        {
        //            lat2 = Math.Min(rt.SequenceOfLocations[j].Due - rt.SequenceOfLocations[j].ServiceTime,
        //                                       lat - CalculateTime(rt.SequenceOfLocations[j], l)
        //                                       - rt.SequenceOfLocations[j].ServiceTime);

        //            if (j > 1 && rt.SequenceOfLocations[j-1].Id == rt.SequenceOfLocations[j].Id)
        //            {
        //                lat2 += rt.SequenceOfLocations[j].ServiceTime - 20; // -20 added
        //            }

        //        } else
        //        {
        //            lat2 = Math.Min(rt.SequenceOfLocations[j].Due - rt.SequenceOfLocations[j].ServiceTime,
        //                                       lat2 - CalculateTime(rt.SequenceOfLocations[j], rt.SequenceOfLocations[j + 1])
        //                                       - rt.SequenceOfLocations[j].ServiceTime);


        //            if (j > 1 && rt.SequenceOfLocations[j - 1].Id == rt.SequenceOfLocations[j].Id)
        //            {
        //                lat2 += rt.SequenceOfLocations[j].ServiceTime - 20; // -20 added

        //            }
        //        }

        //        if (lat2 < rt.SequenceOfEct[j]) { 
        //            return new double[] { 1, 0 }; 
        //        };
        //    }

        //    double[] tw = new double[] { ect, lat };
        //    return tw;
        //}

        //public Tuple<bool, double[], double[]> RespectsTimeWindow3(Route rt, int loc, Location location)
        //{

        //    double[] ects = new double[rt.SequenceOfLocations.Count + 1];
        //    double[] lats = Enumerable.Repeat((double)7200, rt.SequenceOfLocations.Count + 1).ToArray();
        //    int k = 1;

        //    for (int i = 1; i < rt.SequenceOfLocations.Count + 1; i++)
        //    {
        //        if (i == loc + 1)
        //        {
        //            k--;
        //            ects[i] = Math.Max(location.Ready + location.ServiceTime + location.DeliveryServiceTime,
        //                                       ects[i - 1] + CalculateTime(location, rt.SequenceOfLocations[k])
        //                                       + location.ServiceTime + location.DeliveryServiceTime);

        //            if (i != 1 && (location.Id == rt.SequenceOfLocations[k].Id))
        //            {
        //                ects[i] = ects[i] - (location.ServiceTime);

        //            }
        //        }
        //        else if (i == loc + 2)
        //        {
        //            ects[i] = Math.Max(rt.SequenceOfLocations[k].Ready + rt.SequenceOfLocations[k].ServiceTime + rt.SequenceOfLocations[k].DeliveryServiceTime,
        //                                       ects[i - 1] + CalculateTime(rt.SequenceOfLocations[k], location)
        //                                       + rt.SequenceOfLocations[k].ServiceTime + rt.SequenceOfLocations[k].DeliveryServiceTime);

        //            if ((location.Id == rt.SequenceOfLocations[k].Id))
        //            {
        //                ects[i] = ects[i] - (rt.SequenceOfLocations[k].ServiceTime);
        //            }
        //        }
        //        else
        //        {
        //            ects[i] = Math.Max(rt.SequenceOfLocations[k].Ready + rt.SequenceOfLocations[k].ServiceTime + rt.SequenceOfLocations[k].DeliveryServiceTime,
        //                                           ects[i - 1] + CalculateTime(rt.SequenceOfLocations[k], rt.SequenceOfLocations[k - 1])
        //                                           + rt.SequenceOfLocations[k].ServiceTime + rt.SequenceOfLocations[k].DeliveryServiceTime);

        //            if (rt.SequenceOfLocations[k - 1] == rt.SequenceOfLocations[k])
        //            {
        //                ects[i] = ects[i] - (rt.SequenceOfLocations[k].ServiceTime);
        //            }
        //        }
        //        k++;
        //    }

        //    k = rt.SequenceOfLocations.Count - 2;
        //    for (int j = rt.SequenceOfLocations.Count - 1; j > -1; j--)
        //    {
        //        if (j == loc + 1)
        //        {
        //            k++;
        //            if (rt.SequenceOfLocations[k].Id == location.Id)
        //            {
        //                lats[j + 1] += (rt.SequenceOfLocations[k].ServiceTime);
        //            }

        //            lats[j] = Math.Min(location.Due - location.ServiceTime - location.DeliveryServiceTime,
        //               lats[j + 1] - CalculateTime(location, rt.SequenceOfLocations[k])
        //               - location.ServiceTime - location.DeliveryServiceTime);

        //        }
        //        else if (j == loc)
        //        {
        //            if (location.Id == rt.SequenceOfLocations[k].Id)
        //            {
        //                lats[j + 1] += (location.ServiceTime);
        //            }

        //            lats[j] = Math.Min(rt.SequenceOfLocations[k].Due - rt.SequenceOfLocations[k].ServiceTime - rt.SequenceOfLocations[k].DeliveryServiceTime,
        //                                      lats[j + 1] - CalculateTime(rt.SequenceOfLocations[k], location)
        //                                      - rt.SequenceOfLocations[k].ServiceTime - rt.SequenceOfLocations[k].DeliveryServiceTime);
        //        }
        //        else
        //        {
        //            if (rt.SequenceOfLocations[k + 1].Id == rt.SequenceOfLocations[k].Id)
        //            {
        //                lats[j + 1] += (rt.SequenceOfLocations[k + 1].ServiceTime);
        //            }

        //            lats[j] = Math.Min(rt.SequenceOfLocations[k].Due - rt.SequenceOfLocations[k].ServiceTime - rt.SequenceOfLocations[k].DeliveryServiceTime,
        //                                           lats[j + 1] - CalculateTime(rt.SequenceOfLocations[k], rt.SequenceOfLocations[k + 1])
        //                                           - rt.SequenceOfLocations[k].ServiceTime - rt.SequenceOfLocations[k].DeliveryServiceTime);
        //        }
        //        k--;
        //    }

        //    bool feasible = ects.Zip(lats, (a, b) => a <= b).All(x => x);//maybe <=

        //    return new Tuple<bool, double[], double[]>(feasible, ects, lats);

        //}



        /// <summary>
        /// Calculates the time windows of the route <paramref>rt</paramref> for
        /// all the <paramref>locations</paramref> to be visited after the specified
        /// index <paramref>loc</paramref>
        /// </summary>
        //double[] tw;
        //public Tuple<bool, double[], double[]> RespectsTimeWindow(Route rt, int loc, List<Location> locations)
        //{
        //    //tw = RespectsTimeWindow(rt, loc, locations.First());
        //    //if (tw[0] > tw[1])
        //    //{
        //    //    return new Tuple<bool, double[], double[]>(false, new double[1], new double[1]);
        //    //}
        //    List<double> ects = new();
        //    List<double> lats = new();
        //    Route tempRoute = new(44, 150, depot);
        //    tempRoute.SequenceOfLocations = rt.SequenceOfLocations.Take(loc + 1).ToList();
        //    tempRoute.SequenceOfLocations.AddRange(locations);
        //    tempRoute.SequenceOfLat.AddRange(Enumerable.Repeat((double)7200, tempRoute.SequenceOfLocations.Count - 2).ToList());
        //    for (int i = 1; i < tempRoute.SequenceOfLocations.Count; i++)
        //    {
        //        double ect = Math.Max(tempRoute.SequenceOfLocations[i].Ready + tempRoute.SequenceOfLocations[i].ServiceTime + tempRoute.SequenceOfLocations[i].DeliveryServiceTime,
        //                                       tempRoute.SequenceOfEct[i - 1] + CalculateTime(tempRoute.SequenceOfLocations[i], tempRoute.SequenceOfLocations[i - 1])
        //                                       + tempRoute.SequenceOfLocations[i].ServiceTime + tempRoute.SequenceOfLocations[i].DeliveryServiceTime);
        //        if (tempRoute.SequenceOfLocations[i - 1] == tempRoute.SequenceOfLocations[i])
        //        {
        //            tempRoute.SequenceOfEct[i] = tempRoute.SequenceOfEct[i] - (tempRoute.SequenceOfLocations[i].ServiceTime);
        //        }
        //        tempRoute.SequenceOfEct.Insert(tempRoute.SequenceOfEct.Count - 1, ect);
        //    }
        //    tempRoute.SequenceOfEct.RemoveAt(tempRoute.SequenceOfEct.Count - 1);
        //    for (int j = tempRoute.SequenceOfLocations.Count - 2; j > -1; j--)
        //    {
        //        double lat = Math.Min(tempRoute.SequenceOfLocations[j].Due - tempRoute.SequenceOfLocations[j].ServiceTime - tempRoute.SequenceOfLocations[j].DeliveryServiceTime,
        //                                       tempRoute.SequenceOfLat[j + 1] - CalculateTime(tempRoute.SequenceOfLocations[j], tempRoute.SequenceOfLocations[j + 1])
        //                                       - tempRoute.SequenceOfLocations[j].ServiceTime - tempRoute.SequenceOfLocations[j].DeliveryServiceTime);
        //        if (tempRoute.SequenceOfLocations[j + 1] == tempRoute.SequenceOfLocations[j])
        //        {
        //            tempRoute.SequenceOfLat[j + 1] += (tempRoute.SequenceOfLocations[j + 1].ServiceTime);

        //            lat = Math.Min(tempRoute.SequenceOfLocations[j].Due - tempRoute.SequenceOfLocations[j].ServiceTime - tempRoute.SequenceOfLocations[j].DeliveryServiceTime,
        //                                       tempRoute.SequenceOfLat[j + 1] - CalculateTime(tempRoute.SequenceOfLocations[j], tempRoute.SequenceOfLocations[j + 1])
        //                                       - tempRoute.SequenceOfLocations[j].ServiceTime - tempRoute.SequenceOfLocations[j].DeliveryServiceTime);
        //        }
        //        tempRoute.SequenceOfLat.RemoveAt(0);
        //        tempRoute.SequenceOfLat.Insert(j, lat);
        //    }
        //    //tempRoute.SequenceOfLat.RemoveAt(0);
        //    ects = tempRoute.SequenceOfEct.ToList();
        //    lats = tempRoute.SequenceOfLat.ToList();
        //    bool xs = !lats.SequenceEqual(lats.OrderBy(x => x));
        //    if (!ects.SequenceEqual(ects.OrderBy(x => x)) || !lats.SequenceEqual(lats.OrderBy(x => x))
        //        || ects.Last() > 7200)
        //    {
        //        return new Tuple<bool, double[], double[]>(false, tempRoute.SequenceOfEct.ToArray(), tempRoute.SequenceOfLat.ToArray());
        //    }
        //    else
        //    {
        //        bool feasible = true;
        //        for (int i = 0; i < tempRoute.SequenceOfEct.Count; i++)
        //        {
        //            if (tempRoute.SequenceOfEct[i] > tempRoute.SequenceOfLat[i])
        //            {
        //                feasible = false;
        //            }
        //        }
        //        return new Tuple<bool, double[], double[]>(feasible, tempRoute.SequenceOfEct.ToArray(), tempRoute.SequenceOfLat.ToArray());
        //    }
        //}

        //public Tuple<bool, double[], double[]> RespectsTimeWindow21(Route rt, int loc, List<Location> locations)
        //{
            // Create a modified route with the new locations inserted
            //Route tempRoute = new(44, 150, depot);

        //    // Take locations up to insertion point, add new locations
        //    tempRoute.SequenceOfLocations = new List<Location>(rt.SequenceOfLocations.Take(loc + 1));
        //    tempRoute.SequenceOfLocations.AddRange(locations);

        //    // Initialize ECT and LAT lists for the modified route
        //    tempRoute.SequenceOfEct = new List<double>(new double[tempRoute.SequenceOfLocations.Count]);
        //    tempRoute.SequenceOfLat = new List<double>(new double[tempRoute.SequenceOfLocations.Count]);

        //    // ------------------- FORWARD PASS -------------------
        //    // ECT of first location = just its DeliveryServiceTime
        //    tempRoute.SequenceOfEct[0] = tempRoute.SequenceOfLocations[0].DeliveryServiceTime;

        //    // Calculate ECT for each subsequent location
        //    for (int i = 1; i < tempRoute.SequenceOfLocations.Count; i++)
        //    {
        //        // Start from previous location's completion time
        //        double currentTime = tempRoute.SequenceOfEct[i - 1];

        //        // Add travel time to current location
        //        currentTime += CalculateTime(tempRoute.SequenceOfLocations[i],
        //                                     tempRoute.SequenceOfLocations[i - 1]);

        //        // Add service time if locations differ (IDs differ)
        //        if (tempRoute.SequenceOfLocations[i].Id != tempRoute.SequenceOfLocations[i - 1].Id)
        //        {
        //            currentTime += tempRoute.SequenceOfLocations[i].ServiceTime;
        //        }

        //        // Wait if arrived before 'Ready' time
        //        if (tempRoute.SequenceOfLocations[i].Ready > currentTime)
        //        {
        //            currentTime = tempRoute.SequenceOfLocations[i].Ready;
        //        }

        //        // Add delivery service time to finalize completion time
        //        currentTime += tempRoute.SequenceOfLocations[i].DeliveryServiceTime;

        //        // Store the ECT
        //        tempRoute.SequenceOfEct[i] = currentTime;
        //    }

        //    // ------------------- BACKWARD PASS -------------------
        //    // LAT of the last location = that location's Due
        //    int lastIndex = tempRoute.SequenceOfLocations.Count - 1;
        //    tempRoute.SequenceOfLat[lastIndex] = tempRoute.SequenceOfLocations[lastIndex].Due;

        //    // Move backward from second-to-last location down to the first
        //    for (int j = lastIndex - 1; j >= 0; j--)
        //    {
        //        // Start from the latest finish time of the next location
        //        double latestFinishNext = tempRoute.SequenceOfLat[j + 1];

        //        // Subtract next location's delivery service time
        //        latestFinishNext -= tempRoute.SequenceOfLocations[j + 1].DeliveryServiceTime;

        //        // If IDs differ, subtract next location's service time
        //        if (tempRoute.SequenceOfLocations[j].Id != tempRoute.SequenceOfLocations[j + 1].Id)
        //        {
        //            latestFinishNext -= tempRoute.SequenceOfLocations[j + 1].ServiceTime;
        //        }

        //        // Subtract travel time from location j to j+1
        //        latestFinishNext -= CalculateTime(tempRoute.SequenceOfLocations[j + 1],
        //                                          tempRoute.SequenceOfLocations[j]);

        //        // Clamp to the current location's Due
        //        tempRoute.SequenceOfLat[j] =
        //            Math.Min(tempRoute.SequenceOfLocations[j].Due, latestFinishNext);
        //    }

        //    // ------------------- EXTRA CHECKS -------------------
        //    // 1. ECT must be non-decreasing
        //    bool timeOrderValid =
        //        tempRoute.SequenceOfEct.SequenceEqual(tempRoute.SequenceOfEct.OrderBy(x => x));

        //    // 2. LAT must be non-decreasing
        //    bool latOrderValid =
        //        tempRoute.SequenceOfLat.SequenceEqual(tempRoute.SequenceOfLat.OrderBy(x => x));

        //    // 3. Final ECT must not exceed 7200
        //    bool withinTimeLimit = tempRoute.SequenceOfEct.Last() <= 7200;

        //    // If any extra check fails, return infeasible immediately
        //    if (!timeOrderValid || !latOrderValid || !withinTimeLimit)
        //    {
        //        return new Tuple<bool, double[], double[]>(
        //            false,
        //            tempRoute.SequenceOfEct.ToArray(),
        //            tempRoute.SequenceOfLat.ToArray()
        //        );
        //    }

        //    // ------------------- FEASIBILITY CHECK -------------------
        //    // ECT[i] must be <= LAT[i] for each location
        //    bool feasible = true;
        //    for (int i = 0; i < tempRoute.SequenceOfEct.Count; i++)
        //    {
        //        if (tempRoute.SequenceOfEct[i] > tempRoute.SequenceOfLat[i])
        //        {
        //            feasible = false;
        //            break;
        //        }
        //    }

        //    // Return (feasible, ECT[], LAT[])
        //    return new Tuple<bool, double[], double[]>(
        //        feasible,
        //        tempRoute.SequenceOfEct.ToArray(),
        //        tempRoute.SequenceOfLat.ToArray()
        //    );
        //}

        // NOTE (perf, Phase 2): added so FindBestTwoOptMove can avoid BOTH the caller-side
        // `otherRt.SequenceOfLocations.GetRange(...)` allocation AND this method's own
        // `List<Location> seq = new(rt.SequenceOfLocations.Take(loc+1)); seq.AddRange(locations);`
        // composition — 2 List allocations per candidate eliminated. LocAt(i) returns exactly what
        // `seq[i]` would have been: rt's prefix through `loc`, then otherRt's suffix starting at
        // `otherLoc+1` (the exact range the old call sites passed in). Same loop structure, order,
        // early-exit conditions, and array sizes as the List-based overload below — a pure
        // indirection change. The original List-based overload is left completely untouched (no
        // other callers found, but kept as-is rather than removed, same conservative approach as
        // CalculateTempServiceLevel's preserved wrapper).
        // NOTE (perf, Phase 2): return type changed from `Tuple<bool,double[],double[]>` (a class —
        // one heap allocation per call, on top of the two double[] arrays) to the ValueTuple
        // `(bool,double[],double[])` (a struct — no wrapper allocation at all). Confirmed safe by
        // checking every consumer of this and the two sibling tuple-returning methods (grep across
        // LocalSearch.cs/Solver.cs/Solution.cs): every single one reads `.Item1`/`.Item2`/`.Item3`,
        // which ValueTuple supports identically to Tuple — none stores the tuple itself, compares it,
        // or relies on reference identity/boxing.
        public (bool, double[], double[]) RespectsTimeWindow(Route rt, int loc, Route otherRt, int otherLoc)
        {
            var seq1 = rt.SequenceOfLocations;
            var seq2 = otherRt.SequenceOfLocations;
            int n = (loc + 1) + (seq2.Count - (otherLoc + 1));
            Location LocAt(int i) => i <= loc ? seq1[i] : seq2[otherLoc + 1 + (i - loc - 1)];

            double[] ect = new double[n];
            double[] lat = new double[n];

            ect[0] = LocAt(0).DeliveryServiceTime;
            for (int i = 1; i < n; i++)
            {
                Location cur = LocAt(i);
                Location prev = LocAt(i - 1);
                double t = ect[i - 1] + CalculateTime(cur, prev);
                if (cur.Id != prev.Id)
                    t += cur.ServiceTime;

                if (cur.Ready > t)
                    t = cur.Ready;

                t += cur.DeliveryServiceTime;
                ect[i] = t;

                if (t > cur.Due || t < ect[i - 1])
                    return (false, ect, lat); // early exit
            }

            lat[n - 1] = LocAt(n - 1).Due;
            for (int j = n - 2; j >= 0; j--)
            {
                Location cur = LocAt(j);
                Location next = LocAt(j + 1);
                double l = lat[j + 1] - next.DeliveryServiceTime;
                if (cur.Id != next.Id)
                    l -= next.ServiceTime;

                l -= CalculateTime(next, cur);
                lat[j] = Math.Min(cur.Due, l);

                if (lat[j] < ect[j])
                    return (false, ect, lat); // infeasible
            }

            if (ect[n - 1] > 7200)
                return (false, ect, lat);

            return (true, ect, lat);
        }

        // NOTE (perf, Phase 2): return type changed to ValueTuple — see the sibling overload's
        // comment just above for the full rationale (every consumer only reads .Item1/.Item2/.Item3).
        public (bool, double[], double[]) RespectsTimeWindow(Route rt, int loc, List<Location> locations)
        {
            // Compose new location sequence
            List<Location> seq = new(rt.SequenceOfLocations.Take(loc + 1));
            seq.AddRange(locations);

            int n = seq.Count;
            double[] ect = new double[n];
            double[] lat = new double[n];

            // Forward pass
            ect[0] = seq[0].DeliveryServiceTime;
            for (int i = 1; i < n; i++)
            {
                double t = ect[i - 1] + CalculateTime(seq[i], seq[i - 1]);
                if (seq[i].Id != seq[i - 1].Id)
                    t += seq[i].ServiceTime;

                if (seq[i].Ready > t)
                    t = seq[i].Ready;

                t += seq[i].DeliveryServiceTime;
                ect[i] = t;

                if (t > seq[i].Due || t < ect[i - 1])
                    return (false, ect, lat); // early exit
            }

            // Backward pass
            lat[n - 1] = seq[n - 1].Due;
            for (int j = n - 2; j >= 0; j--)
            {
                double l = lat[j + 1] - seq[j + 1].DeliveryServiceTime;
                if (seq[j].Id != seq[j + 1].Id)
                    l -= seq[j + 1].ServiceTime;

                l -= CalculateTime(seq[j + 1], seq[j]);
                lat[j] = Math.Min(seq[j].Due, l);

                if (lat[j] < ect[j])
                    return (false, ect, lat); // infeasible
            }

            if (ect[n - 1] > 7200)
                return (false, ect, lat);

            return (true, ect, lat);
        }

        //public void UpdateTimes(Route rt)
        //{
        //    for (int i = 1; i < rt.SequenceOfLocations.Count; i++)
        //    {
        //        rt.SequenceOfEct[i] = Math.Max(rt.SequenceOfLocations[i].Ready + rt.SequenceOfLocations[i].ServiceTime + rt.SequenceOfLocations[i].DeliveryServiceTime, 
        //                                       rt.SequenceOfEct[i - 1] + CalculateTime(rt.SequenceOfLocations[i], rt.SequenceOfLocations[i - 1])
        //                                       + rt.SequenceOfLocations[i].ServiceTime + rt.SequenceOfLocations[i].DeliveryServiceTime); 
        //        if (rt.SequenceOfLocations[i - 1].Id == rt.SequenceOfLocations[i].Id)
        //        {
        //            rt.SequenceOfEct[i] = rt.SequenceOfEct[i] - (rt.SequenceOfLocations[i].ServiceTime);

        //        }
        //    }

        //    for (int j = rt.SequenceOfLocations.Count - 2; j > -1; j--)
        //    {
        //        if (rt.SequenceOfLocations[j + 1].Id == rt.SequenceOfLocations[j].Id)
        //        {
        //            rt.SequenceOfLat[j + 1] = rt.SequenceOfLat[j + 1] + (rt.SequenceOfLocations[j + 1].ServiceTime);
        //        }

        //        rt.SequenceOfLat[j] = Math.Min(rt.SequenceOfLocations[j].Due - rt.SequenceOfLocations[j].ServiceTime - rt.SequenceOfLocations[j].DeliveryServiceTime,
        //                                       rt.SequenceOfLat[j + 1] - CalculateTime(rt.SequenceOfLocations[j + 1], rt.SequenceOfLocations[j])
        //                                       - rt.SequenceOfLocations[j].ServiceTime - rt.SequenceOfLocations[j].DeliveryServiceTime);
        //    }
        //}


        public void UpdateTimes(Route rt)
        {

            for (int i = 1; i < rt.SequenceOfLocations.Count; i++)
            {
                // Start from previous location's completion time
                double currentTime = rt.SequenceOfEct[i - 1];

                // Add travel time to current location
                currentTime += CalculateTime(rt.SequenceOfLocations[i], rt.SequenceOfLocations[i - 1]);

                // Add service time if locations are different
                if (rt.SequenceOfLocations[i - 1].Id != rt.SequenceOfLocations[i].Id)
                {
                    currentTime += rt.SequenceOfLocations[i].ServiceTime;
                }

                // Wait if arrived before ready time
                if (rt.SequenceOfLocations[i].Ready > currentTime)
                {
                    currentTime = rt.SequenceOfLocations[i].Ready;
                }

                // Add delivery service time to get completion time
                currentTime += rt.SequenceOfLocations[i].DeliveryServiceTime;

                // Record completion time
                rt.SequenceOfEct[i] = currentTime;
            }

            for (int j = rt.SequenceOfLocations.Count - 2; j >= 0; j--)
            {
                // Starting with the latest time we can finish at the next location
                double latestFinishTimeNext = rt.SequenceOfLat[j + 1];

                // Subtract delivery service time to get latest arrival at next location
                double latestArrivalTimeNext = latestFinishTimeNext - rt.SequenceOfLocations[j + 1].DeliveryServiceTime;

                // Subtract service time if locations differ
                if (rt.SequenceOfLocations[j].Id != rt.SequenceOfLocations[j + 1].Id)
                {
                    latestArrivalTimeNext -= rt.SequenceOfLocations[j + 1].ServiceTime;
                }

                // Subtract travel time to get latest departure from current location
                double latestDepartureTimeJ = latestArrivalTimeNext -
                                              CalculateTime(rt.SequenceOfLocations[j + 1], rt.SequenceOfLocations[j]);

                // The latest time we can finish at current location
                rt.SequenceOfLat[j] = Math.Min(rt.SequenceOfLocations[j].Due, latestDepartureTimeJ);
            }
        }

        public bool CalculateTimes(Route rt)
        {
            //Console.WriteLine("Route_Id: " + rt.Id);
            //Console.WriteLine("--------");
            double totalTime = 0;
            //Console.WriteLine("Location {0} is visited at time {1}. The tw opens at {2}", rt.SequenceOfLocations[0].Id, totalTime, rt.SequenceOfLocations[0].Ready);
            //Console.WriteLine("Location {0} is finished at time {1}. The tw ends at {2}", rt.SequenceOfLocations[0].Id, totalTime, rt.SequenceOfLocations[0].Due);

            for (int i = 1; i < rt.SequenceOfLocations.Count; i++)
            {
                totalTime += CalculateTime(rt.SequenceOfLocations[i], rt.SequenceOfLocations[i - 1]);// + rt.SequenceOfLocations[i].DeliveryServiceTime;

                if (rt.SequenceOfLocations[i - 1].Id != rt.SequenceOfLocations[i].Id)
                {
                    totalTime += rt.SequenceOfLocations[i].ServiceTime;
                }

                if (rt.SequenceOfLocations[i].Ready > totalTime)
                {
                    totalTime = rt.SequenceOfLocations[i].Ready;
                }

                //Console.WriteLine("Location {0} is visited at time {1}. The tw opens at {2}", rt.SequenceOfLocations[i].Id, totalTime, rt.SequenceOfLocations[i].Ready);

                totalTime += rt.SequenceOfLocations[i].DeliveryServiceTime;

                //Console.WriteLine("Location {0} is finished at time {1}. The tw ends at {2}", rt.SequenceOfLocations[i].Id, totalTime, rt.SequenceOfLocations[i].Due);

                if (!(totalTime <= rt.SequenceOfLocations[i].Due))
                {
                    Console.WriteLine("Time Window Feasibility Error");
                    //Console.WriteLine("Current option {0} and next option {1}", rt.SequenceOfOptions[i].Id, rt.SequenceOfOptions[i + 1].Id);
                    //Console.WriteLine("Location {0} is visited at time {1} but it is due at {2}", rt.SequenceOfLocations[i + 1].Id, totalTime, rt.SequenceOfLocations[i + 1].Due);
                    //Console.WriteLine("Current opt {0} and next opt {1}", rt.SequenceOfOptions[i].Id, rt.SequenceOfOptions[i + 1].Id);
                    return false;
                }
            }
            return true;
        }


        // NOTE (perf, Phase 2): return type changed to ValueTuple — see RespectsTimeWindow's comment
        // for the full rationale. This method is the most heavily-used of the three (9 call sites
        // across all 5 operators), so removing the Tuple wrapper allocation here has the widest reach.
        public (bool, double[], double[]) RespectsTimeWindow2(Route rt, int loc, Location location)
        {
            // NOTE (perf, Phase 2): previously built `new List<Location>(rt.SequenceOfLocations)` then
            // `.Insert(loc + 1, location)` — an O(route length) allocation + copy + shift, done on
            // EVERY candidate-move feasibility check across all 5 operators (9 call sites). LocAt(i)
            // below returns EXACTLY what `newLocations[i]` would have returned after that Insert — same
            // conditional structure, same objects at the same logical positions — without materializing
            // the list. Loop structure, computation order, and the returned array sizes/contents are
            // otherwise byte-for-byte unchanged: this is a pure indirection change, not an algorithmic
            // one (no early-exit added, no prefix/suffix reuse from rt's cached Ect/Lat arrays — that is
            // a larger, separately-scoped change given how many callers read Item2/Item3 differently;
            // this pass only removes the allocation).
            var sequence = rt.SequenceOfLocations;
            int n = sequence.Count + 1;
            Location LocAt(int i) => i <= loc ? sequence[i] : (i == loc + 1 ? location : sequence[i - 1]);

            // Arrays to hold the earliest completion times (ECT) and latest times (LAT)
            double[] ects = new double[n];
            double[] lats = new double[n];

            // === Forward Pass (Earliest Completion Times) ===
            // 2) ECT of first location: only its DeliveryServiceTime
            ects[0] = LocAt(0).DeliveryServiceTime;

            // 3) Calculate ECT for each subsequent location
            for (int i = 1; i < n; i++)
            {
                Location cur = LocAt(i);
                Location prev = LocAt(i - 1);
                double currentTime = ects[i - 1];

                // Add travel time from previous to current
                currentTime += CalculateTime(cur, prev);

                // If IDs differ, then add the current location's ServiceTime
                if (cur.Id != prev.Id)
                {
                    currentTime += cur.ServiceTime;
                }

                // If we arrive too early, push forward to the ready time
                if (cur.Ready > currentTime)
                {
                    currentTime = cur.Ready;
                }

                // Finally, add the delivery service time of the current location
                currentTime += cur.DeliveryServiceTime;

                // Store in the ECT array
                ects[i] = currentTime;
            }

            // === Backward Pass (Latest Start Times) ===
            // 4) LAT of the last location: its Due time
            lats[n - 1] = LocAt(n - 1).Due;

            for (int j = n - 2; j >= 0; j--)
            {
                Location cur = LocAt(j);
                Location next = LocAt(j + 1);
                double latestFinishTimeNext = lats[j + 1];

                // Subtract next location's DeliveryServiceTime
                latestFinishTimeNext -= next.DeliveryServiceTime;

                // If IDs differ, subtract the next location's ServiceTime
                if (cur.Id != next.Id)
                {
                    latestFinishTimeNext -= next.ServiceTime;
                }

                // Subtract travel time from current to next
                latestFinishTimeNext -= CalculateTime(next, cur);

                // Clamp to the current location's Due
                lats[j] = Math.Min(cur.Due, latestFinishTimeNext);
            }

            // 5) Check feasibility: ECT[i] <= LAT[i] for all i
            bool feasible = true;
            for (int i = 0; i < n; i++)
            {
                if (ects[i] > lats[i])
                {
                    feasible = false;
                    break;
                }
            }

            return (feasible, ects, lats);
        }

        // NOTE (perf, Phase 3): bool-only, allocation-free siblings of the array-returning time-window
        // checks above/below. Every LocalSearch caller of RespectsTimeWindow2 reads ONLY `.Item1` (the
        // arrays are consumed solely by TwoOpt's best-move bookkeeping and by Solver's construction
        // phase, both of which keep using the array-returning versions), yet each call allocated two
        // fresh double[] per candidate. These variants run the exact same arithmetic in the exact same
        // order into reusable per-Solution scratch buffers, so the boolean is bit-for-bit identical;
        // they also stop at the first violated `ects[i] > lats[i]` instead of finishing the scan (the
        // original's result is "false iff ANY i violates", so checking i as it is produced is equivalent,
        // and the checks have no side effects). Scratch buffers are only live during a single call and
        // never escape, so reuse is safe (single-threaded by design, per project decision).
        private double[] twScratchEct;
        private double[] twScratchLat;

        private void EnsureTwScratch(int n)
        {
            if (twScratchEct == null || twScratchEct.Length < n)
            {
                int size = Math.Max(n, 64);
                twScratchEct = new double[size];
                twScratchLat = new double[size];
            }
        }

        public bool RespectsTimeWindow2Feasible(Route rt, int loc, Location location)
        {
            return TimeWindow2FeasibleCore(rt.SequenceOfLocations, loc, location, -1, null);
        }

        // Same as RespectsTimeWindow2Feasible, but evaluated on `rt`'s sequence with position `replaceAt`
        // swapped for `replacement` (used by FindBestSwapMove's same-route branch, which previously built a
        // full temp route via getTempCopy just to overwrite one location in it).
        public bool RespectsTimeWindow2FeasibleWithReplacement(Route rt, int loc, Location location, int replaceAt, Location replacement)
        {
            return TimeWindow2FeasibleCore(rt.SequenceOfLocations, loc, location, replaceAt, replacement);
        }

        private bool TimeWindow2FeasibleCore(List<Location> sequence, int loc, Location location, int replaceAt, Location replacement)
        {
            int n = sequence.Count + 1;
            // replaceAt == -1 means "no replacement": Base(k) is then exactly sequence[k], and LocAt(i) is exactly
            // what RespectsTimeWindow2's own LocAt returns.
            Location Base(int k) => k == replaceAt ? replacement : sequence[k];
            Location LocAt(int i) => i <= loc ? Base(i) : (i == loc + 1 ? location : Base(i - 1));
            EnsureTwScratch(n);
            double[] ects = twScratchEct;
            double[] lats = twScratchLat;

            ects[0] = LocAt(0).DeliveryServiceTime;
            for (int i = 1; i < n; i++)
            {
                Location cur = LocAt(i);
                Location prev = LocAt(i - 1);
                double currentTime = ects[i - 1];
                currentTime += CalculateTime(cur, prev);
                if (cur.Id != prev.Id)
                {
                    currentTime += cur.ServiceTime;
                }
                if (cur.Ready > currentTime)
                {
                    currentTime = cur.Ready;
                }
                currentTime += cur.DeliveryServiceTime;
                ects[i] = currentTime;
            }

            lats[n - 1] = LocAt(n - 1).Due;
            if (ects[n - 1] > lats[n - 1]) { return false; }

            for (int j = n - 2; j >= 0; j--)
            {
                Location cur = LocAt(j);
                Location next = LocAt(j + 1);
                double latestFinishTimeNext = lats[j + 1];
                latestFinishTimeNext -= next.DeliveryServiceTime;
                if (cur.Id != next.Id)
                {
                    latestFinishTimeNext -= next.ServiceTime;
                }
                latestFinishTimeNext -= CalculateTime(next, cur);
                lats[j] = Math.Min(cur.Due, latestFinishTimeNext);
                if (ects[j] > lats[j]) { return false; }
            }
            return true;
        }

        // NOTE (perf, Phase 4): Id -> Location lookup used by Route.ReplaceCustomerOptionsWithClones, built from "the
        // distinct Location objects of this solution's options". Every Find* call used to rebuild it from scratch
        // (a LINQ Select, a HashSet, a List and the array — four times per iteration). It cannot change while this
        // Solution lives: an existing Option's Location is never reassigned (Option.Clone sets it only on the NEW
        // clone) and Solution.Options is only assigned in the constructors. The cache is still validated against the
        // Options list reference and its Count, so it is rebuilt if either ever differs.
        private Location[] locationLookupCache;
        private List<Option> locationLookupSource;
        private int locationLookupSourceCount;

        public Location[] GetLocationLookup()
        {
            var opts = Options;
            if (locationLookupCache == null || !ReferenceEquals(locationLookupSource, opts) || locationLookupSourceCount != opts.Count)
            {
                locationLookupCache = Route.BuildLocationLookup(opts.Select(x => x.Location).ToHashSet().ToList());
                locationLookupSource = opts;
                locationLookupSourceCount = opts.Count;
            }
            return locationLookupCache;
        }

        // NOTE (perf, Phase 4): memo for RespectsTimeWindow2Feasible. That function is a pure function of (the route's
        // location sequence, the insertion position, the inserted location), and every input reduces to location Ids:
        // a Location's Ready/Due/ServiceTime/DeliveryServiceTime are set once in its constructor and never written
        // again anywhere in the code base, so two Location objects with the same Id carry the same values, and the
        // travel times come from a matrix indexed by Id. A route's sequence only changes when a move is APPLIED
        // (once per iteration, touching one or two routes), but every Find* call re-asks the same
        // (route, position, location) questions for all the untouched routes. The memo is keyed by route SLOT
        // (index in Routes) and validated by content: BeginTimeWindowMemo() compares each slot's current location-Id
        // sequence with the one its cached answers were computed for and drops that slot's answers if it differs, so
        // no assumption is made about where or how routes get modified. Answers are computed by the very same
        // TimeWindow2FeasibleCore, so a cache hit returns exactly what a fresh computation would.
        private int[][] twMemoIds;
        private sbyte[][][] twMemoRows;   // [slot][position][locationId]: 0 = unknown, 1 = feasible, 2 = infeasible

        public void BeginTimeWindowMemo()
        {
            int n = Routes.Count;
            if (twMemoIds == null || twMemoIds.Length < n)
            {
                var ids = new int[n][];
                var rows = new sbyte[n][][];
                if (twMemoIds != null)
                {
                    Array.Copy(twMemoIds, ids, twMemoIds.Length);
                    Array.Copy(twMemoRows, rows, twMemoRows.Length);
                }
                twMemoIds = ids;
                twMemoRows = rows;
            }
            for (int r = 0; r < n; r++)
            {
                var seq = Routes[r].SequenceOfLocations;
                int[] stored = twMemoIds[r];
                bool same = stored != null && stored.Length == seq.Count;
                if (same)
                {
                    for (int i = 0; i < stored.Length; i++)
                    {
                        if (stored[i] != seq[i].Id) { same = false; break; }
                    }
                }
                if (same) { continue; }
                if (stored == null || stored.Length != seq.Count)
                {
                    stored = new int[seq.Count];
                    twMemoIds[r] = stored;
                    twMemoRows[r] = null;   // the row table is sized by the route length
                }
                for (int i = 0; i < stored.Length; i++) { stored[i] = seq[i].Id; }
                var slotRows = twMemoRows[r];
                if (slotRows != null)
                {
                    for (int i = 0; i < slotRows.Length; i++)
                    {
                        if (slotRows[i] != null) { Array.Clear(slotRows[i], 0, slotRows[i].Length); }
                    }
                }
            }
        }

        // Same answer as RespectsTimeWindow2Feasible(rt, loc, location) for `rt` = Routes[slot]; call
        // BeginTimeWindowMemo() at the start of the Find* call that uses it.
        public bool RespectsTimeWindow2FeasibleMemo(int slot, Route rt, int loc, Location location)
        {
            int id = location.Id;
            int idRange = TimeMatrix.GetLength(0);
            if (id < 0 || id >= idRange)
            {
                return TimeWindow2FeasibleCore(rt.SequenceOfLocations, loc, location, -1, null);
            }
            var slotRows = twMemoRows[slot];
            if (slotRows == null)
            {
                slotRows = new sbyte[rt.SequenceOfLocations.Count][];
                twMemoRows[slot] = slotRows;
            }
            var row = slotRows[loc];
            if (row == null)
            {
                row = new sbyte[idRange];
                slotRows[loc] = row;
            }
            sbyte v = row[id];
            if (v != 0) { return v == 1; }
            bool result = TimeWindow2FeasibleCore(rt.SequenceOfLocations, loc, location, -1, null);
            row[id] = (sbyte)(result ? 1 : 2);
            return result;
        }

        // Bool-only sibling of RespectsTimeWindow(Route, int, Route, int) — TwoOpt. Verbatim arithmetic and
        // the same three exit conditions as the original; only the two result arrays are gone (TwoOpt
        // recomputes them via the array-returning version if and only if the candidate is recorded as the
        // new best move, which is when they are actually consumed).
        public bool RespectsTimeWindowFeasible(Route rt, int loc, Route otherRt, int otherLoc)
        {
            var seq1 = rt.SequenceOfLocations;
            var seq2 = otherRt.SequenceOfLocations;
            int n = (loc + 1) + (seq2.Count - (otherLoc + 1));
            Location LocAt(int i) => i <= loc ? seq1[i] : seq2[otherLoc + 1 + (i - loc - 1)];

            EnsureTwScratch(n);
            double[] ect = twScratchEct;
            double[] lat = twScratchLat;

            ect[0] = LocAt(0).DeliveryServiceTime;
            for (int i = 1; i < n; i++)
            {
                Location cur = LocAt(i);
                Location prev = LocAt(i - 1);
                double t = ect[i - 1] + CalculateTime(cur, prev);
                if (cur.Id != prev.Id)
                    t += cur.ServiceTime;

                if (cur.Ready > t)
                    t = cur.Ready;

                t += cur.DeliveryServiceTime;
                ect[i] = t;

                if (t > cur.Due || t < ect[i - 1])
                    return false;
            }

            lat[n - 1] = LocAt(n - 1).Due;
            for (int j = n - 2; j >= 0; j--)
            {
                Location cur = LocAt(j);
                Location next = LocAt(j + 1);
                double l = lat[j + 1] - next.DeliveryServiceTime;
                if (cur.Id != next.Id)
                    l -= next.ServiceTime;

                l -= CalculateTime(next, cur);
                lat[j] = Math.Min(cur.Due, l);

                if (lat[j] < ect[j])
                    return false;
            }

            if (ect[n - 1] > 7200)
                return false;

            return true;
        }


        public Tuple<bool, List<double>, List<double>> CheckEctLatTestingSolution(Route rt)
        {
            // Initialize sequence lists
            rt.SequenceOfEct = new List<double>(new double[rt.SequenceOfLocations.Count]);
            rt.SequenceOfLat = new List<double>(new double[rt.SequenceOfLocations.Count]);

            // FORWARD PASS
            // Initialize first location - start at time 0
            rt.SequenceOfEct[0] = rt.SequenceOfLocations[0].DeliveryServiceTime;

            // Calculate earliest completion time for each subsequent location
            for (int i = 1; i < rt.SequenceOfLocations.Count; i++)
            {
                // Start from previous location's completion time
                double currentTime = rt.SequenceOfEct[i - 1];

                // Add travel time to current location
                currentTime += CalculateTime(rt.SequenceOfLocations[i], rt.SequenceOfLocations[i - 1]);

                // Add service time if locations are different
                if (rt.SequenceOfLocations[i - 1].Id != rt.SequenceOfLocations[i].Id)
                {
                    currentTime += rt.SequenceOfLocations[i].ServiceTime;
                }

                // Wait if arrived before ready time
                if (rt.SequenceOfLocations[i].Ready > currentTime)
                {
                    currentTime = rt.SequenceOfLocations[i].Ready;
                }

                // Add delivery service time to get completion time
                currentTime += rt.SequenceOfLocations[i].DeliveryServiceTime;

                // Record completion time
                rt.SequenceOfEct[i] = currentTime;
            }

            // BACKWARD PASS
            // Start with latest possible time for last location
            rt.SequenceOfLat[rt.SequenceOfLocations.Count - 1] =
            rt.SequenceOfLocations[rt.SequenceOfLocations.Count - 1].Due;

            for (int j = rt.SequenceOfLocations.Count - 2; j >= 0; j--)
            {
                // Starting with the latest time we can finish at the next location
                double latestFinishTimeNext = rt.SequenceOfLat[j + 1];

                // Subtract delivery service time to get latest arrival at next location
                double latestArrivalTimeNext = latestFinishTimeNext - rt.SequenceOfLocations[j + 1].DeliveryServiceTime;

                // Subtract service time if locations differ
                if (rt.SequenceOfLocations[j].Id != rt.SequenceOfLocations[j + 1].Id)
                {
                    latestArrivalTimeNext -= rt.SequenceOfLocations[j + 1].ServiceTime;
                }

                // Subtract travel time to get latest departure from current location
                double latestDepartureTimeJ = latestArrivalTimeNext -
                                              CalculateTime(rt.SequenceOfLocations[j + 1], rt.SequenceOfLocations[j]);

                // The latest time we can finish at current location
                rt.SequenceOfLat[j] = Math.Min(rt.SequenceOfLocations[j].Due, latestDepartureTimeJ);
            }

            // Check feasibility - compare completion times to due times
            bool feasible = true;
            for (var i = 0; i < rt.SequenceOfEct.Count; i++)
            {
                bool locationFeasible = rt.SequenceOfEct[i] <= rt.SequenceOfLat[i];
                Console.WriteLine(
                    $@"{rt.SequenceOfEct[i]} <= {rt.SequenceOfLat[i]} ({rt.SequenceOfLat[i] - rt.SequenceOfEct[i]})");
                feasible = feasible && locationFeasible;
            }

            return new Tuple<bool, List<double>, List<double>>(feasible, rt.SequenceOfEct, rt.SequenceOfLat);
        }

        //public Tuple<bool, List<double>, List<double>> CheckEctLatTestingSolution2(Route rt)
        //{

        //    rt.SequenceOfEct = new List<double>(new double[rt.SequenceOfLocations.Count]);
        //    rt.SequenceOfLat = Enumerable.Repeat(7200.0, rt.SequenceOfLocations.Count).ToList();

        //    for (int i = 1; i < rt.SequenceOfLocations.Count; i++)
        //    {
        //        var arrivaltime = rt.SequenceOfEct[i - 1] + CalculateTime(rt.SequenceOfLocations[i], rt.SequenceOfLocations[i - 1]) +
        //            (rt.SequenceOfLocations[i - 1].Id == rt.SequenceOfLocations[i].Id
        //            ? 0
        //            : rt.SequenceOfLocations[i].ServiceTime);

        //        arrivaltime = Math.Max(arrivaltime, rt.SequenceOfLocations[i].Ready);

        //        rt.SequenceOfEct[i] = arrivaltime + rt.SequenceOfLocations[i].DeliveryServiceTime;

        //    }

        //    for (int j = rt.SequenceOfLocations.Count - 2; j >= 0; j--)
        //    {
        //        //if (rt.SequenceOfLocations[j + 1].Id == rt.SequenceOfLocations[j].Id)
        //        //{
        //        //    rt.SequenceOfLat[j + 1] += rt.SequenceOfLocations[j + 1].ServiceTime;
        //        //}

        //        double travelTime = CalculateTime(rt.SequenceOfLocations[j + 1], rt.SequenceOfLocations[j]);

        //        double requiredTime = travelTime + rt.SequenceOfLocations[j].DeliveryServiceTime;

        //        //bool isCurrentFirstInBlock = (j == 0) || (rt.SequenceOfLocations[j].Id != rt.SequenceOfLocations[j - 1].Id);
        //        bool isCurrentFirstInBlock = (j > 0) && (rt.SequenceOfLocations[j].Id != rt.SequenceOfLocations[j - 1].Id);

        //        if (isCurrentFirstInBlock)
        //        {
        //            requiredTime += rt.SequenceOfLocations[j].ServiceTime;
        //        }

        //        double latestArrivalFromNext = rt.SequenceOfLat[j + 1] - requiredTime;

        //        double dueConstraint = rt.SequenceOfLocations[j].Due
        //                               - (isCurrentFirstInBlock ? rt.SequenceOfLocations[j].ServiceTime : 0)
        //                               - rt.SequenceOfLocations[j].DeliveryServiceTime;

        //        rt.SequenceOfLat[j] = Math.Min(dueConstraint, latestArrivalFromNext);
        //    }


        //    //for (int j = rt.SequenceOfLocations.Count - 2; j > -1; j--)
        //    //{
        //    //    if (rt.SequenceOfLocations[j + 1].Id == rt.SequenceOfLocations[j].Id)
        //    //    {
        //    //        rt.SequenceOfLat[j + 1] = rt.SequenceOfLat[j + 1] + (rt.SequenceOfLocations[j + 1].ServiceTime);
        //    //    }

        //    //    //var latestarrival = rt.SequenceOfLat[j + 1] - CalculateTime(rt.SequenceOfLocations[j + 1], rt.SequenceOfLocations[j]) -
        //    //    //    rt.SequenceOfLocations[j].DeliveryServiceTime - rt.SequenceOfLocations[j].ServiceTime;


        //    //    bool skipParkingTime = (j > 0 && rt.SequenceOfLocations[j - 1].Id == rt.SequenceOfLocations[j].Id);

        //    //    var latestarrival = rt.SequenceOfLat[j + 1] - CalculateTime(rt.SequenceOfLocations[j + 1], rt.SequenceOfLocations[j])
        //    //                       - rt.SequenceOfLocations[j].DeliveryServiceTime
        //    //                       - (skipParkingTime ? 0 : rt.SequenceOfLocations[j].ServiceTime);

        //    //    rt.SequenceOfLat[j] = Math.Min(rt.SequenceOfLocations[j].Due - rt.SequenceOfLocations[j].ServiceTime - rt.SequenceOfLocations[j].DeliveryServiceTime, latestarrival);


        //    //}

        //    bool feasible = rt.SequenceOfEct.Zip(rt.SequenceOfLat, (a, b) => a <= b).All(x => x);

        //    return new Tuple<bool, List<double>, List<double>>(feasible, rt.SequenceOfEct, rt.SequenceOfLat);

        //}

        public double CalculateUtilizationMetric()
        {
            double utilizationMetric = 0;
            foreach (Route rt in Routes)
            {
                utilizationMetric += rt.RouteUtilizationMetric;
            }
            return utilizationMetric;
        }

        public bool CheckEverything(Solution sol)
        {
            bool feasible;
            // NOTE (perf, Phase 2): `sol.Customers.Where(x => x.IsRouted).ToList().Count !=
            // sol.Customers.Count` used to live INSIDE the per-route loop below, re-evaluated (and
            // reallocating a fresh List every time via Where+ToList) once per route even though it
            // checks a solution-wide invariant that doesn't depend on which route is currently being
            // examined. Hoisted to run once, and simplified to `Count(predicate)` (an enumerate-and-count,
            // no intermediate list) — same boolean result, no behavior change: this check and the
            // per-route feasibility checks below are independent AND-ed conditions, so evaluation order
            // doesn't affect the final true/false CheckEverything returns for any input.
            bool allCustomersRouted = sol.Customers.Count(x => x.IsRouted) == sol.Customers.Count;
            Dictionary<int, int> timesVisited = new Dictionary<int, int>();
            foreach (Route route in sol.Routes)
            {
                feasible = CheckRouteFeasibility(route);
                if (!feasible)
                {
                    return false;
                }
                foreach (Location location in route.SequenceOfLocations)
                {
                    if (!timesVisited.ContainsKey(location.Id))
                    {
                        timesVisited.Add(location.Id, 0);
                    }
                    timesVisited[location.Id] += 1;
                    if (location.Type == 1 && location.MaxCap < timesVisited[location.Id])
                    {
                        Console.WriteLine("Shared location exceeds max capacity!");
                        return false;
                    }
                }
                if (!allCustomersRouted)
                {
                    return false;
                }

            }
            foreach (Route rt in sol.routes)
            {
                foreach (Location location in rt.SequenceOfLocations)
                {
                    if (location.Type == 1 && location.Cap != timesVisited[location.Id])
                    {
                        Console.WriteLine("Location {0} capacity is wrong (solution may be feasible)!", location.Id);
                        
                        return false;
                    }
                    else if (location.Type == 2 && timesVisited[location.Id] > 1)
                    {
                        Console.WriteLine("Private location is used two times!");
                        return false;
                    }
                }
            }
            return true;
        }

        public void TestSolution(Solution checkingSolution)
        {
            Route route1 = new Route(40, 150, checkingSolution.Depot);
            Route route2 = new Route(41, 150, checkingSolution.Depot);
            Route route3 = new Route(42, 150, checkingSolution.Depot);
            checkingSolution.Routes.Add(route1);
            checkingSolution.Routes.Add(route2);
            checkingSolution.Routes.Add(route3);


            List<int> list1 = new List<int> { 26, 19, 25, 9, 2, 17, 31, 6, 8 };
            List<int> list2 = new List<int> { 0, 3, 22, 47, 5, 38, 10 };
            List<int> list3 = new List<int> { 45, 14, 7, 42, 1, 4, 13, 34, 35 };

            //List<int> list1 = new List<int> { 2, 30, 40, 2, 34, 14 };
            //List<int> list2 = new List<int> { 35, 6, 46, 1, 0, 3, 7, 41, 8, 49, 19 };
            //List<int> list3 = new List<int> { 5, 24, 17, 11, 4, 27, 13, 22, 9 };


            //List<int> list1 = new List<int> { 7, 24, 10, 3, 4, 19, 33, 37, 28, 6 };
            //List<int> list2 = new List<int> { 8, 14, 9, 5, 21, 27, 0, 1, 2, 23 };
            //List<int> list3 = new List<int> { 12, 17, 35, 11, 30 };


            var optionsToAdd1 = checkingSolution.Options
                .Where(x => list1.Contains(x.Id))
                .OrderBy(x => list1.IndexOf(x.Id)) 
                .ToList();
            checkingSolution.Routes[0].SequenceOfOptions.AddRange(optionsToAdd1);

            var optionsToAdd2 = checkingSolution.Options
                .Where(x => list2.Contains(x.Id))
                .OrderBy(x => list2.IndexOf(x.Id)) 
                .ToList();
            checkingSolution.Routes[1].SequenceOfOptions.AddRange(optionsToAdd2);

            var optionsToAdd3 = checkingSolution.Options
                .Where(x => list3.Contains(x.Id))
                .OrderBy(x => list3.IndexOf(x.Id)) 
                .ToList();
            checkingSolution.Routes[2].SequenceOfOptions.AddRange(optionsToAdd3);

            Option opt = checkingSolution.Routes[0].SequenceOfOptions[0];
            //Console.WriteLine("Option to be moved: {0}", opt.Id);
            checkingSolution.Routes[0].SequenceOfOptions.RemoveAt(0);
            checkingSolution.Routes[1].SequenceOfOptions.RemoveAt(0);
            checkingSolution.Routes[2].SequenceOfOptions.RemoveAt(0);
            checkingSolution.Routes[0].SequenceOfOptions.Add(opt);
            checkingSolution.Routes[1].SequenceOfOptions.Add(opt);
            checkingSolution.Routes[2].SequenceOfOptions.Add(opt);

            var sequenceOfLocations1 = checkingSolution.Routes[0]
                .SequenceOfOptions
                .Select(option => option.Location)
                .ToList();

            var sequenceOfLocations2 = checkingSolution.Routes[1]
                .SequenceOfOptions
                .Select(option => option.Location)
                .ToList();

            var sequenceOfLocations3 = checkingSolution.Routes[2]
                .SequenceOfOptions
                .Select(option => option.Location)
                .ToList();

            checkingSolution.Routes[0].SequenceOfLocations = new();
            checkingSolution.Routes[0].SequenceOfLocations.AddRange(sequenceOfLocations1);
            checkingSolution.Routes[1].SequenceOfLocations = new();
            checkingSolution.Routes[1].SequenceOfLocations.AddRange(sequenceOfLocations2);
            checkingSolution.Routes[2].SequenceOfLocations = new();
            checkingSolution.Routes[2].SequenceOfLocations.AddRange(sequenceOfLocations3);

            double cost = 0;
            foreach (Route rt in checkingSolution.Routes)
            {
                bool tw = CalculateTimes(rt);
                if (!tw)
                {
                    Console.WriteLine("Time Window Feasibility Error");
                }
                for (int i = 0; i < rt.SequenceOfOptions.Count - 1; i++)
                {
                    Option currentOpt = rt.SequenceOfOptions[i];
                    Option nextOpt = rt.SequenceOfOptions[i + 1];
                    
                    cost += CalculateDistance(rt.SequenceOfOptions[i].Location, nextOpt.Location);
                }

                var tw2 = CheckEctLatTestingSolution(rt);
                if (!tw2.Item1)
                {
                    Console.WriteLine("Time Window Feasibility Error");
                    Console.WriteLine("Hello World");
                }
                //rt1 total time: 5998
                //rt2 total time: 6581
                //rt3 total time: 4643
                //total cost: 2805
            }
            Solver solp = new Solver();
            solp.CalculateServiceLevel(checkingSolution, true);
            solp.PrintSolutionForTesting(checkingSolution);
            CheckEverything(checkingSolution);
            Console.WriteLine("Total cost: {0}", cost);
            Console.WriteLine("Hello World");
        }

        public bool CheckRouteFeasibility(Route rt)
        {
            /*Console.WriteLine("Options in route {0}", rt.Id);
            foreach (Option opt in rt.SequenceOfOptions)
            {
                Console.WriteLine(opt.Id);
            }
            */
            int totalCapacity = 0;
            bool timeWindowFeasibility = true;
            bool depotFeasibility = true;
            bool costFeasibility = true;
            double cost = 0;
            // NOTE (perf, Phase 2): `CalculateTimes(rt)` used to be called INSIDE this loop, once per
            // stop — an O(route length) full recomputation repeated once per iteration, making this
            // whole method O(route length²) instead of O(route length). The call takes only `rt` and
            // never reads the loop variables (`i`/`currentOpt`/`nextOpt`), so its result is provably the
            // same on every iteration; hoisted to run exactly once. `CalculateTimes` is a pure read of
            // `rt` (no mutation — confirmed) so this changes nothing observable about the computed
            // feasibility result; the only difference is that an infeasible route now prints its
            // diagnostic message once instead of up to (route length − 1) times, which the regression
            // harness (report-file diff) doesn't compare against anyway — see CheckEverything's
            // similarly-hoisted check just below for the same reasoning.
            bool tw = CalculateTimes(rt);
            if (!tw)
            {
                Console.WriteLine("Time Window Feasibility Error");
                timeWindowFeasibility = false;
            }
            for (int i = 0; i < rt.SequenceOfOptions.Count - 1; i++)
            {
                Option currentOpt = rt.SequenceOfOptions[i];
                Option nextOpt = rt.SequenceOfOptions[i + 1];
                if (currentOpt.Location.Type == 0)
                {
                    if (i + 1 != rt.SequenceOfLocations.Count - 1 && i != 0)
                    {
                        Console.WriteLine("Depot Feasibility Error");
                        depotFeasibility = false;
                    }
                }
                //Console.WriteLine("Current location: {0} Next location: {1}", currentOpt.Location.Id, nextOpt.Location.Id);
                cost += CalculateDistance(rt.SequenceOfOptions[i].Location, nextOpt.Location);
                //Console.WriteLine("Cost: {0}", cost);
            }
            if (Math.Abs(cost - rt.Cost) > 0.001)
            {
                Console.WriteLine("Cost Feasibility Error");
                costFeasibility = false;
            }
            for (int i = 0; i < rt.SequenceOfCustomers.Count; i++)
            {
                totalCapacity += rt.SequenceOfCustomers[i].Dem;
            }
            bool capacityFeasibility = (totalCapacity > rt.Capacity) ? false : true;
            if (!capacityFeasibility)
            {
                Console.WriteLine("Route Capacity Feasibility Error");
            }
            return timeWindowFeasibility && capacityFeasibility && depotFeasibility && costFeasibility;
        }

        public void RemoveEmptyRoutes()
        {
            this.Routes.RemoveAll(rt => rt.Load == 0);
        }

        public bool Check(Solution sol)
        {
            cost = 0;
            foreach (Route rt in sol.Routes)
            {
                double totalTime = 0;
                for (int i = 0; i < rt.SequenceOfLocations.Count - 1; i++)
                {
                    totalTime += CalculateTime(rt.SequenceOfLocations[i], rt.SequenceOfLocations[i + 1]);

                    if (rt.SequenceOfLocations[i + 1].Ready > totalTime)
                    {
                        totalTime = rt.SequenceOfLocations[i + 1].Ready;
                    }

                    if (rt.SequenceOfLocations[i].Id != rt.SequenceOfLocations[i + 1].Id)
                    {
                        totalTime += rt.SequenceOfLocations[i + 1].ServiceTime;
                    }

                    if (!(totalTime <= rt.SequenceOfLocations[i + 1].Due))
                    {
                        return false;
                    }
                }

                for (int i = 0; i < rt.SequenceOfOptions.Count - 1; i++)
                {
                    Option currentOpt = rt.SequenceOfOptions[i];
                    Option nextOpt = rt.SequenceOfOptions[i + 1];
                    cost += CalculateDistance(rt.SequenceOfOptions[i].Location, nextOpt.Location);
                }
            }
            Console.WriteLine("Cost: " + cost);
            if (sol.Cost != cost)
            {
                Console.WriteLine("Cost: " + cost);
                return false;
            }
            return true;
        }

        //public void ExportToJson(string filePath)
        //{
        //    List<object> routeDataList = new List<object>();
        //    foreach (Route rt in Routes)
        //    {
        //        // Get JSON string for each route and parse it to an object
        //        string routeJson = rt.ExportToJson(null);
        //        var routeData = JsonConvert.DeserializeObject<object>(routeJson);
        //        routeDataList.Add(routeData);
        //    }

        //    var solutionData = new
        //    {
        //        Cost = this.Cost,
        //        Routes = routeDataList
        //    };

        //    // Serialize the dto object to JSON
        //    string json = JsonConvert.SerializeObject(solutionData, Newtonsoft.Json.Formatting.Indented);

        //    // Write JSON to file
        //    File.WriteAllText(filePath, json);
        //}
    }
}

