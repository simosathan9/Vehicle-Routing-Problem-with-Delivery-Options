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
        Random rnd = new Random(14);

        public Solver(InstanceReader ir)
        {
            this.customers = ir.AllCustomers;
            this.DistanceMatrix = ir.DistanceMatrix;
            this.timeMatrix = ir.TimeMatrix;
            this.cap = ir.Cap;
            this.depot = ir.Depot;
            this.Options = ir.Options;
            this.Promises = new double[Options.Count + 1, Options.Count + 1];
            for (int i = 0; i < Math.Pow(Options.Count + 1, 2); i++) promises[i % (Options.Count + 1), i / (Options.Count + 1)] = double.MaxValue;
        }

        public void Solve()
        {
            Solution restartBestSol = new Solution();
            restartBestSol.Cost = Math.Pow(10, 9);

            for (int restart = 0; restart < 10; restart++)
            {
                this.Promises = new double[Options.Count + 1, Options.Count + 1];
                for (int i = 0; i < Math.Pow(Options.Count + 1, 2); i++) promises[i % (Options.Count + 1), i / (Options.Count + 1)] = double.MaxValue;
                this.Sol = new();
                // restartBestSol best of each restart
                LocalSearch ls = new();
                Random rnd = new(restart);//25 when is stable, the first few iterations are different and after a few they stay the same 
                SetRoutedToFalse(customers);
                SetServedToFalse(options);
                MinimumInsertions();
                Route empty = new Route(166, 0, depot);
                foreach (Route r in this.Sol.Routes) //chang
                {
                    if (r.SequenceOfLocations.Count == 2)
                    {
                        this.Sol.Cost -= r.Cost;
                        empty = r;
                        continue;
                    }
                    Console.WriteLine("LOCATION | CUSTOMER");
                    for (int i = 0; i < r.SequenceOfOptions.Count; i++)
                    {
                        Console.WriteLine("{0} {1}", r.SequenceOfOptions[i].Location.Id, r.SequenceOfOptions[i].Cust.Id);
                    }
                    Console.WriteLine("Max capacity: {0} Load:{1}", r.Capacity, r.Load);
                    if (!CheckRouteFeasibility(r)) { Console.WriteLine("INFEASIBLE"); }
                    Console.WriteLine("--------------");
                }
                this.Sol.Routes.Remove(empty);
                Console.WriteLine(this.Sol.Cost); //chang
                CalculateServiceLevel(this.Sol); //chang
                Console.WriteLine("---------------------------");
                Console.WriteLine("---------------------------");
                Console.WriteLine("---------------------------");

                double bestSolCost = 10000000;
                int reinitCount = -1;
                int c = 0;
                int lastImprovement = 0;
                for (int i = 0; i < 7000; i++)
                {
                    if (i - lastImprovement > 3000)
                    {
                        break;
                    }

                    reinitCount++;
                    Relocation rm = new();
                    Swap sm = new();
                    TwoOpt top = new();
                    Flip flip = new();
                    if (reinitCount == options.Count * 1.5)
                    {
                        for (int j = 0; j < Math.Pow(Options.Count + 1, 2); j++) promises[j % (Options.Count + 1), j / (Options.Count + 1)] = double.MaxValue;
                        reinitCount = 0;
                    }

                    int k = rnd.Next(1, 5);
                    if (k == 1)
                    {
                        sm = ls.FindBestSwapMove(sm, this);
                        ls.ApplySwapMove(sm, this);
                    }
                    else if (k == 2)
                    {
                        rm = ls.FindBestRelocationMove(rm, this);
                        ls.ApplyRelocationMove(rm, this);
                    }
                    else if (k == 3)
                    {
                        top = ls.FindBestTwoOptMove(top, this);
                        ls.ApplyTwoOptMove(top, this);
                    }
                    else if (k == 4)
                    {
                        if (i > 2000 && ((i - c) > 500))
                        {
                            c = i;
                            flip = ls.FindBestFlipMove(flip, this);
                            ls.ApplyFlipMove(flip, this);
                        }
                        else
                        {
                            sm = ls.FindBestSwapMove(sm, this);
                            rm = ls.FindBestRelocationMove(rm, this);
                            top = ls.FindBestTwoOptMove(top, this);
                            if (rm.MoveCost < sm.MoveCost && rm.MoveCost < top.MoveCost)
                            {
                                ls.ApplyRelocationMove(rm, this);
                            }
                            else if (sm.MoveCost < top.MoveCost && sm.MoveCost < rm.MoveCost)
                            {
                                ls.ApplySwapMove(sm, this);
                            }
                            else
                            {
                                ls.ApplyTwoOptMove(top, this);

                            }
                        }
                    }

                    if (this.Sol.Cost < bestSolCost) //chang
                    {
                        bestSolCost = this.Sol.Cost; //chang
                        bestSol = this.Sol.DeepCopy(); //chang
                        lastImprovement = i;
                    }
                    Console.WriteLine(Convert.ToString(i) + ' ' + Convert.ToString(this.Sol.Cost) + ' ' + Convert.ToString(bestSolCost)); //chang
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

                if (bestSol.Cost < restartBestSol.Cost)
                {
                    restartBestSol = bestSol;
                }
               
                Console.WriteLine("///////////////////////");
                Console.WriteLine(bestSolCost + " " + restartBestSol.Cost);
                System.Threading.Thread.Sleep(5000);
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

        void SetRoutedToFalse(List<Customer> customers)
        {
            foreach(Customer customer1 in customers)
            {
                customer1.IsRouted = false;
            }
        }

        void SetServedToFalse(List<Option> options)
        {
            foreach (Option option1 in options)
            {
                option1.IsServed = false;
            }
        }

        void AlwaysKeepAnEmptyRoute() //chang
        {
            if (this.Sol.Routes.Count < 10)
            {
                if (this.Sol.Routes.Count == 0)
                {
                    Route newRoute = new(this.Sol.Routes.Count, cap, depot);
                    this.Sol.Routes.Add(newRoute);
                    this.Sol.Cost += newRoute.Cost;
                }
                else
                {
                    if (this.Sol.Routes.Last().SequenceOfLocations.Count > 2)
                    {
                        Route newRoute = new(this.Sol.Routes.Count, cap, depot);
                        this.Sol.Routes.Add(newRoute);
                        this.Sol.Cost += newRoute.Cost;
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

            writetext.WriteLine("Total cost: " + this.Sol.Cost + "\n");
            writetext.WriteLine("\n");

            for (int i = 0; i < this.Sol.Routes.Count; i++)
            {
                writetext.WriteLine("Route " + Convert.ToString(i) + " " + "Location " + "Option " + "Customer" + "\n");
                Route rt = this.Sol.Routes[i];
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
            /// loc: the position to be placed after
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



        /// <summary>
        /// Calculates the time windows of the route <paramref>rt</paramref> for
        /// all the <paramref>locations</paramref> to be visited after the specified
        /// index <paramref>loc</paramref>
        /// </summary>
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
            for (int j = tempRoute.SequenceOfLocations.Count - 2; j > - 1; j--)
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
            } else
            {
                bool feasible = true;
                for(int i = 0; i < tempRoute.SequenceOfEct.Count; i++)
                {
                    if (tempRoute.SequenceOfEct[i] > tempRoute.SequenceOfLat[i])
                    {
                        feasible = false;
                    }
                }
                return new Tuple<bool, double[], double[]>(feasible, tempRoute.SequenceOfEct.ToArray(), tempRoute.SequenceOfLat.ToArray());
            }
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
        public List<Customer> Customers { get => customers; set => customers = value; }

        List<CustomerInsertionAllPositions> IdentifyMinimumCostInsertion(CustomerInsertionAllPositions bestInsertion)
        {
            //goodoptions list
            // new list with the 3 best insertions
            List<CustomerInsertionAllPositions> topThree = new List<CustomerInsertionAllPositions>();
            topThree.Add(bestInsertion);
            //Random rnd = new();
            for (int i = 0; i < Options.Count ; i++)
            {
                candidateOpt = Options[i];
                if (candidateOpt.Cust.IsRouted == false & candidateOpt.IsServed == false)
                {
                    foreach (Route rt in this.Sol.Routes) //chang
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

                                    if (trialCost <= topThree.Last().Cost || topThree.Count < 3)
                                    {
                                        if (candidateOpt.Location.Type == 2 | candidateOpt.Location.Cap < candidateOpt.Location.MaxCap)
                                        {
                                            // update the best insertion list and leave out the worst
                                            bestInsertion.Option = candidateOpt;
                                            bestInsertion.Customer = candidateOpt.Cust;
                                            bestInsertion.Location = candidateOpt.Location;
                                            bestInsertion.Route = rt;
                                            bestInsertion.InsertionPosition = j + 1;
                                            bestInsertion.Duration = trialTime;
                                            bestInsertion.Cost = trialCost;
                                            bestInsertion.Ect = tw[0];
                                            bestInsertion.Lat = tw[1];

                                            CustomerInsertionAllPositions custTemp = new CustomerInsertionAllPositions(bestInsertion);
                                            topThree.Add(custTemp);
                                            topThree = topThree.OrderBy(o=>o.Cost).ToList();
                                            topThree = topThree.Take(3).ToList();
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            return topThree;
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
            this.Sol.Cost += insertion.Cost; //chang
            this.Sol.Duration += insertion.Duration; //chnag
            insertion.Location.Cap += 1;
            insertion.Route.SequenceOfEct.Insert(insertion.InsertionPosition, insertion.Ect);
            insertion.Route.SequenceOfLat.Insert(insertion.InsertionPosition, insertion.Lat);
            UpdateTimes(insertion.Route, insertion.InsertionPosition);
        }

        void MinimumInsertions()
        {
            bool modelIsFeasible = true;
            while (customers.Any(x => !x.IsRouted))
            {   
                bestInsertion = new CustomerInsertionAllPositions();
                AlwaysKeepAnEmptyRoute();
                List<CustomerInsertionAllPositions> topThree = IdentifyMinimumCostInsertion(bestInsertion);
                bestInsertion = topThree[rnd.Next(topThree.Count)];
                if (bestInsertion.Customer != null)
                {
                    ApplyCustomerInsertionAllPositions(bestInsertion);
                } else
                {
                    modelIsFeasible = false;
                    break;
                }
            }
            ReportSolution(this.Sol); //chang
        }
        void CalculateServiceLevel(Solution sol)
        {
            int po0Sum = 0;
            int po1Sum = 0;
            int po2Sum = 0;
            double sum = 0;
            int po = -1;

            for (int r = 0; r < this.Sol.Routes.Count; r++)
            {
                for (int c = 1; c < this.Sol.Routes[r].SequenceOfOptions.Count - 1; c++)
                {
                    po = this.Sol.Routes[r].SequenceOfOptions[c].Prio;
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
            Console.WriteLine("Priority 2: {0}", po1Sum/(sum - po0Sum));
        }

        void UpdatePromise(int o1, int o2, int newCost)
        {
            Promises[o1, o2] = newCost;
        }
    }
}
