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
        InstanceReader ir = new InstanceReader();
        CustomerInsertionAllPositions bestInsertion;
        Solution sol;


        public void solve()
        {
            setRoutedToFalse(ir.Customers);
            minimumInsertions();
            Console.WriteLine(sol.Routes.Length);
        }

        void setRoutedToFalse(List<Node> nodes)
        {
            foreach(Node node1 in nodes)
            {
                node1.IsRouted = false;
            }
        }

        void alwaysKeepAnEmptyRoute()
        {
            if (sol.Routes.Length < 10)
            {
                if (sol.Routes.Length == 0)
                {
                    Route newRoute = new Route(ir.Cap, 1000, ir.Storage);
                    sol.Routes.Append(newRoute);
                    sol.Cost += newRoute.Cost;
                }
                else
                {
                    if (sol.Routes.Last().SequenceOfNodes.Count > 2)
                    {
                        Route newRoute = new Route(ir.Cap, 1000, ir.Storage);
                        sol.Routes.Append(newRoute);
                        sol.Cost += newRoute.Cost;
                    }
                }
            }
        }

        double calculateDistance(Node n1, Node n2)
        {
            if (n1.Id > n2.Id)
            {
                return ir.Matrix[n2.Id, n1.Id - n2.Id];
            } else
            {
                return ir.Matrix[n1.Id, n2.Id - n1.Id];
            }
        }

        Node candidateCust, A, B;
        double timeAdded, timeRemoved, trialTime;  
        void IdentifyMinimumCostInsertion(CustomerInsertionAllPositions bestInsertion)
        {
            for (int i = 0; i < ir.Customers.Count ; i++)
            {
                candidateCust = ir.Customers[i];
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
                                timeAdded = calculateDistance(A, candidateCust) + calculateDistance(candidateCust, B);
                                timeRemoved = calculateDistance(A, B);
                                trialTime = timeAdded - timeRemoved;
                                if (trialTime < bestInsertion.Duration & rt.Duration + trialTime + candidateCust.ServiceTime <= rt.MaxDuration)
                                {
                                    bestInsertion.Customer = candidateCust;
                                    bestInsertion.Route = rt;
                                    bestInsertion.InsertionPosition = j;
                                    bestInsertion.Duration = trialTime + candidateCust.ServiceTime;
                                    bestInsertion.Cost = Math.Ceiling(calculateDistance(A, B) * 10);
                                }
                            }
                        }
                        else
                        {
                            continue;
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

        void minimumInsertions()
        {
            bool modelIsFeasible = true;
            sol = new Solution();
            while(! ir.Customers.All(x => x.IsRouted))
            {
                bestInsertion = new CustomerInsertionAllPositions();
                alwaysKeepAnEmptyRoute();
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
    }
}
