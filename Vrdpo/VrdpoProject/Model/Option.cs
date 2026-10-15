namespace VrdpoProject
{
    // A delivery option of a customer: a location with a priority (0 = preferred). Exactly one option
    // per customer is served.
    public class Option
    {
        public Option(int id, Location location, Customer cust, int prio, int serviceTime, int cost)
        {
            Id = id;
            Location = location;
            Cust = cust;
            Prio = prio;
            ServiceTime = serviceTime * 10;
            Cost = cost;
            IsServed = false;
        }

        // Shallow copy pointing at the given (cloned) location; the caller re-points Cust afterwards.
        public Option Clone(Location clonedLocation)
        {
            var clone = (Option)MemberwiseClone();
            clone.Location = clonedLocation;
            return clone;
        }

        public int Id { get; set; }
        public Location Location { get; set; }
        public Customer Cust { get; set; }
        public int Prio { get; set; }
        public int ServiceTime { get; set; }
        public int Cost { get; set; }
        public bool IsServed { get; set; }
        // False for the options a customer may not use (settings.lockToHome); true otherwise.
        public bool Allowed { get; set; } = true;
    }
}
