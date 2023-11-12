using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace VrdpoProject
{

    public class LocalSearch
    {

        private Route rt1, rt2;
        public Relocation FindBestRelocationMove(Relocation rm, Solution sol)
        {
            for (int originRouteIndex = 0; originRouteIndex < sol.Routes.Count; originRouteIndex++)
            {
                rt1 = sol.Routes[originRouteIndex];

                for (int targetRouteIndex = 0; targetRouteIndex < sol.Routes.Count; targetRouteIndex++)
                {
                    rt2 = sol.Routes[targetRouteIndex];

                    for (int originOptionIndex = 1; originOptionIndex < rt1.SequenceOfOptions.Count - 1; originOptionIndex++)
                    {
                        for (int targetOptionIndex = 0; targetOptionIndex < rt2.SequenceOfOptions.Count - 1; targetOptionIndex++)
                        {
                            if (originRouteIndex == targetRouteIndex && (targetOptionIndex == originOptionIndex || targetOptionIndex == originOptionIndex - 1))
                            {
                                continue;
                            }

                            double[] tw = sol.RespectsTimeWindow(rt2, targetOptionIndex, rt1.SequenceOfLocations[originOptionIndex]);
                            double ect = tw[0];
                            double lat = tw[1];

                            if (ect > lat) { continue; }

                            Option A = rt1.SequenceOfOptions[originOptionIndex - 1];
                            Option B = rt1.SequenceOfOptions[originOptionIndex];
                            Option C = rt1.SequenceOfOptions[originOptionIndex + 1];

                            Option F = rt2.SequenceOfOptions[targetOptionIndex];
                            Option G = rt2.SequenceOfOptions[targetOptionIndex + 1];
                            if (rt1 != rt2)
                            {
                                if (rt2.Load + B.Cust.Dem > rt2.Capacity)
                                {
                                    continue;
                                }
                            }

                            double costAdded = sol.CalculateDistance(A.Location, C.Location) + sol.CalculateDistance(F.Location, B.Location)
                                                + sol.CalculateDistance(B.Location, G.Location);
                            double costRemoved = sol.CalculateDistance(A.Location, B.Location) + sol.CalculateDistance(B.Location, C.Location)
                                                + sol.CalculateDistance(F.Location, G.Location);
                            double moveCost = costAdded - costRemoved;

                            double costChangeOriginRt = sol.CalculateDistance(A.Location, C.Location) - sol.CalculateDistance(A.Location, B.Location)
                                                - sol.CalculateDistance(B.Location, C.Location);
                            double costChangeTargetRt = sol.CalculateDistance(F.Location, B.Location) + sol.CalculateDistance(B.Location, G.Location)
                                                - sol.CalculateDistance(F.Location, G.Location);

                            if (moveCost < rm.MoveCost & targetRouteIndex != 0 & moveCost!=0)
                            {
                                List<Option[]> arcs = new();
                                arcs.Add(new Option[] { F, B });
                                arcs.Add(new Option[] { B, G });
                                arcs.Add(new Option[] { A, C });
                                if (!CheckPromises(arcs, moveCost + sol.Cost, sol)) continue;
                                rm.MoveCost = moveCost;
                                rm.OriginRoutePosition = originRouteIndex;
                                rm.TargetRoutePosition = targetRouteIndex;
                                rm.OriginOptionPosition = originOptionIndex;
                                rm.TargetOptionPosition = targetOptionIndex;
                                rm.CostChangeOriginRt = costChangeOriginRt;
                                rm.CostChangeTargetRt = costChangeTargetRt;
                            }
                        }
                    }
                }
            }
            return rm;
        }
        public void ApplyRelocationMove(Relocation rm, Solution sol)
        {
            if ((rm.MoveCost != Math.Pow(10, 9)) & (rm.TargetRoutePosition != 0))
            {
                Route originRt = sol.Routes[rm.OriginRoutePosition];
                Route targetRt = sol.Routes[rm.TargetRoutePosition];
                if (!sol.CheckRouteFeasibility(originRt) || !sol.CheckRouteFeasibility(targetRt))
                {
                    Console.WriteLine("-----");
                }
                Option A = originRt.SequenceOfOptions[rm.OriginOptionPosition - 1];
                Option B = originRt.SequenceOfOptions[rm.OriginOptionPosition];
                Option C = originRt.SequenceOfOptions[rm.OriginOptionPosition + 1];
                Option F = targetRt.SequenceOfOptions[rm.TargetOptionPosition];
                Option G = targetRt.SequenceOfOptions[rm.TargetOptionPosition + 1];

                if (originRt == targetRt)
                {
                    originRt.SequenceOfOptions.RemoveAt(rm.OriginOptionPosition);
                    originRt.SequenceOfCustomers.RemoveAt(rm.OriginOptionPosition);
                    originRt.SequenceOfLocations.RemoveAt(rm.OriginOptionPosition);
                    if (rm.OriginOptionPosition < rm.TargetOptionPosition)
                    {
                        targetRt.SequenceOfOptions.Insert(rm.TargetOptionPosition, B);
                        targetRt.SequenceOfCustomers.Insert(rm.TargetOptionPosition, B.Cust);
                        targetRt.SequenceOfLocations.Insert(rm.TargetOptionPosition, B.Location);
                    }
                    else
                    {
                        targetRt.SequenceOfOptions.Insert(rm.TargetOptionPosition + 1, B);
                        targetRt.SequenceOfCustomers.Insert(rm.TargetOptionPosition + 1, B.Cust);
                        targetRt.SequenceOfLocations.Insert(rm.TargetOptionPosition + 1, B.Location);
                    }
                    sol.UpdateTimes(originRt);
                    originRt.Cost += rm.MoveCost;
                }
                else
                {
                    originRt.SequenceOfOptions.RemoveAt(rm.OriginOptionPosition);
                    originRt.SequenceOfCustomers.RemoveAt(rm.OriginOptionPosition);
                    originRt.SequenceOfLocations.RemoveAt(rm.OriginOptionPosition);
                    originRt.SequenceOfEct.RemoveAt(rm.OriginOptionPosition);
                    originRt.SequenceOfLat.RemoveAt(rm.OriginOptionPosition);
                    targetRt.SequenceOfOptions.Insert(rm.TargetOptionPosition + 1, B);
                    targetRt.SequenceOfCustomers.Insert(rm.TargetOptionPosition + 1, B.Cust);
                    targetRt.SequenceOfLocations.Insert(rm.TargetOptionPosition + 1, B.Location);
                    targetRt.SequenceOfEct.Insert(rm.TargetOptionPosition + 1, 0);
                    targetRt.SequenceOfLat.Insert(rm.TargetOptionPosition + 1, 0);
                    originRt.Cost += rm.CostChangeOriginRt;
                    targetRt.Cost += rm.CostChangeTargetRt;
                    originRt.Load -= B.Cust.Dem;
                    targetRt.Load += B.Cust.Dem;
                    sol.UpdateTimes(originRt);
                    sol.UpdateTimes(targetRt);
                }
                sol.Cost += rm.MoveCost;
                sol.Promises[A.Id, C.Id] = sol.Cost;
                sol.Promises[F.Id, B.Id] = sol.Cost;
                sol.Promises[B.Id, G.Id] = sol.Cost;
                if (!sol.CheckRouteFeasibility(originRt))
                {
                    Console.WriteLine("-----");
                }
                if (!sol.CheckRouteFeasibility(targetRt))
                {
                    Console.WriteLine("-----");
                }
            }
        }

        public Swap FindBestSwapMove(Swap sm, Solution sol)
        {
            Route rt1, rt2;
            int startOfSecondOptionIndex;
            Option a1, b1, c1, a2, b2, c2;
            for (int firstRouteIndex = 0; firstRouteIndex < sol.Routes.Count; firstRouteIndex++)
            {
                rt1 = sol.Routes[firstRouteIndex];
                for (int secondRouteIndex = firstRouteIndex; secondRouteIndex < sol.Routes.Count; secondRouteIndex++)
                {
                    rt2 = sol.Routes[secondRouteIndex];
                    for (int firstOptionIndex = 1; firstOptionIndex < rt1.SequenceOfOptions.Count - 1; firstOptionIndex++)
                    {
                        startOfSecondOptionIndex = 1;
                        if (rt1 == rt2)
                        {
                            startOfSecondOptionIndex = firstOptionIndex + 1;
                        }
                        for (int secondOptionIndex = startOfSecondOptionIndex; secondOptionIndex < rt2.SequenceOfOptions.Count - 1; secondOptionIndex++)
                        {
                            a1 = rt1.SequenceOfOptions[firstOptionIndex - 1];
                            b1 = rt1.SequenceOfOptions[firstOptionIndex];
                            c1 = rt1.SequenceOfOptions[firstOptionIndex + 1];
                            a2 = rt2.SequenceOfOptions[secondOptionIndex - 1];
                            b2 = rt2.SequenceOfOptions[secondOptionIndex];
                            c2 = rt2.SequenceOfOptions[secondOptionIndex + 1];

                            double moveCost;
                            double costChangeFirstRoute;
                            double costChangeSecondRoute;

                            double[] tw1 = sol.RespectsTimeWindow(rt1, firstOptionIndex, b2.Location);
                            double[] tw2 = sol.RespectsTimeWindow(rt2, secondOptionIndex, b1.Location);
                            double ect1 = tw1[0];
                            double lat1 = tw1[1];
                            double ect2 = tw2[0];
                            double lat2 = tw2[1];

                            if (ect1 > lat1 || ect2 > lat2) { continue; }

                            if (rt1 == rt2)
                            {
                                if (firstOptionIndex == secondOptionIndex - 1)
                                {
                                    double costRemoved = sol.CalculateDistance(a1.Location, b1.Location) + sol.CalculateDistance(b1.Location, b2.Location) + sol.CalculateDistance(b2.Location, c2.Location);
                                    double costAdded = sol.CalculateDistance(a1.Location, b2.Location) + sol.CalculateDistance(b2.Location, b1.Location) + sol.CalculateDistance(b1.Location, c2.Location);
                                    moveCost = costAdded - costRemoved;
                                } else {
                                    double costRemoved1 = sol.CalculateDistance(a1.Location, b1.Location) + sol.CalculateDistance(b1.Location, c1.Location);
                                    double costAdded1 = sol.CalculateDistance(a1.Location, b2.Location) + sol.CalculateDistance(b2.Location, c1.Location);
                                    double costRemoved2 = sol.CalculateDistance(a2.Location, b2.Location) + sol.CalculateDistance(b2.Location, c2.Location);
                                    double costAdded2 = sol.CalculateDistance(a2.Location, b1.Location) + sol.CalculateDistance(b1.Location, c2.Location);
                                    moveCost = costAdded1 + costAdded2 - (costRemoved1 + costRemoved2);
                                }
                            } else {
                                if (rt1.Load - b1.Cust.Dem + b2.Cust.Dem > rt1.Capacity) { continue; }
                                if (rt2.Load - b2.Cust.Dem + b1.Cust.Dem > rt2.Capacity) { continue; }
                                double costRemoved1 = sol.CalculateDistance(a1.Location, b1.Location) + sol.CalculateDistance(b1.Location, c1.Location);
                                double costAdded1 = sol.CalculateDistance(a1.Location, b2.Location) + sol.CalculateDistance(b2.Location, c1.Location);
                                double costRemoved2 = sol.CalculateDistance(a2.Location, b2.Location) + sol.CalculateDistance(b2.Location, c2.Location);
                                double costAdded2 = sol.CalculateDistance(a2.Location, b1.Location) + sol.CalculateDistance(b1.Location, c2.Location);
                                costChangeFirstRoute = costAdded1 - costRemoved1;
                                costChangeSecondRoute = costAdded2 - costRemoved2;
                                moveCost = costAdded1 + costAdded2 - (costRemoved1 + costRemoved2);
                                if (moveCost < sm.MoveCost & moveCost !=0)
                                {
                                    List<Option[]> arcs = new();
                                    arcs.Add(new Option[] { a1, b2 });
                                    arcs.Add(new Option[] { b2, c1 });
                                    arcs.Add(new Option[] { a2, b1 });
                                    arcs.Add(new Option[] { b1, c2 });
                                    if (!CheckPromises(arcs, moveCost + sol.Cost, sol)) continue;
                                    sm.PositionOfFirstRoute = firstRouteIndex;
                                    sm.PositionOfSecondRoute = secondRouteIndex;
                                    sm.PositionOfFirstOption = firstOptionIndex;
                                    sm.PositionOfSecondOption = secondOptionIndex;
                                    sm.CostChangeFirstRt = costChangeFirstRoute;
                                    sm.CostChangeSecondRt = costChangeSecondRoute;
                                    sm.MoveCost = moveCost;
                                }
                            }
                        }
                    }
                }
            }
            return sm;
        }

        public void ApplySwapMove(Swap sm, Solution sol)
        {
            if ((sm.PositionOfFirstOption != 0) & (sm.MoveCost != Math.Pow(10, 9)))
            {
                Route rt1 = sol.Routes[sm.PositionOfFirstRoute];
                Route rt2 = sol.Routes[sm.PositionOfSecondRoute];
                if (!sol.CheckRouteFeasibility(rt1) || !sol.CheckRouteFeasibility(rt2))
                {
                    Console.WriteLine("-----");
                }
                Option a1 = rt1.SequenceOfOptions[sm.PositionOfFirstOption - 1];
                Option a2 = rt2.SequenceOfOptions[sm.PositionOfSecondOption - 1];
                Option b1 = rt1.SequenceOfOptions[sm.PositionOfFirstOption];
                Option b2 = rt2.SequenceOfOptions[sm.PositionOfSecondOption];
                Option c1 = rt1.SequenceOfOptions[sm.PositionOfFirstOption + 1];
                Option c2 = rt2.SequenceOfOptions[sm.PositionOfSecondOption + 1];
                rt1.SequenceOfOptions[sm.PositionOfFirstOption] = b2;
                rt1.SequenceOfCustomers[sm.PositionOfFirstOption] = b2.Cust;
                rt1.SequenceOfLocations[sm.PositionOfFirstOption] = b2.Location;
                rt2.SequenceOfOptions[sm.PositionOfSecondOption] = b1;
                rt2.SequenceOfCustomers[sm.PositionOfSecondOption] = b1.Cust;
                rt2.SequenceOfLocations[sm.PositionOfSecondOption] = b1.Location;
                if (rt1 == rt2)
                {
                    rt1.Cost += sm.MoveCost;
                    sol.UpdateTimes(rt1);
                }
                else
                {
                    rt1.Cost += sm.CostChangeFirstRt;
                    rt2.Cost += sm.CostChangeSecondRt;
                    rt1.Load = rt1.Load - b1.Cust.Dem + b2.Cust.Dem;
                    rt2.Load = rt2.Load + b1.Cust.Dem - b2.Cust.Dem;
                    sol.UpdateTimes(rt1);
                    sol.UpdateTimes(rt2);
                }
                sol.Cost += sm.MoveCost;
                sol.Promises[a1.Id, b2.Id] = sol.Cost;
                sol.Promises[b2.Id, c1.Id] = sol.Cost;
                sol.Promises[a2.Id, b1.Id] = sol.Cost;
                sol.Promises[b1.Id, c2.Id] = sol.Cost;
                if (!sol.CheckRouteFeasibility(rt1) || !sol.CheckRouteFeasibility(rt2))
                {
                    Console.WriteLine("-----");
                }
            }
        }

        public TwoOpt FindBestTwoOptMove(TwoOpt top, Solution sol) {
            for (int rtInd1 = 0; rtInd1 < sol.Routes.Count; rtInd1++) {
                Route rt1 = sol.Routes[rtInd1];
                for (int rtInd2 = 0; rtInd2 < sol.Routes.Count; rtInd2++) {
                    Route rt2 = sol.Routes[rtInd2];
                    for (int optInd1 = 0; optInd1 < rt1.SequenceOfOptions.Count - 1; optInd1++) {
                        int start2 = 0;
                        if (rt1 == rt2) {
                            start2 = optInd1 + 2;
                        }
                        for (int optInd2 = start2; optInd2 < rt2.SequenceOfOptions.Count - 1; optInd2++) {
                            double moveCost = Math.Pow(10, 9);
                            double costAdded;
                            double costRemoved;

                            Option A = rt1.SequenceOfOptions[optInd1];
                            Option B = rt1.SequenceOfOptions[optInd1 + 1];
                            Option K = rt2.SequenceOfOptions[optInd2];
                            Option L = rt2.SequenceOfOptions[optInd2 + 1];

                            var tw1 = sol.RespectsTimeWindow(rt1, optInd1,
                                            rt2.SequenceOfLocations.GetRange(optInd2 + 1, rt2.SequenceOfLocations.Count - (optInd2 + 1)));
                            var tw2 = sol.RespectsTimeWindow(rt2, optInd2,
                                            rt1.SequenceOfLocations.GetRange(optInd1 + 1, rt1.SequenceOfLocations.Count - (optInd1 + 1)));

                            bool respectsTw1 = tw1.Item1;
                            bool respectsTw2 = tw2.Item1;
                            
                            if (!respectsTw1 || !respectsTw2) { continue; }

                            if (rt1 == rt2) {
                                if (optInd1 == 0 & optInd2 == rt1.SequenceOfOptions.Count - 2) { continue; }

                                costAdded = sol.CalculateDistance(A.Location, K.Location) + sol.CalculateDistance(B.Location, L.Location);
                                costRemoved = sol.CalculateDistance(A.Location, B.Location) + sol.CalculateDistance(K.Location, L.Location);
                                moveCost = costAdded - costRemoved;

                            } else {
                                if (optInd1 == 0 && optInd2 == 0) { continue; }

                                if (optInd1 == rt1.SequenceOfOptions.Count - 2 & optInd2 == rt2.SequenceOfOptions.Count - 2) { continue; }

                                if (CapacityIsViolated(rt1, optInd1, rt2, optInd2)) { continue; }

                                costAdded = sol.CalculateDistance(A.Location, L.Location) + sol.CalculateDistance(B.Location, K.Location);
                                costRemoved = sol.CalculateDistance(A.Location, B.Location) + sol.CalculateDistance(K.Location, L.Location);
                                moveCost = costAdded - costRemoved;

                                if (moveCost < top.MoveCost & moveCost != 0)
                                {
                                    List<Option[]> arcs = new();
                                    arcs.Add(new Option[] { A, L });
                                    arcs.Add(new Option[] { B, K });
                                    if (!CheckPromises(arcs, moveCost + sol.Cost, sol)) { continue; }
                                    top.PositionOfFirstRoute = rtInd1;
                                    top.PositionOfSecondRoute = rtInd2;
                                    top.PositionOfFirstOption = optInd1;
                                    top.PositionOfSecondOption = optInd2;
                                    top.Ect1 = tw1.Item2;
                                    top.Ect2 = tw2.Item2;
                                    top.Lat1 = tw1.Item3;
                                    top.Lat2 = tw2.Item3;
                                    top.MoveCost = moveCost;
                                }
                            }
                        }
                    }
                }
            }
            return top;
        }

        public bool CapacityIsViolated(Route rt1, int optionInd1, Route rt2, int optionInd2) {
            double rt1FirstSegmentLoad = 0;
            for (int i = 0; i < optionInd1 + 1; i++) {
                Option n = rt1.SequenceOfOptions[i];
                rt1FirstSegmentLoad += n.Cust.Dem;
            }
            double rt1SecondSegmentLoad = rt1.Load - rt1FirstSegmentLoad;
            double rt2FirstSegmentLoad = 0;
            for (int i = 0; i < optionInd2 + 1; i++) {
                Option n = rt2.SequenceOfOptions[i];
                rt2FirstSegmentLoad += n.Cust.Dem;
            }
            double rt2SecondSegmentLoad = rt2.Load - rt2FirstSegmentLoad;
            if (rt1FirstSegmentLoad + rt2SecondSegmentLoad > rt1.Capacity) {
                return true;
            }
            if (rt2FirstSegmentLoad + rt1SecondSegmentLoad > rt2.Capacity) {
                return true;
            }
            return false;
        }

        public void ApplyTwoOptMove(TwoOpt top, Solution sol) {
            if ((top.Ect1 == null) || (top.MoveCost == Math.Pow(10, 9))) { return; }

            Route rt1 = sol.Routes[top.PositionOfFirstRoute];
            Route rt2 = sol.Routes[top.PositionOfSecondRoute];
            if (!sol.CheckRouteFeasibility(rt1) || !sol.CheckRouteFeasibility(rt2))
            {
                Console.WriteLine("-----");
            }
            Option A = rt1.SequenceOfOptions[top.PositionOfFirstOption];
            Option B = rt1.SequenceOfOptions[top.PositionOfFirstOption + 1];
            Option K = rt2.SequenceOfOptions[top.PositionOfSecondOption];
            Option L = rt2.SequenceOfOptions[top.PositionOfSecondOption + 1];
            if (rt1 == rt2)
            {
                // reverses the nodes in the segment [positionOfFirstNode + 1,  top.positionOfSecondNode]
                int frombase = top.PositionOfFirstOption + 1;
                int fromend = top.PositionOfSecondOption + 1;
                List<Option> reversedSegment = Enumerable.Reverse(rt1.SequenceOfOptions.GetRange(frombase, fromend - frombase)).ToList();
                List<Location> reversedLocations = Enumerable.Reverse(rt1.SequenceOfLocations.GetRange(frombase, fromend - frombase)).ToList();
                List<Customer> reversedCustomers = Enumerable.Reverse(rt1.SequenceOfCustomers.GetRange(frombase, fromend - frombase)).ToList();
                rt1.SequenceOfOptions.RemoveRange(frombase, fromend - frombase);
                rt1.SequenceOfOptions.InsertRange(frombase, reversedSegment);
                rt1.SequenceOfLocations.RemoveRange(frombase, fromend - frombase);
                rt1.SequenceOfLocations.InsertRange(frombase, reversedLocations);
                rt1.SequenceOfCustomers.RemoveRange(frombase, fromend - frombase);
                rt1.SequenceOfCustomers.InsertRange(frombase, reversedCustomers);
                rt1.Cost += top.MoveCost;
                sol.UpdateTimes(rt1);
            }
            else
            {
                int frombase = top.PositionOfFirstOption + 1;
                int fromend = top.PositionOfSecondOption + 1;
                // slice with the nodes from position top.positionOfFirstNode + 1 onwards
                List<Option> relocatedSegmentOfRt1 = rt1.SequenceOfOptions.GetRange(frombase, rt1.SequenceOfOptions.Count - frombase).ToList();
                List<Location> relocatedLocations1 = rt1.SequenceOfLocations.GetRange(frombase, rt1.SequenceOfLocations.Count - frombase).ToList();
                List<Customer> relocatedCustomers1 = rt1.SequenceOfCustomers.GetRange(frombase, rt1.SequenceOfCustomers.Count - frombase).ToList();
                // slice with the nodes from position top.positionOfFirstNode + 1 onwards
                List<Option> relocatedSegmentOfRt2 = rt2.SequenceOfOptions.GetRange(fromend, rt2.SequenceOfOptions.Count - fromend).ToList();
                List<Location> relocatedLocations2 = rt2.SequenceOfLocations.GetRange(fromend, rt2.SequenceOfLocations.Count - fromend).ToList();
                List<Customer> relocatedCustomers2 = rt2.SequenceOfCustomers.GetRange(fromend, rt2.SequenceOfCustomers.Count - fromend).ToList();

                int length = rt1.SequenceOfOptions.Count - 1;
                for (int i = length; i >= top.PositionOfFirstOption + 1; i--)
                {
                    rt1.SequenceOfOptions.RemoveAt(i);
                    rt1.SequenceOfLocations.RemoveAt(i);
                    rt1.SequenceOfCustomers.RemoveAt(i);
                }
                length = rt2.SequenceOfOptions.Count - 1;
                for (int i = length; i >= top.PositionOfSecondOption + 1; i--)
                {
                    rt2.SequenceOfOptions.RemoveAt(i);
                    rt2.SequenceOfLocations.RemoveAt(i);
                    rt2.SequenceOfCustomers.RemoveAt(i);
                }
                rt1.SequenceOfOptions.AddRange(relocatedSegmentOfRt2);
                rt2.SequenceOfOptions.AddRange(relocatedSegmentOfRt1);
                rt1.SequenceOfLocations.AddRange(relocatedLocations2);
                rt2.SequenceOfLocations.AddRange(relocatedLocations1);
                rt1.SequenceOfCustomers.AddRange(relocatedCustomers2);
                rt2.SequenceOfCustomers.AddRange(relocatedCustomers1);
                UpdateRouteCostAndLoad(rt1, sol);
                UpdateRouteCostAndLoad(rt2, sol);
                rt1.SequenceOfEct = top.Ect1.ToList();
                rt2.SequenceOfEct = top.Ect2.ToList();
                rt1.SequenceOfLat = top.Lat1.ToList();
                rt2.SequenceOfLat = top.Lat2.ToList();

            }
            sol.Cost += top.MoveCost;
            sol.Promises[A.Id, L.Id] = sol.Cost;
            sol.Promises[B.Id, K.Id] = sol.Cost;
            if (!sol.CheckRouteFeasibility(rt1) || !sol.CheckRouteFeasibility(rt2))
            {
                Console.WriteLine("-----");
            }
        }

        public Flip FindBestFlipMove(Flip flip, Solution sol)
        {
            for (int rtInd1 = 0; rtInd1 < sol.Routes.Count; rtInd1++)
            {
                Route rt1 = sol.Routes[rtInd1];

                for (int custInd1 = 1; custInd1 < rt1.SequenceOfCustomers.Count - 1; custInd1++)
                {
                    Customer custA = rt1.SequenceOfCustomers[custInd1 - 1];
                    Customer custB = rt1.SequenceOfCustomers[custInd1];
                    Customer custC = rt1.SequenceOfCustomers[custInd1 + 1];

                    // check if cust has more than 1 option
                    // new route copy of rt1 = solver.Sol.Routes[rtInd1]
                    //Route rt1_copy = solver.Sol.Routes[rtInd1];
                    Route rt1_copy = new Route(rt1);
                    rt1_copy.SequenceOfCustomers.RemoveAt(custInd1);
                    rt1_copy.SequenceOfOptions.RemoveAt(custInd1);
                    rt1_copy.SequenceOfLocations.RemoveAt(custInd1);
                    rt1_copy.SequenceOfEct.RemoveAt(custInd1);
                    rt1_copy.SequenceOfLat.RemoveAt(custInd1);
                    sol.UpdateTimes(rt1_copy);
                    rt1_copy.Load = rt1_copy.Load - custB.Dem;

                    // remove customer and update time windows and capacity
                    for (int optInd = 0; optInd < custB.Options.Count; optInd++)
                    {
                        for (int rtInd2 = 0; rtInd2 < sol.Routes.Count; rtInd2++)
                        {
                            Route rt2 = sol.Routes[rtInd2];
                            int indCust = rt1.SequenceOfCustomers.IndexOf(custB);
                            int targetRouteIndex = 0;

                            if (rt2 == rt1)
                            {
                                targetRouteIndex = custInd1 + 1;
                            }

                            if (custB.Options[optInd] == rt1.SequenceOfOptions[indCust])
                            {
                                continue;
                            }
                            if (rt2.Load + custB.Dem > rt2.Capacity)
                            {
                                continue;
                            }

                            for (int targetOptionIndex = targetRouteIndex; targetOptionIndex < rt2.SequenceOfOptions.Count - 1; targetOptionIndex++) //-1
                            {

                                double[] tw = sol.RespectsTimeWindow(rt2, targetOptionIndex, custB.Options[optInd].Location);
                                double ect = tw[0];
                                double lat = tw[1];

                                if (ect > lat) { continue; }

                                if (rt1.SequenceOfOptions[custInd1].Prio < custB.Options[optInd].Prio)
                                {
                                    continue;
                                }

                                Option A = rt1.SequenceOfOptions[custInd1 - 1];
                                Option B1 = rt1.SequenceOfOptions[custInd1];
                                Option C = rt1.SequenceOfOptions[custInd1 + 1];

                                Option F = rt2.SequenceOfOptions[targetOptionIndex];
                                Option B2 = custB.Options[optInd];
                                Option G = rt2.SequenceOfOptions[targetOptionIndex + 1];

                                if (rt1 != rt2)
                                {
                                    if (rt2.Load + custB.Dem > rt2.Capacity)
                                    {
                                        continue;
                                    }
                                }

                                double costAdded = sol.CalculateDistance(A.Location, C.Location) + sol.CalculateDistance(F.Location, B2.Location)
                                                    + sol.CalculateDistance(B2.Location, G.Location);
                                double costRemoved = sol.CalculateDistance(A.Location, B1.Location) + sol.CalculateDistance(B1.Location, C.Location)
                                                    + sol.CalculateDistance(F.Location, G.Location);
                                double moveCost = costAdded - costRemoved;

                                double costChangeOriginRt = sol.CalculateDistance(A.Location, C.Location) - sol.CalculateDistance(A.Location, B1.Location)
                                                    - sol.CalculateDistance(B1.Location, C.Location);
                                double costChangeTargetRt = sol.CalculateDistance(F.Location, B2.Location) + sol.CalculateDistance(B2.Location, G.Location)
                                                    - sol.CalculateDistance(F.Location, G.Location);

                                if (moveCost < flip.MoveCost & rtInd2 != 0)
                                {
                                    List<Option[]> arcs = new();
                                    arcs.Add(new Option[] { F, B2 });
                                    arcs.Add(new Option[] { B2, G });
                                    arcs.Add(new Option[] { A, C });
                                    if (!CheckPromises(arcs, moveCost + sol.Cost, sol)) continue;
                                    flip.MoveCost = moveCost;
                                    flip.OriginRoutePosition = rtInd1;
                                    flip.TargetRoutePosition = rtInd2;
                                    flip.TargetOptionPosition = targetOptionIndex;
                                    flip.OriginOptionPosition = custInd1;
                                    flip.CostChangeOriginRt = costChangeOriginRt;
                                    flip.CostChangeTargetRt = costChangeTargetRt;
                                    flip.NewOptionIndex = optInd;
                                }
                            }
                        }
                    }
                    sol.Routes[rtInd1] = rt1;
                }
            }
            return flip;
        }

        public void ApplyFlipMove(Flip flip, Solution sol)
        { 
            if (flip.TargetRoutePosition != 0 && flip.MoveCost != Math.Pow(10,9))
            {
                Route originRt = sol.Routes[flip.OriginRoutePosition];
                Route targetRt = sol.Routes[flip.TargetRoutePosition];
                if (!sol.CheckRouteFeasibility(targetRt) || !sol.CheckRouteFeasibility(originRt))
                {
                    Console.WriteLine("-----");
                }
                Option A = originRt.SequenceOfOptions[flip.OriginOptionPosition - 1];
                Option B1 = originRt.SequenceOfOptions[flip.OriginOptionPosition];
                Option C = originRt.SequenceOfOptions[flip.OriginOptionPosition + 1];
                Option F = targetRt.SequenceOfOptions[flip.TargetOptionPosition];
                Option G = targetRt.SequenceOfOptions[flip.TargetOptionPosition + 1];
                Option B2 = originRt.SequenceOfCustomers[flip.OriginOptionPosition].Options[flip.NewOptionIndex];

                /**
                if (originRt == targetRt)
                {
                    originRt.SequenceOfOptions.RemoveAt(flip.OriginOptionPosition);
                    originRt.SequenceOfCustomers.RemoveAt(flip.OriginOptionPosition);
                    originRt.SequenceOfLocations.RemoveAt(flip.OriginOptionPosition);
                    if (flip.OriginOptionPosition < flip.TargetOptionPosition)
                    {
                        targetRt.SequenceOfOptions.Insert(flip.TargetOptionPosition, B2);
                        targetRt.SequenceOfCustomers.Insert(flip.TargetOptionPosition, B2.Cust);
                        targetRt.SequenceOfLocations.Insert(flip.TargetOptionPosition, B2.Location);
                    }
                    else
                    {
                        targetRt.SequenceOfOptions.Insert(flip.TargetOptionPosition + 1, B2);
                        targetRt.SequenceOfCustomers.Insert(flip.TargetOptionPosition + 1, B2.Cust);
                        targetRt.SequenceOfLocations.Insert(flip.TargetOptionPosition + 1, B2.Location);
                    }
                    solver.UpdateTimes(originRt);
                    originRt.Cost += flip.MoveCost;
                }
                else
                {**/
                originRt.SequenceOfOptions.RemoveAt(flip.OriginOptionPosition);
                originRt.SequenceOfCustomers.RemoveAt(flip.OriginOptionPosition);
                originRt.SequenceOfLocations.RemoveAt(flip.OriginOptionPosition);
                originRt.SequenceOfEct.RemoveAt(flip.OriginOptionPosition);
                originRt.SequenceOfLat.RemoveAt(flip.OriginOptionPosition);

                targetRt.SequenceOfOptions.Insert(flip.TargetOptionPosition + 1, B2);
                targetRt.SequenceOfCustomers.Insert(flip.TargetOptionPosition + 1, B2.Cust);
                targetRt.SequenceOfLocations.Insert(flip.TargetOptionPosition + 1, B2.Location);
                targetRt.SequenceOfEct.Insert(flip.TargetOptionPosition + 1, 0);
                targetRt.SequenceOfLat.Insert(flip.TargetOptionPosition + 1, 0);
                if (originRt == targetRt)
                {
                    originRt.Cost += flip.MoveCost;
                    sol.UpdateTimes(originRt);
                } else 
                { 
                    originRt.Cost += flip.CostChangeOriginRt;
                    targetRt.Cost += flip.CostChangeTargetRt;
                    originRt.Load -= B1.Cust.Dem;
                    targetRt.Load += B2.Cust.Dem;
                    sol.UpdateTimes(originRt);
                    sol.UpdateTimes(targetRt);
                }
                sol.Cost += flip.MoveCost;
                B1.IsServed = false; B2.IsServed = true;
                sol.Promises[A.Id, C.Id] = sol.Cost;
                sol.Promises[F.Id, B2.Id] = sol.Cost;
                sol.Promises[B2.Id, G.Id] = sol.Cost;
                if (!sol.CheckRouteFeasibility(targetRt) || !sol.CheckRouteFeasibility(originRt))
                {
                    Console.WriteLine("-----");
                }
            }
        }




        public void UpdateRouteCostAndLoad(Route rt, Solution sol) {
            double tc = 0;
            double tl = 0;
            for (int i = 0; i < rt.SequenceOfOptions.Count - 1; i++) {
                Option A = rt.SequenceOfOptions[i];
                Option B = rt.SequenceOfOptions[i + 1];
                tc += sol.CalculateDistance(A.Location, B.Location);
                tl += A.Cust.Dem;
            }
            rt.Load = tl;
            rt.Cost = tc;
        }

        bool CheckPromises(List<Option[]> arcs, double newCost, Solution sol)
        {
            foreach(Option[] arc in arcs)
            {
                if (newCost >= sol.Promises[arc[0].Id, arc[1].Id])
                {
                    return false;
                }
            }
            return true;
        }
    }

}
