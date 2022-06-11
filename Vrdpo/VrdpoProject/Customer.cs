using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VrdpoProject
{
    internal class Customer
    {
        private int id;
        private List<Location> locations;
        private List<Option> options;
        private int dem;
        private bool isRouted;
        //option routed

        public Customer(int id, int dem, bool isRouted)
        {
            this.id = id;
            this.dem = dem;
            this.isRouted = isRouted;
        }

        public int Id { get => id; set => id = value; }
        public int Dem { get => dem; set => dem = value; }
        public bool IsRouted { get => isRouted; set => isRouted = value; }
        internal List<Location> Locations { get => locations; set => locations = value; }
        internal List<Option> Options { get => options; set => options = value; }
    }
}