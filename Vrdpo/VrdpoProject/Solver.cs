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
                return distanceMatrix[id2, id1 - id2];
            }
            else
            {
                return distanceMatrix[id1, id2 - id1];
            }
        }

        Option candidateOpt;
        Location A, B;
        double timeAdded, timeRemoved, trialTime;
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
                                trialTime = timeAdded - timeRemoved + candidateOpt.Location.ServiceTime;
                                //if respects time window(Rt rt, j, trialtime)
                                // j - 1 take earliest completion time
                                // j + 1 take latest arrival time
                                // return (ect + trialtime <= lat) (if true feasible)
                                if (rt.Duration + trialTime <= rt.MaxDuration & trialCost < bestInsertion.Cost)
                                {
                                    //decide starting time = max(starting time allowed, time of arrival)
                                    //if starting time + service time  <= finishing time allowed
                                    //calculate time windows for the next customers of route
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
            //update sequences of time(Rt rt)
            //ect = max(arrival time, starting time window) + service time
            //lat = min(due time window next + travel time, due time window - service time)
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
