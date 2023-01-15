using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using OxyPlot;


namespace VrdpoProject
{
    public class Solver
    {
        private CustomerInsertionAllPositions bestInsertion = new();
        private Solution sol = new();
        private List<Customer> customers = new();
        private double[,] distanceMatrix;
        private double[,] timeMatrix;
        private int cap;
        private Location depot;
        private List<Option> options = new();
        private double[,] promises;
        Solution bestSol = new Solution();

        public Solver(InstanceReader ir)
        {
            this.customers = ir.AllCustomers;
            this.DistanceMatrix = ir.DistanceMatrix;
            this.timeMatrix = ir.TimeMatrix;
            this.cap = ir.Cap;
            this.depot = ir.Depot;
            this.Options = ir.Options;
            this.promises = new double[Options.Count + 1, Options.Count + 1];
            for (int i = 0; i < Math.Pow(Options.Count + 1, 2); i++) promises[i % (Options.Count + 1), i / (Options.Count + 1)] = double.MaxValue;
        }

        public Solver()
        {
            this.Sol = Sol;
            this.customers = customers;
            this.DistanceMatrix = DistanceMatrix;
            this.timeMatrix = timeMatrix;
            this.depot = depot;
            this.Options = Options;
        }

        public void Solve()
        {
            LocalSearch ls = new();
            Random rnd = new(25);//25
            SetRoutedToFalse(customers);
            MinimumInsertions();
            foreach (Route r in Sol.Routes)
            {
                Console.WriteLine("LOCATION | CUSTOMER");
                for (int i = 0; i < r.SequenceOfOptions.Count; i++)
                {
                    Console.WriteLine("{0} {1}", r.SequenceOfOptions[i].Location.Id, r.SequenceOfOptions[i].Cust.Id);
                }
                Console.WriteLine("Max capacity: {0} Load:{1}", r.Capacity, r.Load);
                if (!CheckRouteFeasibility(r)) { Console.WriteLine("INFEASIBLE"); }
                Console.WriteLine("--------------");
            }
            Console.WriteLine(Sol.Cost);
            CalculateServiceLevel(Sol);
            Console.WriteLine("---------------------------");
            Console.WriteLine("---------------------------");
            Console.WriteLine("---------------------------");

            double bestSolCost = 10000000;
            int reinitCount = 0;
            for (int i = 0; i < 100000; i++)
            {
                Relocation rm = new();
                Swap sm = new();
                TwoOpt top = new();
                if (reinitCount == options.Count*2)
                {
                    for (int j = 0; j < Math.Pow(Options.Count + 1, 2); j++) promises[j % (Options.Count + 1), j / (Options.Count + 1)] = double.MaxValue;
                    reinitCount = 0;
                }
                rm = ls.FindBestRelocationMove(rm, this);
                sm = ls.FindBestSwapMove(sm, this);
                top = ls.FindBestTwoOptMove(top, this);
                if (rm.MoveCost == 1000000000 & rm.TargetRoutePosition != 0)//null checks
                {
                    for (int j = 0; j < Math.Pow(Options.Count + 1, 2); j++) promises[j % (Options.Count + 1), j / (Options.Count + 1)] = double.MaxValue;
                    reinitCount = 0;
                    continue;
                } else if (sm.PositionOfFirstOption == 0)//null check
                {
                    ls.ApplyRelocationMove(rm, this);
                } else if (rm.MoveCost == 1000000000)//null check
                {
                    ls.ApplySwapMove(sm, this);
                } else if (top.MoveCost == 1000000)
                {
                    ls.ApplyTwoOptMove(top, this);
                } else
                {
                    int k = rnd.Next(1, 4);
                    if (k == 1)
                    {
                        ls.ApplySwapMove(sm, this);
                    } else if (k == 2)
                    {
                        ls.ApplyRelocationMove(rm, this);
                    } else
                    {
                        ls.ApplyTwoOptMove(top, this);
                    }
                }
                reinitCount++;
                
                if (Sol.Cost < bestSolCost)
                {
                    bestSolCost = Sol.Cost;
                    bestSol = Sol.DeepCopy();
                }
                Console.WriteLine(Convert.ToString(i) + ' ' + Convert.ToString(Sol.Cost) + ' ' + Convert.ToString(bestSolCost));
            }
            foreach (Route r in bestSol.Routes)
            {
                Console.WriteLine("LOCATION | CUSTOMER");
                for (int i = 0; i < r.SequenceOfOptions.Count; i++)
                {
                    Console.WriteLine("{0} {1}", r.SequenceOfOptions[i].Location.Id, r.SequenceOfOptions[i].Cust.Id);
                }
                Console.WriteLine("Max capacity: {0} Load:{1}", r.Capacity, r.Load);
                if (!CheckRouteFeasibility(r)) { Console.WriteLine("INFEASIBLE"); }
                Console.WriteLine("--------------");
            }
            Console.WriteLine(bestSol.Cost);
            CalculateServiceLevel(bestSol);
            //RouteCustomersToSharedLocations();
        }

        public bool CheckRouteFeasibility(Route rt)
        {
            int totalCapacity = 0;
            bool timeWindowFeasibility = true;
            bool depotFeasibility = true;
            bool costFeasibility = true;
            double cost = 1000000;
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

        void SetRoutedToFalse(List<Customer> customers)
        {
            foreach(Customer customer1 in customers)
            {
                customer1.IsRouted = false;
            }
        }

        void AlwaysKeepAnEmptyRoute()
        {
            if (Sol.Routes.Count < 10)
            {
                if (Sol.Routes.Count == 0)
                {
                    Route newRoute = new(Sol.Routes.Count, cap, depot);
                    Sol.Routes.Add(newRoute);
                    Sol.Cost += newRoute.Cost;
                }
                else
                {
                    if (Sol.Routes.Last().SequenceOfLocations.Count > 2)
                    {
                        Route newRoute = new(Sol.Routes.Count, cap, depot);
                        Sol.Routes.Add(newRoute);
                        Sol.Cost += newRoute.Cost;
                    }
                }
            }
        }

        public double CalculateDistance(Location n1, Location n2)
        {
            if (n1.Id > n2.Id)
            {
                return DistanceMatrix[n2.Id, n1.Id - n2.Id];
            } else
            {
                return DistanceMatrix[n1.Id, n2.Id - n1.Id];
            }
        }

        double CalculateTime(Location n1, Location n2)
        {
            if (n1.Id > n2.Id)
            {
                return timeMatrix[n2.Id, n1.Id - n2.Id];
            }
            else
            {
                return timeMatrix[n1.Id, n2.Id - n1.Id];
            }
        }

        double FindMatrix(int id1, int id2)
        {
            if (id1 > id2)
            {
                return timeMatrix[id2, id1 - id2];
            }
            else
            {
                return timeMatrix[id1, id2 - id1];
            }
        }


        void ReportSolution(Solution sol)
        {
            StreamWriter writetext = new("write.txt");

            writetext.WriteLine("Total cost: " + sol.Cost + "\n");
            writetext.WriteLine("\n");

            for (int i = 0; i < sol.Routes.Count; i++)
            {
                writetext.WriteLine("Route " + Convert.ToString(i) + " " + "Location " + "Option " + "Customer" + "\n");
                Route rt = sol.Routes[i];
                for (int j = 0; j < rt.SequenceOfOptions.Count; j++)
                {
                    if (j == 0 | j == rt.SequenceOfOptions.Count - 1)
                    {
                        writetext.WriteLine(rt.SequenceOfLocations[j] + " " + "-" + " " + "-" + "\n");
                    }
                    else
                    {
                        writetext.WriteLine(rt.SequenceOfLocations[j] + " " + rt.SequenceOfOptions[j] + " " + rt.SequenceOfCustomers[j] + "\n");
                    }
                }
            }
            writetext.Close();
        }
        public double[] RespectsTimeWindow(Route rt, int loc, Location l)
        {
            // loc: the position to be placed after
            double lat = Math.Min(rt.SequenceOfLat[loc+1] - CalculateTime(l, rt.SequenceOfLocations[loc+1]) - l.ServiceTime,l.Due - l.ServiceTime);
            if (l == rt.SequenceOfLocations[loc + 1])
            {
                lat += l.ServiceTime;
            }
            double ect = Math.Max(rt.SequenceOfEct[loc] + CalculateTime(rt.SequenceOfLocations[loc], l) + l.ServiceTime, l.Ready + l.ServiceTime);
            if (l == rt.SequenceOfLocations[loc])
            {
                ect -= l.ServiceTime;
            }
            double[] tw = new double[]{ ect, lat };
            return tw;
        }

        Option candidateOpt;
        Location A, B;
        double timeAdded, timeRemoved, trialTime;
        double costAdded, costRemoved, trialCost;
        double[] tw;

        public double[,] DistanceMatrix { get => distanceMatrix; set => distanceMatrix = value; }
        public Solution Sol { get => sol; set => sol = value; }
        internal List<Option> Options { get => options; set => options = value; }
        public double[,] Promises { get => promises; set => promises = value; }

        void IdentifyMinimumCostInsertion(CustomerInsertionAllPositions bestInsertion)
        {
            //goodoptions list
            for (int i = 0; i < Options.Count ; i++)
            {
                candidateOpt = Options[i];
                if (candidateOpt.Cust.IsRouted == false & candidateOpt.IsServed == false)
                {
                    foreach (Route rt in Sol.Routes)
                    {
                        if (rt.Load + candidateOpt.Cust.Dem <= rt.Capacity)
                        {
                            for (int j = 0; j < rt.SequenceOfLocations.Count - 1; j++)
                            {
                                A = rt.SequenceOfLocations[j];
                                B = rt.SequenceOfLocations[j + 1];
                                timeAdded = CalculateTime(A, candidateOpt.Location) + CalculateTime(candidateOpt.Location, B);
                                timeRemoved = CalculateTime(A, B);
                                costAdded = CalculateDistance(A, candidateOpt.Location) + CalculateDistance(candidateOpt.Location, B);
                                costRemoved = CalculateDistance(A, B);
                                trialCost = costAdded - costRemoved;
                                trialTime = timeAdded - timeRemoved + candidateOpt.ServiceTime;
                                tw = RespectsTimeWindow(rt, j, candidateOpt.Location);
                                if (tw[0] <= tw[1]) {

                                    if (trialCost < bestInsertion.Cost)
                                    {
                                        if (candidateOpt.Location.Type == 2 | candidateOpt.Location.Cap < candidateOpt.Location.MaxCap)
                                        {
                                            bestInsertion.Option = candidateOpt;
                                            bestInsertion.Customer = candidateOpt.Cust;
                                            bestInsertion.Location = candidateOpt.Location;
                                            bestInsertion.Route = rt;
                                            bestInsertion.InsertionPosition = j + 1;
                                            bestInsertion.Duration = trialTime;
                                            bestInsertion.Cost = trialCost;
                                            bestInsertion.Ect = tw[0];
                                            bestInsertion.Lat = tw[1];
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
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

        void ApplyCustomerInsertionAllPositions(CustomerInsertionAllPositions insertion)
        {
            insertion.Route.SequenceOfLocations.Insert(insertion.InsertionPosition, insertion.Location);
            insertion.Route.SequenceOfCustomers.Insert(insertion.InsertionPosition, insertion.Customer);
            insertion.Route.SequenceOfOptions.Insert(insertion.InsertionPosition, insertion.Option);
            insertion.Route.Duration += insertion.Duration;
            insertion.Route.Cost += insertion.Cost;
            insertion.Route.Load += insertion.Customer.Dem;
            insertion.Customer.IsRouted = true;
            insertion.Option.IsServed = true;
            Sol.Cost += insertion.Cost;
            Sol.Duration += insertion.Duration;
            insertion.Location.Cap += 1;
            insertion.Route.SequenceOfEct.Insert(insertion.InsertionPosition, insertion.Ect);
            insertion.Route.SequenceOfLat.Insert(insertion.InsertionPosition, insertion.Lat);
            UpdateTimes(insertion.Route, insertion.InsertionPosition);
        }

        void MinimumInsertions()
        {
            bool modelIsFeasible = true;
            while(customers.Any(x => !x.IsRouted))
            {   
                bestInsertion = new CustomerInsertionAllPositions();
                AlwaysKeepAnEmptyRoute();
                IdentifyMinimumCostInsertion(bestInsertion);
                if (bestInsertion.Customer != null)
                {
                    ApplyCustomerInsertionAllPositions(bestInsertion);
                } else
                {
                    modelIsFeasible = false;
                    break;
                }
            }
            ReportSolution(Sol);
        }
        void CalculateServiceLevel(Solution sol)
        {
            int po0Sum = 0;
            int po1Sum = 0;
            int po2Sum = 0;
            double sum = 0;
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
            sum = po0Sum + po1Sum + po2Sum;
            Console.WriteLine("Priority 1: {0}", po0Sum/sum);
            Console.WriteLine("Priority 2: {0}", po1Sum/sum);
            Console.WriteLine("Priority 3: {0}", po2Sum/sum);
        }

        void UpdatePromise(int o1, int o2, int newCost)
        {
            Promises[o1, o2] = newCost;
        }
    }
}
