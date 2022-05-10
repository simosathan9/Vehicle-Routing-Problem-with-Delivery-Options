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

        void minimumInsertions()
        {
            bool modelIsFeasible = true;
            Solution solution = new Solution();
            while(! ir.Customers.All(x => x.IsRouted ))
            {
                //bestInsertion = new CustomerInsertionAllPositions();
                alwaysKeepAnEmptyRoute();

            }
        }
    }
}
