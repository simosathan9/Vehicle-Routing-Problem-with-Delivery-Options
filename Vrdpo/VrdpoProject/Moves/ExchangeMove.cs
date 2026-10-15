namespace VrdpoProject
{
    // Best exchange found in one iteration: two option changes done together, one customer to a worse
    // priority option and another to a better one, so that the service level still holds. Both stops are
    // re-inserted at their best positions. Holds the resulting stop lists of the routes that change.
    public class ExchangeMove
    {
        public List<(int route, List<Option> stops)> Changes = new();
        public List<(Option from, Option to)> Flips = new();
        public double MoveCost = Math.Pow(10, 9);
        public double TotalCost = Math.Pow(10, 9);

        public void ReinitializeVariables()
        {
            Changes = new();
            Flips = new();
            MoveCost = Math.Pow(10, 9);
            TotalCost = Math.Pow(10, 9);
        }

        public bool IsValid()
        {
            return Flips.Count > 0;
        }
    }
}
