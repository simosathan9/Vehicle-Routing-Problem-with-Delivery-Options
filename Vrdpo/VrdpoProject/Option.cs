using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VrdpoProject
{
    internal class Option
    {
        private int id;
        private int location;
        private int cust;
        private int prio;
        private int serviceTime;
        private int cost;

        public Option(int id, int location, int cust, int prio, int serviceTime, int cost)
        {
            this.id = id;
            this.location = location;
            this.cust = cust;
            this.prio = prio;
            this.serviceTime = serviceTime;
            this.cost = cost;
        }

        public int Id { get => id; set => id = value; }
        public int Location { get => location; set => location = value; }
        public int Cust { get => cust; set => cust = value; }
        public int Prio { get => prio; set => prio = value; }
        public int ServiceTime { get => serviceTime; set => serviceTime = value; }
        public int Cost { get => cost; set => cost = value; }
    }
}
