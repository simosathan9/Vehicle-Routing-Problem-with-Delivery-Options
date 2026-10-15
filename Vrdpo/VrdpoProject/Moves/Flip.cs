namespace VrdpoProject
{
    // Best flip found in one iteration: a customer's served option is replaced by another of its options,
    // and the new stop is inserted at a (possibly different) position of any route.
    public class Flip
    {
        public Flip()
        {
            ReinitializeVariables();
        }

        public void ReinitializeVariables()
        {
            TotalCost = Math.Pow(10, 9);
            MoveCost = Math.Pow(10, 9);
            TargetRoutePosition = -1;
            TargetOptionPosition = -1;
            OriginRoutePosition = -1;
            OriginOptionPosition = -1;
            NewOptionIndex = -1;
            CostChangeOriginRt = -1;
            CostChangeTargetRt = -1;
        }

        public bool IsValid()
        {
            return TargetOptionPosition != -1;
        }

        public double TotalCost { get; set; }
        public double MoveCost { get; set; }
        public int TargetRoutePosition { get; set; }
        public int TargetOptionPosition { get; set; }
        public double CostChangeOriginRt { get; set; }
        public double CostChangeTargetRt { get; set; }
        public int OriginRoutePosition { get; set; }
        public int OriginOptionPosition { get; set; }
        // Index of the new option in the customer's option list.
        public int NewOptionIndex { get; set; }
    }
}
