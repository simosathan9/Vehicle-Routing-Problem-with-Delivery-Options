using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace VrdpoProject
{

    public class LocalSearch
    {

        private Route rt1, rt2;
        public Relocation FindBestRelocationMove(Relocation rm, Solver solver)
        {
            Solution currentSol = solver.Sol;
            for (int originRouteIndex = 0; originRouteIndex < currentSol.Routes.Count; originRouteIndex++)
            {
                rt1 = currentSol.Routes[originRouteIndex];

                for (int targetRouteIndex = 0; targetRouteIndex < currentSol.Routes.Count; targetRouteIndex++)
                {
                    rt2 = currentSol.Routes[targetRouteIndex];

                    for (int originOptionIndex = 1; originOptionIndex < rt1.SequenceOfOptions.Count - 1; originOptionIndex++)
                    {
                        for (int targetOptionIndex = 0; targetOptionIndex < rt2.SequenceOfOptions.Count - 1; targetOptionIndex++)
                        {
                            if (originRouteIndex == targetRouteIndex && (targetOptionIndex == originOptionIndex || targetOptionIndex == originOptionIndex - 1))
                            {
                                continue;
                            }

                            double[] tw = solver.RespectsTimeWindow(rt2, targetOptionIndex, rt1.SequenceOfLocations[originOptionIndex]);
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

                            double costAdded = solver.CalculateDistance(A.Location, C.Location) + solver.CalculateDistance(F.Location, B.Location)
                                                + solver.CalculateDistance(B.Location, G.Location);
                            double costRemoved = solver.CalculateDistance(A.Location, B.Location) + solver.CalculateDistance(B.Location, C.Location)
                                                + solver.CalculateDistance(F.Location, G.Location);
                            double moveCost = costAdded - costRemoved;

                            double costChangeOriginRt = solver.CalculateDistance(A.Location, C.Location) - solver.CalculateDistance(A.Location, B.Location)
                                                - solver.CalculateDistance(B.Location, C.Location);
                            double costChangeTargetRt = solver.CalculateDistance(F.Location, B.Location) + solver.CalculateDistance(B.Location, G.Location)
                                                - solver.CalculateDistance(F.Location, G.Location);

                            if (moveCost < rm.MoveCost & targetRouteIndex != 0)
                            {
                                List<Option[]> arcs = new();
                                arcs.Add(new Option[] { F, B });
                                arcs.Add(new Option[] { B, G });
                                arcs.Add(new Option[] { A, C });
                                if (!CheckPromises(arcs, moveCost + solver.Sol.Cost, solver)) continue;
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
        public void ApplyRelocationMove(Relocation rm, Solver solver)
        {
            if (rm.TargetRoutePosition != 0)
            {
                Solution currentSol = solver.Sol;
                Route originRt = currentSol.Routes[rm.OriginRoutePosition];
                Route targetRt = currentSol.Routes[rm.TargetRoutePosition];
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
                    solver.UpdateTimes(originRt);
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
                    solver.UpdateTimes(originRt);//, rm.OriginRoutePosition - 1, rm.OriginRoutePosition - 2);
                    //solver.UpdateTimes(targetRt, rm.TargetRoutePosition + 1, rm.TargetRoutePosition + 1);
                    //solver.UpdateTimes(targetRt, 1, targetRt.SequenceOfLocations.Count - 3);
                    solver.UpdateTimes(targetRt);
                }
                currentSol.Cost += rm.MoveCost;
                solver.Promises[A.Id, C.Id] = currentSol.Cost;
                solver.Promises[F.Id, B.Id] = currentSol.Cost;
                solver.Promises[B.Id, G.Id] = currentSol.Cost;
            }
        }

        public Swap FindBestSwapMove(Swap sm, Solver solver)
        {
            Route rt1, rt2;
            int startOfSecondOptionIndex;
            Option a1, b1, c1, a2, b2, c2;
            for (int firstRouteIndex = 0; firstRouteIndex < solver.Sol.Routes.Count; firstRouteIndex++)
            {
                rt1 = solver.Sol.Routes[firstRouteIndex];
                for (int secondRouteIndex = firstRouteIndex; secondRouteIndex < solver.Sol.Routes.Count; secondRouteIndex++)
                {
                    rt2 = solver.Sol.Routes[secondRouteIndex];
                    for (int firstOptionIndex = 1; firstOptionIndex < rt1.SequenceOfOptions.Count - 1; firstOptionIndex++)
                    {
                        //Na to doume
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

                            double[] tw1 = solver.RespectsTimeWindow(rt1, firstOptionIndex, b2.Location);
                            double[] tw2 = solver.RespectsTimeWindow(rt2, secondOptionIndex, b1.Location);
                            double ect1 = tw1[0];
                            double lat1 = tw1[1];
                            double ect2 = tw2[0];
                            double lat2 = tw2[1];

                            if (ect1 > lat1 || ect2 > lat2) { continue; }

                            if (rt1 == rt2)
                            {
                                if (firstOptionIndex == secondOptionIndex - 1)
                                {
                                    double costRemoved = solver.CalculateDistance(a1.Location, b1.Location) + solver.CalculateDistance(b1.Location, b2.Location) + solver.CalculateDistance(b2.Location, c2.Location);
                                    double costAdded = solver.CalculateDistance(a1.Location, b2.Location) + solver.CalculateDistance(b2.Location, b1.Location) + solver.CalculateDistance(b1.Location, c2.Location);
                                    moveCost = costAdded - costRemoved;
                                } else {
                                    double costRemoved1 = solver.CalculateDistance(a1.Location, b1.Location) + solver.CalculateDistance(b1.Location, c1.Location);
                                    double costAdded1 = solver.CalculateDistance(a1.Location, b2.Location) + solver.CalculateDistance(b2.Location, c1.Location);
                                    double costRemoved2 = solver.CalculateDistance(a2.Location, b2.Location) + solver.CalculateDistance(b2.Location, c2.Location);
                                    double costAdded2 = solver.CalculateDistance(a2.Location, b1.Location) + solver.CalculateDistance(b1.Location, c2.Location);
                                    moveCost = costAdded1 + costAdded2 - (costRemoved1 + costRemoved2);
                                }
                            } else {
                                if (rt1.Load - b1.Cust.Dem + b2.Cust.Dem > rt1.Capacity) { continue; }
                                if (rt2.Load - b2.Cust.Dem + b1.Cust.Dem > rt2.Capacity) { continue; }
                                double costRemoved1 = solver.CalculateDistance(a1.Location, b1.Location) + solver.CalculateDistance(b1.Location, c1.Location);
                                double costAdded1 = solver.CalculateDistance(a1.Location, b2.Location) + solver.CalculateDistance(b2.Location, c1.Location);
                                double costRemoved2 = solver.CalculateDistance(a2.Location, b2.Location) + solver.CalculateDistance(b2.Location, c2.Location);
                                double costAdded2 = solver.CalculateDistance(a2.Location, b1.Location) + solver.CalculateDistance(b1.Location, c2.Location);
                                costChangeFirstRoute = costAdded1 - costRemoved1;
                                costChangeSecondRoute = costAdded2 - costRemoved2;
                                moveCost = costAdded1 + costAdded2 - (costRemoved1 + costRemoved2);
                                if (moveCost < sm.MoveCost)
                                {
                                    List<Option[]> arcs = new();
                                    arcs.Add(new Option[] { a1, b2 });
                                    arcs.Add(new Option[] { b2, c1 });
                                    arcs.Add(new Option[] { a2, b1 });
                                    arcs.Add(new Option[] { b1, c2 });
                                    if (!CheckPromises(arcs, moveCost + solver.Sol.Cost, solver)) continue;
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

        public void ApplySwapMove(Swap sm, Solver solver)
        {
            if (sm.PositionOfFirstOption != 0)
            {
                Solution currentSol = solver.Sol;
                Route rt1 = solver.Sol.Routes[sm.PositionOfFirstRoute];
                Route rt2 = solver.Sol.Routes[sm.PositionOfSecondRoute];
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
                    solver.UpdateTimes(rt1);
                }
                else
                {
                    rt1.Cost += sm.CostChangeFirstRt;
                    rt2.Cost += sm.CostChangeSecondRt;
                    rt1.Load = rt1.Load - b1.Cust.Dem + b2.Cust.Dem;
                    rt2.Load = rt2.Load + b1.Cust.Dem - b2.Cust.Dem;
                    solver.UpdateTimes(rt1);
                    solver.UpdateTimes(rt2);
                }
                solver.Sol.Cost += sm.MoveCost;
                solver.Promises[a1.Id, b2.Id] = currentSol.Cost;
                solver.Promises[b2.Id, c1.Id] = currentSol.Cost;
                solver.Promises[a2.Id, b1.Id] = currentSol.Cost;
                solver.Promises[b1.Id, c2.Id] = currentSol.Cost;
            }
        }

        public TwoOpt FindBestTwoOptMove(TwoOpt top, Solver solver) {
            for (int rtInd1 = 0; rtInd1 < solver.Sol.Routes.Count; rtInd1++) {
                Route rt1 = solver.Sol.Routes[rtInd1];
                for (int rtInd2 = 0; rtInd2 < solver.Sol.Routes.Count; rtInd2++) {
                    Route rt2 = solver.Sol.Routes[rtInd2];
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

                            bool respectsTw1 = solver.RespectsTimeWindow(rt1, optInd1,
                                            rt2.SequenceOfLocations.GetRange(optInd2 + 1, rt2.SequenceOfLocations.Count - (optInd2 + 1)));
                            bool respectsTw2 = solver.RespectsTimeWindow(rt2, optInd2,
                                            rt1.SequenceOfLocations.GetRange(optInd1 + 1, rt1.SequenceOfLocations.Count - (optInd1 + 1)));
                            
                            if (!respectsTw1 || !respectsTw2) { continue; }

                            if (rt1 == rt2) {
                                if (optInd1 == 0 & optInd2 == rt1.SequenceOfOptions.Count - 2) { continue; }

                                costAdded = solver.CalculateDistance(A.Location, K.Location) + solver.CalculateDistance(B.Location, L.Location);
                                costRemoved = solver.CalculateDistance(A.Location, B.Location) + solver.CalculateDistance(K.Location, L.Location);
                                moveCost = costAdded - costRemoved;

                            } else {
                                if (optInd1 == 0 && optInd2 == 0) { continue; }

                                if (optInd1 == rt1.SequenceOfOptions.Count - 2 & optInd2 == rt2.SequenceOfOptions.Count - 2) { continue; }

                                if (CapacityIsViolated(rt1, optInd1, rt2, optInd2)) { continue; }

                                costAdded = solver.CalculateDistance(A.Location, L.Location) + solver.CalculateDistance(B.Location, K.Location);
                                costRemoved = solver.CalculateDistance(A.Location, B.Location) + solver.CalculateDistance(K.Location, L.Location);
                                moveCost = costAdded - costRemoved;

                                if (moveCost < top.MoveCost)
                                {
                                    List<Option[]> arcs = new();
                                    arcs.Add(new Option[] { A, L });
                                    arcs.Add(new Option[] { B, K });
                                    if (!CheckPromises(arcs, moveCost + solver.Sol.Cost, solver)) { continue; }
                                    top.PositionOfFirstRoute = rtInd1;
                                    top.PositionOfSecondRoute = rtInd2;
                                    top.PositionOfFirstOption = optInd1;
                                    top.PositionOfSecondOption = optInd2;
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

        public void ApplyTwoOptMove(TwoOpt top, Solver solver) {
            Solution currentSol = solver.Sol;
            Route rt1 = solver.Sol.Routes[top.PositionOfFirstRoute];
            Route rt2 = solver.Sol.Routes[top.PositionOfSecondRoute];
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
                //rt1.SequenceOfOptions[frombase..fromend] = reversedSegment;//???
                rt1.SequenceOfOptions.RemoveRange(frombase, fromend - frombase);
                rt1.SequenceOfOptions.InsertRange(frombase, reversedSegment);
                rt1.Cost += top.MoveCost;
                solver.UpdateTimes(rt1);
            }
            else
            {
                int frombase = top.PositionOfFirstOption + 1;
                int fromend = top.PositionOfSecondOption + 1;
                // slice with the nodes from position top.positionOfFirstNode + 1 onwards
                List<Option> relocatedSegmentOfRt1 = rt1.SequenceOfOptions.GetRange(frombase, rt1.SequenceOfOptions.Count - frombase).ToList();
                // slice with the nodes from position top.positionOfFirstNode + 1 onwards
                List<Option> relocatedSegmentOfRt2 = rt2.SequenceOfOptions.GetRange(fromend, rt2.SequenceOfOptions.Count - fromend).ToList();

                int length = rt1.SequenceOfOptions.Count - 1;
                for (int i = length; i >= top.PositionOfFirstOption + 1; i--)
                {
                    rt1.SequenceOfOptions.RemoveAt(i);
                }
                length = rt2.SequenceOfOptions.Count - 1;
                for (int i = length; i >= top.PositionOfSecondOption + 1; i--)
                {
                    rt2.SequenceOfOptions.RemoveAt(i);
                }
                rt1.SequenceOfOptions.InsertRange(0, relocatedSegmentOfRt2);
                rt2.SequenceOfOptions.InsertRange(0, relocatedSegmentOfRt1);
                UpdateRouteCostAndLoad(rt1, solver);
                UpdateRouteCostAndLoad(rt2, solver);
                solver.UpdateTimes(rt1);
                solver.UpdateTimes(rt2);
            }
            solver.Sol.Cost += top.MoveCost;
            solver.Promises[A.Id, L.Id] = currentSol.Cost;
            solver.Promises[B.Id, K.Id] = currentSol.Cost;
        }

        public void UpdateRouteCostAndLoad(Route rt, Solver solver) {
            double tc = 0;
            double tl = 0;
            for (int i = 0; i < rt.SequenceOfOptions.Count - 1; i++) {
                Option A = rt.SequenceOfOptions[i];
                Option B = rt.SequenceOfOptions[i + 1];
                tc += solver.CalculateDistance(A.Location, B.Location);
                tl += A.Cust.Dem;
            }
            rt.Load = tl;
            rt.Cost = tc;
        }

        bool CheckPromises(List<Option[]> arcs, double newCost, Solver solver)
        {
            foreach(Option[] arc in arcs)
            {
                if (newCost >= solver.Promises[arc[0].Id, arc[1].Id])
                {
                    return false;
                }
            }
            return true;
        }
    }

}
