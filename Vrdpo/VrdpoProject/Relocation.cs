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
        decimal costChangeOriginRt;
        decimal costChangeTargetRt;
        decimal moveCost;
        decimal totalCost;

        public Relocation()
        {
            this.totalCost = (decimal)Math.Pow(10, 9);
            this.MoveCost = (decimal)Math.Pow(10, 9);
        }

        public void ReinitializeVariables()
        {
            originRoutePosition = -1;
            targetRoutePosition = -1;
            originOptionPosition = -1;
            targetOptionPosition = -1;
            costChangeOriginRt = -1;
            costChangeTargetRt = -1;
            totalCost = (decimal)Math.Pow(10, 9);
            moveCost = (decimal)Math.Pow(10, 9);
        }

        public bool IsValid()
        {
            return originRoutePosition != -1;
        }

        public int OriginRoutePosition { get => originRoutePosition; set => originRoutePosition = value; }
        public int TargetRoutePosition { get => targetRoutePosition; set => targetRoutePosition = value; }
        public int OriginOptionPosition { get => originOptionPosition; set => originOptionPosition = value; }
        public int TargetOptionPosition { get => targetOptionPosition; set => targetOptionPosition = value; }
        public decimal CostChangeOriginRt { get => costChangeOriginRt; set => costChangeOriginRt = value; }
        public decimal CostChangeTargetRt { get => costChangeTargetRt; set => costChangeTargetRt = value; }
        public decimal MoveCost { get => moveCost; set => moveCost = value; }
        public decimal TotalCost { get => totalCost; set => totalCost = value; }
    }
}
