using System.Globalization;
using System.Text;

namespace VrdpoProject
{
    // Per-iteration trace of one solve (settings.traceIterations): <results>/<tag>/traces/<instance>.csv.
    //
    //   restart, iteration   restart index (0-based) and local search iteration
    //   move                 applied move: swap, relocate, twoopt, flip, exchange, kick_flip (the flip
    //                        applied alone when the search is stuck), or none (no move found)
    //   move_cost            distance change of the applied move
    //   cost, routes         current solution after the move (distance, open routes)
    //   best_cost, best_routes  best solution of the restart so far that meets the service levels
    //                        (empty until there is one)
    //   sl0, sl1             current service levels
    // With settings.countMoves, per operator op in {swap, relocate, twoopt, flip, exchange}:
    //   eval_<op>, imp_<op>, adm_<op>   evaluated / improving / admissible candidates (see MoveStats)
    // and their sums eval_total, imp_total, adm_total. The counts describe the neighborhood of the solution
    // before the move of the row; they are empty on kick_flip rows (no scan is made there).
    public sealed class TraceWriter : IDisposable
    {
        private readonly StreamWriter writer;
        private readonly bool withCounts;
        private readonly StringBuilder line = new();
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        // Costs and service levels with 4 decimals keep the file small (15 x 15000 rows per solve).
        private const string Num = "0.####";

        public string Path { get; }

        public TraceWriter(RunOptions run, Instance instance, bool withCounts)
        {
            this.withCounts = withCounts;
            Path = System.IO.Path.Combine(run.ResultsDir, run.Tag, "traces", instance.RelativeName + ".csv");
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            writer = new StreamWriter(Path, append: false, new UTF8Encoding(false), bufferSize: 1 << 16);

            line.Append("restart,iteration,move,move_cost,cost,routes,best_cost,best_routes,sl0,sl1");
            if (withCounts)
            {
                foreach (string kind in new[] { "eval", "imp", "adm" })
                {
                    foreach (string op in MoveStats.Names)
                    {
                        line.Append(',').Append(kind).Append('_').Append(op);
                    }
                }
                line.Append(",eval_total,imp_total,adm_total");
            }
            writer.WriteLine(line.ToString());
        }

        public void Row(int restart, int iteration, string move, double moveCost, Solution current, Solution restartBest,
                        double sl0, double sl1, MoveStats stats)
        {
            line.Clear();
            line.Append(restart.ToString(Inv)).Append(',')
                .Append(iteration.ToString(Inv)).Append(',')
                .Append(move).Append(',')
                .Append(moveCost.ToString(Num, Inv)).Append(',')
                .Append(current.Cost.ToString(Num, Inv)).Append(',')
                .Append(current.Routes.Count(r => !r.IsEmpty).ToString(Inv)).Append(',');
            if (restartBest.Cost < double.MaxValue)
            {
                line.Append(restartBest.Cost.ToString(Num, Inv)).Append(',')
                    .Append(restartBest.Routes.Count(r => !r.IsEmpty).ToString(Inv)).Append(',');
            }
            else
            {
                line.Append(",,");
            }
            line.Append(sl0.ToString(Num, Inv)).Append(',').Append(sl1.ToString(Num, Inv));

            if (withCounts)
            {
                if (stats == null)
                {
                    line.Append(',', 3 * MoveStats.Names.Length + 3);
                }
                else
                {
                    AppendCounts(stats.Evaluated);
                    AppendCounts(stats.Improving);
                    AppendCounts(stats.Admissible);
                    line.Append(',').Append(stats.Evaluated.Sum().ToString(Inv))
                        .Append(',').Append(stats.Improving.Sum().ToString(Inv))
                        .Append(',').Append(stats.Admissible.Sum().ToString(Inv));
                }
            }
            writer.WriteLine(line.ToString());
        }

        private void AppendCounts(long[] counts)
        {
            foreach (long c in counts)
            {
                line.Append(',').Append(c.ToString(Inv));
            }
        }

        public void Dispose()
        {
            writer.Dispose();
        }
    }
}
