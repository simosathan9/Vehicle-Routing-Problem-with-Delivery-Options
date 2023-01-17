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
        double moveCost = 100000000;

        public TwoOpt()
        {

        }

        public int PositionOfFirstRoute { get => positionOfFirstRoute; set => positionOfFirstRoute = value; }
        public int PositionOfSecondRoute { get => positionOfSecondRoute; set => positionOfSecondRoute = value; }
        public int PositionOfFirstOption { get => positionOfFirstOption; set => positionOfFirstOption = value; }
        public int PositionOfSecondOption { get => positionOfSecondOption; set => positionOfSecondOption = value; }
        public double MoveCost { get => moveCost; set => moveCost = value; }

    }
}
