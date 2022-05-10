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
        private Node[] sequenceOfNodes;
        private int load;
        private int capacity;
        private int duration;
        private int maxDuration;

        public Route(int capacity, int maxDuration, Node storage)
        {
            this.sequenceOfNodes.Append(storage);
            this.sequenceOfNodes.Append(storage);
            this.load = 0;
            this.capacity = capacity;
            this.duration = 0;
            this.maxDuration = maxDuration;
        }

        public int Id { get => id; set => id = value; }
        public int Load { get => load; set => load = value; }
        public int Capacity { get => capacity; set => capacity = value; }
        public int Duration { get => duration; set => duration = value; }
        public int MaxDuration { get => maxDuration; set => maxDuration = value; }
        internal Node[] SequenceOfNodes { get => sequenceOfNodes; set => sequenceOfNodes = value; }
    }
}
