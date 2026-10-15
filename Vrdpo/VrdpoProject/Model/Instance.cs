namespace VrdpoProject
{
    // One parsed instance file. Immutable; BuildModel() creates a fresh, mutable object graph
    // (locations, customers, options) for every Solution, since Location.Cap, Customer.IsRouted and
    // Option.IsServed change during the search.
    public sealed class Instance
    {
        private readonly int[] depotRow;
        private readonly int[][] customerRows;
        private readonly int[][] locationRows;
        private readonly int[][] optionRows;

        public string Path { get; }
        public string Name { get; }
        // Path below the Instances/ folder without extension, e.g. "U/50/U_50_1"; used to place the outputs.
        public string RelativeName { get; }
        public int VehicleCapacity { get; }
        public double[,] DistanceMatrix { get; }
        public double[,] TimeMatrix { get; }
        public int CustomerCount => customerRows.Length;
        public int OptionCount => optionRows.Length;

        internal Instance(string path, int capacity, int[] depotRow, int[][] customerRows, int[][] locationRows, int[][] optionRows,
            double[,] distanceMatrix, double[,] timeMatrix)
        {
            Path = path;
            Name = System.IO.Path.GetFileNameWithoutExtension(path);
            RelativeName = ComputeRelativeName(path);
            VehicleCapacity = capacity;
            this.depotRow = depotRow;
            this.customerRows = customerRows;
            this.locationRows = locationRows;
            this.optionRows = optionRows;
            DistanceMatrix = distanceMatrix;
            TimeMatrix = timeMatrix;
        }

        public InstanceModel BuildModel()
        {
            // Depot: ready/due are stored x10 like every other time; its service time is used as given.
            int[] d = depotRow;
            var depot = new Location(d[0], d[1], d[2], d[3], 10 * d[4], 10 * d[5], d[6], d[7], 0);
            var locations = new List<Location> { depot };
            var customers = new List<Customer>();
            var options = new List<Option>();

            foreach (int[] row in customerRows)
            {
                customers.Add(new Customer(row[0], row[1], false));
            }

            foreach (int[] row in locationRows)
            {
                int type = row[6];
                // Delivery duration: 2 min at a shared location (type 1), 5 min at a private one; times are x10.
                var loc = new Location(row[0], row[1], row[2], row[3], 10 * row[4], 10 * row[5], type, 10 * row[7], type == 1 ? 20 : 50);
                // The time window bounds the start of the delivery, so the delivery duration is added to the due date.
                loc.Due += loc.DeliveryServiceTime;
                locations.Add(loc);
            }

            foreach (int[] row in optionRows)
            {
                var opt = new Option(row[0], locations[row[1]], customers[row[2]], row[3], row[4], row[5]);
                options.Add(opt);
                customers[row[2]].Options.Add(opt);
            }

            return new InstanceModel(depot, locations, customers, options);
        }

        private static string ComputeRelativeName(string path)
        {
            string full = System.IO.Path.GetFullPath(path).Replace('\\', '/');
            string[] parts = full.Split('/');
            int at = Array.FindLastIndex(parts, p => string.Equals(p, "Instances", StringComparison.OrdinalIgnoreCase));
            string rel = at >= 0 && at < parts.Length - 1
                ? string.Join('/', parts, at + 1, parts.Length - at - 1)
                : parts[^1];
            return rel.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ? rel[..^4] : rel;
        }
    }

    public sealed class InstanceModel
    {
        public Location Depot { get; }
        public List<Location> Locations { get; }
        public List<Customer> Customers { get; }
        public List<Option> Options { get; }

        public InstanceModel(Location depot, List<Location> locations, List<Customer> customers, List<Option> options)
        {
            Depot = depot;
            Locations = locations;
            Customers = customers;
            Options = options;
        }
    }
}
