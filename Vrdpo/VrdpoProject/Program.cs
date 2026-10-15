namespace VrdpoProject
{
    internal static class Program
    {
        // Usage: VrdpoProject <instance.txt> [--settings settings.json] [--tag name] [--results results] [--set key=value]...
        static int Main(string[] args)
        {
            RunOptions run;
            try
            {
                run = RunOptions.Parse(args);
            }
            catch (ArgumentException e)
            {
                Console.Error.WriteLine(e.Message);
                Console.Error.WriteLine(RunOptions.Usage);
                return 2;
            }

            Settings settings = Settings.Load(run.SettingsPath, run.Overrides);
            Instance instance = InstanceReader.Read(run.InstancePath, settings);

            var solver = new Solver(instance, settings);
            Solution best = solver.Solve();

            string report = ResultsWriter.Write(run, settings, instance, best, solver.BestRestartTime, solver.TotalTime);
            Console.WriteLine("Report written to " + report);
            return 0;
        }
    }
}
