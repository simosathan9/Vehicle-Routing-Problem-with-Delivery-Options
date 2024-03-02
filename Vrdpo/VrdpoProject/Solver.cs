using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using OxyPlot;


namespace VrdpoProject
{
    public class Solver
    {
        private CustomerInsertionAllPositions bestInsertion = new();
        // this is unused so far.Make a method to write the final solution into a text file 
        Solution globalBestSol = new Solution();
        double globalBestSolCost = Math.Pow(10, 9);
        //Random rnd = new Random(14);
        LocalSearch ls = new();


        public void Solve()
        {
            Solution bestSol = new();
            for (int restart = 0; restart < 10; restart++)
            {
                Solution currentSol = new Solution();

                Random rnd = new(restart);

                SetRoutedToFalse(currentSol.Customers);
                SetServedToFalse(currentSol.Options);
                MinimumInsertions(currentSol, rnd); // give the rnd of each restart into the construction heuristic

                foreach (Route r in currentSol.Routes)
                {
                    if (r.SequenceOfLocations.Count == 2)
                    {
                        currentSol.Cost -= r.Cost;
                        continue;
                    }
                    Console.WriteLine("LOCATION | CUSTOMER");
                    for (int i = 0; i < r.SequenceOfOptions.Count; i++)
                    {
                        Console.WriteLine("{0} {1}", r.SequenceOfOptions[i].Location.Id, r.SequenceOfOptions[i].Cust.Id);
                    }
                    Console.WriteLine("Max capacity: {0} Load:{1}", r.Capacity, r.Load);
                    if (!currentSol.CheckRouteFeasibility(r)) { Console.WriteLine("INFEASIBLE"); }
                    Console.WriteLine("--------------");
                }
                if (currentSol.Routes[currentSol.Routes.Count - 1].Load == 0) { 
                    currentSol.Routes.RemoveAt(currentSol.Routes.Count - 1);
                }
                Console.WriteLine(currentSol.Cost);
                CalculateServiceLevel(currentSol);
                Console.WriteLine("---------------------------");
                Console.WriteLine("---------------------------");
                Console.WriteLine("---------------------------");

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
                        if (i > 1 && ((i - c) > 1))//100
                        {
                            c = i;
                            flip = ls.FindBestFlipMove(flip, currentSol);
                            var service_level = CalculateServiceLevel(currentSol, false);
                            if (service_level[0] > 0.84 && service_level[1] > 0.9)//0.8
                            {
                                if (flip.MoveCost < 0)
                                {
                                    ls.ApplyFlipMove(flip, currentSol);
                                }
                            } else
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
                    }
                    if (!currentSol.CheckEverything(currentSol)) {
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
            /**Flip fl = new();
            fl.ReinitializeVariables();
            while (CalculateServiceLevel(globalBestSol)[0] < 80 && CalculateServiceLevel(globalBestSol)[1] < 90)
            {
                fl = ls.FindBestFlipMove(fl, globalBestSol, false);
                if (!fl.IsValid())
                {
                    break;
                }
                ls.ApplyFlipMove(fl, globalBestSol);
                globalBestSolCost = globalBestSol.Cost;
                Console.WriteLine("cost: " + globalBestSolCost);
            }**/
            CalculateServiceLevel(globalBestSol);
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

        List<CustomerInsertionAllPositions> IdentifyMinimumCostInsertion(CustomerInsertionAllPositions bestInsertion, Solution sol)
        {

            List<CustomerInsertionAllPositions> topThree = new List<CustomerInsertionAllPositions>
            {
                bestInsertion
            };

            for (int i = 0; i < sol.Options.Count ; i++)
            {
                candidateOpt = sol.Options[i];
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

        void MinimumInsertions(Solution sol, Random rnd)
        {
            bool modelIsFeasible = true;
            while (sol.Customers.Any(x => !x.IsRouted))
            {   
                bestInsertion = new CustomerInsertionAllPositions();
                AlwaysKeepAnEmptyRoute(sol);
                List<CustomerInsertionAllPositions> topThree = IdentifyMinimumCostInsertion(bestInsertion, sol);
                bestInsertion = topThree[rnd.Next(topThree.Count)];
                if (bestInsertion.Customer != null)
                {
                    ApplyCustomerInsertionAllPositions(bestInsertion, sol);
                } else
                {
                    modelIsFeasible = false;
                    break;
                }
            }
            ReportSolution(sol);
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
    }
}
