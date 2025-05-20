using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;

namespace VrdpoProject
{

    public class LocalSearch
    {
        public static int power = 2;
        private double smallDouble;
        private Route rt1, rt2;
        public LocalSearch()
        {
            string jsonContent = File.ReadAllText("settings.json");
            var settings = JsonSerializer.Deserialize<Settings>(jsonContent);
            if (settings.type == "double")
            {
                this.smallDouble = 0.00001;
            } else if (settings.type == "int")
            {
                this.smallDouble = 0;
            }
        }
        public Relocation FindBestRelocationMove(Relocation rm, Solution sol)
        {
            int openRoutes;
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
                            openRoutes = sol.Routes.Count;
                            if (originRouteIndex == targetRouteIndex && (targetOptionIndex == originOptionIndex || targetOptionIndex == originOptionIndex - 1))
                            {
                                continue;
                            }

                            var tw = sol.RespectsTimeWindow2(rt2, targetOptionIndex,
                                            rt1.SequenceOfLocations[originOptionIndex]);

                            if (!tw.Item1) { continue; };

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
                            if (rt1.Load - B.Cust.Dem == 0) { // if route becomes empty
                                //Console.WriteLine("This RELOCATION move empties a route");
                                //Console.WriteLine("Routes before : " + openRoutes);
                                openRoutes--;
                                //Console.WriteLine("Routes after : " + openRoutes);
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

                            var newUtilizationMetricRoute1 = Math.Pow(Convert.ToDouble(rt1.Capacity - (rt1.Load - B.Cust.Dem)), power);
                            var newUtilizationMetricRoute2 = Math.Pow(Convert.ToDouble(rt2.Capacity - (rt2.Load + B.Cust.Dem)), power);
                            var newSolUtilizationMetric = sol.SolutionUtilizationMetric - rt1.RouteUtilizationMetric - rt2.RouteUtilizationMetric + newUtilizationMetricRoute1 + newUtilizationMetricRoute2;
                            var ratio = (sol.SolutionUtilizationMetric + 1) / (newSolUtilizationMetric + 1);
                            if (sol.Routes.Count == sol.LowerBoundRoutes)
                            {
                                ratio = 1;
                            }
                            //else
                            //{
                            //    ratio = Math.Clamp(ratio, 0.8, 1.3);
                            //}

                            sol.RatioCombinedMoveCost = ratio * moveCost;

                            //favor relocations from very small routes
                            //int bonus = 0;
                            //if (rt1.SequenceOfLocations.Count <= 4)
                            //{
                            //    bonus = -2000;
                            //}
                            ////prevent relocating to empty/small routes
                            //if (rt2.SequenceOfLocations.Count <= 4)
                            //{
                            //    continue;
                            //}
                            if (sol.RatioCombinedMoveCost + openRoutes * 10000 < rm.TotalCost + smallDouble & targetRouteIndex != 0 & moveCost != 0) // + bpnus
                            {
                                // Console.WriteLine("Total cost : " + rm.TotalCost + " Open Routes : " + openRoutes);
                                if (PromiseIsBroken(F.Id,B.Id, moveCost + sol.Cost + smallDouble, sol))
                                {
                                    continue;
                                }
                                if (PromiseIsBroken(B.Id, G.Id, moveCost + sol.Cost + smallDouble, sol))
                                {
                                    continue;
                                }
                                if (PromiseIsBroken(A.Id, C.Id, moveCost + sol.Cost + smallDouble, sol))
                                {
                                    continue;
                                }
                                
                                rm.TotalCost = moveCost + openRoutes * 10000;
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
            if (rm.IsValid())
            {
                sol.LastMove = "relocate";
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
                    UpdateRouteCostAndLoad(originRt, sol);
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
                    originRt.RouteUtilizationMetric = Math.Pow(Convert.ToDouble(originRt.Capacity - originRt.Load), 2);
                    targetRt.RouteUtilizationMetric = Math.Pow(Convert.ToDouble(targetRt.Capacity - targetRt.Load), 2);
                    sol.UpdateTimes(originRt);
                    sol.UpdateTimes(targetRt);
                    UpdateRouteCostAndLoad(originRt, sol);
                    UpdateRouteCostAndLoad(targetRt, sol);
                }
                sol.Cost += rm.MoveCost;
                sol.Promises[A.Id, C.Id] = sol.Cost;
                sol.Promises[F.Id, B.Id] = sol.Cost;
                sol.Promises[B.Id, G.Id] = sol.Cost;
                if (!sol.CheckRouteFeasibility(originRt))
                {
                    Console.WriteLine("-----");
                    sol.CheckRouteFeasibility(originRt);
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
            int openRoutes;
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
                            openRoutes = sol.Routes.Count;
                            a1 = rt1.SequenceOfOptions[firstOptionIndex - 1];
                            b1 = rt1.SequenceOfOptions[firstOptionIndex];
                            c1 = rt1.SequenceOfOptions[firstOptionIndex + 1];
                            a2 = rt2.SequenceOfOptions[secondOptionIndex - 1];
                            b2 = rt2.SequenceOfOptions[secondOptionIndex];
                            c2 = rt2.SequenceOfOptions[secondOptionIndex + 1];

                            double moveCost;
                            double costChangeFirstRoute = 0;
                            double costChangeSecondRoute = 0;
                            double ratio = 1;

                            var tw1 = sol.RespectsTimeWindow2(rt1, firstOptionIndex, b2.Location);
                            var tw2 = sol.RespectsTimeWindow2(rt2, secondOptionIndex, b1.Location);  

                            if (!tw1.Item1 || !tw2.Item1) { continue; }

                            if (rt1 == rt2)
                            {
                                Route rtTemp = rt1.getTempCopy(rt1, sol.Options.Select(x => x.Location).ToHashSet().ToList());
                                rtTemp.SequenceOfOptions[firstOptionIndex] = b2;
                                rtTemp.SequenceOfCustomers[firstOptionIndex] = b2.Cust;
                                rtTemp.SequenceOfLocations[firstOptionIndex] = b2.Location;
                                tw2 = sol.RespectsTimeWindow2(rtTemp, secondOptionIndex, b1.Location);
                                if (!tw2.Item1)
                                {
                                    continue;
                                }
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
                                var newUtilizationMetricRoute1 = Math.Pow(Convert.ToDouble(rt1.Capacity - (rt1.Load - b1.Cust.Dem + b2.Cust.Dem)), power);
                                var newUtilizationMetricRoute2 = Math.Pow(Convert.ToDouble(rt2.Capacity - (rt2.Load - b2.Cust.Dem + b1.Cust.Dem)), power);
                                var newSolUtilizationMetric = sol.SolutionUtilizationMetric - rt1.RouteUtilizationMetric - rt2.RouteUtilizationMetric + newUtilizationMetricRoute1 + newUtilizationMetricRoute2;
                                ratio = (sol.SolutionUtilizationMetric + 1) / (newSolUtilizationMetric + 1);
                                if (sol.Routes.Count == sol.LowerBoundRoutes)
                                {
                                    ratio = 1;
                                }
                                //else
                                //{
                                //    ratio = Math.Clamp(ratio, 0.8, 1.3);
                                //}
                            }

                            if (ratio * moveCost < sm.MoveCost + smallDouble & moveCost !=0)
                            {
                                if (PromiseIsBroken(a1.Id, b2.Id, moveCost + sol.Cost + smallDouble, sol))
                                {
                                    continue;
                                }
                                if (PromiseIsBroken(b2.Id, c1.Id, moveCost + sol.Cost + smallDouble, sol))
                                {
                                    continue;
                                }
                                if (PromiseIsBroken(a2.Id, b1.Id, moveCost + sol.Cost + smallDouble, sol))
                                {
                                    continue;
                                }                                    
                                if (PromiseIsBroken(b1.Id, c2.Id, moveCost + sol.Cost + smallDouble, sol))
                                {
                                    continue;
                                }

                                sm.TotalCost = moveCost + openRoutes * 10000;
                                sm.PositionOfFirstRoute = firstRouteIndex;
                                sm.PositionOfSecondRoute = secondRouteIndex;
                                sm.PositionOfFirstOption = firstOptionIndex;
                                sm.PositionOfSecondOption = secondOptionIndex;
                                sm.MoveCost = moveCost;

                                if (rt1 != rt2)
                                {
                                    sm.CostChangeFirstRt = costChangeFirstRoute;
                                    sm.CostChangeSecondRt = costChangeSecondRoute;
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
            if (sm.IsValid())
            {
                sol.LastMove = "swap";
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
                    UpdateRouteCostAndLoad(rt1, sol);
                    sol.UpdateTimes(rt1);
                }
                else
                {
                    rt1.Cost += sm.CostChangeFirstRt;
                    rt2.Cost += sm.CostChangeSecondRt;
                    rt1.Load = rt1.Load - b1.Cust.Dem + b2.Cust.Dem;
                    rt2.Load = rt2.Load + b1.Cust.Dem - b2.Cust.Dem;
                    rt1.RouteUtilizationMetric = Math.Pow(Convert.ToDouble(rt1.Capacity - rt1.Load), 2);
                    rt2.RouteUtilizationMetric = Math.Pow(Convert.ToDouble(rt2.Capacity - rt2.Load), 2);
                    UpdateRouteCostAndLoad(rt1, sol);
                    UpdateRouteCostAndLoad(rt2, sol);
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
            int openRoutes;
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
                            openRoutes = sol.Routes.Count;

                            double moveCost;
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

                                //tw2 = sol.RespectsTimeWindow(rt1, optInd2, rt1.SequenceOfLocations.GetRange(optInd1 + 1, rt1.SequenceOfLocations.Count - (optInd1 + 1)));
                                //respectsTw1 = tw1.Item1;
                                //respectsTw2 = tw2.Item1;
                                //if (!respectsTw1 || !respectsTw2) { continue; }

                                Route rtTemp = rt1.getTempCopy(rt1, sol.Options.Select(x => x.Location).ToHashSet().ToList());
                                int frombase = optInd1 + 1;
                                int fromend = optInd2 + 1;
                                List<Option> reversedSegment = Enumerable.Reverse(rtTemp.SequenceOfOptions.GetRange(frombase, fromend - frombase)).ToList();
                                List<Location> reversedLocations = Enumerable.Reverse(rtTemp.SequenceOfLocations.GetRange(frombase, fromend - frombase)).ToList();
                                List<Customer> reversedCustomers = Enumerable.Reverse(rtTemp.SequenceOfCustomers.GetRange(frombase, fromend - frombase)).ToList();
                                rtTemp.SequenceOfOptions.RemoveRange(frombase, fromend - frombase);
                                rtTemp.SequenceOfOptions.InsertRange(frombase, reversedSegment);
                                rtTemp.SequenceOfLocations.RemoveRange(frombase, fromend - frombase);
                                rtTemp.SequenceOfLocations.InsertRange(frombase, reversedLocations);
                                rtTemp.SequenceOfCustomers.RemoveRange(frombase, fromend - frombase);
                                rtTemp.SequenceOfCustomers.InsertRange(frombase, reversedCustomers);
                                rtTemp.SequenceOfEct = new List<double>(rt1.SequenceOfEct);
                                rtTemp.SequenceOfLat = new List<double>(rt1.SequenceOfLat);
                                // Update rtTemp times and check if it respects time windows
                                // Parse the sequence of options and update the list of ect and lat
                                for (int i = 0; i < rtTemp.SequenceOfOptions.Count - 1; i++)
                                {
                                    rtTemp.SequenceOfEct[i] = rtTemp.SequenceOfOptions[i].Due;
                                    rtTemp.SequenceOfLat[i] = rtTemp.SequenceOfOptions[i].Ready;
                                }

                                
                                if (!rtTemp.CheckTimeWindowsFeasibility()) 
                                { 
                                    continue; 
                                }

                                costAdded = sol.CalculateDistance(A.Location, K.Location) + sol.CalculateDistance(B.Location, L.Location);
                                costRemoved = sol.CalculateDistance(A.Location, B.Location) + sol.CalculateDistance(K.Location, L.Location);
                                moveCost = costAdded - costRemoved;
                                sol.RatioCombinedMoveCost = moveCost;
                            } else {
                                if (optInd1 == 0 && optInd2 == 0) { continue; }

                                if (optInd1 == rt1.SequenceOfOptions.Count - 2 & optInd2 == rt2.SequenceOfOptions.Count - 2) { continue; }

                                if (CapacityIsViolated(rt1, optInd1, rt2, optInd2)) { continue; }

                                costAdded = sol.CalculateDistance(A.Location, L.Location) + sol.CalculateDistance(B.Location, K.Location);
                                costRemoved = sol.CalculateDistance(A.Location, B.Location) + sol.CalculateDistance(K.Location, L.Location);
                                moveCost = costAdded - costRemoved;
                                if (rt1.Load - B.Cust.Dem == 0 || rt2.Load - K.Cust.Dem == 0) {
                                    //Console.WriteLine("This TWO-OPT move empties a route");
                                    openRoutes--;
                                }
                                var newUtilizationMetricRoute1 = Math.Pow(Convert.ToDouble(rt1.Capacity - (rt1.Load - B.Cust.Dem)), power);
                                var newUtilizationMetricRoute2 = Math.Pow(Convert.ToDouble(rt2.Capacity - (rt2.Load - K.Cust.Dem)), power);
                                var newSolUtilizationMetric = sol.SolutionUtilizationMetric - rt1.RouteUtilizationMetric - rt2.RouteUtilizationMetric + newUtilizationMetricRoute1 + newUtilizationMetricRoute2;
                                var ratio = (sol.SolutionUtilizationMetric + 1) / (newSolUtilizationMetric + 1);
                                if (sol.Routes.Count == sol.LowerBoundRoutes)
                                {
                                    ratio = 1;
                                }
                                //else
                                //{
                                //    ratio = Math.Clamp(ratio, 0.8, 1.3);
                                //}
                                sol.RatioCombinedMoveCost = ratio * moveCost;
                            }

                            if (sol.RatioCombinedMoveCost + openRoutes * 10000 < top.TotalCost + smallDouble & moveCost != 0)
                            {

                                if (PromiseIsBroken(A.Id, L.Id, moveCost + sol.Cost + smallDouble, sol))
                                {
                                    continue;
                                }
                                if (PromiseIsBroken(B.Id, K.Id, moveCost + sol.Cost + smallDouble, sol))
                                {
                                    continue;
                                }

                                top.TotalCost = moveCost + openRoutes * 10000;
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
            if (!top.IsValid()) { return; }
            sol.LastMove = "two opt";
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
            int frombase = top.PositionOfFirstOption + 1;
            int fromend = top.PositionOfSecondOption + 1;
            if (rt1 == rt2)
            {
                // reverses the nodes in the segment [positionOfFirstNode + 1,  top.positionOfSecondNode]
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
                UpdateRouteCostAndLoad(rt1, sol);
                rt1.RouteUtilizationMetric = Math.Pow(Convert.ToDouble(rt1.Capacity - rt1.Load), 2);
            }
            else
            {
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
                rt1.RouteUtilizationMetric = Math.Pow(Convert.ToDouble(rt1.Capacity - rt1.Load), 2);
                rt2.RouteUtilizationMetric = Math.Pow(Convert.ToDouble(rt2.Capacity - rt2.Load), 2);
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

        // 1) Added mechanisms to allow flips that reduce the overall service level if the bottom levels are not violated
        // 2) Removed rtInd2 != 0 check
        // 3) Added check for selecting customers with >1 options only
        // 4) Added check to avoid target options that are same with the current served option for the examined customer
        public Flip FindBestFlipMove(Flip flip, Solution sol, bool cond = false)
        {
            int openRoutes;
            for (int rtInd1 = 0; rtInd1 < sol.Routes.Count; rtInd1++)
            {
                Route rt1 = sol.Routes[rtInd1];

                for (int custInd1 = 1; custInd1 < rt1.SequenceOfCustomers.Count - 1; custInd1++)
                {
                    Customer custA = rt1.SequenceOfCustomers[custInd1 - 1];
                    Customer custB = rt1.SequenceOfCustomers[custInd1];
                    Customer custC = rt1.SequenceOfCustomers[custInd1 + 1];
                    //Route rt1_copy = new Route(rt1);
                    if (custB.Options.Count() < 2) { continue; }
                    Route rt1_copy = rt1.getTempCopy(rt1, sol.Options.Select(x => x.Location).ToHashSet().ToList());
                    rt1_copy.SequenceOfCustomers.RemoveAt(custInd1);
                    rt1_copy.SequenceOfOptions.RemoveAt(custInd1);
                    rt1_copy.SequenceOfLocations.RemoveAt(custInd1);
                    rt1_copy.SequenceOfEct.RemoveAt(custInd1);
                    rt1_copy.SequenceOfLat.RemoveAt(custInd1);
                    sol.UpdateTimes(rt1_copy);
                    rt1_copy.Load = rt1_copy.Load - custB.Dem;

                    for (int optInd = 0; optInd < custB.Options.Count; optInd++)
                    {
                        Option custBServedOption = null;
                        List<Option> options = new List<Option>(custB.Options);
                        foreach (Option opt in options) {
                            if (sol.Options[opt.Id].IsServed) {
                                custBServedOption = opt;
                                break;
                            }
                        }
                        if (custBServedOption == custB.Options[optInd]) {
                            continue;
                        }
                        for (int rtInd2 = 0; rtInd2 < sol.Routes.Count; rtInd2++)
                        {
                            openRoutes = sol.Routes.Count;
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
                            if (custB.Options[optInd].Location.MaxCap == custB.Options[optInd].Location.Cap)
                            {
                                continue;
                            }

                            for (int targetOptionIndex = targetRouteIndex; targetOptionIndex < rt2.SequenceOfOptions.Count - 1; targetOptionIndex++) //-1
                            {

                                var tw = sol.RespectsTimeWindow2(rt2, targetOptionIndex, custB.Options[optInd].Location);
                                
                                if (!tw.Item1) { continue; }

                                var newServiceLevel = CalculateTempServiceLevel(sol, rt1.SequenceOfOptions[custInd1].Prio, custB.Options[optInd].Prio);
                                if (newServiceLevel[0] < 0.8 || newServiceLevel[1] < 0.9)
                                {
                                    if (rt1.SequenceOfOptions[custInd1].Prio < custB.Options[optInd].Prio)
                                    {
                                        continue;
                                    }
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

                                if (rt1.Load - B1.Cust.Dem == 0)
                                {
                                    openRoutes--;
                                }

                                double costAdded = sol.CalculateDistance(A.Location, C.Location) + sol.CalculateDistance(F.Location, B2.Location)
                                                    + sol.CalculateDistance(B2.Location, G.Location);
                                double costRemoved = sol.CalculateDistance(A.Location, B1.Location) + sol.CalculateDistance(B1.Location, C.Location)
                                                    + sol.CalculateDistance(F.Location, G.Location);
                                double moveCost = costAdded - costRemoved;
                                var newUtilizationMetricRoute1 = Math.Pow(Convert.ToDouble(rt1.Capacity - (rt1.Load - B1.Cust.Dem)), power);
                                var newUtilizationMetricRoute2 = Math.Pow(Convert.ToDouble(rt2.Capacity - (rt2.Load + B2.Cust.Dem)), power);
                                var newSolUtilizationMetric = sol.SolutionUtilizationMetric - rt1.RouteUtilizationMetric - rt2.RouteUtilizationMetric + newUtilizationMetricRoute1 + newUtilizationMetricRoute2;
                                var ratio = (sol.SolutionUtilizationMetric + 1) / (newSolUtilizationMetric + 1);
                                if (sol.Routes.Count == sol.LowerBoundRoutes)
                                {
                                    ratio = 1;
                                }
                                //else
                                //{
                                //    ratio = Math.Clamp(ratio, 0.8, 1.3);
                                //}
                                sol.RatioCombinedMoveCost = ratio * moveCost;

                                double costChangeOriginRt = sol.CalculateDistance(A.Location, C.Location) - sol.CalculateDistance(A.Location, B1.Location)
                                                    - sol.CalculateDistance(B1.Location, C.Location);
                                double costChangeTargetRt = sol.CalculateDistance(F.Location, B2.Location) + sol.CalculateDistance(B2.Location, G.Location)
                                                    - sol.CalculateDistance(F.Location, G.Location);


                                if (sol.RatioCombinedMoveCost + openRoutes * 10000 < flip.TotalCost + smallDouble) // & rtInd2 != 0)
                                {
                                    if (PromiseIsBroken(F.Id, B2.Id, moveCost + sol.Cost + smallDouble, sol))
                                    {
                                        continue;
                                    }
                                    if (PromiseIsBroken(B2.Id, G.Id, moveCost + sol.Cost + smallDouble, sol))
                                    {
                                        continue;
                                    }
                                    if (PromiseIsBroken(A.Id, C.Id, moveCost + sol.Cost + smallDouble, sol))
                                    {
                                        continue;
                                    }
                                    flip.TotalCost = moveCost + openRoutes * 10000;
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
            if (flip.IsValid())
            {
                sol.LastMove = "flip";
                Route originRt = sol.Routes[flip.OriginRoutePosition];
                Route targetRt = sol.Routes[flip.TargetRoutePosition];
                if (!sol.CheckRouteFeasibility(targetRt) || !sol.CheckRouteFeasibility(originRt))
                {
                    Console.WriteLine("-----");
                }
                Option A = originRt.SequenceOfOptions[flip.OriginOptionPosition - 1];
                Option B1 = originRt.SequenceOfOptions[flip.OriginOptionPosition];
                Option B2 = originRt.SequenceOfCustomers[flip.OriginOptionPosition].Options[flip.NewOptionIndex];//new option to be placed in place of B1
                //Console.WriteLine("Customer ID: " + originRt.SequenceOfCustomers[flip.OriginOptionPosition].Id);
                //Console.WriteLine("B1 ID: " + B1.Id + " B2 ID: " + B2.Id);
                Option C = originRt.SequenceOfOptions[flip.OriginOptionPosition + 1];
                Option F = targetRt.SequenceOfOptions[flip.TargetOptionPosition];
                Option G = targetRt.SequenceOfOptions[flip.TargetOptionPosition + 1];

                originRt.SequenceOfOptions.RemoveAt(flip.OriginOptionPosition);
                originRt.SequenceOfCustomers.RemoveAt(flip.OriginOptionPosition);
                originRt.SequenceOfLocations.RemoveAt(flip.OriginOptionPosition);

                if (originRt == targetRt)
                {
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
                    sol.UpdateTimes(originRt);
                    originRt.Cost += flip.MoveCost;
                    UpdateRouteCostAndLoad(originRt, sol);
                    originRt.RouteUtilizationMetric = Math.Pow(Convert.ToDouble(originRt.Capacity - originRt.Load), 2);
                }
                else
                {
                originRt.SequenceOfEct.RemoveAt(flip.OriginOptionPosition);
                originRt.SequenceOfLat.RemoveAt(flip.OriginOptionPosition);

                targetRt.SequenceOfOptions.Insert(flip.TargetOptionPosition + 1, B2);
                targetRt.SequenceOfCustomers.Insert(flip.TargetOptionPosition + 1, B2.Cust);
                targetRt.SequenceOfLocations.Insert(flip.TargetOptionPosition + 1, B2.Location);
                targetRt.SequenceOfEct.Insert(flip.TargetOptionPosition + 1, 0);
                targetRt.SequenceOfLat.Insert(flip.TargetOptionPosition + 1, 0);

                originRt.Cost += flip.CostChangeOriginRt;
                targetRt.Cost += flip.CostChangeTargetRt;
                originRt.Load -= B1.Cust.Dem;
                targetRt.Load += B2.Cust.Dem;
                sol.UpdateTimes(originRt);
                sol.UpdateTimes(targetRt);
                UpdateRouteCostAndLoad(originRt, sol);
                UpdateRouteCostAndLoad(targetRt, sol);

                originRt.RouteUtilizationMetric = Math.Pow(Convert.ToDouble(originRt.Capacity - originRt.Load), 2);
                targetRt.RouteUtilizationMetric = Math.Pow(Convert.ToDouble(targetRt.Capacity - targetRt.Load), 2);
                }
                sol.Cost += flip.MoveCost;
                B1.IsServed = false;
                B2.IsServed = true;
                sol.Options.Where(x => x.Id == B1.Id).ToList()[0].IsServed = false;
                sol.Options.Where(x => x.Id == B2.Id).ToList()[0].IsServed = true;
                //adjust capacity for shared locations
                B2.Location.Cap++;
                B1.Location.Cap--;
                sol.Promises[A.Id, C.Id] = sol.Cost;
                sol.Promises[F.Id, B2.Id] = sol.Cost;
                sol.Promises[B2.Id, G.Id] = sol.Cost;
                if (!sol.CheckRouteFeasibility(targetRt) || !sol.CheckRouteFeasibility(originRt))
                {
                    Console.WriteLine("-----");
                }
            }
            /*
            else {
                Console.WriteLine("Invalid flip move");
            }
            */
        }

        public PrioritySwap FindBestPrioritySwapMove(PrioritySwap psm, Solution sol)
        {
            Dictionary<int, List<Option>> optionsPerCustomer = new Dictionary<int, List<Option>>();
            foreach (Route rt in sol.Routes)
            {
            // Iterate through Route 1 customers and their corresponding options
                for (int i = 0; i < rt.SequenceOfCustomers.Count; i++)
                {
                    Customer customer = rt.SequenceOfCustomers[i];
                    foreach (Option option in customer.Options)
                    {
                        // If the customer is not already in the dictionary, add them
                        if (!optionsPerCustomer.ContainsKey(customer.Id))
                        {
                            optionsPerCustomer[customer.Id] = new List<Option>();
                        }

                        // Add the current option to the customer's option list
                        optionsPerCustomer[customer.Id].Add(option);
                    }
                }
            }
            Option b1, b2;
            int openRoutes = sol.Routes.Count;
            foreach (Route rt1 in sol.Routes) { //Start iterating over the available Routes
                foreach (Option opt1 in rt1.SequenceOfOptions) { // Iterate over each option of the current Route rt1
                    if (opt1 == rt1.SequenceOfOptions.First() || opt1 == rt1.SequenceOfOptions.Last()) {continue;} //Avoid the first and last option of the route
                    b1 = opt1;
                    Customer customerB1 = b1.Cust;
                    if (customerB1.Id == 1000) {continue;} //Probably unnecessary because there are no options corresponding to the warehouse but add it to be safe
                    if (optionsPerCustomer[customerB1.Id].Count <= 1) {continue;} // Avoid creating a list for customers with only one available option
                    List<Option> notServedOptionsCustomerB1 = optionsPerCustomer[customerB1.Id]; //Create a list with the remaining options of customer B1
                    notServedOptionsCustomerB1.RemoveAll(x => x.Id == b1.Id);
                    foreach (Option notServedOptionB1 in notServedOptionsCustomerB1) { //Start searching to find a match for each not served option of customer B1
                        if (notServedOptionB1.Location.Type == 1 && notServedOptionB1.Location.Cap >= notServedOptionB1.Location.MaxCap) {continue;} //If the location of that option is shared location and there is no available capacity for it continue
                        // Otherwise start searching for match either in the same or other route
                        foreach (Route rt2 in sol.Routes) {
                            foreach (Option opt2 in rt2.SequenceOfOptions) {
                                if (opt2 == rt2.SequenceOfOptions.First() || opt2 == rt2.SequenceOfOptions.Last()) {continue;} //Avoid the first and last option of the route
                                if (opt1 == opt2) {continue;} //Avoid searching if it is the same option
                                b2 = opt2;
                                Customer customerB2 = b2.Cust;
                                if (customerB2.Id == 1000) {continue;} //Probably unnecessary because there are no options corresponding to the warehouse but add it to be safe
                                if (optionsPerCustomer[customerB2.Id].Count <= 1) {continue;} // Avoid creating a list for customers with only one available option
                                List<Option> notServedOptionsCustomerB2 = optionsPerCustomer[customerB2.Id]; //Create a list with the remaining options of customer B2
                                notServedOptionsCustomerB2.RemoveAll(x => x.Id == b2.Id);
                                foreach (Option notServedOptionB2 in notServedOptionsCustomerB2) {
                                    if (notServedOptionB2.Location.Type == 1 && notServedOptionB2.Location.Cap >= notServedOptionB2.Location.MaxCap) {continue;} //If the location of that option is shared location and there is no available capacity for it continue
                                    if (notServedOptionB1.Location.Id.Equals(notServedOptionB2.Location.Id)) {
                                        if (notServedOptionB1.Location.Cap >= notServedOptionB1.Location.MaxCap - 1) {continue;} //If the location of that option is shared location and there is no available capacity for it continue
                                    }
                                    if (notServedOptionB1.Prio == notServedOptionB2.Prio) {continue;} // No reason to check for options with the same priorities
                                    if (notServedOptionB1.Prio != b2.Prio) {continue;} // If the priority of the cust B1 option that we want to insert is not the same with the priority of the cust B2 option that we want to remove continue
                                    if (notServedOptionB2.Prio != b1.Prio) {continue;} // If the priority of the cust B2 option that we want to insert is not the same with the priority of the cust B1 option that we want to remove continue
                                    // Do Time Window checks and Capacity checks
                                    if (rt1.Load - b1.Cust.Dem + notServedOptionB1.Cust.Dem > rt1.Capacity) {continue;} // If the capacity of the route will be violated from the insertion continue
                                    if (rt2.Load - b2.Cust.Dem + notServedOptionB2.Cust.Dem > rt2.Capacity) {continue;} // If the capacity of the route will be violated from the insertion continue
                                    // Check if the time windows are respected for the insertion of the new options in different routes
                                    if (rt1 != rt2) {
                                        var tw1 = sol.RespectsTimeWindow2(rt1, rt1.SequenceOfOptions.IndexOf(b1), notServedOptionB1.Location);
                                        var tw2 = sol.RespectsTimeWindow2(rt2, rt2.SequenceOfOptions.IndexOf(b2), notServedOptionB2.Location);
                                        if (!tw1.Item1 || !tw2.Item1) {continue;}
                                    }
                                    else {
                                        var tw1 = sol.RespectsTimeWindow2(rt1, rt1.SequenceOfOptions.IndexOf(b1), notServedOptionB1.Location);
                                        if (!tw1.Item1) {continue;} // If the insertion of the first option leads to TW violation continue.
                                        // If no TW window violation then insert the new option in the temp route and check for the second option
                                        Route rtTemp = rt1.getTempCopy(rt1, sol.Options.Select(x => x.Location).ToHashSet().ToList());
                                        List<int> sequenceOfOptionsIDrtTemp1 = rtTemp.SequenceOfOptions.Select(x => x.Id).ToList(); // Create this because rtTemp.SequenceOfOptions contains cloned objects that are not the same with rt1.SequenceOfOptions
                                        int indexB1 = sequenceOfOptionsIDrtTemp1.IndexOf(b1.Id);
                                        List<int> sequenceOfLocationsIDrtTemp1 = rtTemp.SequenceOfLocations.Select(x => x.Id).ToList(); // Create this because rtTemp.SequenceOfLocations contains cloned objects that are not the same with rt1.SequenceOfLocations
                                        int indexB1Location = sequenceOfLocationsIDrtTemp1.IndexOf(b1.Location.Id);
                                        rtTemp.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b1)] = notServedOptionB1;
                                        rtTemp.SequenceOfLocations[rt1.SequenceOfOptions.IndexOf(b1)] = notServedOptionB1.Location;
                                        List<int> sequenceOfOptionsIDrtTemp2 = rtTemp.SequenceOfOptions.Select(x => x.Id).ToList(); // Create this because rtTemp.SequenceOfOptions contains cloned objects that are not the same with rt1.SequenceOfOptions
                                        int indexB2 = sequenceOfOptionsIDrtTemp2.IndexOf(b2.Id);
                                        List<int> sequenceOfLocationsIDrtTemp2 = rtTemp.SequenceOfLocations.Select(x => x.Id).ToList();
                                        int indexB2Location = sequenceOfLocationsIDrtTemp2.IndexOf(b2.Location.Id);
                                        var tw2 = sol.RespectsTimeWindow2(rtTemp, indexB2, notServedOptionB2.Location);
                                        if (!tw2.Item1) {continue;} // If the insertion of the second option leads to TW violation continue.
                                    }
                                    double newUtilizationMetricRoute1 = 0;
                                    double newUtilizationMetricRoute2 = 0;
                                    double newSolUtilizationMetric = 0;
                                    double costChangeFirstRoute = 0;
                                    double costChangeSecondRoute = 0;
                                    double moveCost = 0;
                                    double ratio = 1;
                                    // Calculate the cost of the move
                                    if (rt1 != rt2) {
                                        double costRemoved1 = sol.CalculateDistance(rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b1) - 1].Location, b1.Location) + sol.CalculateDistance(b1.Location, rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b1) + 1].Location);
                                        double costAdded1 = sol.CalculateDistance(rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b1) - 1].Location, notServedOptionB1.Location) + sol.CalculateDistance(notServedOptionB1.Location, rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b1) + 1].Location);
                                        double costRemoved2 = sol.CalculateDistance(rt2.SequenceOfOptions[rt2.SequenceOfOptions.IndexOf(b2) - 1].Location, b2.Location) + sol.CalculateDistance(b2.Location, rt2.SequenceOfOptions[rt2.SequenceOfOptions.IndexOf(b2) + 1].Location);
                                        double costAdded2 = sol.CalculateDistance(rt2.SequenceOfOptions[rt2.SequenceOfOptions.IndexOf(b2) - 1].Location, notServedOptionB2.Location) + sol.CalculateDistance(notServedOptionB2.Location, rt2.SequenceOfOptions[rt2.SequenceOfOptions.IndexOf(b2) + 1].Location);
                                        moveCost = costAdded1 + costAdded2 - costRemoved1 - costRemoved2;
                                        costChangeFirstRoute = costAdded1 - costRemoved1;
                                        costChangeSecondRoute = costAdded2 - costRemoved2;
                                        newUtilizationMetricRoute1 = Math.Pow(Convert.ToDouble(rt1.Capacity - (rt1.Load - b1.Cust.Dem + notServedOptionB1.Cust.Dem)), power);
                                        newUtilizationMetricRoute2 = Math.Pow(Convert.ToDouble(rt2.Capacity - (rt2.Load - b2.Cust.Dem + notServedOptionB2.Cust.Dem)), power);
                                        newSolUtilizationMetric = sol.SolutionUtilizationMetric - rt1.RouteUtilizationMetric - rt2.RouteUtilizationMetric + newUtilizationMetricRoute1 + newUtilizationMetricRoute2;
                                    } else {
                                        if (Math.Abs(rt1.SequenceOfOptions.IndexOf(b1) - rt1.SequenceOfOptions.IndexOf(b2)) == 1) { // Calculate cost change if they are next to each other
                                            if (rt1.SequenceOfOptions.IndexOf(b1) < rt1.SequenceOfOptions.IndexOf(b2)) {
                                                double costRemoved = sol.CalculateDistance(rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b1) - 1].Location, b1.Location) + sol.CalculateDistance(b1.Location, b2.Location) + sol.CalculateDistance(b2.Location, rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b2) + 1].Location);
                                                double costAdded = sol.CalculateDistance(rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b1) - 1].Location, notServedOptionB1.Location) + sol.CalculateDistance(notServedOptionB1.Location, notServedOptionB2.Location) + sol.CalculateDistance(notServedOptionB2.Location, rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b2) + 1].Location);
                                                moveCost = costAdded - costRemoved;
                                            } else {
                                                double costRemoved = sol.CalculateDistance(rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b2) - 1].Location, b2.Location) + sol.CalculateDistance(b2.Location, b1.Location) + sol.CalculateDistance(b1.Location, rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b1) + 1].Location);
                                                double costAdded = sol.CalculateDistance(rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b2) - 1].Location, notServedOptionB2.Location) + sol.CalculateDistance(notServedOptionB2.Location, notServedOptionB1.Location) + sol.CalculateDistance(notServedOptionB1.Location, rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b1) + 1].Location);
                                                moveCost = costAdded - costRemoved;
                                            }
                                        } else {
                                            double costRemoved = sol.CalculateDistance(rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b1) - 1].Location, b1.Location) + sol.CalculateDistance(b1.Location, rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b1) + 1].Location);
                                            costRemoved += sol.CalculateDistance(rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b2) - 1].Location, b2.Location) + sol.CalculateDistance(b2.Location, rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b2) + 1].Location);
                                            double costAdded = sol.CalculateDistance(rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b1) - 1].Location, notServedOptionB1.Location) + sol.CalculateDistance(notServedOptionB1.Location, rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b1) + 1].Location);
                                            costAdded += sol.CalculateDistance(rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b2) - 1].Location, notServedOptionB2.Location) + sol.CalculateDistance(notServedOptionB2.Location, rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b2) + 1].Location);
                                            moveCost = costAdded - costRemoved;
                                        }
                                        newSolUtilizationMetric = Math.Pow(Convert.ToDouble(rt1.Capacity - (rt1.Load - b1.Cust.Dem + notServedOptionB1.Cust.Dem + b2.Cust.Dem - notServedOptionB2.Cust.Dem)), power);
                                    }
                                    ratio = (sol.SolutionUtilizationMetric + 1) / (newSolUtilizationMetric + 1);
                                    if (sol.Routes.Count == sol.LowerBoundRoutes) {
                                        ratio = 1;
                                    }
                                    //else
                                    //{
                                    //    ratio = Math.Clamp(ratio, 0.8, 1.3);
                                    //}
                                    double ratioCombinedMoveCost = ratio * moveCost;
                                    if (ratioCombinedMoveCost + openRoutes * 10000 < psm.TotalCost + smallDouble) {
                                        if (rt1 == rt2) {
                                            if (Math.Abs(rt1.SequenceOfOptions.IndexOf(b1) - rt1.SequenceOfOptions.IndexOf(b2)) == 1) { // Calculate cost change if they are next to each other
                                                if (rt1.SequenceOfOptions.IndexOf(b1) < rt1.SequenceOfOptions.IndexOf(b2)) {
                                                    if (PromiseIsBroken(rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b1) - 1].Id, notServedOptionB1.Id, moveCost + sol.Cost + smallDouble, sol)) {continue;}
                                                    if (PromiseIsBroken(notServedOptionB1.Id, notServedOptionB2.Id, moveCost + sol.Cost + smallDouble, sol)) {continue;}
                                                    if (PromiseIsBroken(notServedOptionB2.Id, rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b2) + 1].Id, moveCost + sol.Cost + smallDouble, sol)) {continue;}
                                                } else {
                                                    if (PromiseIsBroken(rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b2) - 1].Id, notServedOptionB2.Id, moveCost + sol.Cost + smallDouble, sol)) {continue;}
                                                    if (PromiseIsBroken(notServedOptionB2.Id, notServedOptionB1.Id, moveCost + sol.Cost + smallDouble, sol)) {continue;}
                                                    if (PromiseIsBroken(notServedOptionB1.Id, rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b1) + 1].Id, moveCost + sol.Cost + smallDouble, sol)) {continue;}
                                                }
                                            } else {
                                                if (PromiseIsBroken(rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b1) - 1].Id, notServedOptionB1.Id, moveCost + sol.Cost + smallDouble, sol)) {continue;}
                                                if (PromiseIsBroken(notServedOptionB1.Id, rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b1) + 1].Id, moveCost + sol.Cost + smallDouble, sol)) {continue;}
                                                if (PromiseIsBroken(rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b2) - 1].Id, notServedOptionB2.Id, moveCost + sol.Cost + smallDouble, sol)) {continue;}
                                                if (PromiseIsBroken(notServedOptionB2.Id, rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b2) + 1].Id, moveCost + sol.Cost + smallDouble, sol)) {continue;}
                                            }
                                        } else {
                                            if (PromiseIsBroken(rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b1) - 1].Id, notServedOptionB1.Id, moveCost + sol.Cost + smallDouble, sol)) {continue;}
                                            if (PromiseIsBroken(notServedOptionB1.Id, rt1.SequenceOfOptions[rt1.SequenceOfOptions.IndexOf(b1) + 1].Id, moveCost + sol.Cost + smallDouble, sol)) {continue;}
                                            if (PromiseIsBroken(rt2.SequenceOfOptions[rt2.SequenceOfOptions.IndexOf(b2) - 1].Id, notServedOptionB2.Id, moveCost + sol.Cost + smallDouble, sol)) {continue;}
                                            if (PromiseIsBroken(notServedOptionB2.Id, rt2.SequenceOfOptions[rt2.SequenceOfOptions.IndexOf(b2) + 1].Id, moveCost + sol.Cost + smallDouble, sol)) {continue;}
                                        }

                                        psm.TotalCost = moveCost + openRoutes * 10000;
                                        psm.MoveCost = moveCost;
                                        psm.PositionOfFirstRoute = sol.Routes.IndexOf(rt1);
                                        psm.PositionOfSecondRoute = sol.Routes.IndexOf(rt2);
                                        psm.PositionOfFirstOption = rt1.SequenceOfOptions.IndexOf(b1);
                                        psm.PositionOfSecondOption = rt2.SequenceOfOptions.IndexOf(b2);
                                        psm.CostChangeFirstRt = costChangeFirstRoute;
                                        psm.CostChangeSecondRt = costChangeSecondRoute;
                                        psm.AltOption1 = notServedOptionB1;
                                        psm.AltOption2 = notServedOptionB2;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            return psm;
        }

        public void ApplyPrioritySwapMove(PrioritySwap psm, Solution sol)
        {
            if (psm.IsValid()) //&& psm.MoveCost < 0) 
            {
                sol.LastMove = "psm";
                Route rt1 = sol.Routes[psm.PositionOfFirstRoute];
                Route rt2 = sol.Routes[psm.PositionOfSecondRoute];
                if (!sol.CheckRouteFeasibility(rt1) || !sol.CheckRouteFeasibility(rt2))
                {
                    Console.WriteLine("-----");
                }
                Option b1 = rt1.SequenceOfOptions[psm.PositionOfFirstOption];
                // Option B2 = originRt.SequenceOfCustomers[flip.OriginOptionPosition].Options[flip.NewOptionIndex];
                //Option d1 = psm.AltOption1;
                Option d1 = rt1.SequenceOfCustomers[psm.PositionOfFirstOption].Options.FirstOrDefault(opt => opt.Id == psm.AltOption1.Id);
                Option b2 = rt2.SequenceOfOptions[psm.PositionOfSecondOption];
                //Option d2 = psm.AltOption2;
                Option d2 = rt2.SequenceOfCustomers[psm.PositionOfSecondOption].Options.FirstOrDefault(opt => opt.Id == psm.AltOption2.Id);
                //
                Console.WriteLine("Customer 1: " + b1.Cust.Id);
                Console.WriteLine("B1 ID: " + b1.Id + " D1 ID: " + d1.Id);
                Console.WriteLine("Customer 2: " + b2.Cust.Id);
                Console.WriteLine("B2 ID: " + b2.Id + " D2 ID: " + d2.Id);
                Console.WriteLine("-----");
                Console.WriteLine("Route 1 Before: " + string.Join(",", rt1.SequenceOfOptions.Select(x => x.Id).ToList()));
                Console.WriteLine("Route 2 Before: " + string.Join(",", rt2.SequenceOfOptions.Select(x => x.Id).ToList()));
                Console.WriteLine("-----");
                //
                rt1.SequenceOfOptions[psm.PositionOfFirstOption] = d1;
                rt1.SequenceOfCustomers[psm.PositionOfFirstOption] = d1.Cust;
                rt1.SequenceOfLocations[psm.PositionOfFirstOption] = d1.Location;
                rt2.SequenceOfOptions[psm.PositionOfSecondOption] = d2;
                rt2.SequenceOfCustomers[psm.PositionOfSecondOption] = d2.Cust;
                rt2.SequenceOfLocations[psm.PositionOfSecondOption] = d2.Location;
                Console.WriteLine("---------");
                Console.WriteLine("Route 1 After: " + string.Join(",", rt1.SequenceOfOptions.Select(x => x.Id).ToList()));
                Console.WriteLine("Route 2 After: " + string.Join(",", rt2.SequenceOfOptions.Select(x => x.Id).ToList()));
                Console.WriteLine("---------");
                b1.Location.Cap -= 1;
                b2.Location.Cap -= 1;
                d1.Location.Cap += 1;
                d2.Location.Cap += 1;
                b1.IsServed = false;
                b2.IsServed = false;
                d1.IsServed = true;
                d2.IsServed = true;
                if (rt1 == rt2)
                {
                    rt1.Cost += psm.MoveCost;
                    sol.UpdateTimes(rt1);
                    UpdateRouteCostAndLoad(rt1, sol);
                    rt1.RouteUtilizationMetric = Math.Pow(Convert.ToDouble(rt1.Capacity - rt1.Load), 2);
                    sol.Cost += psm.MoveCost;
                    if (rt1 == rt2 && (psm.PositionOfFirstOption == psm.PositionOfSecondOption - 1 || psm.PositionOfSecondOption == psm.PositionOfFirstOption - 1))
                    {
                        if (psm.PositionOfFirstOption == psm.PositionOfSecondOption - 1)
                        {
                            sol.Promises[rt1.SequenceOfOptions[psm.PositionOfFirstOption - 1].Id, d1.Id] = sol.Cost;
                            sol.Promises[d1.Id, d2.Id] = sol.Cost;
                            sol.Promises[d2.Id, rt1.SequenceOfOptions[psm.PositionOfSecondOption + 1].Id] = sol.Cost;
                        }
                        else if (psm.PositionOfSecondOption == psm.PositionOfFirstOption - 1)
                        {
                            sol.Promises[rt1.SequenceOfOptions[psm.PositionOfSecondOption - 1].Id, d2.Id] = sol.Cost;
                            sol.Promises[d2.Id, d1.Id] = sol.Cost;
                            sol.Promises[d1.Id, rt1.SequenceOfOptions[psm.PositionOfFirstOption + 1].Id] = sol.Cost;
                        }
                    }
                    else
                    {
                        sol.Promises[rt1.SequenceOfOptions[psm.PositionOfFirstOption - 1].Id, d1.Id] = sol.Cost;
                        sol.Promises[d1.Id, rt1.SequenceOfOptions[psm.PositionOfFirstOption + 1].Id] = sol.Cost;
                        sol.Promises[rt1.SequenceOfOptions[psm.PositionOfSecondOption - 1].Id, d2.Id] = sol.Cost;
                        sol.Promises[d2.Id, rt1.SequenceOfOptions[psm.PositionOfSecondOption + 1].Id] = sol.Cost;
                    }
                    if (!sol.CheckRouteFeasibility(rt1) || !sol.CheckRouteFeasibility(rt2))
                    {
                        Console.WriteLine("-----");
                    }
                }
                else
                {
                    rt1.Cost += psm.CostChangeFirstRt;
                    rt2.Cost += psm.CostChangeSecondRt;
                    sol.UpdateTimes(rt1);
                    sol.UpdateTimes(rt2);
                    UpdateRouteCostAndLoad(rt1, sol);
                    UpdateRouteCostAndLoad(rt2, sol);
                    rt1.RouteUtilizationMetric = Math.Pow(Convert.ToDouble(rt1.Capacity - rt1.Load), 2);
                    rt2.RouteUtilizationMetric = Math.Pow(Convert.ToDouble(rt2.Capacity - rt2.Load), 2);
                    sol.Cost += psm.MoveCost;
                    sol.Promises[rt1.SequenceOfOptions[psm.PositionOfFirstOption - 1].Id, d1.Id] = sol.Cost;
                    sol.Promises[d1.Id, rt1.SequenceOfOptions[psm.PositionOfFirstOption + 1].Id] = sol.Cost;
                    sol.Promises[rt2.SequenceOfOptions[psm.PositionOfSecondOption - 1].Id, d2.Id] = sol.Cost;
                    sol.Promises[d2.Id, rt2.SequenceOfOptions[psm.PositionOfSecondOption + 1].Id] = sol.Cost;
                    if (!sol.CheckRouteFeasibility(rt1) || !sol.CheckRouteFeasibility(rt2))
                    {
                        Console.WriteLine("-----");
                    }
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

        double[] CalculateTempServiceLevel(Solution sol, int leavingPriority, int enteringPriority, bool verbal = false)
        {
            int po0Sum = 0;
            int po1Sum = 0;
            int po2Sum = 0;
            double sum = 0;
            int po = -1;

            for (int r = 0; r < sol.Routes.Count; r++)
            {
                for (int c = 1; c < sol.Routes[r].SequenceOfOptions.Count - 1; c++)
                {
                    po = sol.Routes[r].SequenceOfOptions[c].Prio;
                    switch (po)
                    {
                        case 0:
                            po0Sum++;
                            break;
                        case 1:
                            po1Sum++;
                            break;
                        case 2:
                            po2Sum++;
                            break;
                    }
                }
            }
            switch (leavingPriority)
            {
                case 0:
                    po0Sum--;
                    break;
                case 1:
                    po1Sum--;
                    break;
                case 2:
                    po2Sum--;
                    break;
            }
            switch (enteringPriority)
            {
                case 0:
                    po0Sum++;
                    break;
                case 1:
                    po1Sum++;
                    break;
                case 2:
                    po2Sum++;
                    break;
            }
            sum = po0Sum + po1Sum + po2Sum;
            var sl0 = po0Sum / sum;
            var sl1 = (po0Sum + po1Sum) / sum;
            if (verbal) {
                Console.WriteLine("Priority 1: {0}", sl0);
                Console.WriteLine("Priority 2: {0}", sl1);
            }

            return new double[] {sl0, sl1};
        }

        bool PromiseIsBroken(int a, int b, double newCost, Solution sol)
        {
            if (newCost >= sol.Promises[a, b])
            {
                return true;
            }

            return false;
        }
    }
}
