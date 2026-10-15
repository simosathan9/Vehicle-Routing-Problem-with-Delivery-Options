namespace VrdpoProject
{
    // Best relocation found in one iteration: one stop moved to another position (same or other route).
    public class Relocation
    {
        public Relocation()
        {
            ReinitializeVariables();
        }

        public void ReinitializeVariables()
        {
            OriginRoutePosition = -1;
            TargetRoutePosition = -1;
            OriginOptionPosition = -1;
            TargetOptionPosition = -1;
            CostChangeOriginRt = -1;
            CostChangeTargetRt = -1;
            TotalCost = Math.Pow(10, 9);
            MoveCost = Math.Pow(10, 9);
        }

        public bool IsValid()
        {
            return OriginRoutePosition != -1;
        }

        public int OriginRoutePosition { get; set; }
        public int TargetRoutePosition { get; set; }
        public int OriginOptionPosition { get; set; }
        public int TargetOptionPosition { get; set; }
        public double CostChangeOriginRt { get; set; }
        public double CostChangeTargetRt { get; set; }
        public double MoveCost { get; set; }
        // MoveCost + open routes after the move * 10000: the value the moves are compared on.
        public double TotalCost { get; set; }
    }
}
