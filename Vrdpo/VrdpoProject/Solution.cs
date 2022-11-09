using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VrdpoProject
{

    public class Solution
    {
        private double duration;
        private double cost;
        private List<Route> routes;
        //priority
        public Solution()
        {
            this.duration = 0;
            this.cost = 0;
            this.routes = new List<Route>();
        }

        public double Duration { get => duration; set => duration = value; }
        public double Cost { get => cost; set => cost = value; }
        internal List<Route> Routes { get => routes; set => routes = value; }
    }
    //Checkverything bool
    //objective
    //customers served
    //location?
    //time windows
    //priority
    //max routes
    //depot first last
}

