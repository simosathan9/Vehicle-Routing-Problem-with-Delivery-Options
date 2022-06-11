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
        private List<Customer> customers = new();
        private double[,] matrix;
        private int cap;
        private Node depot;
        private List<Option> options = new();
        //travel time travel cost
        public int Cap { get => cap; set => cap = value; }
        public double[,] Matrix { get => matrix; set => matrix = value; }
        internal List<Customer> Customers { get => customers; set => customers = value; }
        internal Node Depot { get => depot; set => depot = value; }
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
            depot = new Node(0, Int32.Parse(temp1[0]), Int32.Parse(temp1[1]), Int32.Parse(temp1[2]), 0,
                Int32.Parse(temp1[3]), Int32.Parse(temp1[7]), false, Int32.Parse(temp1[4]), Int32.Parse(temp1[5]), Int32.Parse(temp1[6]));
            allNodes.Add(depot);

            for (var i = 6; i < 6 + numbCus; i++)
            {
               var temp2 = Regex.Split(instance[i], @"\t*\s");
               Node customer = new(Int32.Parse(temp2[0]) + 1, 0, 0, 0, Int32.Parse(temp2[1]), 0, 0, false, 0, 0 ,0);
               Location location = new(Int32.Parse(temp2[0]) + 1, 0, 0, 0, 0, 0, 0, 0);
               allNodes.Add(customer);
               allLocations.Add(location);
               customers.Add(customer);
            }

            for (var j = 9 + numbCus; j <= 8 + numbCus + numbCus; j++)
            {
                string[] temp2 = Regex.Split(instance[j], @"\t+");
                allNodes[Int32.Parse(temp2[0])].Id = Int32.Parse(temp2[0]);
                allNodes[Int16.Parse(temp2[0])].Location = Int16.Parse(temp2[0]);
                allNodes[Int16.Parse(temp2[0])].Xx = Int16.Parse(temp2[1]);
                allNodes[Int16.Parse(temp2[0])].Yy = Int16.Parse(temp2[2]);
                allNodes[Int16.Parse(temp2[0])].Cap = Int16.Parse(temp2[3]);
                allNodes[Int16.Parse(temp2[0])].ServiceTime = Int16.Parse(temp2[7]);
                allNodes[Int16.Parse(temp2[0])].IsRouted = false;
                allNodes[Int16.Parse(temp2[0])].Ready = Int16.Parse(temp2[4]);
                allNodes[Int16.Parse(temp2[0])].Due = Int16.Parse(temp2[5]);
                allNodes[Int16.Parse(temp2[0])].Type = Int16.Parse(temp2[6]);

                customers[Int16.Parse(temp2[0]) - 1].Location = Int16.Parse(temp2[0]);
                customers[Int16.Parse(temp2[0]) - 1].Xx = Int16.Parse(temp2[1]);
                customers[Int16.Parse(temp2[0]) - 1].Yy = Int16.Parse(temp2[2]);
                customers[Int16.Parse(temp2[0]) - 1].Cap = Int16.Parse(temp2[3]);
                customers[Int16.Parse(temp2[0]) - 1].ServiceTime = Int16.Parse(temp2[7]);
                customers[Int16.Parse(temp2[0]) - 1].Ready = Int16.Parse(temp2[4]);
                customers[Int16.Parse(temp2[0]) - 1].Due = Int16.Parse(temp2[5]);
                customers[Int16.Parse(temp2[0]) - 1].Type = Int16.Parse(temp2[6]);

                allLocations[Int32.Parse(temp2[0]) - 1].Id = Int32.Parse(temp2[0]);
                allLocations[Int16.Parse(temp2[0]) - 1].Xx = Int16.Parse(temp2[1]);
                allLocations[Int16.Parse(temp2[0]) - 1].Yy = Int16.Parse(temp2[2]);
                allLocations[Int16.Parse(temp2[0]) - 1].Cap = Int16.Parse(temp2[3]);
                allLocations[Int16.Parse(temp2[0]) - 1].ServiceTime = Int16.Parse(temp2[7]);
                allLocations[Int16.Parse(temp2[0]) - 1].Ready = Int16.Parse(temp2[4]);
                allLocations[Int16.Parse(temp2[0]) - 1].Due = Int16.Parse(temp2[5]);
                allLocations[Int16.Parse(temp2[0]) - 1].Type = Int16.Parse(temp2[6]);
            }

            for (var k = 9 + 2 * numbCus; k < numbCus + numbLoc + 8; k++)
            {
                string[] temp2 = Regex.Split(instance[k], @"\t+");
                Node shLoc = new(Int32.Parse(temp2[0]), Int32.Parse(temp2[0]), Int32.Parse(temp2[1]), Int32.Parse(temp2[2]),
                    0, Int32.Parse(temp2[3]), Int32.Parse(temp2[7]), false, Int32.Parse(temp2[4]), Int32.Parse(temp2[5]), Int32.Parse(temp2[6]));
                allNodes.Add(shLoc);
                Location loc = new(Int32.Parse(temp2[0]), Int32.Parse(temp2[1]), Int32.Parse(temp2[2]),
                    Int32.Parse(temp2[3]), Int32.Parse(temp2[7]), Int32.Parse(temp2[4]), Int32.Parse(temp2[5]), Int32.Parse(temp2[6]));
                allLocations.Add(loc);
            }
            
            for (var m = numbCus + numbLoc + 9; m < numbCus + numbLoc + numbOpt + 9; m++)
            {
                string[] temp2 = Regex.Split(instance[m], @"\t+");
                //Node location = allNodes[Int32.Parse(temp2[1])];
                //Node customer = customers[Int32.Parse(temp2[2])];
                //Location loc = new(location.Id, location.Xx, location.Yy, location.Cap, location.Ready, location.Due,
                //                   location.Type, location.ServiceTime);
                //Node cust = new(customer.Id, customer.Location, customer.Xx, customer.Yy, customer.Dem, customer.Cap,
                //                customer.ServiceTime, customer.IsRouted, customer.Ready, customer.Due, customer.Type);
                Option opt = new(Int32.Parse(temp2[0]), allLocations[Int32.Parse(temp2[1]) - 1], customers[Int32.Parse(temp2[2])], Int32.Parse(temp2[3]),
                    Int32.Parse(temp2[4]), Int32.Parse(temp2[5]), allLocations[Int32.Parse(temp2[1]) - 1].Ready, allLocations[Int32.Parse(temp2[1]) - 1].Due);         
                options.Add(opt);
            }

            int rows = allNodes.Count;
            matrix = new double[rows,rows];

            for (int i = 0; i < rows; i++)
            {
                for(int j = i; j < rows; j++)
                {
                    matrix[i,j] = 0.0;
                }
            }

            Node a;
            Node b;
            double dist;
            for (int i = 0; i < rows; i++)
            {
                for (int j = i; j < rows; j++)
                {
                    a = allNodes[i];
                    b = allNodes[j];
                    dist = Math.Sqrt(Math.Pow(a.Xx - b.Xx, 2) + Math.Pow(a.Yy - b.Yy, 2));//int(ceil(10*
                    matrix[i, j - i] = dist;
                }
            }
            //Update all data structures
            //time matrix and cost matrix
        }
    }
}
