using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VrdpoProject
{
    internal class Start
    {
        static void Main()
        {
            //InstanceReader model = new();
            //model.BuildModel();
            Solver solver = new();
            solver.Solve();
        }
    }
}
