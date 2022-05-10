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
        CustomerInsertionAllPositions bestInsertion = null;
        Route rt = null;
        List<Route> routes = new List<Route>();
        void setRoutedToFalse(Node[] nodes)
        {
            foreach(Node node in nodes)
            {
                node.IsRouted = false;
            }
        }

        void alwaysKeepAnEmptyRoute()
        {
            if (routes.Count < 10)
            {
                if (routes.Count == 0)
                {
                    routes.Add(new Route(ir.Cap, 1000, ir.Storage));
                }
                else
                {
                    if (routes.Last().SequenceOfNodes.Length > 2)
                    {
                        routes.Add(new Route(ir.Cap, 1000, ir.Storage));
                    }
                }
            }
        }

        double calculateDistance(Node n1, Node n2)
        {
            if (n1.Id > n2.Id)
            {
                return ir.Matrix[n2.Id][n1.Id - n2.Id];
            } else
            {
                return ir.Matrix[n1.Id][n2.Id - n1.Id];
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
                    foreach (Route rt in routes)
                    {
                        if (rt.Load + candidateCust.Dem <= rt.Capacity & rt.Duration + candidateCust.ServiceTime <= rt.MaxDuration)
                        {
                            for (int j = 0; j < rt.SequenceOfNodes.Length - 1; j++)
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

        void minimumInsertions()
        {
            bool modelIsFeasible = true;
            Solution solution = new Solution();
            while(! ir.Customers.All(x => x.IsRouted))
            {
                bestInsertion = new CustomerInsertionAllPositions();
                alwaysKeepAnEmptyRoute();
                IdentifyMinimumCostInsertion(bestInsertion);
               
            }
        }
    }
}
