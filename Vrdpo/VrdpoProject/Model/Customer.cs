namespace VrdpoProject
{
    public class Customer
    {
        private List<Option> options = new();

        public Customer(int id, int dem, bool isRouted)
        {
            Id = id;
            Dem = dem;
            IsRouted = isRouted;
        }

        // Shallow copy with the given option list (the caller provides already-cloned options).
        public Customer Clone(List<Option> clonedOptions)
        {
            var clone = (Customer)MemberwiseClone();
            clone.options = clonedOptions;
            return clone;
        }

        public int Id { get; set; }
        public int Dem { get; set; }
        public bool IsRouted { get; set; }
        public List<Option> Options { get => options; set => options = value; }
    }
}
