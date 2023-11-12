using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VrdpoProject
{

    public class Solution
    {
        private double duration;
        private double cost;
        private List<Route> routes;
        private double[,] timeMatrix;
        private double[,] distanceMatrix;
        private int cap;
        private Location depot;
        private List<Option> options = new();
        private double[,] promises;
        private List<Customer> customers = new();

        public Solution()
        {
            InstanceReader model = new();
            model.BuildModel();

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
            for (int i = 0; i < Math.Pow(Options.Count + 1, 2); i++) promises[i % (Options.Count + 1), i / (Options.Count + 1)] = double.MaxValue;
        }

        public Solution(double duration, double cost, List<Route> routes, double[,] distanceMatrix, 
            double[,] timeMatrix, int cap, Location depot, List<Option> options, double[,] promises, List<Customer> customers)
        {
            this.Duration = duration;
            this.Cost = cost;
            this.Routes = routes;
            this.DistanceMatrix = distanceMatrix;
            this.TimeMatrix = timeMatrix;
            this.Cap = cap;
            this.Depot = depot;
            this.Options = options;
            this.Customers = customers;
            this.Promises = promises;
        }

        public Solution DeepCopy(Solution sol)
        {
            Solution deepCopySol = new Solution(sol.Duration, sol.Cost, sol.Routes, sol.DistanceMatrix, sol.TimeMatrix, sol.Cap, sol.Depot, sol.Options, sol.Promises, sol.Customers);
            return deepCopySol;
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

        public double[] RespectsTimeWindow(Route rt, int loc, Location l)
        {
            /// loc: the position to be placed after
            double lat = Math.Min(rt.SequenceOfLat[loc + 1] - CalculateTime(l, rt.SequenceOfLocations[loc + 1]) - l.ServiceTime, l.Due - l.ServiceTime);
            if (l == rt.SequenceOfLocations[loc + 1])
            {
                lat += l.ServiceTime;
            }
            double ect = Math.Max(rt.SequenceOfEct[loc] + CalculateTime(rt.SequenceOfLocations[loc], l) + l.ServiceTime, l.Ready + l.ServiceTime);
            if (l == rt.SequenceOfLocations[loc])
            {
                ect -= l.ServiceTime;
            }
            double[] tw = new double[] { ect, lat };
            return tw;
        }

        /// <summary>
        /// Calculates the time windows of the route <paramref>rt</paramref> for
        /// all the <paramref>locations</paramref> to be visited after the specified
        /// index <paramref>loc</paramref>
        /// </summary>
        double[] tw;
        public Tuple<bool, double[], double[]> RespectsTimeWindow(Route rt, int loc, List<Location> locations)
        {
            tw = RespectsTimeWindow(rt, loc, locations.First());
            if (tw[0] > tw[1])
            {
                return new Tuple<bool, double[], double[]>(false, new double[1], new double[1]);
            }
            List<double> ects = new();
            List<double> lats = new();
            Route tempRoute = new(44, 150, depot);
            tempRoute.SequenceOfLocations = rt.SequenceOfLocations.Take(loc + 1).ToList();
            tempRoute.SequenceOfLocations.AddRange(locations);
            tempRoute.SequenceOfLat.AddRange(Enumerable.Repeat((double)7200, tempRoute.SequenceOfLocations.Count - 2).ToList());
            for (int i = 1; i < tempRoute.SequenceOfLocations.Count; i++)
            {
                double ect = Math.Max(tempRoute.SequenceOfLocations[i].Ready + tempRoute.SequenceOfLocations[i].ServiceTime,
                                               tempRoute.SequenceOfEct[i - 1] + CalculateTime(tempRoute.SequenceOfLocations[i], tempRoute.SequenceOfLocations[i - 1])
                                               + tempRoute.SequenceOfLocations[i].ServiceTime);
                if (tempRoute.SequenceOfLocations[i - 1] == tempRoute.SequenceOfLocations[i])
                {
                    tempRoute.SequenceOfEct[i] -= (tempRoute.SequenceOfLocations[i].ServiceTime - 20);
                }
                tempRoute.SequenceOfEct.Insert(tempRoute.SequenceOfEct.Count - 1, ect);
            }
            tempRoute.SequenceOfEct.RemoveAt(tempRoute.SequenceOfEct.Count - 1);
            for (int j = tempRoute.SequenceOfLocations.Count - 2; j > -1; j--)
            {
                double lat = Math.Min(tempRoute.SequenceOfLocations[j].Due - tempRoute.SequenceOfLocations[j].ServiceTime,
                                               tempRoute.SequenceOfLat[j + 1] - CalculateTime(tempRoute.SequenceOfLocations[j], tempRoute.SequenceOfLocations[j + 1])
                                               - tempRoute.SequenceOfLocations[j].ServiceTime);
                if (tempRoute.SequenceOfLocations[j + 1] == tempRoute.SequenceOfLocations[j])
                {
                    tempRoute.SequenceOfLat[j] += (tempRoute.SequenceOfLocations[j].ServiceTime - 20);
                }
                tempRoute.SequenceOfLat.RemoveAt(0);
                tempRoute.SequenceOfLat.Insert(j, lat);
            }
            //tempRoute.SequenceOfLat.RemoveAt(0);
            ects = tempRoute.SequenceOfEct.ToList();
            lats = tempRoute.SequenceOfLat.ToList();
            bool xs = !lats.SequenceEqual(lats.OrderBy(x => x));
            if (!ects.SequenceEqual(ects.OrderBy(x => x)) || !lats.SequenceEqual(lats.OrderBy(x => x))
                || ects.Last() > 7200)
            {
                return new Tuple<bool, double[], double[]>(false, tempRoute.SequenceOfEct.ToArray(), tempRoute.SequenceOfLat.ToArray());
            }
            else
            {
                bool feasible = true;
                for (int i = 0; i < tempRoute.SequenceOfEct.Count; i++)
                {
                    if (tempRoute.SequenceOfEct[i] > tempRoute.SequenceOfLat[i])
                    {
                        feasible = false;
                    }
                }
                return new Tuple<bool, double[], double[]>(feasible, tempRoute.SequenceOfEct.ToArray(), tempRoute.SequenceOfLat.ToArray());
            }
        }

        public void UpdateTimes(Route rt, int loc)
        {
            for (int i = loc; i < rt.SequenceOfLocations.Count; i++)
            {
                rt.SequenceOfEct[i] = Math.Max(rt.SequenceOfLocations[i].Ready + rt.SequenceOfLocations[i].ServiceTime,
                                               rt.SequenceOfEct[i - 1] + CalculateTime(rt.SequenceOfLocations[i], rt.SequenceOfLocations[i - 1])
                                               + rt.SequenceOfLocations[i].ServiceTime);
                if (rt.SequenceOfLocations[i - 1] == rt.SequenceOfLocations[i])
                {
                    rt.SequenceOfEct[i] -= (rt.SequenceOfLocations[i].ServiceTime - 20);
                }
            }

            for (int j = loc; j > -1; j--)
            {
                rt.SequenceOfLat[j] = Math.Min(rt.SequenceOfLocations[j].Due - rt.SequenceOfLocations[j].ServiceTime,
                                               rt.SequenceOfLat[j + 1] - CalculateTime(rt.SequenceOfLocations[j], rt.SequenceOfLocations[j + 1])
                                               - rt.SequenceOfLocations[j].ServiceTime);
                if (rt.SequenceOfLocations[j + 1] == rt.SequenceOfLocations[j])
                {
                    rt.SequenceOfLat[j] += (rt.SequenceOfLocations[j].ServiceTime - 20);
                }
            }
        }

        public void UpdateTimes(Route rt)
        {
            for (int i = 1; i < rt.SequenceOfLocations.Count; i++)
            {
                rt.SequenceOfEct[i] = Math.Max(rt.SequenceOfLocations[i].Ready + rt.SequenceOfLocations[i].ServiceTime,
                                               rt.SequenceOfEct[i - 1] + CalculateTime(rt.SequenceOfLocations[i], rt.SequenceOfLocations[i - 1])
                                               + rt.SequenceOfLocations[i].ServiceTime);
                if (rt.SequenceOfLocations[i - 1] == rt.SequenceOfLocations[i])
                {
                    rt.SequenceOfEct[i] -= (rt.SequenceOfLocations[i].ServiceTime - 20);
                }
            }

            for (int j = rt.SequenceOfLocations.Count - 2; j > -1; j--)
            {
                rt.SequenceOfLat[j] = Math.Min(rt.SequenceOfLocations[j].Due - rt.SequenceOfLocations[j].ServiceTime,
                                               rt.SequenceOfLat[j + 1] - CalculateTime(rt.SequenceOfLocations[j], rt.SequenceOfLocations[j + 1])
                                               - rt.SequenceOfLocations[j].ServiceTime);
                if (rt.SequenceOfLocations[j + 1] == rt.SequenceOfLocations[j])
                {
                    rt.SequenceOfLat[j] += (rt.SequenceOfLocations[j].ServiceTime - 20);
                }
            }
        }

        public bool CheckRouteFeasibility(Route rt)
        {
            int totalCapacity = 0;
            bool timeWindowFeasibility = true;
            bool depotFeasibility = true;
            bool costFeasibility = true;
            double cost = 0;
            for (int i = 0; i < rt.SequenceOfOptions.Count - 1; i++)
            {
                Option currentOpt = rt.SequenceOfOptions[i];
                Option nextOpt = rt.SequenceOfOptions[i + 1];
                double[] tw = RespectsTimeWindow(rt, i, nextOpt.Location);
                double ect = tw[0];
                double lat = tw[1];
                if (ect > lat && ect >= nextOpt.Location.Ready && ect <= nextOpt.Location.Due)
                {
                    timeWindowFeasibility = false;
                    //break;
                }
                if (currentOpt.Location.Type == 0)
                {
                    if (i + 1 != rt.SequenceOfLocations.Count - 1 && i != 0)
                    {
                        depotFeasibility = false;
                    }
                    //break;
                }
                cost += CalculateDistance(rt.SequenceOfOptions[i].Location, nextOpt.Location);
            }
            if (cost != rt.Cost)
            {
                costFeasibility = false;
            }
            for (int i = 0; i < rt.SequenceOfCustomers.Count; i++)
            {
                totalCapacity += rt.SequenceOfCustomers[i].Dem;
            }
            bool capacityFeasibility = (totalCapacity > rt.Capacity) ? false : true;
            return timeWindowFeasibility && capacityFeasibility && depotFeasibility && costFeasibility;
        }
    }
}

