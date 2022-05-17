using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VrdpoProject
{ 

    internal class CustomerInsertionAllPositions
    {
        private Node customer;
        private Route route;
        private int insertionPosition;
        private double cost;
        private double duration;
        private Option option;

        public CustomerInsertionAllPositions(Node customer, Route route, int insertionPosition, double cost, double duration, Option option)
        {
            this.customer = customer;
            this.route = route;
            this.insertionPosition = insertionPosition;
            this.cost = cost;
            this.duration = duration;
            this.option = option;
        }

        public CustomerInsertionAllPositions()
        {
            this.customer = null;
            this.route = null;
            this.insertionPosition = -1000;
            this.cost = 1000000;    
            this.duration = 10000000;
            this.option = null;
        }

        public int InsertionPosition { get => insertionPosition; set => insertionPosition = value; }
        public double Cost { get => cost; set => cost = value; }
        public double Duration { get => duration; set => duration = value; }
        internal Node Customer { get => customer; set => customer = value; }
        internal Route Route { get => route; set => route = value; }
        internal Option Option { get => option; set => option = value; }
    }
}
