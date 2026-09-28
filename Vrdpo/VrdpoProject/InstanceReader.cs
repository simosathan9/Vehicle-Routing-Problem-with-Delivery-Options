using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Text.Json;
//using Newtonsoft.Json;


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
        private string[] temp1;
        private int numbLoc;
        private int numbOpt;
        private int numbCus;
        private static string[] instance;
        private static string filename;
        private Dictionary<int, List<Option>> optionsPerCustomer = new Dictionary<int, List<Option>>();
        private Dictionary<int, List<int>> optionsPrioritiesPerCustomer = new Dictionary<int, List<int>>();

        // NOTE (perf, Phase 1b): distance/time matrix cache, shared across every InstanceReader/
        // Solution built within one process run. Safe because (a) exactly one instance file is ever
        // loaded per process (InstanceReader.instance/filename are already static/process-global — the
        // parameterless ctor always re-derives from the same static data), so the matrix's numeric
        // VALUES are identical every time regardless of which restart is building it; (b) the matrix is
        // read-only after construction everywhere else in the codebase (confirmed via repo-wide grep —
        // only Solution.CalculateDistance/CalculateTime read it, via the indexer, never assign into it).
        // Populated on the first BuildModel() call in the process; every subsequent call reuses the same
        // array reference instead of re-looping over every location pair. This is deliberately NOT the
        // same caching strategy as the mutable per-restart object graph (Locations/Customers/Options) —
        // those keep being rebuilt fresh every restart below, unchanged, because Location.Cap and
        // Option.IsServed genuinely are mutated during search and must reset each restart (confirmed via
        // grep: Location.Cap is written in LocalSearch.cs's move-application methods and Solver.cs's
        // insertion code) — only the immutable, coordinates-derived matrix is cached.
        private static double[,] cachedDistanceMatrix;
        private static double[,] cachedTimeMatrix;


        public InstanceReader()
        {
            temp1 = instance[3].Split('\t', StringSplitOptions.RemoveEmptyEntries);
            //Console.WriteLine(instance[3]);
            cap = Int32.Parse(temp1[1]);
            numbLoc = Int32.Parse(temp1[2]);
            numbCus = Int32.Parse(temp1[3]);
            numbOpt = Int32.Parse(temp1[4]);
            temp1 = instance[8 + numbCus].Split('\t', StringSplitOptions.RemoveEmptyEntries);
        }

        public InstanceReader(string filename)
        {
            InstanceReader.filename = filename;
            InstanceReader.instance = System.IO.File.ReadAllLines(filename);
        }

        public int Cap { get => cap; set => cap = value; }
        public double[,] DistanceMatrix { get => distanceMatrix; set => distanceMatrix = value; }
        public double[,] TimeMatrix { get => timeMatrix; set => timeMatrix = value; }
        internal List<Customer> AllCustomers { get => allCustomers; set => allCustomers = value; }
        public Location Depot { get => depot; set => depot = value; }
        public Dictionary<int, List<Option>> OptionsPerCustomer { get => optionsPerCustomer; set => optionsPerCustomer = value; }
        public Dictionary<int, List<int>> OptionsPrioritiesPerCustomer { get => optionsPrioritiesPerCustomer; set => optionsPrioritiesPerCustomer = value; }
        internal List<Node> AllNodes { get => allNodes; set => allNodes = value; }
        internal List<Option> Options { get => options; set => options = value; }
        internal int NumbOpt { get => numbOpt; set => numbOpt = value; }

        public void BuildModel()
        {
            depot = new Location(Int32.Parse(temp1[0]), Int32.Parse(temp1[1]), Int32.Parse(temp1[2]),
                Int32.Parse(temp1[3]), 10*Int32.Parse(temp1[4]), 10*Int32.Parse(temp1[5]), Int32.Parse(temp1[6]), Int32.Parse(temp1[7]), 0);
            allLocations.Add(depot);

            for (var i = 6; i < 6 + numbCus; i++)
            {
               var temp2 = Regex.Split(instance[i], @"\t*\s");
               Customer customer = new(Int32.Parse(temp2[0]), Int32.Parse(temp2[1]), false);
               Location location = new(Int32.Parse(temp2[0]) + 1, 0, 0, 0, 0, 0, 0, 0, 0);
               allCustomers.Add(customer);
            }

            for (var j = 9 + numbCus; j < 8 + numbCus + numbLoc; j++)
            {
                string[] temp2 = Regex.Split(instance[j], @"\t+");
                var type = Int32.Parse(temp2[6]);
                Location loc = new(Int32.Parse(temp2[0]), Int32.Parse(temp2[1]), Int32.Parse(temp2[2]),
                   Int32.Parse(temp2[3]), 10*Int32.Parse(temp2[4]), 10*Int32.Parse(temp2[5]), Int32.Parse(temp2[6]), 10*Int32.Parse(temp2[7]), type==1?20:50,0);
                //if (loc.Type == 1)
                //{
                //    loc.Due += 20;
                //}
                //else if (loc.Type == 2)
                //{
                //    loc.Due += 50;
                //}
                allLocations.Add(loc);
            }
            
            for (var m = numbCus + numbLoc + 9; m < numbCus + numbLoc + numbOpt + 9; m++)
            {
                string[] temp2 = Regex.Split(instance[m], @"\t+");
                Option opt = new(Int32.Parse(temp2[0]), allLocations[Int32.Parse(temp2[1])], allCustomers[Int32.Parse(temp2[2])], Int32.Parse(temp2[3]),
                    Int32.Parse(temp2[4]), Int32.Parse(temp2[5]), allLocations[Int32.Parse(temp2[1])].Ready, allLocations[Int32.Parse(temp2[1])].Due);
                options.Add(opt);
                if (allCustomers[Int32.Parse(temp2[2])].Options.Count == 0)
                {
                    allCustomers[Int32.Parse(temp2[2])].Options = new List<Option>() { opt };
                } else
                {
                    allCustomers[Int32.Parse(temp2[2])].Options.Add(opt);
                }
                //allCustomers[Int32.Parse(temp2[2])].Options = (List<Option>)allCustomers[Int32.Parse(temp2[2])].Options.Append(opt);
            }

            // NOTE (perf, Phase 1b): matrix build is cached across restarts within a process — see the
            // cachedDistanceMatrix/cachedTimeMatrix fields for why this is safe. First call in the
            // process computes it exactly as before (same loop, same arithmetic, same settings read —
            // just hoisted out of the inner loop since settings.json never changes mid-run); every
            // subsequent call just reuses the cached arrays.
            if (cachedDistanceMatrix != null)
            {
                distanceMatrix = cachedDistanceMatrix;
                timeMatrix = cachedTimeMatrix;
            }
            else
            {
                int rows = allLocations.Count;
                distanceMatrix = new double[rows, rows];
                timeMatrix = new double[rows, rows];

                for (int i = 0; i < rows; i++)
                {
                    for (int j = i; j < rows; j++)
                    {
                        distanceMatrix[i, j] = 0;
                        timeMatrix[i, j] = 0;
                    }
                }

                // Hoisted out of the (i, j) loop below: settings.json was previously re-read and
                // re-deserialized once PER MATRIX CELL (i.e. ~rows²/2 times) purely to learn a constant
                // that never changes during a run. Reading it once here produces the exact same
                // `settings.type` value for every cell, since the file isn't touched mid-run.
                string jsonContent = File.ReadAllText("settings.json");
                var settings = JsonSerializer.Deserialize<Settings>(jsonContent);

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

                        if (settings.type == "int")
                        {
                            timeMatrix[i, j - i] = (int)(Math.Ceiling(10 * dist));
                            distanceMatrix[i, j - i] = (int)(Math.Ceiling(10 * dist));
                        }
                        else if (settings.type == "double")
                        {
                            timeMatrix[i, j - i] = dist;
                            distanceMatrix[i, j - i] = dist;
                        }
                    }
                }

                cachedDistanceMatrix = distanceMatrix;
                cachedTimeMatrix = timeMatrix;
            }
            //Update all data structures
            foreach(Option opt in options)
            {
                if (!optionsPerCustomer.ContainsKey(opt.Cust.Id))
                {
                    optionsPerCustomer[opt.Cust.Id] = new List<Option>();
                }
                if (!optionsPrioritiesPerCustomer.ContainsKey(opt.Cust.Id))
                {
                    optionsPrioritiesPerCustomer[opt.Cust.Id] = new List<int>();
                }

            }
            optionsPerCustomer[1000] = new List<Option>(); // because of the fake Customer
            optionsPrioritiesPerCustomer[1000] = new List<int>();
            //Dictionary optionsPerCustomer contains a list for every customer with all of his options 
            foreach (Option opt in Options)
            {
                int custID = opt.Cust.Id;
                optionsPerCustomer[custID].Add(opt);
                optionsPrioritiesPerCustomer[custID].Add(opt.Prio);
            }
        }
        //public void ExportToJson(string filePath)
        //{
        //    // Create a JSON object to hold all necessary data
        //    var data = new
        //    {
        //        Cap = Cap,
        //        DistanceMatrix = DistanceMatrix,
        //        TimeMatrix = TimeMatrix,
        //        Depot = depot,
        //        AllCustomers = AllCustomers,
        //        AllNodes = AllNodes,
        //        Options = Options,
        //        OptionsPerCustomer = optionsPerCustomer,
        //        OptionsPrioritiesPerCustomer = optionsPrioritiesPerCustomer
        //    };

        //    // Serialize to JSON
        //    string json = JsonConvert.SerializeObject(data, Formatting.Indented);

        //    // Write JSON to file
        //    File.WriteAllText(filePath, json);
        //}
    }
}
