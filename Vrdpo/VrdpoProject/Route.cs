using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualBasic.FileIO;

namespace VrdpoProject
{
    public class Route
    {
        private InstanceReader ir = new();
        private int id;
        private List<Customer> sequenceOfCustomers = new();
        private List<Location> sequenceOfLocations = new();
        private List<Option> sequenceOfOptions = new();
        private double load;
        private double capacity;
        private double duration;
        private double cost;
        private double fixedCost;
        private int[] sequenceOfStartingTime;
        private int[] sequenceOfEndingTime;
        private List<double> sequenceOfEct = new();
        private List<double> sequenceOfLat = new();
        Customer fakeCustomer = new(1000, 0, true);

        public Route(int id, double capacity, Location storage)
        {
            Location depot = ir.Depot;
            this.sequenceOfLocations.Add(storage);
            this.sequenceOfLocations.Add(storage);
            this.sequenceOfCustomers.Add(fakeCustomer);
            this.sequenceOfCustomers.Add(fakeCustomer);
            Option fakeOpt = new(ir.NumbOpt, storage, fakeCustomer, 0, 0, 0, 0, 7200);
            fakeOpt.IsServed = true;
            this.sequenceOfOptions.Add(fakeOpt);
            this.sequenceOfOptions.Add(fakeOpt);
            this.load = 0;
            this.capacity = capacity;
            this.duration = 0;
            this.fixedCost = 1000000;
            this.cost = 0;
            this.Id = id;
            this.sequenceOfEct.Add(0);
            this.sequenceOfEct.Add(0);
            this.sequenceOfLat.Add(7200);
            this.sequenceOfLat.Add(7200);
        }

        public Route(Route original)
        {
            this.Id = original.Id;
            this.capacity = original.capacity;
            this.sequenceOfLocations = new List<Location>(original.sequenceOfLocations);
            this.sequenceOfCustomers = new List<Customer>(original.sequenceOfCustomers);
            this.sequenceOfOptions = new List<Option>(original.sequenceOfOptions);
            this.load = original.load;
            this.duration = original.duration;
            this.fixedCost = original.fixedCost;
            this.cost = original.cost;
            this.sequenceOfEct = new List<double>(original.sequenceOfEct);
            this.sequenceOfLat = new List<double>(original.sequenceOfLat);
        }

        public object Clone5()
        {
            var clone = new Route(this);
            List<Customer> customersCopy = new List<Customer>();
            List<Location> locationsCopy = new List<Location>();
            List<Option> optionsCopy = new List<Option>();

            //for (int i=0; i < this.sequenceOfCustomers.Count; i++)
            //{
            //    if ((locationsCopy.Select(x => x.Id).ToList()).Contains(sequenceOfLocations[i].Id))
            //    {
            //        Location clonedLocation = locationsCopy.Where(x => x.Id == sequenceOfLocations[i].Id).ToList()[0];
            //        locationsCopy.Add(clonedLocation);
            //    }
            //    else
            //    {
            //        locationsCopy.Add((Location)sequenceOfLocations[i].Clone());
            //    }

            //    Location optionsLoc = locationsCopy.Where(x => x.Id == sequenceOfOptions[i].Location.Id).ToList()[0];
            //    optionsCopy.Add((Option)sequenceOfOptions[i].Clone(optionsLoc));

            //    Option customersOpt = optionsCopy[i];
            //    customersCopy.Add((Customer)SequenceOfCustomers[i].Clone());
            //}
            clone.sequenceOfLocations = locationsCopy;
            clone.sequenceOfCustomers = customersCopy;
            clone.sequenceOfOptions = optionsCopy;
            clone.sequenceOfEct = new List<double>(this.sequenceOfEct);
            clone.sequenceOfLat = new List<double>(this.sequenceOfLat);
            return clone;
        }

        public int Id { get => id; set => id = value; }
        public double Load { get => load; set => load = value; }
        public double Capacity { get => capacity; set => capacity = value; }
        public double Duration { get => duration; set => duration = value; }
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
