namespace VrdpoProject
{
    // A physical place: depot (Type 0), shared location such as a locker (Type 1, limited by MaxCap) or
    // private address (Type 2). Times are stored x10.
    public class Location
    {
        public Location(int id, int xx, int yy, int maxCap, int ready, int due, int type, int serviceTime, int deliveryServiceTime, int cap = 0)
        {
            Id = id;
            Xx = xx;
            Yy = yy;
            MaxCap = maxCap;
            Ready = ready;
            Due = due;
            Type = type;
            ServiceTime = serviceTime;
            DeliveryServiceTime = deliveryServiceTime;
            Cap = cap;
        }

        public Location Clone()
        {
            return new Location(Id, Xx, Yy, MaxCap, Ready, Due, Type, ServiceTime, DeliveryServiceTime, Cap);
        }

        public int Id { get; set; }
        public int Xx { get; set; }
        public int Yy { get; set; }
        public int MaxCap { get; set; }
        public int Ready { get; set; }
        public int Due { get; set; }
        public int Type { get; set; }
        // Parking/handling time paid once per visit of the location (not again for consecutive stops at the same one).
        public int ServiceTime { get; set; }
        // Number of served options currently assigned to this location (shared-location capacity use).
        public int Cap { get; set; }
        // Per-delivery duration.
        public int DeliveryServiceTime { get; set; }
    }
}
