using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VrdpoProject
{
    // Multi-restart local search: every restart builds a solution (see Construction.cs) and improves it
    // for `repetitions` iterations, each applying the best of the Swap, Relocation, TwoOpt, Flip and
    // Exchange moves. Solutions are compared on cost + open routes * 10000 (fleet size first).
    public partial class Solver
    {
        // Added to every open route when comparing solutions and moves.
        public const double RoutePenalty = 10000;
        // Minimum share of customers served at priority 0, and at priority 0 or 1.
        public const double MinServiceLevel0 = 0.8;
        public const double MinServiceLevel1 = 0.9;
        // A solution with a cost at or above this is treated as unacceptable (empty solutions cost 1e9).
        private const double MaxAcceptableCost = 100000;
        // After this many iterations without a new restart-best, 1% of the iterations apply a flip only.
        private const int KickAfterIterations = 500;
        private const double KickThreshold = 0.99;

        private readonly Instance instance;
        private readonly Settings settings;
        private readonly LocalSearch ls;
        // Tolerance when comparing solution values ("int" metric: exact).
        private readonly double smallDouble;

        public TimeSpan BestRestartTime { get; private set; } = TimeSpan.Zero;
        public TimeSpan TotalTime { get; private set; } = TimeSpan.Zero;

        public Solver(Instance instance, Settings settings)
        {
            this.instance = instance;
            this.settings = settings;
            ls = new LocalSearch(settings);
            smallDouble = settings.IntegerMetric ? 0 : 0.001;
        }

        public Solution Solve()
        {
            Console.WriteLine($"Running on: {RuntimeInformation.FrameworkDescription}");
            var totalTimer = Stopwatch.StartNew();
            Solution best = RunRestarts();
            PrintSolution(best);
            Console.WriteLine("The best solution's cost: " + best.Cost);
            PrintServiceLevel(best);
            totalTimer.Stop();
            TotalTime = totalTimer.Elapsed;
            Console.WriteLine("Total time: " + TotalTime);
            return best;
        }

        private Solution RunRestarts()
        {
            Solution bestSol = new(instance);
            bestSol.Cost = Math.Pow(10, 9);
            int numberOfRestarts = settings.Restarts;
            int noImprovementLimit = settings.NoImprovementLimit > 0 ? settings.NoImprovementLimit : int.MaxValue;
            bool useExchange = settings.ExchangeCandidates > 0;
            var restartTimer = new Stopwatch();
            int restartCounter = -1;

            for (int restart = 0; restart < numberOfRestarts; restart++)
            {
                restartTimer.Restart();
                Random constructionRnd = new(restart);
                Random kickRnd = new(restart);
                int reinitCount = -1;
                int lastImprovement = 0;
                Relocation rm = new();
                Swap sm = new();
                TwoOpt top = new();
                Flip flip = new();
                ExchangeMove exch = new();

                Solution currentSol = new(instance);
                SetRoutedToFalse(currentSol.Customers);
                SetServedToFalse(currentSol.Options);
                if (!MinimumInsertions(currentSol, constructionRnd))
                {
                    // Construction failed: this restart does not count.
                    numberOfRestarts++;
                    continue;
                }
                RemoveLastRouteIfEmpty(currentSol);
                currentSol.SeedServiceLevelCounts();

                Solution localBest = new(instance);
                localBest.Cost = double.MaxValue;
                restartCounter++;
                Console.WriteLine("Restart: " + restartCounter);

                for (int i = 0; i < settings.Repetitions; i++)
                {
                    if (i - lastImprovement > noImprovementLimit)
                    {
                        break;
                    }

                    reinitCount++;
                    rm.ReinitializeVariables();
                    sm.ReinitializeVariables();
                    top.ReinitializeVariables();
                    flip.ReinitializeVariables();
                    if (useExchange) { exch.ReinitializeVariables(); }

                    if (reinitCount == currentSol.Options.Count * settings.PromisesRestartRatio)
                    {
                        currentSol.InitPromises();
                        reinitCount = 0;
                    }

                    // Kick: when stuck, occasionally apply the best flip alone.
                    if (i - lastImprovement > KickAfterIterations && kickRnd.NextDouble() > KickThreshold)
                    {
                        flip = ls.FindBestFlipMove(flip, currentSol);
                        ls.ApplyFlipMove(flip, currentSol);
                        continue;
                    }

                    sm = ls.FindBestSwapMove(sm, currentSol);
                    rm = ls.FindBestRelocationMove(rm, currentSol);
                    top = ls.FindBestTwoOptMove(top, currentSol);
                    flip = ls.FindBestFlipMove(flip, currentSol);
                    if (useExchange) { exch = ls.FindBestExchangeMove(exch, currentSol); }

                    double mincost = Math.Min(Math.Min(Math.Min(sm.TotalCost, rm.TotalCost), top.TotalCost), flip.TotalCost);
                    if (useExchange) { mincost = Math.Min(mincost, exch.TotalCost); }

                    if (mincost == sm.TotalCost)
                    {
                        ls.ApplySwapMove(sm, currentSol);
                    }
                    else if (mincost == rm.TotalCost)
                    {
                        ls.ApplyRelocationMove(rm, currentSol);
                    }
                    else if (mincost == top.TotalCost)
                    {
                        ls.ApplyTwoOptMove(top, currentSol);
                    }
                    else if (mincost == flip.TotalCost)
                    {
                        ls.ApplyFlipMove(flip, currentSol);
                    }
                    else if (useExchange && mincost == exch.TotalCost)
                    {
                        ls.ApplyExchangeMove(exch, currentSol);
                    }
                    RemoveLastRouteIfEmpty(currentSol);
                    currentSol.SolutionUtilizationMetric = currentSol.CalculateUtilizationMetric();

                    if (!currentSol.CheckEverything())
                    {
                        Console.WriteLine("Infeasible Solution!!!");
                    }

                    var (sl0, sl1) = currentSol.ServiceLevelFromCounts();
                    int openRoutes = currentSol.Routes.Count(x => !x.IsEmpty);
                    if (currentSol.Cost + openRoutes * RoutePenalty < localBest.Cost + localBest.Routes.Count * RoutePenalty + smallDouble
                        && currentSol.Cost < MaxAcceptableCost && sl0 >= MinServiceLevel0 && sl1 >= MinServiceLevel1)
                    {
                        currentSol.Repetition = i;
                        localBest = currentSol.DeepCopy();
                        localBest.RemoveEmptyRoutes();
                        lastImprovement = i;
                        if (!localBest.CheckEverything())
                        {
                            Console.WriteLine("Infeasible restart-best!!!");
                        }
                        currentSol.Routes = currentSol.Routes.Where(rt => !rt.IsEmpty).ToList();
                        if (settings.Verbal)
                        {
                            Console.WriteLine("{0} {1} {2} {3} {4}", i, currentSol.Cost, localBest.Cost, localBest.Routes.Count(x => !x.IsEmpty), currentSol.LastMove);
                        }
                    }
                    if (settings.Verbal)
                    {
                        Console.WriteLine("{0} {1} {2} {3}", i, currentSol.Cost, localBest.Cost, localBest.Routes.Count(x => !x.IsEmpty));
                    }
                }
                restartTimer.Stop();
                PrintServiceLevel(localBest);
                PrintSolution(localBest);

                if (localBest.Cost + localBest.Routes.Count * RoutePenalty < bestSol.Cost + bestSol.Routes.Count * RoutePenalty + smallDouble
                    && localBest.Cost < MaxAcceptableCost)
                {
                    bestSol = localBest.DeepCopy();
                    bestSol.Restart = restartCounter;
                    bestSol.Repetition = localBest.Repetition;
                    BestRestartTime = restartTimer.Elapsed;
                }
            }
            PrintServiceLevel(bestSol);
            return bestSol;
        }

        // The construction keeps a spare empty route at the end; drop it.
        private static void RemoveLastRouteIfEmpty(Solution sol)
        {
            if (sol.Routes[sol.Routes.Count - 1].Load == 0)
            {
                sol.Routes.RemoveAt(sol.Routes.Count - 1);
            }
        }

        private static void PrintServiceLevel(Solution sol)
        {
            var (sl0, sl1) = sol.ServiceLevel();
            Console.WriteLine("Priority 1: {0}", sl0);
            Console.WriteLine("Priority 2: {0}", sl1);
        }

        private static void PrintSolution(Solution sol)
        {
            Console.WriteLine("///////////////////");
            Console.WriteLine(sol.CheckEverything() ? "Feasible" : "INFEASIBLE!");
            Console.WriteLine("Solution Cost: {0}", sol.Cost);
            foreach (Route r in sol.Routes)
            {
                Console.WriteLine("--------------");
                Console.WriteLine("Route {0}:", r.Id);
                Console.WriteLine();
                if (r.IsEmpty)
                {
                    continue;
                }
                Console.WriteLine("LOCATION | CUSTOMER");
                for (int i = 0; i < r.SequenceOfOptions.Count; i++)
                {
                    Console.WriteLine("{0} {1}", r.SequenceOfOptions[i].Location.Id, r.SequenceOfOptions[i].Cust.Id);
                }
                Console.WriteLine("Max capacity: {0} Load:{1}", r.Capacity, r.Load);
                if (!sol.CheckRouteFeasibility(r)) { Console.WriteLine("INFEASIBLE!"); }
                Console.WriteLine("--------------");
            }
            Console.WriteLine("Solution Cost: " + sol.Cost);
            PrintServiceLevel(sol);
            Console.WriteLine("///////////////////");
        }

        private static void SetRoutedToFalse(List<Customer> customers)
        {
            foreach (Customer customer in customers)
            {
                customer.IsRouted = false;
            }
        }

        private static void SetServedToFalse(List<Option> options)
        {
            foreach (Option option in options)
            {
                option.IsServed = false;
            }
        }
    }
}
