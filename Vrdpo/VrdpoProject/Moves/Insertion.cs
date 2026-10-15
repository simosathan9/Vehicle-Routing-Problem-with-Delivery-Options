namespace VrdpoProject
{
    // Candidate insertion of an option into a route during construction.
    public class Insertion
    {
        public Insertion()
        {
            Customer = null;
            Route = null;
            InsertionPosition = -1000;
            Cost = 1000000;
            Option = null;
        }

        public Insertion(Insertion original)
        {
            Customer = original.Customer;
            Route = original.Route;
            InsertionPosition = original.InsertionPosition;
            Cost = original.Cost;
            Option = original.Option;
            Location = original.Location;
        }

        public int InsertionPosition { get; set; }
        public double Cost { get; set; }
        public Customer Customer { get; set; }
        public Route Route { get; set; }
        public Option Option { get; set; }
        public Location Location { get; set; }
    }
}
