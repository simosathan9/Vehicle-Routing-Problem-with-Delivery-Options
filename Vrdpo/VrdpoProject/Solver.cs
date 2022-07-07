using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Linq;


namespace VrdpoProject
{
    internal class Solver
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
            this.distanceMatrix = ir.DistanceMatrix;
            this.timeMatrix = ir.TimeMatrix;
            this.cap = ir.Cap;
            this.depot = ir.Depot;
            this.options = ir.Options;
        }

        public void Solve()
        {
            SetRoutedToFalse(customers);
            MinimumInsertions();
           foreach (Route r in sol.Routes)
           {
                for (int i = 0; i < r.SequenceOfOptions.Count; i++)
                {
                    Console.WriteLine("{0} {1}", r.SequenceOfOptions[i].Location.Id, r.SequenceOfOptions[i].Cust.Id);
                }
                Console.WriteLine("Max capacity: {0} Load:{1}", r.Capacity, r.Load);
                Console.WriteLine("Max duration: {0} Duration:{1}", r.MaxDuration, r.Duration);
                Console.WriteLine("--------------");

            }
            Console.WriteLine(sol.Cost);
           //Console.WriteLine();
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
            if (sol.Routes.Count < 10)
            {
                if (sol.Routes.Count == 0)
                {
                    Route newRoute = new(sol.Routes.Count, cap, 100000, depot);
                    sol.Routes.Add(newRoute);
                    sol.Cost += newRoute.Cost;
                }
                else
                {
                    if (sol.Routes.Last().SequenceOfLocations.Count > 2)
                    {
                        Route newRoute = new(sol.Routes.Count, cap, 100000, depot);
                        sol.Routes.Add(newRoute);
                        sol.Cost += newRoute.Cost;
                    }
                }
            }
        }

        double CalculateDistance(Location n1, Location n2)
        {
            if (n1.Id > n2.Id)
            {
                return distanceMatrix[n2.Id, n1.Id - n2.Id];
            } else
            {
                return distanceMatrix[n1.Id, n2.Id - n1.Id];
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

        /**bool RespectsTimeWindow(Route rt, int loc, Location l)
        {
            double lat = Math.Min(l.Due - l.ServiceTime, rt.SequenceOfLat[loc + 1] - CalculateDistance(l, rt.SequenceOfLocations[loc + 1]) - l.ServiceTime);

            double ect = Math.Max(l.Ready + l.ServiceTime, rt.SequenceOfEct[loc] + CalculateDistance(rt.SequenceOfLocations[loc], l) + l.ServiceTime);
            return (ect <= lat);
        }
        **/
        bool RespectsTimeWindow(Route rt, int loc, Location l)
        {
            double lat = Math.Min(rt.SequenceOfLat[loc+1] - CalculateDistance(l, rt.SequenceOfLocations[loc+1]) - l.ServiceTime,l.Due - l.ServiceTime);//+1???
            double ect = Math.Max(rt.SequenceOfEct[loc] + CalculateDistance(rt.SequenceOfLocations[loc], l) + l.ServiceTime, l.Ready + l.ServiceTime) ;

            return (ect <= lat);
        }
        

        //void UpdateTimeWindows

        Option candidateOpt;
        Location A, B;
        double timeAdded, timeRemoved, trialTime, startingTime;
        double costAdded, costRemoved, trialCost;

        void IdentifyMinimumCostInsertion(CustomerInsertionAllPositions bestInsertion)
        {
            //goodoptions list
            for (int i = 0; i < options.Count ; i++)
            {
                candidateOpt = options[i];
                if (candidateOpt.Cust.IsRouted == false & candidateOpt.IsServed == false)
                {
                    foreach (Route rt in sol.Routes)
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
                                if (RespectsTimeWindow(rt, j, candidateOpt.Location)) { 

                                    if (rt.Duration + trialTime <= rt.MaxDuration & trialCost < bestInsertion.Cost &
                                        candidateOpt.Location.Cap < candidateOpt.Location.MaxCap)
                                    {
                                        bestInsertion.Option = candidateOpt;
                                        bestInsertion.Customer = candidateOpt.Cust;
                                        bestInsertion.Location = candidateOpt.Location;
                                        bestInsertion.Route = rt;
                                        bestInsertion.InsertionPosition = j;
                                        bestInsertion.Duration = trialTime;
                                        bestInsertion.Cost = trialCost;
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
                                               rt.SequenceOfEct[i - 1] + FindMatrix(i - 1, i) + rt.SequenceOfLocations[i].ServiceTime);
            }

            for (int j = loc; j > 0; j--)
            {
                rt.SequenceOfLat[j] = Math.Min(rt.SequenceOfLocations[j].Due - rt.SequenceOfLocations[j].ServiceTime,
                                               rt.SequenceOfLat[j + 1] - FindMatrix(j, j + 1) - rt.SequenceOfLocations[j].ServiceTime);
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
            sol.Cost += insertion.Cost;
            sol.Duration += insertion.Duration;
            insertion.Location.Cap += 1;
            double tempEct = Math.Max(insertion.Route.SequenceOfLocations[insertion.InsertionPosition + 1].Ready + insertion.Route.SequenceOfLocations[insertion.InsertionPosition + 1].ServiceTime,
                                      insertion.Route.SequenceOfEct[insertion.InsertionPosition] + FindMatrix(insertion.InsertionPosition, insertion.InsertionPosition + 1) + insertion.Route.SequenceOfLocations[insertion.InsertionPosition + 1].ServiceTime);
            double tempLat = Math.Min(insertion.Route.SequenceOfLocations[insertion.InsertionPosition + 1].Due - insertion.Route.SequenceOfLocations[insertion.InsertionPosition + 1].ServiceTime,
                                      insertion.Route.SequenceOfLat[insertion.InsertionPosition + 1] - FindMatrix(insertion.InsertionPosition + 1, insertion.InsertionPosition + 2) - insertion.Route.SequenceOfLocations[insertion.InsertionPosition + 1].ServiceTime);
            insertion.Route.SequenceOfEct.Insert(insertion.InsertionPosition + 1, tempEct);
            insertion.Route.SequenceOfLat.Insert(insertion.InsertionPosition + 1, tempLat);
            UpdateTimes(insertion.Route, insertion.InsertionPosition + 1);
            //
            //update sequences of time(Rt rt)
            //for i to n
            //ect = max(arrival time, starting time window) + service time
            //for n to i
            //lat = min(ending time previous + travel time, due time window - service time)
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
