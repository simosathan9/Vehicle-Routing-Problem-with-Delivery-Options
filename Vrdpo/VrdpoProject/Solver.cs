using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using OxyPlot;
using Microsoft.VisualBasic.FileIO;


namespace VrdpoProject
{
    public class Solver
    {
        private CustomerInsertionAllPositions bestInsertion = new();
        // this is unused so far.Make a method to write the final solution into a text file 
        Solution globalBestSol = new Solution();
        double globalBestSolCost = Math.Pow(10, 9);
        LocalSearch ls = new();
        Random rnd2 = new Random();


        public void Solve()
        {
            List<Solution> feasibleSolutions = new List<Solution>();
            feasibleSolutions = ConstructFeasibleSolutions();
            foreach (Solution sol in feasibleSolutions)
            {
                foreach (Route r in sol.Routes)
                {
                    if (r.SequenceOfLocations.Count == 2)
                    {
                        sol.Cost -= r.Cost;
                        continue;
                    }
                    Console.WriteLine("LOCATION | CUSTOMER");
                    for (int i = 0; i < r.SequenceOfOptions.Count; i++)
                    {
                        Console.WriteLine("{0} {1}", r.SequenceOfOptions[i].Location.Id, r.SequenceOfOptions[i].Cust.Id);
                    }
                    Console.WriteLine("Max capacity: {0} Load:{1}", r.Capacity, r.Load);
                    if (!sol.CheckRouteFeasibility(r)) { Console.WriteLine("INFEASIBLE"); }
                    Console.WriteLine("--------------");
                }
                if (sol.Routes[sol.Routes.Count - 1].Load == 0)
                {
                    sol.Routes.RemoveAt(sol.Routes.Count - 1);
                }
                Console.WriteLine(sol.Cost);
                CalculateServiceLevel(sol);
                Console.WriteLine("---------------------------");
                Console.WriteLine("---------------------------");
                Console.WriteLine("---------------------------");
            }

            foreach (Solution s in feasibleSolutions)
            {
                LocalSearch(s);
                Console.WriteLine("The best solution is: " + globalBestSol);
                Console.WriteLine("------------------");
                Console.WriteLine("------------------");
                Console.WriteLine("------------------");
            }
        }


        Solution LocalSearch(Solution currentSol)
        {
            Solution bestSol = new();
            int numberOfRestarts = 10;
            for (int restart = 0; restart < numberOfRestarts; restart++)
            {
                Random rnd = new(restart);
                double bestSolCost = 10000000;
                int reinitCount = -1;
                int c = 0;
                int lastImprovement = 0;
                Relocation rm = new();
                Swap sm = new();
                TwoOpt top = new();
                Flip flip = new();
                for (int i = 0; i < 7000; i++)
                {
                    if (i - lastImprovement > 3000)
                    {
                        break;
                    }

                    reinitCount++;
                    rm.ReinitializeVariables();
                    sm.ReinitializeVariables();
                    top.ReinitializeVariables();
                    flip.ReinitializeVariables();

                    if (reinitCount == currentSol.Options.Count * 1.5)
                    {
                        currentSol.InitPromises();
                        reinitCount = 0;
                    }

                    sm = ls.FindBestSwapMove(sm, currentSol);
                    rm = ls.FindBestRelocationMove(rm, currentSol);
                    top = ls.FindBestTwoOptMove(top, currentSol);

                    if (i > 2000 && ((i - c) > 500))
                    {
                        c = i;
                        flip = ls.FindBestFlipMove(flip, currentSol);
                    }

                    var mincost = FindMinMoveCost(sm, rm, top, flip);
                    if (mincost == sm.MoveCost)
                    {
                        ls.ApplySwapMove(sm, currentSol);
                        //Console.Write(" swap");
                    }
                    else if (mincost == rm.MoveCost)
                    {
                        ls.ApplyRelocationMove(rm, currentSol);
                        //Console.Write(" reloc");

                    }
                    else if (mincost == top.MoveCost)
                    {
                        ls.ApplyTwoOptMove(top, currentSol);
                        //Console.Write(" two opt");

                    }
                    else if (mincost == flip.MoveCost)
                    {
                        ls.ApplyFlipMove(flip, currentSol);
                        //Console.Write(" flip");

                    }

                    /**
                    int k = rnd.Next(1, 5);
                    if (k == 1)
                    {
                        sm = ls.FindBestSwapMove(sm, currentSol);
                        ls.ApplySwapMove(sm, currentSol);
                    }
                    else if (k == 2)
                    {
                        rm = ls.FindBestRelocationMove(rm, currentSol);
                        ls.ApplyRelocationMove(rm, currentSol);
                    }
                    else if (k == 3)
                    {
                        top = ls.FindBestTwoOptMove(top, currentSol);
                        ls.ApplyTwoOptMove(top, currentSol);
                    }
                    else if (k == 4)
                    {
                        if (i > 500 && ((i - c) > 100))//100
                        {
                            c = i;
                            flip = ls.FindBestFlipMove(flip, currentSol);
                            var service_level = CalculateServiceLevel(currentSol, false);
                            if (service_level[0] > 0.8 && service_level[1] > 0.9)//0.8
                            {
                                if (flip.MoveCost < 0)
                                {
                                    ls.ApplyFlipMove(flip, currentSol);
                                }
                            }
                            else
                            {
                                ls.ApplyFlipMove(flip, currentSol);
                            }
                            //Console.Write(" flip");
                        }
                        else
                        {
                            sm = ls.FindBestSwapMove(sm, currentSol);
                            rm = ls.FindBestRelocationMove(rm, currentSol);
                            top = ls.FindBestTwoOptMove(top, currentSol);
                            if (rm.MoveCost < sm.MoveCost && rm.MoveCost < top.MoveCost)
                            {
                                ls.ApplyRelocationMove(rm, currentSol);
                            }
                            else if (sm.MoveCost < top.MoveCost && sm.MoveCost < rm.MoveCost)
                            {
                                ls.ApplySwapMove(sm, currentSol);
                            }
                            else
                            {
                                ls.ApplyTwoOptMove(top, currentSol);

                            }
                        }
                    }**/
                    if (!currentSol.CheckEverything(currentSol))
                    {
                        Console.WriteLine("Infeasible Solution!!!");
                    }

                    if (currentSol.Cost < bestSolCost && CalculateServiceLevel(currentSol, false)[0] >= 0.8)
                    {
                        bestSolCost = currentSol.Cost;
                        bestSol = currentSol.DeepCopy(currentSol);
                        lastImprovement = i;
                    }
                    Console.WriteLine(Convert.ToString(i) + ' ' + Convert.ToString(currentSol.Cost) + ' ' + Convert.ToString(bestSolCost));// + CalculateServiceLevel(currentSol));
                }
                CalculateServiceLevel(bestSol);
                //bestSol = currentSol.DeepCopy(currentSol);

                foreach (Route r in bestSol.Routes)
                {
                    Console.WriteLine("LOCATION | CUSTOMER");
                    for (int i = 0; i < r.SequenceOfOptions.Count; i++)
                    {
                        Console.WriteLine("{0} {1}", r.SequenceOfOptions[i].Location.Id, r.SequenceOfOptions[i].Cust.Id);
                    }
                    Console.WriteLine("Max capacity: {0} Load:{1}", r.Capacity, r.Load);
                    if (!bestSol.CheckRouteFeasibility(r)) { Console.WriteLine("INFEASIBLE"); }
                    Console.WriteLine("--------------");
                }
                Console.WriteLine(bestSol.Cost);
                //CalculateServiceLevel(bestSol);

                if (bestSolCost < globalBestSolCost)
                {
                    // !this is a ref not a copy. the solution is lost immediatelly after rhis
                    globalBestSol = bestSol.DeepCopy(bestSol);
                    globalBestSolCost = bestSolCost;
                }

                Console.WriteLine("///////////////////////");
                Console.WriteLine(bestSolCost + " " + globalBestSolCost);
                System.Threading.Thread.Sleep(5000);
            }
            CalculateServiceLevel(globalBestSol);
            return globalBestSol;
        }

        List<Solution> ConstructFeasibleSolutions()
        {
            List<Solution> solutionList = new List<Solution>();
            Random rnd = new Random();
            int Solutions = 10;

            for (int i = 0; i < Solutions; i++)
            {
                Solution sol = new Solution();
                SetRoutedToFalse(sol.Customers);
                SetServedToFalse(sol.Options);
                solutionList.Add(sol);
            
                Console.WriteLine("----");
                var selectedOptions = new List<Option>();

                // mqny hqve 1 option so the priority is bad from the start
                foreach (Customer cus in sol.Customers)
                {
                    if (cus.Options.Count == 1)
                    {
                        selectedOptions.Add(cus.Options[0]);
                        cus.IsRouted = true;
                    }
                }
                // all customers are included and the priority levels arent reached
                while (CalculateServiceLevel(selectedOptions, sol)[0] < 0.8)
                {
                    InsertBestFirstOption2(selectedOptions, sol);
                    Console.WriteLine(CalculateServiceLevel(selectedOptions, sol)[0] + "  " + CalculateServiceLevel(selectedOptions, sol)[1]);
                }
                /**while (CalculateServiceLevel(selectedOptions, sol)[1] < 0.9)
                {
                    InsertBestFirstOption2(selectedOptions, sol);
                    Console.WriteLine(CalculateServiceLevel(selectedOptions, sol)[0] + "  " + CalculateServiceLevel(selectedOptions, sol)[1]);
                }**/ 
                Random rnd3 = new Random();
                Dictionary<Option, double> sc = new Dictionary<Option, double>();
                Option opt;
                // add customers that are not served
                foreach (Customer cus in sol.Customers)
                {
                    if (!cus.IsRouted)
                    {
                        sc = CalculateObjective(selectedOptions, sol, cus.Options);
                        opt = sc.OrderBy(kvp => kvp.Value).FirstOrDefault().Key;
                        selectedOptions.Add(opt);
                        opt.Cust.IsRouted = true;
                        sc.Clear();
                    }
                }
                // Also, check for each option if teh capacity is violated if the option is added

                SetRoutedToFalse(sol.Customers);
                SetServedToFalse(selectedOptions);
                if(!MinimumInsertions(sol, selectedOptions, rnd))
                {
                    solutionList.Remove(sol);
                    Solutions++;
                    continue;
                };
            }
            return solutionList;
        }

    

        void InsertBestFirstOption2(List<Option> selectedOptions, Solution sol)
        {
            double bestOptionObjective = double.MaxValue;

            Dictionary<Option, double> optionScores = new Dictionary<Option, double>();

            optionScores = CalculateObjective(selectedOptions, sol, AvailableFirstOptions(sol));

            // Then standardize the scores if optionScores is not empty
            if (optionScores.Any())
            {
                /**double mean = optionScores.Values.Average();
                double stdDev = Math.Sqrt(optionScores.Values.Sum(v => Math.Pow(v - mean, 2)) / optionScores.Count);
                Dictionary<Option, double> standardizedScores = optionScores.ToDictionary(
                    kvp => kvp.Key,
                    kvp => (kvp.Value - mean) / stdDev);

                // Now, select the top three options based on standardized scores
                var topThreeOptions = standardizedScores.OrderBy(kvp => kvp.Value).Take(3).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);**/

                var topThreeOptions = optionScores.OrderBy(kvp => kvp.Value).Take(3).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

                // Random selection among the top options
                var keysList = topThreeOptions.Keys.ToList();
                Option bestOption = keysList[rnd2.Next(keysList.Count)];
                bestOptionObjective = topThreeOptions[bestOption];

                selectedOptions.Add(bestOption);
                bestOption.Cust.IsRouted = true;
                Console.WriteLine($"Best Option: {bestOption.Id} Best Option's Objective: {bestOptionObjective}");
            }
        }

        Dictionary<Option, double> CalculateObjective(List<Option> selectedOptions, Solution sol, List<Option> candOptions)
        {
            double weightDistance = 0.8;
            double weightDueReadyDiff = 0.5;
            double weightOverlap = -0.2;
            List<Option> nearestOptions;
            double weightedSum;
            Dictionary<Option, double> scores = new Dictionary<Option, double>();
            double maxDistance = sol.DistanceMatrix.Max2D();
            double maxDuration = sol.Depot.Due - sol.Depot.Ready;
            foreach (Option option in candOptions)
            {
                nearestOptions = FindXnearest(option, sol, selectedOptions);
                weightedSum = (weightDistance * (DistanceFromXnearest(nearestOptions, option, sol))) / (5 * maxDistance) -
                (weightDueReadyDiff * (option.Location.Due - option.Location.Ready) / 5 * maxDuration)
                                       + (weightOverlap * (CalculateOverlap(option, nearestOptions) / 5 * maxDuration));

                scores[option] = weightedSum;
            }

            return scores;
        }


        List<Option> AvailableFirstOptions(Solution sol)
        {
            List<Option> tempOptions = new List<Option>();
            foreach (Customer cus in sol.Customers)
            {
                if (!cus.IsRouted)
                {
                    tempOptions.Add(cus.Options.OrderBy(x => x.Prio).FirstOrDefault());
                }
            }
            return tempOptions;
        }

        double CalculateOverlap(Option target, List<Option> nrOptions)
        {
            double overlap = 0;
            foreach (var opt in nrOptions)
            {
                overlap += Math.Max(0, Math.Min(target.Due, opt.Due) - Math.Max(target.Ready, opt.Ready));
            }
            return overlap;
        }

        List<Option> FindXnearest(Option targetOpt, Solution sol, List<Option> selectedOptions)
        {
            // Sort the points based on distance to central point ** maybe it should be calculated candidate option distance to already selected options
            //List<Option> sortedPoints = availableFirstOptions(sol).OrderBy(point => sol.CalculateDistance(targetOpt.Location, point.Location)).ToList();
            List<Option> sortedPoints = selectedOptions.OrderBy(point => sol.CalculateDistance(targetOpt.Location, point.Location)).ToList();

            // Take the first k points as the nearest neighbors
            List<Option> nearestNeighbors = sortedPoints.Take(5).ToList();

            return nearestNeighbors;
        }


        double DistanceFromXnearest(List<Option> opts, Option target, Solution sol)
        {
            double sumOfDistances = opts.Sum(neighbor => sol.CalculateDistance(target.Location, neighbor.Location));

            // Calculate the average distance
            double averageDistance = sumOfDistances / opts.Count;

            return averageDistance;
        }

        void RestoreFeasibility(Solution sol, Flip flip)
        {
            flip.ReinitializeVariables();
            while (CalculateServiceLevel(sol)[0] < 80 || CalculateServiceLevel(sol)[1] < 90)
            {
                flip.ReinitializeVariables();
                flip = ls.FindBestFlipMove(flip, sol, false);
                if (!flip.IsValid())
                {
                    break;
                }
                ls.ApplyFlipMove(flip, sol);
                Console.WriteLine("cost: " + sol.Cost);
            }
        }

        private double FindMinMoveCost(Swap sm, Relocation rm, TwoOpt top, Flip flip) => Math.Min(Math.Min(Math.Min(sm.MoveCost, rm.MoveCost), top.MoveCost), flip.MoveCost);

        void SetRoutedToFalse(List<Customer> customers)
        {
            foreach(Customer customer1 in customers)
            {
                customer1.IsRouted = false;
            }
        }

        void SetServedToFalse(List<Option> options)
        {
            foreach (Option option1 in options)
            {
                option1.IsServed = false;
            }
        }

        void AlwaysKeepAnEmptyRoute(Solution sol)
        {
            if (sol.Routes.Count < 10)
            {
                if (sol.Routes.Count == 0)
                {
                    Route newRoute = new(sol.Routes.Count, sol.Cap, sol.Depot);
                    sol.Routes.Add(newRoute);
                    sol.Cost += newRoute.Cost;
                }
                else
                {
                    if (sol.Routes.Last().SequenceOfLocations.Count > 2)
                    {
                        Route newRoute = new(sol.Routes.Count, sol.Cap, sol.Depot);
                        sol.Routes.Add(newRoute);
                        sol.Cost += newRoute.Cost;
                    }
                }
            }
        }


        void ReportSolution(Solution sol)
        {
            StreamWriter writetext = new("write.txt");

            writetext.WriteLine("Total cost: " + sol.Cost + "\n");
            writetext.WriteLine("\n");

            for (int i = 0; i < sol.Routes.Count; i++)
            {
                writetext.WriteLine("Route " + Convert.ToString(i) + " " + "Location " + "Option " + "Customer" + "\n");
                Route rt = sol.Routes[i];
                for (int j = 0; j < rt.SequenceOfOptions.Count; j++)
                {
                    if (j == 0 | j == rt.SequenceOfOptions.Count - 1)
                    {
                        writetext.WriteLine(rt.SequenceOfLocations[j] + " " + "-" + " " + "-" + "\n");
                    }
                    else
                    {
                        writetext.WriteLine(rt.SequenceOfLocations[j] + " " + rt.SequenceOfOptions[j] + " " + rt.SequenceOfCustomers[j] + "\n");
                    }
                }
            }
            writetext.Close();
        }
        

        Option candidateOpt;
        Location A, B;
        double timeAdded, timeRemoved, trialTime;
        double costAdded, costRemoved, trialCost;
        double[] tw;

        List<CustomerInsertionAllPositions> IdentifyMinimumCostInsertion(CustomerInsertionAllPositions bestInsertion, Solution sol, List<Option> selectedOptions)
        {

            List<CustomerInsertionAllPositions> topThree = new List<CustomerInsertionAllPositions>
            {
                bestInsertion
            };

            for (int i = 0; i < selectedOptions.Count ; i++)
            {
                candidateOpt = selectedOptions[i];
                if (candidateOpt.Cust.IsRouted == false & candidateOpt.IsServed == false)
                {
                    foreach (Route rt in sol.Routes)
                    {
                        if (rt.Load + candidateOpt.Cust.Dem <= rt.Capacity)
                        {
                            for (int j = 0; j < rt.SequenceOfLocations.Count - 1; j++)
                            {
                                A = rt.SequenceOfLocations[j];
                                B = rt.SequenceOfLocations[j + 1];
                                timeAdded = sol.CalculateTime(A, candidateOpt.Location) + sol.CalculateTime(candidateOpt.Location, B);
                                timeRemoved = sol.CalculateTime(A, B);
                                costAdded = sol.CalculateDistance(A, candidateOpt.Location) + sol.CalculateDistance(candidateOpt.Location, B);
                                costRemoved = sol.CalculateDistance(A, B);
                                trialCost = costAdded - costRemoved;
                                trialTime = timeAdded - timeRemoved + candidateOpt.Location.ServiceTime;//candidateOpt.ServiceTime;// if loc == loc+1??? servicetime--
                               // tw = sol.RespectsTimeWindow(rt, j, candidateOpt.Location);
                                var t = sol.RespectsTimeWindow2(rt, j, candidateOpt.Location);

                                //if (tw[0] <= tw[1]) { 
                                if (t.Item1)
                                {

                                    if (trialCost <= topThree.Last().Cost || topThree.Count < 3)
                                    {
                                        if (candidateOpt.Location.Type == 2 | candidateOpt.Location.Cap < candidateOpt.Location.MaxCap)
                                        {
                                            bestInsertion.Option = candidateOpt;
                                            bestInsertion.Customer = candidateOpt.Cust;
                                            bestInsertion.Location = candidateOpt.Location;
                                            bestInsertion.Route = rt;
                                            bestInsertion.InsertionPosition = j + 1;
                                            bestInsertion.Duration = trialTime;
                                            bestInsertion.Cost = trialCost;
                                            //bestInsertion.Ect = tw[0];
                                            //bestInsertion.Lat = tw[1];
                                            bestInsertion.Ect = t.Item2[j+1];
                                            bestInsertion.Lat = t.Item3[j+1];

                                            CustomerInsertionAllPositions custTemp = new CustomerInsertionAllPositions(bestInsertion);
                                            topThree.Add(custTemp);
                                            topThree = topThree.OrderBy(o=>o.Cost).ToList();
                                            topThree = topThree.Take(3).ToList();
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            return topThree;
        }



        

        void ApplyCustomerInsertionAllPositions(CustomerInsertionAllPositions insertion, Solution sol)
        {
            insertion.Route.SequenceOfLocations.Insert(insertion.InsertionPosition, insertion.Location);
            insertion.Route.SequenceOfCustomers.Insert(insertion.InsertionPosition, insertion.Customer);
            insertion.Route.SequenceOfOptions.Insert(insertion.InsertionPosition, insertion.Option);
            insertion.Route.Duration += insertion.Duration;
            insertion.Route.Cost += insertion.Cost;
            insertion.Route.Load += insertion.Customer.Dem;
            insertion.Customer.IsRouted = true;
            insertion.Option.IsServed = true;
            sol.Cost += insertion.Cost;
            sol.Duration += insertion.Duration;
            insertion.Location.Cap += 1;
            insertion.Route.SequenceOfEct.Insert(insertion.InsertionPosition, insertion.Ect);
            insertion.Route.SequenceOfLat.Insert(insertion.InsertionPosition, insertion.Lat);
            sol.UpdateTimes(insertion.Route);
            /**if (!sol.CheckRouteFeasibility(insertion.Route))
            {
                Console.WriteLine("-----");
                sol.CheckRouteFeasibility(insertion.Route);
            };**/
        }

        bool MinimumInsertions(Solution sol, List<Option> selectedOptions, Random rnd)
        {
            bool modelIsFeasible = true;
            while (sol.Customers.Any(x => !x.IsRouted))
            {   
                bestInsertion = new CustomerInsertionAllPositions();
                AlwaysKeepAnEmptyRoute(sol);
                List<CustomerInsertionAllPositions> topThree = IdentifyMinimumCostInsertion(bestInsertion, sol, selectedOptions);
                bestInsertion = topThree[rnd.Next(topThree.Count)];
                if (bestInsertion.Customer != null)
                {
                    ApplyCustomerInsertionAllPositions(bestInsertion, sol);
                } else
                {
                    modelIsFeasible = false;
                    return modelIsFeasible; ;
                }
            }
            ReportSolution(sol);
            return modelIsFeasible;
        }
        double[] CalculateServiceLevel(Solution sol, bool verbal = true)
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
            sum = po0Sum + po1Sum + po2Sum;
            var sl0 = po0Sum / sum;
            var sl1 = (po0Sum + po1Sum) / sum;
            if (verbal) {
                Console.WriteLine("Priority 1: {0}", sl0);
                Console.WriteLine("Priority 2: {0}", sl1);
            }

            return new double[] {sl0, sl1};
        }

        double[] CalculateServiceLevel(List<Option> selectedOptions, Solution sol)
        {
            int po0Sum = 0;
            int po1Sum = 0;
            int po2Sum = 0;
            double sum = 0;
            int po = -1;

            for (int c = 0; c < selectedOptions.Count - 1; c++)
            {
                po = selectedOptions[c].Prio;
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
            sum = po0Sum + po1Sum + po2Sum;
            var sl0 = po0Sum / (double)sol.Customers.Count;
            var sl1 = (po0Sum + po1Sum) / (double)sol.Customers.Count;
            Console.WriteLine("Priority 1: {0}", sl0);
            Console.WriteLine("Priority 2: {0}", sl1);
            return new double[] { sl0, sl1 };
        }
    }
}
