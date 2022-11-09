using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Linq;


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

        public Solver(InstanceReader ir)
        {
            this.customers = ir.AllCustomers;
            this.DistanceMatrix = ir.DistanceMatrix;
            this.timeMatrix = ir.TimeMatrix;
            this.cap = ir.Cap;
            this.depot = ir.Depot;
            this.options = ir.Options;
        }

        public Solver()
        {
            this.Sol = Sol;
            this.customers = customers;
            this.DistanceMatrix = DistanceMatrix;
            this.timeMatrix = timeMatrix;
            this.depot = depot;
            this.options = options;
        }

        public void Solve()
        {
            LocalSearch ls = new();
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
                Console.WriteLine("Max duration: {0} Duration:{1}", r.MaxDuration, r.Duration);
                Console.WriteLine("--------------");

            }
            Console.WriteLine(Sol.Cost);
            CalculateServiceLevel(Sol);
            Relocation rm = new();
            ls.FindBestRelocationMove(rm, this);
            foreach (Route r in Sol.Routes)
            {
                Console.WriteLine("LOCATION | CUSTOMER");
                for (int i = 0; i < r.SequenceOfOptions.Count; i++)
                {
                    Console.WriteLine("{0} {1}", r.SequenceOfOptions[i].Location.Id, r.SequenceOfOptions[i].Cust.Id);
                }
                Console.WriteLine("Max capacity: {0} Load:{1}", r.Capacity, r.Load);
                Console.WriteLine("Max duration: {0} Duration:{1}", r.MaxDuration, r.Duration);
                Console.WriteLine("--------------");
            
            }
            Console.WriteLine(Sol.Cost);
            CalculateServiceLevel(Sol);
            //RouteCustomersToSharedLocations();
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
                    Route newRoute = new(Sol.Routes.Count, cap, 100000, depot);
                    Sol.Routes.Add(newRoute);
                    Sol.Cost += newRoute.Cost;
                }
                else
                {
                    if (Sol.Routes.Last().SequenceOfLocations.Count > 2)
                    {
                        Route newRoute = new(Sol.Routes.Count, cap, 100000, depot);
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
                        writetext.WriteLine(rt.SequenceOfLocations[j] + " " + rt.SequenceOfOptions[j - 1] + " " + rt.SequenceOfCustomers[j - 1] + "\n");
                    }
                }
            }
            writetext.Close();
        }
        public double[] RespectsTimeWindow(Route rt, int loc, Location l)
        {
            // loc: the position to be after
            double lat = Math.Min(rt.SequenceOfLat[loc+1] - CalculateTime(l, rt.SequenceOfLocations[loc+1]) - l.ServiceTime,l.Due - l.ServiceTime);
            double ect = Math.Max(rt.SequenceOfEct[loc] + CalculateTime(rt.SequenceOfLocations[loc], l) + l.ServiceTime, l.Ready + l.ServiceTime);
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

        void IdentifyMinimumCostInsertion(CustomerInsertionAllPositions bestInsertion)
        {
            //goodoptions list
            for (int i = 0; i < options.Count ; i++)
            {
                candidateOpt = options[i];
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
                                            bestInsertion.InsertionPosition = j;
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



        void UpdateTimes(Route rt, int loc) 
        {
            for (int i = loc; i < rt.SequenceOfLocations.Count; i++)
            {
                rt.SequenceOfEct[i] = Math.Max(rt.SequenceOfLocations[i].Ready + rt.SequenceOfLocations[i].ServiceTime,
                                               rt.SequenceOfEct[i - 1] + CalculateTime(rt.SequenceOfLocations[i], rt.SequenceOfLocations[i - 1])
                                               + rt.SequenceOfLocations[i].ServiceTime);
            }

            for (int j = loc; j > -1; j--)
            {
                rt.SequenceOfLat[j] = Math.Min(rt.SequenceOfLocations[j].Due - rt.SequenceOfLocations[j].ServiceTime,
                                               rt.SequenceOfLat[j + 1] - CalculateTime(rt.SequenceOfLocations[j], rt.SequenceOfLocations[j + 1])
                                               - rt.SequenceOfLocations[j].ServiceTime);
            }
        }

        void ApplyCustomerInsertionAllPositions(CustomerInsertionAllPositions insertion)
        {
            insertion.Route.SequenceOfLocations.Insert(insertion.InsertionPosition + 1, insertion.Location);
            insertion.Route.SequenceOfCustomers.Insert(insertion.InsertionPosition, insertion.Customer);
            insertion.Route.SequenceOfOptions.Insert(insertion.InsertionPosition, insertion.Option);
            insertion.Route.Duration += insertion.Duration;
            insertion.Route.Load += insertion.Customer.Dem;
            insertion.Customer.IsRouted = true;
            insertion.Option.IsServed = true;
            Sol.Cost += insertion.Cost;
            Sol.Duration += insertion.Duration;
            insertion.Location.Cap += 1;
            insertion.Route.SequenceOfEct.Insert(insertion.InsertionPosition + 1, insertion.Ect);
            insertion.Route.SequenceOfLat.Insert(insertion.InsertionPosition + 1, insertion.Lat);
            UpdateTimes(insertion.Route, insertion.InsertionPosition + 1);
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
                for (int c = 0; c < sol.Routes[r].SequenceOfOptions.Count; c++)
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
        /*
        List<List<int>> GroupSharedLocations(List<Option> shList)
        {
            List<Option> query = shList.GroupBy(x => x.Location).Select(x => x.First()).ToList();
            double numOfRoutes = Math.Ceiling(query.Count() * 0.8);

            List<List<int>> couples = new();
            for (int i = 0; i < numOfRoutes / 2; i++)
            {
                Console.WriteLine("------------------");
                double minDist = 1000000;
                List<int> couple = new();
                int l1 = -1;
                int l2 = -1;
                int index1 = -1;
                int index2 = -1;
                for (int j = 0; j < query.Count - 1; j++)
                {
                    for (int k = j + 1; k < query.Count; k++)
                    {
                        Console.WriteLine(FindMatrix(query[j].Location.Id, query[j + 1].Location.Id));
                        if (FindMatrix(query[j].Location.Id, query[k].Location.Id) < minDist)
                        {
                            Console.WriteLine("{0} {1} {2}", query[j].Location, query[k].Location, FindMatrix(query[j].Location.Id, query[k].Location.Id));
                            minDist = FindMatrix(query[j].Location.Id, query[k].Location.Id);
                            l1 = query[j].Location.Id;
                            l2 = query[k].Location.Id;
                            index1 = j;
                            index2 = k;
                        }
                    }
                }
                Console.WriteLine("{0} {1} {2}", query[index1].Location, query[index2].Location, FindMatrix(query[index1].Location.Id, query[index2].Location.Id));
                couple.Add(l1);
                couple.Add(l2);
                query.RemoveAt(index1);
                query.RemoveAt(index2 - 1);
                couples.Add(couple);
            }
            return couples;
        }
        
        void RouteCustomersToSharedLocations()
        {
            List<Option> sharedLocationsOptions = options.Where(x => x.ServiceTime == 2)
                                                  .OrderByDescending(p => FindMatrix(depot.Id, p.Cust.Id))
                                                  .ThenBy(p => p.Prio)
                                                  .ToList();

            IEnumerable<Option> query = sharedLocationsOptions.GroupBy(x => x.Cust).Select(x => x.First());
            List<List<int>> sharedLocationGroups = GroupSharedLocations(query.ToList());
            double costOfInsertions = 0;
            for (int i = 0; i < sharedLocationGroups.Count; i++)
            {
                Route newRoute = new(sol.Routes.Count, cap, 100000, depot);
                for (int j = 0; j < sharedLocationGroups[i].Count; j++)
                {
                    newRoute.SequenceOfNodes.Insert(newRoute.SequenceOfNodes.Count - 2,
                                            allNodes[sharedLocationGroups[i][j]]);
                }
                costOfInsertions += FindMatrix(sharedLocationGroups[i][0], depot.Id)
                                    + FindMatrix(sharedLocationGroups[i][1], depot.Id)
                                    +FindMatrix(sharedLocationGroups[i][0], sharedLocationGroups[i][1]);
                newRoute.Cost += costOfInsertions;
                sol.Routes.Add(newRoute);
                sol.Cost += newRoute.Cost;
            }
        }*/
    }
}
