using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VrdpoProject
{
    public class Swap
    {
        int positionOfFirstRoute;
        int positionOfSecondRoute;
        int positionOfFirstOption;
        int positionOfSecondOption;
        decimal costChangeFirstRt;
        decimal costChangeSecondRt;
        decimal moveCost;

        public Swap()
        {
            this.MoveCost = (decimal)Math.Pow(10, 9);
        }

        public void ReinitializeVariables()
        {
            positionOfFirstRoute = -1;
            positionOfSecondRoute = -1;
            positionOfFirstOption = -1;
            positionOfSecondOption = -1;
            costChangeFirstRt = -1;
            costChangeSecondRt = -1;
            moveCost = (decimal)Math.Pow(10, 9);
        }

        public bool IsValid()
        {
            return positionOfFirstRoute != -1;
        }

        public int PositionOfFirstRoute { get => positionOfFirstRoute; set => positionOfFirstRoute = value; }
        public int PositionOfSecondRoute { get => positionOfSecondRoute; set => positionOfSecondRoute = value; }
        public int PositionOfFirstOption { get => positionOfFirstOption; set => positionOfFirstOption = value; }
        public int PositionOfSecondOption { get => positionOfSecondOption; set => positionOfSecondOption = value; }
        public decimal CostChangeFirstRt { get => costChangeFirstRt; set => costChangeFirstRt = value; }
        public decimal CostChangeSecondRt { get => costChangeSecondRt; set => costChangeSecondRt = value; }
        public decimal MoveCost { get => moveCost; set => moveCost = value; }
    }
}
