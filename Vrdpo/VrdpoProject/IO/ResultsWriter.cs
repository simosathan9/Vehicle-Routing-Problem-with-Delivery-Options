using System.Globalization;
using System.Text;

namespace VrdpoProject
{
    // Writes the outputs of one solve under <results>/<tag>/:
    //   settings.json            the settings the experiment ran with (written once per tag)
    //   runs.csv                 one line per solve (appended)
    //   <set>/<instance>.txt     the full solution: summary + route listing
    public static class ResultsWriter
    {
        private const string CsvHeader =
            "timestamp,instance,feasible,cost,routes,sl0,sl1,restart,repetition,restart_time_s,total_time_s," +
            "restarts,repetitions,exchange_candidates,no_improvement_limit,type";

        public static string Write(RunOptions run, Settings settings, Instance instance, Solution sol, TimeSpan restartTime, TimeSpan totalTime)
        {
            var inv = CultureInfo.InvariantCulture;
            string dir = Path.Combine(run.ResultsDir, run.Tag);
            Directory.CreateDirectory(dir);

            string settingsCopy = Path.Combine(dir, "settings.json");
            if (!File.Exists(settingsCopy))
            {
                File.WriteAllText(settingsCopy, settings.ToJson());
            }

            bool feasible = sol.CheckEverything();
            int routes = sol.Routes.Count(r => r.SequenceOfLocations.Count > 2);
            var (sl0, sl1) = sol.ServiceLevel();

            var report = new StringBuilder();
            report.AppendLine(feasible ? "Feasible" : "INFEASIBLE!");
            report.AppendLine(string.Format(inv, "Restart {0}, Repetition {1}", sol.Restart, sol.Repetition));
            report.AppendLine(string.Format(inv, "Total time: {0}, Restart time: {1}", totalTime, restartTime));
            report.AppendLine(string.Format(inv, "Total cost: {0}", sol.Cost));
            report.AppendLine(string.Format(inv, "Number of Routes: {0}", routes));
            report.AppendLine(string.Format(inv, "Priority 1: {0}", sl0));
            report.AppendLine(string.Format(inv, "Priority 2: {0}", sl1));
            report.AppendLine();
            for (int i = 0; i < sol.Routes.Count; i++)
            {
                Route rt = sol.Routes[i];
                report.AppendLine("Route " + i.ToString(inv));
                report.AppendLine("Location Option Customer");
                for (int j = 0; j < rt.SequenceOfOptions.Count; j++)
                {
                    if (j == 0 || j == rt.SequenceOfOptions.Count - 1)
                    {
                        report.AppendLine(rt.SequenceOfLocations[j].Id.ToString(inv) + " - -");
                    }
                    else
                    {
                        report.AppendLine(string.Format(inv, "{0} {1} {2}", rt.SequenceOfLocations[j].Id, rt.SequenceOfOptions[j].Id, rt.SequenceOfCustomers[j].Id));
                    }
                }
                report.AppendLine();
            }

            string reportPath = Path.Combine(dir, instance.RelativeName + ".txt");
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
            File.WriteAllText(reportPath, report.ToString());

            string csvPath = Path.Combine(dir, "runs.csv");
            bool newCsv = !File.Exists(csvPath);
            using (var csv = new StreamWriter(csvPath, append: true))
            {
                if (newCsv)
                {
                    csv.WriteLine(CsvHeader);
                }
                csv.WriteLine(string.Join(",",
                    DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", inv),
                    instance.RelativeName,
                    feasible ? "1" : "0",
                    sol.Cost.ToString("R", inv),
                    routes.ToString(inv),
                    sl0.ToString("R", inv),
                    sl1.ToString("R", inv),
                    sol.Restart.ToString(inv),
                    sol.Repetition.ToString(inv),
                    restartTime.TotalSeconds.ToString("F3", inv),
                    totalTime.TotalSeconds.ToString("F3", inv),
                    settings.Restarts.ToString(inv),
                    settings.Repetitions.ToString(inv),
                    settings.ExchangeCandidates.ToString(inv),
                    settings.NoImprovementLimit.ToString(inv),
                    settings.Type));
            }
            return reportPath;
        }
    }
}
