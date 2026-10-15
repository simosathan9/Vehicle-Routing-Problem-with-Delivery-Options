namespace VrdpoProject
{
    // A vehicle route: depot, stops, depot. The three sequences (options, locations, customers) run in
    // parallel; SequenceOfEct/SequenceOfLat hold the earliest completion / latest completion time per stop
    // (see Solution.UpdateTimes).
    public class Route
    {
        public const int DepotCustomerId = 1000;
        public const double DepotLatestTime = 7200;

        public Route(int id, double capacity, Location depot, int depotOptionId)
        {
            var depotCustomer = new Customer(DepotCustomerId, 0, true);
            var depotOption = new Option(depotOptionId, depot, depotCustomer, 0, 0, 0) { IsServed = true };
            Id = id;
            Capacity = capacity;
            SequenceOfLocations.Add(depot);
            SequenceOfLocations.Add(depot);
            SequenceOfCustomers.Add(depotCustomer);
            SequenceOfCustomers.Add(depotCustomer);
            SequenceOfOptions.Add(depotOption);
            SequenceOfOptions.Add(depotOption);
            SequenceOfEct.Add(0);
            SequenceOfEct.Add(0);
            SequenceOfLat.Add(DepotLatestTime);
            SequenceOfLat.Add(DepotLatestTime);
        }

        // Shallow copy: new lists, same elements.
        public Route(Route original)
        {
            Id = original.Id;
            Capacity = original.Capacity;
            SequenceOfLocations = new List<Location>(original.SequenceOfLocations);
            SequenceOfCustomers = new List<Customer>(original.SequenceOfCustomers);
            SequenceOfOptions = new List<Option>(original.SequenceOfOptions);
            Load = original.Load;
            Cost = original.Cost;
            SequenceOfEct = new List<double>(original.SequenceOfEct);
            SequenceOfLat = new List<double>(original.SequenceOfLat);
            RouteUtilizationMetric = original.RouteUtilizationMetric;
        }

        public int Id { get; set; }
        public double Load { get; set; }
        public double Capacity { get; set; }
        public double Cost { get; set; }
        public List<double> SequenceOfEct { get; set; } = new();
        public List<double> SequenceOfLat { get; set; } = new();
        public List<Option> SequenceOfOptions { get; set; } = new();
        public List<Location> SequenceOfLocations { get; set; } = new();
        public List<Customer> SequenceOfCustomers { get; set; } = new();
        // (Capacity - Load)^2; the sum over the routes steers moves towards consolidated loads.
        public double RouteUtilizationMetric { get; set; }

        public bool IsEmpty => SequenceOfLocations.Count == 2;
    }
}
