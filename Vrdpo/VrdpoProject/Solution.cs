using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VrdpoProject
{

    internal class Solution
    {
        private double duration;
        private double cost;
        private Route[] routes;

        public Solution()
        {
            this.duration = 0;
            this.cost = 0;
            this.routes = Array.Empty<Route>();
        }

        public double Duration { get => duration; set => duration = value; }
        public double Cost { get => cost; set => cost = value; }
        internal Route[] Routes { get => routes; set => routes = value; }
    }
}

