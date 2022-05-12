using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VrdpoProject
{
    internal class Route
    {
        private int id;
        private List<Node> sequenceOfNodes;
        private double load;
        private double capacity;
        private double duration;
        private double maxDuration;
        private double cost;
        private double fixedCost;

        public Route(double capacity, double maxDuration, Node storage)
        {
            this.sequenceOfNodes.Add(storage);
            this.sequenceOfNodes.Add(storage);
            this.load = 0;
            this.capacity = capacity;
            this.duration = 0;
            this.maxDuration = maxDuration;
            this.fixedCost = 1000000;
            this.cost = fixedCost;
        }

        public int Id { get => id; set => id = value; }
        public double Load { get => load; set => load = value; }
        public double Capacity { get => capacity; set => capacity = value; }
        public double Duration { get => duration; set => duration = value; }
        public double MaxDuration { get => maxDuration; set => maxDuration = value; }
        public double Cost { get => cost; set => cost = value; }
        public double FixedCost { get => fixedCost; set => fixedCost = value; }
        internal List<Node> SequenceOfNodes { get => sequenceOfNodes; set => sequenceOfNodes = value; }
    }
}
