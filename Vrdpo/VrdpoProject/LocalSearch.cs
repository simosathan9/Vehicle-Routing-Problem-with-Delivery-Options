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

        public TwoOpt FindBestTwoOptMove(TwoOpt top) {
            for (int rtInd1 in range(0, len(self.sol.routes))) {
                Route rt1 = self.sol.routes[rtInd1];
                for (int rtInd2 in range(rtInd1, len(self.sol.routes))) {
                    Route rt2 = self.sol.routes[rtInd2];
                    for (int optInd1 in range(0, len(rt1.sequenceOfNodes) - 1)) {
                        int start2 = 0;
                        if (rt1 == rt2) {
                            start2 = nodeInd1 + 2;
                        }
                        for (int OptInd2 in range(start2, len(rt2.sequenceOfNodes) - 1)) {
                            int moveCost = 10 * *9;
                            Option A = rt1.sequenceOfOptions[optionInd1];
                            Option B = rt1.sequenceOfOptions[optionInd1 + 1];
                            Option K = rt2.sequenceOfOptions[optionInd2];
                            Option L = rt2.sequenceOfOptions[optionInd2 + 1];
                            if (rt1 == rt2) {
                                if (nodeInd1 == 0 & nodeInd2 == len(rt1.sequenceOfNodes) - 2) {
                                    continue;
                                }
                                double costAdded = self.distanceMatrix[A.ID][K.ID] + self.distanceMatrix[B.ID][L.ID];
                                double costRemoved = self.distanceMatrix[A.ID][B.ID] + self.distanceMatrix[K.ID][L.ID];
                                moveCost = costAdded - costRemoved;
                            else {
                                    if (nodeInd1 == 0 && nodeInd2 == 0) {
                                        continue;
                                    }
                                    if (nodeInd1 == len(rt1.sequenceOfNodes) - 2 and nodeInd2 == len(rt2.sequenceOfNodes) - 2) {
                                        continue;
                                    }
                                    if (self.CapacityIsViolated(rt1, nodeInd1, rt2, nodeInd2)) {
                                        continue;
                                    }
                                    costAdded = self.distanceMatrix[A.ID][L.ID] + self.distanceMatrix[B.ID][K.ID];
                                    costRemoved = self.distanceMatrix[A.ID][B.ID] + self.distanceMatrix[K.ID][L.ID];
                                    moveCost = costAdded - costRemoved;
                                }
                                if (self.MoveIsTabuArc(A, K, iterator, moveCost) or self.MoveIsTabuArc(B, L, iterator, moveCost)) {
                                    continue;
                                }
                                if moveCost < top.moveCost and abs(moveCost) {
                                        self.StoreBestTwoOptMove(rtInd1, rtInd2, nodeInd1, nodeInd2, moveCost, top);
                                }
                            }
                        }
                    }
                }
            }
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
