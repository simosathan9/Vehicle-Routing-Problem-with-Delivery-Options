using System;


namespace VrdpoProject
{
    public class Flip
    {
        decimal moveCost;
        int targetRoutePosition;
        int targetOptionPosition;
        int originRoutePosition;
        int originOptionPosition;
        int newOptionIndex;
        decimal costChangeOriginRt;
        decimal costChangeTargetRt;

        public Flip()
        {
            this.MoveCost = (decimal) Math.Pow(10, 9);
        }

        public void ReinitializeVariables()
        {
            moveCost = (decimal) Math.Pow(10, 9);
            targetRoutePosition = -1;
            targetOptionPosition = -1;
            originRoutePosition = -1;
            originOptionPosition = -1;
            newOptionIndex = -1;
            costChangeOriginRt = -1;
            costChangeTargetRt = -1;
        }

        public bool IsValid()
        {
            return targetOptionPosition != -1;
        }

        public decimal MoveCost { get => moveCost; set => moveCost = value; }
        public int TargetRoutePosition { get => targetRoutePosition; set => targetRoutePosition = value; }
        public int TargetOptionPosition { get => targetOptionPosition; set => targetOptionPosition = value; }
        public decimal CostChangeOriginRt { get => costChangeOriginRt; set => costChangeOriginRt = value; }
        public decimal CostChangeTargetRt { get => costChangeTargetRt; set => costChangeTargetRt = value; }
        public int OriginRoutePosition { get => originRoutePosition; set => originRoutePosition = value; }
        public int OriginOptionPosition { get => originOptionPosition; set => originOptionPosition = value; }
        public int NewOptionIndex { get => newOptionIndex; set => newOptionIndex = value; }
    }
}
