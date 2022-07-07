using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace VrdpoProject
{
    public class InstanceReader
    {
        private List<Node> allNodes = new();
        private List<Location> allLocations = new();
        private List<Customer> allCustomers = new();
        private double[,] distanceMatrix;
        private double[,] timeMatrix;

        private int cap;
        private Location depot;
        private List<Option> options = new();
        //travel time travel cost
        public int Cap { get => cap; set => cap = value; }
        public double[,] DistanceMatrix { get => distanceMatrix; set => distanceMatrix = value; }
        public double[,] TimeMatrix { get => timeMatrix; set => timeMatrix = value; }
        internal List<Customer> AllCustomers { get => allCustomers; set => allCustomers = value; }
        internal Location Depot { get => depot; set => depot = value; }
        internal List<Node> AllNodes { get => allNodes; set => allNodes = value; }
        internal List<Option> Options { get => options; set => options = value; }

        public void BuildModel()
        {
            string[] instance = System.IO.File.ReadAllLines("U_25medium.txt");
            //string[] instance = System.IO.File.ReadAllLines("U_50large.txt");
            //string[] instance = System.IO.File.ReadAllLines("newFile1.txt");
            var temp1 = instance[3].Split('\t', StringSplitOptions.RemoveEmptyEntries);
            cap = Int32.Parse(temp1[1]);
            int numbLoc = Int32.Parse(temp1[2]);
            int numbCus = Int32.Parse(temp1[3]);
            int numbOpt = Int32.Parse(temp1[4]);
            temp1 = instance[8+numbCus].Split('\t', StringSplitOptions.RemoveEmptyEntries);
            depot = new Location(Int32.Parse(temp1[0]), Int32.Parse(temp1[1]), Int32.Parse(temp1[2]),
                Int32.Parse(temp1[3]), Int32.Parse(temp1[4]), Int32.Parse(temp1[5]), Int32.Parse(temp1[6]), Int32.Parse(temp1[6]));
            allLocations.Add(depot);

            for (var i = 6; i < 6 + numbCus; i++)
            {
               var temp2 = Regex.Split(instance[i], @"\t*\s");
               Customer customer = new(Int32.Parse(temp2[0]), Int32.Parse(temp2[1]), false);
               Location location = new(Int32.Parse(temp2[0]) + 1, 0, 0, 0, 0, 0, 0, 0);
               allCustomers.Add(customer);
            }

            for (var j = 9 + numbCus; j < 8 + numbCus + numbLoc; j++)
            {
                string[] temp2 = Regex.Split(instance[j], @"\t+");
                Location loc = new(Int32.Parse(temp2[0]), Int32.Parse(temp2[1]), Int32.Parse(temp2[2]),
                   Int32.Parse(temp2[3]), Int32.Parse(temp2[4]), Int32.Parse(temp2[5]), Int32.Parse(temp2[6]), Int32.Parse(temp2[7]));
                allLocations.Add(loc);
            }
            
            for (var m = numbCus + numbLoc + 9; m < numbCus + numbLoc + numbOpt + 9; m++)
            {
                string[] temp2 = Regex.Split(instance[m], @"\t+");
                Option opt = new(Int32.Parse(temp2[0]), allLocations[Int32.Parse(temp2[1])], allCustomers[Int32.Parse(temp2[2])], Int32.Parse(temp2[3]),
                    Int32.Parse(temp2[4]), Int32.Parse(temp2[5]), allLocations[Int32.Parse(temp2[1])].Ready, allLocations[Int32.Parse(temp2[1])].Due);         
                options.Add(opt);
            }

            int rows = allLocations.Count;
            distanceMatrix = new double[rows,rows];
            timeMatrix = new double[rows, rows];

            for (int i = 0; i < rows; i++)
            {
                for(int j = i; j < rows; j++)
                {
                    distanceMatrix[i,j] = 0.0;
                    timeMatrix[i,j] = 0.0;
                }
            }

            Location a;
            Location b;
            double dist;
            for (int i = 0; i < rows; i++)
            {
                for (int j = i; j < rows; j++)
                {
                    a = allLocations[i];
                    b = allLocations[j];
                    dist = Math.Sqrt(Math.Pow(a.Xx - b.Xx, 2) + Math.Pow(a.Yy - b.Yy, 2));
                    timeMatrix[i, j - i] = (int)(Math.Ceiling(10 * dist));
                    distanceMatrix[i, j - i] = (int)(Math.Ceiling(10 * dist));
                }
            }
            //Update all data structures
        }
    }
}
