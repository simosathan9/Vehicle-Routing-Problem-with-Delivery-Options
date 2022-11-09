using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VrdpoProject
{
    
    public class LocalSearch
    {

        private Route rt1, rt2;
        public void FindBestRelocationMove(Relocation rm, Solver solver)
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

                            Option A = rt1.SequenceOfOptions[originOptionIndex - 1];
                            Option B = rt1.SequenceOfOptions[originOptionIndex];
                            Option C = rt1.SequenceOfOptions[originOptionIndex + 1];

                            Option F = rt2.SequenceOfOptions[targetOptionIndex];
                            Option G = rt2.SequenceOfOptions[targetOptionIndex + 1];

                            if (rt1 != rt2)
                            {
                                double ect = solver.RespectsTimeWindow(rt2, targetOptionIndex, rt2.SequenceOfLocations[targetOptionIndex])[0];
                                double lat = solver.RespectsTimeWindow(rt2, targetOptionIndex, rt2.SequenceOfLocations[targetOptionIndex])[1];
                                if (rt2.Load + B.Cust.Dem > rt2.Capacity && ect <= lat)
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

                            if (moveCost < rm.MoveCost)
                            {
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
            ApplyRelocationMove(rm, solver);
        }

        void ApplyRelocationMove(Relocation rm, Solver solver)
        {
            Solution currentSol = solver.Sol;
            Route originRt = currentSol.Routes[rm.OriginRoutePosition];
            Route targetRt = currentSol.Routes[rm.TargetRoutePosition];
            Option A = originRt.SequenceOfOptions[rm.OriginOptionPosition - 1];
            Option B = originRt.SequenceOfOptions[rm.OriginOptionPosition];
            Option C = originRt.SequenceOfOptions[rm.OriginOptionPosition + 1];
            Option F = targetRt.SequenceOfOptions[rm.TargetOptionPosition];
            Option G = targetRt.SequenceOfOptions[rm.TargetOptionPosition + 1];
            //SetTabuIteratorArc(A, B, iterator)
            //SetTabuIteratorArc(B, C, iterator)
            //SetTabuIteratorArc(F, G, iterator)
            if (originRt == targetRt)
            {
                originRt.SequenceOfOptions.RemoveAt(rm.OriginOptionPosition);
                if (rm.OriginOptionPosition < rm.TargetOptionPosition)
                {
                    targetRt.SequenceOfOptions.Insert(rm.TargetOptionPosition, B);
                } else {
                    targetRt.SequenceOfOptions.Insert(rm.TargetOptionPosition + 1, B);
                }
                originRt.Cost += rm.MoveCost;
            } else {
                originRt.SequenceOfOptions.RemoveAt(rm.OriginOptionPosition);
                targetRt.SequenceOfOptions.Insert(rm.TargetOptionPosition + 1, B);
                originRt.Cost += rm.CostChangeOriginRt;
                targetRt.Cost += rm.CostChangeTargetRt;
                originRt.Load -= B.Cust.Dem;
                targetRt.Load += B.Cust.Dem;
            }
            currentSol.Cost += rm.MoveCost;
            //self.SetTabuIterator(B, iterator)
        }
    }

}
