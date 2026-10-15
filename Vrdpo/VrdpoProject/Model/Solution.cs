namespace VrdpoProject
{
    public class Solution
    {
        private int po0Count;
        private int po1Count;
        private int po2Count;

        // Fresh, empty solution over a new mutable copy of the instance model.
        public Solution(Instance instance)
        {
            InstanceModel model = instance.BuildModel();
            Cost = 0;
            Routes = new List<Route>();
            DistanceMatrix = instance.DistanceMatrix;
            TimeMatrix = instance.TimeMatrix;
            Cap = instance.VehicleCapacity;
            Depot = model.Depot;
            Options = model.Options;
            Customers = model.Customers;
            Promises = new double[Options.Count + 1, Options.Count + 1];
            InitPromises();
            SolutionUtilizationMetric = 0;
            LowerBoundRoutes = (int)Math.Ceiling((double)Customers.Sum(customer => customer.Dem) / Cap);
        }

        // Deep copy: locations, options, customers and routes are cloned and re-linked by id. The
        // promise matrix is shared with the source on purpose (the copies are never searched).
        private Solution(Solution src)
        {
            Cost = src.Cost;
            SolutionUtilizationMetric = src.SolutionUtilizationMetric;
            LowerBoundRoutes = src.LowerBoundRoutes;
            po0Count = src.po0Count;
            po1Count = src.po1Count;
            po2Count = src.po2Count;
            Repetition = src.Repetition;
            DistanceMatrix = src.DistanceMatrix;
            TimeMatrix = src.TimeMatrix;
            Cap = src.Cap;
            Depot = src.Depot;
            Promises = src.Promises;

            var clonedLocations = new Dictionary<int, Location>();
            var clonedOptions = new List<Option>(src.Options.Count);
            foreach (Option option in src.Options)
            {
                if (!clonedLocations.TryGetValue(option.Location.Id, out Location clonedLoc))
                {
                    clonedLoc = option.Location.Clone();
                    clonedLocations.Add(clonedLoc.Id, clonedLoc);
                }
                clonedOptions.Add(option.Clone(clonedLoc));
            }

            var optionsByCustomer = new Dictionary<int, List<Option>>();
            foreach (Option option in clonedOptions)
            {
                if (!optionsByCustomer.TryGetValue(option.Cust.Id, out List<Option> list))
                {
                    list = new List<Option>();
                    optionsByCustomer.Add(option.Cust.Id, list);
                }
                list.Add(option);
            }
            var clonedCustomers = new List<Customer>(src.Customers.Count);
            var customersById = new Dictionary<int, Customer>();
            foreach (Customer customer in src.Customers)
            {
                if (!optionsByCustomer.TryGetValue(customer.Id, out List<Option> list))
                {
                    list = new List<Option>();
                }
                Customer clone = customer.Clone(list);
                clonedCustomers.Add(clone);
                customersById[clone.Id] = clone;
            }
            foreach (Option option in clonedOptions)
            {
                option.Cust = customersById[option.Cust.Id];
            }
            var optionsById = new Dictionary<int, Option>();
            foreach (Option option in clonedOptions)
            {
                optionsById.TryAdd(option.Id, option);
            }

            var clonedRoutes = new List<Route>(src.Routes.Count);
            foreach (Route rt in src.Routes)
            {
                var clonedRoute = new Route(rt);
                for (int i = 1; i < rt.SequenceOfCustomers.Count - 1; i++)
                {
                    clonedRoute.SequenceOfLocations[i] = clonedLocations[rt.SequenceOfLocations[i].Id];
                    clonedRoute.SequenceOfCustomers[i] = customersById[rt.SequenceOfCustomers[i].Id];
                    clonedRoute.SequenceOfOptions[i] = optionsById[rt.SequenceOfOptions[i].Id];
                }
                clonedRoutes.Add(clonedRoute);
            }

            Options = clonedOptions;
            Customers = clonedCustomers;
            Routes = clonedRoutes;
        }

        public Solution DeepCopy()
        {
            return new Solution(this);
        }

        public double Cost { get; set; }
        public List<Route> Routes { get; set; }
        public double[,] DistanceMatrix { get; }
        public double[,] TimeMatrix { get; }
        public List<Option> Options { get; }
        public double[,] Promises { get; }
        public List<Customer> Customers { get; }
        public Location Depot { get; }
        public int Cap { get; }
        public int Repetition { get; set; }
        public int Restart { get; set; }
        public string LastMove { get; set; }
        // Sum of the routes' (Capacity - Load)^2.
        public double SolutionUtilizationMetric { get; set; }
        // ceil(total demand / vehicle capacity): below this fleet size no feasible solution exists.
        public int LowerBoundRoutes { get; }

        public int Po0Count => po0Count;
        public int Po1Count => po1Count;
        public int Po2Count => po2Count;

        public Route CreateEmptyRoute(int id)
        {
            // The depot's option id is Options.Count, the extra row/column of the promise matrix.
            return new Route(id, Cap, Depot, Options.Count);
        }

        // Count of served options per priority, scanned from the routes. Call once after construction;
        // moves that change a served option keep the counts current with AdjustServiceLevelCounts.
        public void SeedServiceLevelCounts()
        {
            po0Count = 0;
            po1Count = 0;
            po2Count = 0;
            for (int r = 0; r < Routes.Count; r++)
            {
                for (int c = 1; c < Routes[r].SequenceOfOptions.Count - 1; c++)
                {
                    switch (Routes[r].SequenceOfOptions[c].Prio)
                    {
                        case 0: po0Count++; break;
                        case 1: po1Count++; break;
                        case 2: po2Count++; break;
                    }
                }
            }
        }

        public void AdjustServiceLevelCounts(int leavingPriority, int enteringPriority)
        {
            switch (leavingPriority)
            {
                case 0: po0Count--; break;
                case 1: po1Count--; break;
                case 2: po2Count--; break;
            }
            switch (enteringPriority)
            {
                case 0: po0Count++; break;
                case 1: po1Count++; break;
                case 2: po2Count++; break;
            }
        }

        // Service level from the maintained counts: share of priority-0 options, share of priority 0 or 1.
        public (double sl0, double sl1) ServiceLevelFromCounts()
        {
            double sum = po0Count + po1Count + po2Count;
            return (po0Count / sum, (po0Count + po1Count) / sum);
        }

        // Service level by scanning the routes.
        public (double sl0, double sl1) ServiceLevel()
        {
            int po0Sum = 0;
            int po1Sum = 0;
            int po2Sum = 0;
            for (int r = 0; r < Routes.Count; r++)
            {
                for (int c = 1; c < Routes[r].SequenceOfOptions.Count - 1; c++)
                {
                    switch (Routes[r].SequenceOfOptions[c].Prio)
                    {
                        case 0: po0Sum++; break;
                        case 1: po1Sum++; break;
                        case 2: po2Sum++; break;
                    }
                }
            }
            double sum = po0Sum + po1Sum + po2Sum;
            return ((double)po0Sum / sum, (double)(po0Sum + po1Sum) / sum);
        }

        public double CalculateDistance(Location n1, Location n2)
        {
            if (n1.Id > n2.Id)
            {
                return DistanceMatrix[n2.Id, n1.Id - n2.Id];
            }
            return DistanceMatrix[n1.Id, n2.Id - n1.Id];
        }

        public double CalculateTime(Location n1, Location n2)
        {
            if (n1.Id > n2.Id)
            {
                return TimeMatrix[n2.Id, n1.Id - n2.Id];
            }
            return TimeMatrix[n1.Id, n2.Id - n1.Id];
        }

        public void InitPromises()
        {
            int n = Options.Count + 1;
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    Promises[i, j] = double.MaxValue;
                }
            }
        }

        public double CalculateUtilizationMetric()
        {
            double utilizationMetric = 0;
            foreach (Route rt in Routes)
            {
                utilizationMetric += rt.RouteUtilizationMetric;
            }
            return utilizationMetric;
        }

        public void RemoveEmptyRoutes()
        {
            Routes.RemoveAll(rt => rt.Load == 0);
        }

        // ---------------------------------------------------------------------------------------------
        // Time windows. A stop's completion time is: previous completion + travel + ServiceTime (only when
        // the location changes) , waited up to Ready, + DeliveryServiceTime; it must not exceed Due.
        // ---------------------------------------------------------------------------------------------

        // Recomputes the earliest completion (forward) and latest completion (backward) times of a route in place.
        public void UpdateTimes(Route rt)
        {
            var locs = rt.SequenceOfLocations;
            for (int i = 1; i < locs.Count; i++)
            {
                double currentTime = rt.SequenceOfEct[i - 1];
                currentTime += CalculateTime(locs[i], locs[i - 1]);
                if (locs[i - 1].Id != locs[i].Id)
                {
                    currentTime += locs[i].ServiceTime;
                }
                if (locs[i].Ready > currentTime)
                {
                    currentTime = locs[i].Ready;
                }
                currentTime += locs[i].DeliveryServiceTime;
                rt.SequenceOfEct[i] = currentTime;
            }

            for (int j = locs.Count - 2; j >= 0; j--)
            {
                double latestArrivalTimeNext = rt.SequenceOfLat[j + 1] - locs[j + 1].DeliveryServiceTime;
                if (locs[j].Id != locs[j + 1].Id)
                {
                    latestArrivalTimeNext -= locs[j + 1].ServiceTime;
                }
                double latestDepartureTimeJ = latestArrivalTimeNext - CalculateTime(locs[j + 1], locs[j]);
                rt.SequenceOfLat[j] = Math.Min(locs[j].Due, latestDepartureTimeJ);
            }
        }

        // Forward-pass feasibility of a route as it is; prints the violation.
        public bool CalculateTimes(Route rt)
        {
            var locs = rt.SequenceOfLocations;
            double totalTime = 0;
            for (int i = 1; i < locs.Count; i++)
            {
                totalTime += CalculateTime(locs[i], locs[i - 1]);
                if (locs[i - 1].Id != locs[i].Id)
                {
                    totalTime += locs[i].ServiceTime;
                }
                if (locs[i].Ready > totalTime)
                {
                    totalTime = locs[i].Ready;
                }
                totalTime += locs[i].DeliveryServiceTime;
                if (!(totalTime <= locs[i].Due))
                {
                    Console.WriteLine("Time Window Feasibility Error");
                    return false;
                }
            }
            return true;
        }

        // Forward-pass feasibility of an arbitrary location sequence (depot ... depot).
        public bool SequenceFeasible(List<Location> seq)
        {
            double totalTime = 0;
            for (int i = 1; i < seq.Count; i++)
            {
                totalTime += CalculateTime(seq[i], seq[i - 1]);
                if (seq[i - 1].Id != seq[i].Id)
                {
                    totalTime += seq[i].ServiceTime;
                }
                if (seq[i].Ready > totalTime)
                {
                    totalTime = seq[i].Ready;
                }
                totalTime += seq[i].DeliveryServiceTime;
                if (!(totalTime <= seq[i].Due))
                {
                    return false;
                }
            }
            return true;
        }

        // Scratch buffers for the allocation-free checks below (single-threaded use).
        private double[] twScratchEct;
        private double[] twScratchLat;

        private void EnsureTwScratch(int n)
        {
            if (twScratchEct == null || twScratchEct.Length < n)
            {
                int size = Math.Max(n, 64);
                twScratchEct = new double[size];
                twScratchLat = new double[size];
            }
        }

        // Is rt still feasible with `location` inserted after position `loc`?
        public bool RespectsTimeWindow2Feasible(Route rt, int loc, Location location)
        {
            return TimeWindow2FeasibleCore(rt.SequenceOfLocations, loc, location, -1, null);
        }

        // Same, evaluated on rt with the stop at `replaceAt` swapped for `replacement` (same-route swap).
        public bool RespectsTimeWindow2FeasibleWithReplacement(Route rt, int loc, Location location, int replaceAt, Location replacement)
        {
            return TimeWindow2FeasibleCore(rt.SequenceOfLocations, loc, location, replaceAt, replacement);
        }

        private bool TimeWindow2FeasibleCore(List<Location> sequence, int loc, Location location, int replaceAt, Location replacement)
        {
            int n = sequence.Count + 1;
            Location Base(int k) => k == replaceAt ? replacement : sequence[k];
            Location LocAt(int i) => i <= loc ? Base(i) : (i == loc + 1 ? location : Base(i - 1));
            EnsureTwScratch(n);
            double[] ects = twScratchEct;
            double[] lats = twScratchLat;

            ects[0] = LocAt(0).DeliveryServiceTime;
            for (int i = 1; i < n; i++)
            {
                Location cur = LocAt(i);
                Location prev = LocAt(i - 1);
                double currentTime = ects[i - 1];
                currentTime += CalculateTime(cur, prev);
                if (cur.Id != prev.Id)
                {
                    currentTime += cur.ServiceTime;
                }
                if (cur.Ready > currentTime)
                {
                    currentTime = cur.Ready;
                }
                currentTime += cur.DeliveryServiceTime;
                ects[i] = currentTime;
            }

            lats[n - 1] = LocAt(n - 1).Due;
            if (ects[n - 1] > lats[n - 1]) { return false; }

            for (int j = n - 2; j >= 0; j--)
            {
                Location cur = LocAt(j);
                Location next = LocAt(j + 1);
                double latestFinishTimeNext = lats[j + 1];
                latestFinishTimeNext -= next.DeliveryServiceTime;
                if (cur.Id != next.Id)
                {
                    latestFinishTimeNext -= next.ServiceTime;
                }
                latestFinishTimeNext -= CalculateTime(next, cur);
                lats[j] = Math.Min(cur.Due, latestFinishTimeNext);
                if (ects[j] > lats[j]) { return false; }
            }
            return true;
        }

        // Memo of RespectsTimeWindow2Feasible, keyed by route slot, insertion position and location id.
        // A slot's answers are dropped when its location sequence differs from the one they were computed
        // for; call BeginTimeWindowMemo() at the start of every neighborhood scan that uses it.
        private int[][] twMemoIds;
        private sbyte[][][] twMemoRows;   // [slot][position][locationId]: 0 = unknown, 1 = feasible, 2 = infeasible

        public void BeginTimeWindowMemo()
        {
            int n = Routes.Count;
            if (twMemoIds == null || twMemoIds.Length < n)
            {
                var ids = new int[n][];
                var rows = new sbyte[n][][];
                if (twMemoIds != null)
                {
                    Array.Copy(twMemoIds, ids, twMemoIds.Length);
                    Array.Copy(twMemoRows, rows, twMemoRows.Length);
                }
                twMemoIds = ids;
                twMemoRows = rows;
            }
            for (int r = 0; r < n; r++)
            {
                var seq = Routes[r].SequenceOfLocations;
                int[] stored = twMemoIds[r];
                bool same = stored != null && stored.Length == seq.Count;
                if (same)
                {
                    for (int i = 0; i < stored.Length; i++)
                    {
                        if (stored[i] != seq[i].Id) { same = false; break; }
                    }
                }
                if (same) { continue; }
                if (stored == null || stored.Length != seq.Count)
                {
                    stored = new int[seq.Count];
                    twMemoIds[r] = stored;
                    twMemoRows[r] = null;   // the row table is sized by the route length
                }
                for (int i = 0; i < stored.Length; i++) { stored[i] = seq[i].Id; }
                var slotRows = twMemoRows[r];
                if (slotRows != null)
                {
                    for (int i = 0; i < slotRows.Length; i++)
                    {
                        if (slotRows[i] != null) { Array.Clear(slotRows[i], 0, slotRows[i].Length); }
                    }
                }
            }
        }

        public bool RespectsTimeWindow2FeasibleMemo(int slot, Route rt, int loc, Location location)
        {
            int id = location.Id;
            int idRange = TimeMatrix.GetLength(0);
            if (id < 0 || id >= idRange)
            {
                return TimeWindow2FeasibleCore(rt.SequenceOfLocations, loc, location, -1, null);
            }
            var slotRows = twMemoRows[slot];
            if (slotRows == null)
            {
                slotRows = new sbyte[rt.SequenceOfLocations.Count][];
                twMemoRows[slot] = slotRows;
            }
            var row = slotRows[loc];
            if (row == null)
            {
                row = new sbyte[idRange];
                slotRows[loc] = row;
            }
            sbyte v = row[id];
            if (v != 0) { return v == 1; }
            bool result = TimeWindow2FeasibleCore(rt.SequenceOfLocations, loc, location, -1, null);
            row[id] = (sbyte)(result ? 1 : 2);
            return result;
        }

        // Time arrays of the route made of rt's prefix through `loc` followed by otherRt's suffix after
        // `otherLoc` (inter-route 2-opt). Returns (feasible, ect, lat).
        public (bool, double[], double[]) RespectsTimeWindow(Route rt, int loc, Route otherRt, int otherLoc)
        {
            var seq1 = rt.SequenceOfLocations;
            var seq2 = otherRt.SequenceOfLocations;
            int n = (loc + 1) + (seq2.Count - (otherLoc + 1));
            Location LocAt(int i) => i <= loc ? seq1[i] : seq2[otherLoc + 1 + (i - loc - 1)];

            double[] ect = new double[n];
            double[] lat = new double[n];

            ect[0] = LocAt(0).DeliveryServiceTime;
            for (int i = 1; i < n; i++)
            {
                Location cur = LocAt(i);
                Location prev = LocAt(i - 1);
                double t = ect[i - 1] + CalculateTime(cur, prev);
                if (cur.Id != prev.Id)
                    t += cur.ServiceTime;

                if (cur.Ready > t)
                    t = cur.Ready;

                t += cur.DeliveryServiceTime;
                ect[i] = t;

                if (t > cur.Due || t < ect[i - 1])
                    return (false, ect, lat);
            }

            lat[n - 1] = LocAt(n - 1).Due;
            for (int j = n - 2; j >= 0; j--)
            {
                Location cur = LocAt(j);
                Location next = LocAt(j + 1);
                double l = lat[j + 1] - next.DeliveryServiceTime;
                if (cur.Id != next.Id)
                    l -= next.ServiceTime;

                l -= CalculateTime(next, cur);
                lat[j] = Math.Min(cur.Due, l);

                if (lat[j] < ect[j])
                    return (false, ect, lat);
            }

            if (ect[n - 1] > Route.DepotLatestTime)
                return (false, ect, lat);

            return (true, ect, lat);
        }

        // Feasibility-only form of RespectsTimeWindow (same arithmetic, no arrays returned).
        public bool RespectsTimeWindowFeasible(Route rt, int loc, Route otherRt, int otherLoc)
        {
            var seq1 = rt.SequenceOfLocations;
            var seq2 = otherRt.SequenceOfLocations;
            int n = (loc + 1) + (seq2.Count - (otherLoc + 1));
            Location LocAt(int i) => i <= loc ? seq1[i] : seq2[otherLoc + 1 + (i - loc - 1)];

            EnsureTwScratch(n);
            double[] ect = twScratchEct;
            double[] lat = twScratchLat;

            ect[0] = LocAt(0).DeliveryServiceTime;
            for (int i = 1; i < n; i++)
            {
                Location cur = LocAt(i);
                Location prev = LocAt(i - 1);
                double t = ect[i - 1] + CalculateTime(cur, prev);
                if (cur.Id != prev.Id)
                    t += cur.ServiceTime;

                if (cur.Ready > t)
                    t = cur.Ready;

                t += cur.DeliveryServiceTime;
                ect[i] = t;

                if (t > cur.Due || t < ect[i - 1])
                    return false;
            }

            lat[n - 1] = LocAt(n - 1).Due;
            for (int j = n - 2; j >= 0; j--)
            {
                Location cur = LocAt(j);
                Location next = LocAt(j + 1);
                double l = lat[j + 1] - next.DeliveryServiceTime;
                if (cur.Id != next.Id)
                    l -= next.ServiceTime;

                l -= CalculateTime(next, cur);
                lat[j] = Math.Min(cur.Due, l);

                if (lat[j] < ect[j])
                    return false;
            }

            if (ect[n - 1] > Route.DepotLatestTime)
                return false;

            return true;
        }

        // ---------------------------------------------------------------------------------------------
        // Consistency checks (print what is wrong and return false).
        // ---------------------------------------------------------------------------------------------

        public bool CheckEverything()
        {
            bool allCustomersRouted = Customers.Count(x => x.IsRouted) == Customers.Count;
            var timesVisited = new Dictionary<int, int>();
            foreach (Route route in Routes)
            {
                if (!CheckRouteFeasibility(route))
                {
                    return false;
                }
                foreach (Location location in route.SequenceOfLocations)
                {
                    if (!timesVisited.ContainsKey(location.Id))
                    {
                        timesVisited.Add(location.Id, 0);
                    }
                    timesVisited[location.Id] += 1;
                    if (location.Type == 1 && location.MaxCap < timesVisited[location.Id])
                    {
                        Console.WriteLine("Shared location exceeds max capacity!");
                        return false;
                    }
                }
                if (!allCustomersRouted)
                {
                    return false;
                }
            }
            foreach (Route rt in Routes)
            {
                foreach (Location location in rt.SequenceOfLocations)
                {
                    if (location.Type == 1 && location.Cap != timesVisited[location.Id])
                    {
                        Console.WriteLine("Location {0} capacity is wrong (solution may be feasible)!", location.Id);
                        return false;
                    }
                    else if (location.Type == 2 && timesVisited[location.Id] > 1)
                    {
                        Console.WriteLine("Private location is used two times!");
                        return false;
                    }
                }
            }
            return true;
        }

        public bool CheckRouteFeasibility(Route rt)
        {
            int totalCapacity = 0;
            bool timeWindowFeasibility = true;
            bool depotFeasibility = true;
            bool costFeasibility = true;
            double cost = 0;
            if (!CalculateTimes(rt))
            {
                Console.WriteLine("Time Window Feasibility Error");
                timeWindowFeasibility = false;
            }
            for (int i = 0; i < rt.SequenceOfOptions.Count - 1; i++)
            {
                Option currentOpt = rt.SequenceOfOptions[i];
                Option nextOpt = rt.SequenceOfOptions[i + 1];
                if (currentOpt.Location.Type == 0)
                {
                    if (i + 1 != rt.SequenceOfLocations.Count - 1 && i != 0)
                    {
                        Console.WriteLine("Depot Feasibility Error");
                        depotFeasibility = false;
                    }
                }
                cost += CalculateDistance(currentOpt.Location, nextOpt.Location);
            }
            if (Math.Abs(cost - rt.Cost) > 0.001)
            {
                Console.WriteLine("Cost Feasibility Error");
                costFeasibility = false;
            }
            for (int i = 0; i < rt.SequenceOfCustomers.Count; i++)
            {
                totalCapacity += rt.SequenceOfCustomers[i].Dem;
            }
            bool capacityFeasibility = totalCapacity <= rt.Capacity;
            if (!capacityFeasibility)
            {
                Console.WriteLine("Route Capacity Feasibility Error");
            }
            return timeWindowFeasibility && capacityFeasibility && depotFeasibility && costFeasibility;
        }
    }
}
