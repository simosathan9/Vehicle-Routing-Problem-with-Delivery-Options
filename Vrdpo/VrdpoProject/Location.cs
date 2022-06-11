using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VrdpoProject
{
    internal class Location
    {
        private int id;
        private int xx;
        private int yy;
        private int cap;
        private int ready;
        private int due;
        private int type;
        private int serviceTime;
        //private int customer_id option_id
        public Location(int id, int xx, int yy, int cap, int ready, int due, int type, int serviceTime)
        {
            this.id = id;
            this.xx = xx;
            this.yy = yy;
            this.cap = cap;
            this.ready = ready;
            this.due = due;
            this.type = type;
            this.serviceTime = serviceTime;
        }

        public int Id { get => id; set => id = value; }
        public int Xx { get => xx; set => xx = value; }
        public int Yy { get => yy; set => yy = value; }
        public int Cap { get => cap; set => cap = value; }
        public int Ready { get => ready; set => ready = value; }
        public int Due { get => due; set => due = value; }
        public int Type { get => type; set => type = value; }
        public int ServiceTime { get => serviceTime; set => serviceTime = value; }
    }
}
