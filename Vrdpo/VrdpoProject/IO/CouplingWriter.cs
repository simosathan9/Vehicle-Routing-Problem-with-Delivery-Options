using System.Globalization;
using System.Text;

namespace VrdpoProject
{
    // Inter-route coupling counts of one solve (settings.countCoupling), see CouplingStats:
    //
    //   <results>/<tag>/coupling/<instance>.csv   one row per local search iteration in which the
    //                                             neighbourhoods were scanned (kick iterations make no scan)
    //   <results>/<tag>/coupling_totals.csv       one row per solve with the sums over all iterations
    //                                             (appended under a file lock, like runs.csv)
    //
    // Columns: restart, iteration, routes (open routes of the solution the scan was made on), then per
    // operator op in {relocate, swap} and category cat of the operator:
    //   eval_<op>_<cat>, cap_<op>_<cat>, feas_<op>_<cat>   evaluated / capacity-feasible / feasible candidates
    // The totals file has instance, restarts, iterations instead of restart, iteration, routes.
    public sealed class CouplingWriter : IDisposable
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private readonly StreamWriter writer;
        private readonly StringBuilder line = new();
        private readonly string totalsPath;
        private readonly string instanceName;
        private readonly CouplingStats totals = new();
        private int restarts = -1;
        private long iterations;

        public string Path { get; }

        public CouplingWriter(RunOptions run, Instance instance)
        {
            instanceName = instance.RelativeName;
            string dir = System.IO.Path.Combine(run.ResultsDir, run.Tag);
            Path = System.IO.Path.Combine(dir, "coupling", instance.RelativeName + ".csv");
            totalsPath = System.IO.Path.Combine(dir, "coupling_totals.csv");
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            writer = new StreamWriter(Path, append: false, new UTF8Encoding(false), bufferSize: 1 << 16);
            writer.WriteLine("restart,iteration,routes" + CounterHeader());
        }

        public static string CounterHeader()
        {
            var sb = new StringBuilder();
            for (int op = 0; op < CouplingStats.Operators.Length; op++)
            {
                foreach (string cat in CouplingStats.Categories[op])
                {
                    foreach (string kind in new[] { "eval", "cap", "feas" })
                    {
                        sb.Append(',').Append(kind).Append('_').Append(CouplingStats.Operators[op]).Append('_').Append(cat);
                    }
                }
            }
            return sb.ToString();
        }

        // The counts of one scanned iteration; `routes` are the open routes of the scanned solution.
        public void Row(int restart, int iteration, int routes, CouplingStats stats)
        {
            line.Clear();
            line.Append(restart.ToString(Inv)).Append(',')
                .Append(iteration.ToString(Inv)).Append(',')
                .Append(routes.ToString(Inv));
            AppendCounters(line, stats);
            writer.WriteLine(line.ToString());

            totals.Add(stats);
            iterations++;
            restarts = Math.Max(restarts, restart);
        }

        private static void AppendCounters(StringBuilder sb, CouplingStats stats)
        {
            for (int op = 0; op < CouplingStats.Operators.Length; op++)
            {
                for (int c = 0; c < CouplingStats.Categories[op].Length; c++)
                {
                    sb.Append(',').Append(stats.Evaluated[op][c].ToString(Inv))
                      .Append(',').Append(stats.CapacityOk[op][c].ToString(Inv))
                      .Append(',').Append(stats.Feasible[op][c].ToString(Inv));
                }
            }
        }

        public void Dispose()
        {
            writer.Dispose();
            var sb = new StringBuilder();
            sb.Append(instanceName).Append(',')
              .Append((restarts + 1).ToString(Inv)).Append(',')
              .Append(iterations.ToString(Inv));
            AppendCounters(sb, totals);
            ResultsWriter.AppendCsvLine(totalsPath, "instance,restarts,iterations" + CounterHeader(), sb.ToString());
        }
    }
}
