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
        private List<Customer> sequenceOfCustomers = new();
        private List<Location> sequenceOfLocations = new();
        private List<Option> sequenceOfOptions = new();
        private double load;
        private double capacity;
        private double duration;
        private double maxDuration;
        private double cost;
        private double fixedCost;
        private int[] sequenceOfStartingTime;
        private int[] sequenceOfEndingTime;
        private List<double> sequenceOfEct = new();
        private List<double> sequenceOfLat = new();

        public Route(int id, double capacity, double maxDuration, Location storage)
        {
            this.sequenceOfLocations.Add(storage);
            this.sequenceOfLocations.Add(storage);
            this.load = 0;
            this.capacity = capacity;
            this.duration = 0;
            this.maxDuration = maxDuration;
            this.fixedCost = 1000000;
            this.cost = fixedCost;
            this.Id = id;
            this.sequenceOfEct.Add(0);
            this.sequenceOfEct.Add(0);
            this.sequenceOfLat.Add(7200);
            this.sequenceOfLat.Add(7200);

        }

        public int Id { get => id; set => id = value; }
        public double Load { get => load; set => load = value; }
        public double Capacity { get => capacity; set => capacity = value; }
        public double Duration { get => duration; set => duration = value; }
        public double MaxDuration { get => maxDuration; set => maxDuration = value; }
        public double Cost { get => cost; set => cost = value; }
        public double FixedCost { get => fixedCost; set => fixedCost = value; }
        public int[] SequenceOfStartingTime { get => sequenceOfStartingTime; set => sequenceOfStartingTime = value; }
        public int[] SequenceOfEndingTime { get => sequenceOfEndingTime; set => sequenceOfEndingTime = value; }
        public List<double> SequenceOfEct { get => sequenceOfEct; set => sequenceOfEct = value; }
        public List<double> SequenceOfLat { get => sequenceOfLat; set => sequenceOfLat = value; }
        internal List<Option> SequenceOfOptions{ get => sequenceOfOptions; set => sequenceOfOptions = value; }
        internal List<Location> SequenceOfLocations { get => sequenceOfLocations; set => sequenceOfLocations = value; }
        internal List<Customer> SequenceOfCustomers { get => sequenceOfCustomers; set => sequenceOfCustomers = value; }
    }
}
