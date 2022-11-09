using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VrdpoProject
{
    public class Relocation
    {
        //current sol
        //optimal sol
        //starting sol
        //deepcopy
        //operators:
        //relocate route
        //  for route for cust
        //      remove cust
        //  for route for position
        //
        int originRoutePosition;
        int targetRoutePosition;
        int originOptionPosition;
        int targetOptionPosition;
        double costChangeOriginRt;
        double costChangeTargetRt;
        double moveCost;

        public Relocation()
        {
            this.MoveCost = Math.Pow(10, 9);
        }

        public int OriginRoutePosition { get => originRoutePosition; set => originRoutePosition = value; }
        public int TargetRoutePosition { get => targetRoutePosition; set => targetRoutePosition = value; }
        public int OriginOptionPosition { get => originOptionPosition; set => originOptionPosition = value; }
        public int TargetOptionPosition { get => targetOptionPosition; set => targetOptionPosition = value; }
        public double CostChangeOriginRt { get => costChangeOriginRt; set => costChangeOriginRt = value; }
        public double CostChangeTargetRt { get => costChangeTargetRt; set => costChangeTargetRt = value; }
        public double MoveCost { get => moveCost; set => moveCost = value; }
    }
}
