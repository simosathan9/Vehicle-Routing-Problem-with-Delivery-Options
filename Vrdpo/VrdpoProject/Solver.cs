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
        double globalBestSolCost = Math.Pow(10,9);
        //Random rnd = new Random(14);
        LocalSearch ls = new();


        public void Solve()
        {
            Solution bestSol = new();
            for (int restart = 0; restart < 10; restart++)
            {
                Solution restartBestSol = new Solution();

                Random rnd = new(restart);

                SetRoutedToFalse(restartBestSol.Customers);
                SetServedToFalse(restartBestSol.Options);
                MinimumInsertions(restartBestSol, rnd); // give the rnd of each restart into the construction heuristic

                Route empty = new Route(166, 0, restartBestSol.Depot);
                foreach (Route r in restartBestSol.Routes) 
                {
                    if (r.SequenceOfLocations.Count == 2)
                    {
                        restartBestSol.Cost -= r.Cost;
                        empty = r;
                        continue;
                    }
                    Console.WriteLine("LOCATION | CUSTOMER");
                    for (int i = 0; i < r.SequenceOfOptions.Count; i++)
                    {
                        Console.WriteLine("{0} {1}", r.SequenceOfOptions[i].Location.Id, r.SequenceOfOptions[i].Cust.Id);
                    }
                    Console.WriteLine("Max capacity: {0} Load:{1}", r.Capacity, r.Load);
                    if (!restartBestSol.CheckRouteFeasibility(r)) { Console.WriteLine("INFEASIBLE"); }
                    Console.WriteLine("--------------");
                }
                restartBestSol.Routes.Remove(empty);
                Console.WriteLine(restartBestSol.Cost); 
                CalculateServiceLevel(restartBestSol); 
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
                    //!move these new outside loop. keep one instance that is overriden inside each loop
                    //Relocation rm = new();
                    //Swap sm = new();
                    //TwoOpt top = new();
                    //Flip flip = new();
                    rm.ReinitializeVariables();
                    sm.ReinitializeVariables();
                    top.ReinitializeVariables();
                    flip.ReinitializeVariables();
                    if (reinitCount == restartBestSol.Options.Count * 1.5)
                    {
                        restartBestSol.InitPromises();
                        reinitCount = 0;
                    }

                    //different schema
                    sm = ls.FindBestSwapMove(sm, restartBestSol);
                    rm = ls.FindBestRelocationMove(rm, restartBestSol);
                    top = ls.FindBestTwoOptMove(top, restartBestSol);
                    if (i > 2000 && ((i - c) > 500))
                    {
                        c = i;
                        flip = ls.FindBestFlipMove(flip, restartBestSol);
                    }

                    var minCost = FindMinMoveCost(sm, rm, top, flip);
                    if (minCost == sm.MoveCost)
                    {
                        ls.ApplySwapMove(sm, restartBestSol);
                        //Console.WriteLine("Swap");
                    } 
                    else if (minCost == rm.MoveCost)
                    {
                        ls.ApplyRelocationMove(rm, restartBestSol);
                        //Console.WriteLine("Reloc");

                    }
                    else if (minCost == top.MoveCost)
                    {
                        ls.ApplyTwoOptMove(top, restartBestSol);
                        //Console.WriteLine("Two opt");

                    }
                    else if (minCost == flip.MoveCost)
                    {
                        ls.ApplyFlipMove(flip, restartBestSol);
                        //Console.WriteLine("Flip");

                    }


                    //int k = rnd.Next(1, 5);
                    //if (k == 1)
                    //{
                    //    sm = ls.FindBestSwapMove(sm, restartBestSol);
                    //    ls.ApplySwapMove(sm, restartBestSol);
                    //}
                    //else if (k == 2)
                    //{
                    //    rm = ls.FindBestRelocationMove(rm, restartBestSol);
                    //    ls.ApplyRelocationMove(rm, restartBestSol);
                    //}
                    //else if (k == 3)
                    //{
                    //    top = ls.FindBestTwoOptMove(top, restartBestSol);
                    //    ls.ApplyTwoOptMove(top, restartBestSol);
                    //}
                    //else if (k == 4)
                    //{
                    //    if (i > 2000 && ((i - c) > 500))
                    //    {
                    //        c = i;
                    //        flip = ls.FindBestFlipMove(flip, restartBestSol);
                    //        ls.ApplyFlipMove(flip, restartBestSol);
                    //    }
                    //    else
                    //    {
                    //        sm = ls.FindBestSwapMove(sm, restartBestSol);
                    //        rm = ls.FindBestRelocationMove(rm, restartBestSol);
                    //        top = ls.FindBestTwoOptMove(top, restartBestSol);
                    //        if (rm.MoveCost < sm.MoveCost && rm.MoveCost < top.MoveCost)
                    //        {
                    //            ls.ApplyRelocationMove(rm, restartBestSol);
                    //        }
                    //        else if (sm.MoveCost < top.MoveCost && sm.MoveCost < rm.MoveCost)
                    //        {
                    //            ls.ApplySwapMove(sm, restartBestSol);
                    //        }
                    //        else
                    //        {
                    //            ls.ApplyTwoOptMove(top, restartBestSol);

                    //        }
                    //    }
                    //}

                    if (restartBestSol.Cost < bestSolCost)
                    {
                        bestSolCost = restartBestSol.Cost; 
                        bestSol = restartBestSol.DeepCopy(restartBestSol); 
                        lastImprovement = i;
                    }
                    Console.WriteLine(Convert.ToString(i) + ' ' + Convert.ToString(restartBestSol.Cost) + ' ' + Convert.ToString(bestSolCost));
                }
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
                CalculateServiceLevel(bestSol);

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
                                trialTime = timeAdded - timeRemoved + candidateOpt.ServiceTime;
                                tw = sol.RespectsTimeWindow(rt, j, candidateOpt.Location);
                                if (tw[0] <= tw[1]) { 

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
                                            bestInsertion.Ect = tw[0];
                                            bestInsertion.Lat = tw[1];

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
            //sol.UpdateTimes(insertion.Route, insertion.InsertionPosition);
            sol.UpdateTimes(insertion.Route);
            if (!sol.CheckRouteFeasibility(insertion.Route))
            {
                Console.WriteLine("-----");
            };
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
        void CalculateServiceLevel(Solution sol)
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
            Console.WriteLine("Priority 1: {0}", po0Sum/sum);
            Console.WriteLine("Priority 2: {0}", po1Sum/(sum - po0Sum));
        }
    }
}
