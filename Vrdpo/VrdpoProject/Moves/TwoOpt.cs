namespace VrdpoProject
{
    // Best 2-opt found in one iteration: segment reversal inside a route, or tail exchange between two routes.
    public class TwoOpt
    {
        public TwoOpt()
        {
            ReinitializeVariables();
        }

        public void ReinitializeVariables()
        {
            PositionOfFirstRoute = -1;
            PositionOfSecondRoute = -1;
            PositionOfFirstOption = -1;
            PositionOfSecondOption = -1;
            TotalCost = Math.Pow(10, 9);
            MoveCost = Math.Pow(10, 9);
        }

        public bool IsValid()
        {
            return PositionOfFirstRoute != -1;
        }

        public int PositionOfFirstRoute { get; set; }
        public int PositionOfSecondRoute { get; set; }
        public int PositionOfFirstOption { get; set; }
        public int PositionOfSecondOption { get; set; }
        public double MoveCost { get; set; }
        public double TotalCost { get; set; }
        // Time arrays of the two resulting routes (inter-route moves only).
        public double[] Ect1 { get; set; }
        public double[] Ect2 { get; set; }
        public double[] Lat1 { get; set; }
        public double[] Lat2 { get; set; }
    }
}
