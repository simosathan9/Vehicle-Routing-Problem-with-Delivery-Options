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
        private List<Node> customers = new();
        private double[,] matrix;
        private int cap;
        private Node storage;

        public int Cap { get => cap; set => cap = value; }
        public double[,] Matrix { get => matrix; set => matrix = value; }
        internal List<Node> Customers { get => customers; set => customers = value; }
        internal Node Storage { get => storage; set => storage = value; }
        internal List<Node> AllNodes { get => allNodes; set => allNodes = value; }

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
            storage = new Node(0, Int32.Parse(temp1[0]), Int32.Parse(temp1[1]), Int32.Parse(temp1[2]), 0,
                Int32.Parse(temp1[3]), Int32.Parse(temp1[7]), false);
            allNodes.Add(storage);

            for (var i = 6; i < 6 + numbCus; i++)
            {
               var temp2 = Regex.Split(instance[i], @"\t*\s");
               Node customer = new(Int32.Parse(temp2[0])+1, 0, 0, 0, Int32.Parse(temp2[1]), 0, 0, false);
               allNodes.Add(customer);
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
            }

            for (var k = 9 + 2 * numbCus; k < numbCus + numbLoc + 8; k++)
            {
                string[] temp2 = Regex.Split(instance[k], @"\t+");
                Node shLoc = new(Int32.Parse(temp2[0]), Int32.Parse(temp2[0]), Int32.Parse(temp2[1]), Int32.Parse(temp2[2]),
                    0, Int32.Parse(temp2[3]), Int32.Parse(temp2[7]), false);
                allNodes.Add(shLoc);
            }
            
            for (var m = numbCus + numbLoc + 9; m < numbCus + numbLoc + numbOpt + 9; m++)
            {
                string[] temp2 = Regex.Split(instance[m], @"\t+");
                Option opt = new(Int32.Parse(temp2[0]), Int32.Parse(temp2[1]), Int32.Parse(temp2[2]) + 1, Int32.Parse(temp2[3]),
                    Int32.Parse(temp2[4]), Int32.Parse(temp2[5]));            
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
                    dist = Math.Sqrt(Math.Pow(a.Xx - b.Xx, 2) + Math.Pow(a.Yy - b.Yy, 2));
                    matrix[i, j - i] = dist;
                }
            }
        }
    }
}
