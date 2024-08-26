using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VrdpoProject
{
    public  class TwoOpt
    {
        int positionOfFirstRoute;
        int positionOfSecondRoute;
        int positionOfFirstOption;
        int positionOfSecondOption;
        decimal[] ect1;
        decimal[] ect2;
        decimal[] lat1;
        decimal[] lat2;
        decimal moveCost = (decimal)Math.Pow(10, 9);
        decimal totalCost = (decimal)Math.Pow(10, 9);

        public TwoOpt()
        {
            this.TotalCost = (decimal)Math.Pow(10, 9);
            this.MoveCost = (decimal)Math.Pow(10, 9);
        }

        public void ReinitializeVariables()
        {
            positionOfFirstRoute = -1;
            positionOfSecondRoute = -1;
            positionOfFirstOption = -1;
            positionOfSecondOption = -1;
            totalCost = (decimal)Math.Pow(10, 9);
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
        public decimal MoveCost { get => moveCost; set => moveCost = value; }
        public decimal TotalCost { get => totalCost; set => totalCost = value; }
        public decimal[] Ect1 { get => ect1; set => ect1 = value; }
        public decimal[] Ect2 { get => ect2; set => ect2 = value; }
        public decimal[] Lat1 { get => lat1; set => lat1 = value; }
        public decimal[] Lat2 { get => lat2; set => lat2 = value; }
    }
}
