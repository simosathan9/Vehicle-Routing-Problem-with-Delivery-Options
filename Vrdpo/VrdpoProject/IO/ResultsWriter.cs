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

            WriteSettingsCopy(Path.Combine(dir, "settings.json"), settings.ToJson());

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

            string csvLine = string.Join(",",
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
                    settings.Type);
            AppendCsvLine(Path.Combine(dir, "runs.csv"), csvLine);
            return reportPath;
        }

        // Several solves of one experiment may run concurrently (one process per instance), so the
        // shared files are written under an exclusive lock, retried while another process holds it.
        private static readonly Random Jitter = new();

        private static FileStream OpenExclusive(string path)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(2);
            while (true)
            {
                try
                {
                    return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException) when (DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(Jitter.Next(20, 120));
                }
            }
        }

        // One line per solve; the header is written by whoever creates the file.
        private static void AppendCsvLine(string path, string line)
        {
            using FileStream fs = OpenExclusive(path);
            using var w = new StreamWriter(fs);
            if (fs.Length == 0)
            {
                w.WriteLine(CsvHeader);
            }
            else
            {
                fs.Seek(0, SeekOrigin.End);
            }
            w.WriteLine(line);
        }

        // Written once per experiment; a concurrent writer of the same content is harmless, a reader
        // never sees a partial file because the final step is a rename.
        private static void WriteSettingsCopy(string path, string json)
        {
            if (File.Exists(path))
            {
                return;
            }
            string tmp = path + "." + Environment.ProcessId + ".tmp";
            File.WriteAllText(tmp, json);
            try
            {
                File.Move(tmp, path, overwrite: false);
            }
            catch (IOException)
            {
                File.Delete(tmp); // another process won the race
            }
        }
    }
}
