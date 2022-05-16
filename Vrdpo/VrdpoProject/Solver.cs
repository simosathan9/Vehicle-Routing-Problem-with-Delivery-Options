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
        private List<Node> allNodes = new();
        private List<Node> customers = new();
        private double[,] matrix;
        private int cap;
        private Node storage;
        private List<Option> options = new();

        public Solver(InstanceReader ir)
        {
            this.allNodes = ir.AllNodes;
            this.customers = ir.Customers;
            this.matrix = ir.Matrix;
            this.cap = ir.Cap;
            this.storage = ir.Storage;
            this.options = ir.Options;
        }

        public void Solve()
        {
            SetRoutedToFalse(customers);
            MinimumInsertions();
           foreach (Route r in sol.Routes)
           {
                for (int i = 0; i < r.SequenceOfNodes.Count; i++)
                {
                    Console.WriteLine(r.SequenceOfNodes[i].Id);
                }
                Console.WriteLine("Max capacity: {0} Load:{1}", r.Capacity, r.Load);
                Console.WriteLine("Max duration: {0} Duration:{1}", r.MaxDuration, r.Duration);
                Console.WriteLine("--------------");
           }
           Console.WriteLine(sol.Cost);
           Console.WriteLine();
           RouteCustomersToSharedLocations();
        }

        void SetRoutedToFalse(List<Node> nodes)
        {
            foreach(Node node1 in nodes)
            {
                node1.IsRouted = false;
            }
        }

        void AlwaysKeepAnEmptyRoute()
        {
            if (sol.Routes.Count < 10)
            {
                if (sol.Routes.Count == 0)
                {
                    Route newRoute = new(sol.Routes.Count, cap, 100000, storage);
                    sol.Routes.Add(newRoute);
                    sol.Cost += newRoute.Cost;
                }
                else
                {
                    if (sol.Routes.Last().SequenceOfNodes.Count > 2)
                    {
                        Route newRoute = new(sol.Routes.Count, cap, 100000, storage);
                        sol.Routes.Add(newRoute);
                        sol.Cost += newRoute.Cost;
                    }
                }
            }
        }

        double CalculateDistance(Node n1, Node n2)
        {
            if (n1.Id > n2.Id)
            {
                return matrix[n2.Id, n1.Id - n2.Id];
            } else
            {
                return matrix[n1.Id, n2.Id - n1.Id];
            }
        }

        Node candidateCust, A, B;
        double timeAdded, timeRemoved, trialTime;

        void IdentifyMinimumCostInsertion(CustomerInsertionAllPositions bestInsertion)
        {
            for (int i = 0; i < customers.Count ; i++)
            {
                candidateCust = customers[i];
                if (candidateCust.IsRouted == false)
                {
                    foreach (Route rt in sol.Routes)
                    {
                        if (rt.Load + candidateCust.Dem <= rt.Capacity & rt.Duration + candidateCust.ServiceTime <= rt.MaxDuration)
                        {
                            for (int j = 0; j < rt.SequenceOfNodes.Count - 1; j++)
                            {
                                A = rt.SequenceOfNodes[j];
                                B = rt.SequenceOfNodes[j + 1];
                                timeAdded = CalculateDistance(A, candidateCust) + CalculateDistance(candidateCust, B);
                                timeRemoved = CalculateDistance(A, B);
                                trialTime = timeAdded - timeRemoved + candidateCust.ServiceTime;
                                //if tria
                                if (trialTime < bestInsertion.Duration & rt.Duration + trialTime <= rt.MaxDuration)
                                {
                                    Console.WriteLine("{0} {1}", trialTime, bestInsertion.Duration);
                                    bestInsertion.Customer = candidateCust;
                                    bestInsertion.Route = rt;
                                    bestInsertion.InsertionPosition = j;
                                    bestInsertion.Duration = trialTime;
                                    bestInsertion.Cost = Math.Ceiling(CalculateDistance(A, B) * 10);
                                }
                            }
                        }
                    }
                }
            }
        }

        void ApplyCustomerInsertionAllPositions(CustomerInsertionAllPositions insertion)
        {
            insertion.Route.SequenceOfNodes.Insert(insertion.InsertionPosition + 1, insertion.Customer);
            insertion.Route.Duration += insertion.Duration;
            sol.Cost += insertion.Cost;
            sol.Duration += insertion.Duration;
            insertion.Route.Load += insertion.Customer.Dem;
            insertion.Customer.IsRouted = true;
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

        void RouteCustomersToSharedLocations()
        {
            List<Option> sharedLocationsOptions = options.Where(x => x.ServiceTime == 2)
                                                  .OrderByDescending(p => matrix[storage.Id, p.Cust])
                                                  .ThenBy(p => p.Prio)
                                                  .ToList();
            for (int i = 0; i < sharedLocationsOptions.Count; i++)
            {

            }
        }
    }
}
