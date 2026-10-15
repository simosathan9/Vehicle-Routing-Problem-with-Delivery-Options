namespace VrdpoProject
{
    // Best swap found in one iteration: two stops exchange positions (same or other route).
    public class Swap
    {
        public Swap()
        {
            ReinitializeVariables();
        }

        public void ReinitializeVariables()
        {
            PositionOfFirstRoute = -1;
            PositionOfSecondRoute = -1;
            PositionOfFirstOption = -1;
            PositionOfSecondOption = -1;
            CostChangeFirstRt = -1;
            CostChangeSecondRt = -1;
            MoveCost = Math.Pow(10, 9);
            TotalCost = Math.Pow(10, 9);
        }

        public bool IsValid()
        {
            return PositionOfFirstRoute != -1;
        }

        public int PositionOfFirstRoute { get; set; }
        public int PositionOfSecondRoute { get; set; }
        public int PositionOfFirstOption { get; set; }
        public int PositionOfSecondOption { get; set; }
        public double CostChangeFirstRt { get; set; }
        public double CostChangeSecondRt { get; set; }
        public double MoveCost { get; set; }
        public double TotalCost { get; set; }
    }
}
