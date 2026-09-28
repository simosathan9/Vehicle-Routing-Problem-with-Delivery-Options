using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualBasic.FileIO;
//using Newtonsoft.Json;

namespace VrdpoProject
{
    public class Route
    {
        // NOTE (perf, Phase 1a): this was `private InstanceReader ir = new();` — a per-instance field
        // initializer that ran the InstanceReader() parameterless ctor (string-splits + int-parses off
        // the shared static `instance` array) on EVERY Route construction, including every clone
        // (getTempCopy, the copy-ctor), even though `ir` is only ever read in the one constructor below
        // (for ir.Depot [itself a dead local, see removal below] and ir.NumbOpt). InstanceReader's
        // parameterless ctor deterministically derives the same cap/numbLoc/numbCus/numbOpt from the
        // same static, read-only-after-first-load `instance`/`filename` fields every time for a given
        // process run (one instance file per process) — so sharing a single instance across all Route
        // objects instead of rebuilding it per object changes nothing about the derived values, it just
        // stops re-deriving them. No behavior change — see baselines/phase0 regression snapshots.
        private static readonly InstanceReader ir = new();
        private int id;
        private List<Customer> sequenceOfCustomers = new();
        private List<Location> sequenceOfLocations = new();
        private List<Option> sequenceOfOptions = new();
        private double load;
        private double capacity;
        private double duration;
        private double cost;
        private double fixedCost;
        private int[] sequenceOfStartingTime;
        private int[] sequenceOfEndingTime;
        private List<double> sequenceOfEct = new();
        private List<double> sequenceOfLat = new();
        Customer fakeCustomer = new(1000, 0, true);
        private double routeUtilizationMetric;

        public Route(int id, double capacity, Location storage)
        {
            // NOTE (perf, Phase 1a): `Location depot = ir.Depot;` used to be assigned here and never
            // read again in this constructor (confirmed via grep — `depot` has no other reference in
            // this file). Removed as dead code; `ir.NumbOpt` below is the only genuinely used member.
            this.sequenceOfLocations.Add(storage);
            this.sequenceOfLocations.Add(storage);
            this.sequenceOfCustomers.Add(fakeCustomer);
            this.sequenceOfCustomers.Add(fakeCustomer);
            Option fakeOpt = new(ir.NumbOpt, storage, fakeCustomer, 0, 0, 0, 0, 7200);
            fakeOpt.IsServed = true;
            this.sequenceOfOptions.Add(fakeOpt);
            this.sequenceOfOptions.Add(fakeOpt);
            this.load = 0;
            this.capacity = capacity;
            this.duration = 0;
            this.fixedCost = 1000000;
            this.cost = 0;
            this.Id = id;
            this.sequenceOfEct.Add(0);
            this.sequenceOfEct.Add(0);
            this.sequenceOfLat.Add(7200);
            this.sequenceOfLat.Add(7200);
            this.routeUtilizationMetric = 0;
        }
        public Route() { }
        public Route getTempCopy(Route rt_copy, List<Location> locs)
        {
            // NOTE (perf, Phase 2): `locs.FirstOrDefault(...)` below used to do a linear scan through
            // `locs` (all distinct locations in the whole solution) for EVERY option being cloned, and
            // AGAIN for every option of every customer being cloned — i.e. O(route length × options ×
            // |locs|) total, dominated by the |locs| factor for larger instances. Building one lookup
            // dictionary here (O(|locs|), once per call) and indexing it (O(1) per lookup) instead
            // removes that multiplicative factor entirely, while keeping this method's signature
            // (and every one of its 4 call sites, including FindBestFlipMove's — left completely
            // untouched) exactly as it was. Same result for a given Location.Id either way: `locs` is
            // built from `sol.Options.Select(x => x.Location).ToHashSet()`, i.e. deduplicated by
            // reference, and Location IDs are unique per physical location in this data model, so
            // GetValueOrDefault(id) resolves to the exact same object FirstOrDefault would have found.
            var locsById = locs.ToDictionary(l => l.Id);
            var route = new Route()
            {
                id = rt_copy.id,
                capacity = rt_copy.capacity,
                sequenceOfLocations = rt_copy.sequenceOfLocations.Select(x => (Location)x.Clone()).ToList(),
                sequenceOfCustomers = rt_copy.sequenceOfCustomers
            .Select(x => (Customer)x.Clone((List<Option>)x.Options.Select(y => y.Clone(locsById.GetValueOrDefault(y.Location.Id))).ToList())).ToList(),
                sequenceOfOptions = rt_copy.sequenceOfOptions.Select(x => (Option)x.Clone(locsById.GetValueOrDefault(x.Location.Id))).ToList(),
                load = rt_copy.load,
                duration = rt_copy.duration,
                fixedCost = rt_copy.fixedCost,
                cost = rt_copy.cost,
                sequenceOfEct = new List<double>(rt_copy.sequenceOfEct),
                sequenceOfLat = new List<double>(rt_copy.sequenceOfLat),
                routeUtilizationMetric = rt_copy.routeUtilizationMetric
            };

            return route;
    }

        public Route(Route original)
        {
            this.Id = original.Id;
            this.capacity = original.capacity;
            this.sequenceOfLocations = new List<Location>(original.sequenceOfLocations);
            this.sequenceOfCustomers = new List<Customer>(original.sequenceOfCustomers);
            this.sequenceOfOptions = new List<Option>(original.sequenceOfOptions);
            this.load = original.load;
            this.duration = original.duration;
            this.fixedCost = original.fixedCost;
            this.cost = original.cost;
            this.sequenceOfEct = new List<double>(original.sequenceOfEct);
            this.sequenceOfLat = new List<double>(original.sequenceOfLat);
            this.routeUtilizationMetric = original.routeUtilizationMetric;
        }

        public int Id { get => id; set => id = value; }
        public double Load { get => load; set => load = value; }
        public double Capacity { get => capacity; set => capacity = value; }
        public double Duration { get => duration; set => duration = value; }
        public double Cost { get => cost; set => cost = value; }
        public double FixedCost { get => fixedCost; set => fixedCost = value; }
        public int[] SequenceOfStartingTime { get => sequenceOfStartingTime; set => sequenceOfStartingTime = value; }
        public int[] SequenceOfEndingTime { get => sequenceOfEndingTime; set => sequenceOfEndingTime = value; }
        public List<double> SequenceOfEct { get => sequenceOfEct; set => sequenceOfEct = value; }
        public List<double> SequenceOfLat { get => sequenceOfLat; set => sequenceOfLat = value; }
        internal List<Option> SequenceOfOptions{ get => sequenceOfOptions; set => sequenceOfOptions = value; }
        internal List<Location> SequenceOfLocations { get => sequenceOfLocations; set => sequenceOfLocations = value; }
        internal List<Customer> SequenceOfCustomers { get => sequenceOfCustomers; set => sequenceOfCustomers = value; }
        public double RouteUtilizationMetric { get => routeUtilizationMetric; set => routeUtilizationMetric = value; }

        // Used for intra-route tw check
        public bool CheckTimeWindowsFeasibility() {
            for (int i = 0; i < SequenceOfOptions.Count - 1; i++)
            {
                if (SequenceOfEct[i + 1] > SequenceOfLat[i + 1])
                {
                    return false;
                }
            }
            return true;
        }

        //public string ExportToJson(string filePath)
        //{
        //// Create an anonymous object to hold your data
        //    var routeData = new
        //    {
        //        Id = this.Id,
        //        Load = this.Load,
        //        Capacity = this.Capacity,
        //        Duration = this.Duration,
        //        Cost = this.Cost,
        //        FixedCost = this.FixedCost,
        //        SequenceOfStartingTime = this.SequenceOfStartingTime,
        //        SequenceOfEndingTime = this.SequenceOfEndingTime,
        //        SequenceOfEct = this.SequenceOfEct,
        //        SequenceOfLat = this.SequenceOfLat,
        //        SequenceOfOptions = this.SequenceOfOptions,
        //        SequenceOfLocations = this.SequenceOfLocations,
        //        SequenceOfCustomers = this.SequenceOfCustomers
        //    };

        //    // Serialize to JSON
        //    string json = JsonConvert.SerializeObject(routeData, Formatting.Indented);
        //    return json;
        //}
    }
}
