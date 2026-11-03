using System.Globalization;
using System.Text;

namespace VrdpoProject
{
    // Per-restart log of one solve (settings.logRestarts): <results>/<tag>/restarts/<instance>.csv, one row per
    // completed restart (review, experiment 5: diminishing returns of the multi-restart scheme).
    //
    //   restart              restart index (0-based; restarts whose construction failed are not counted)
    //   best_cost, best_routes  the restart's best solution that meets the service levels (empty when none did)
    //   found_iteration      local search iteration at which that solution was reached
    //   restart_time_s       wall time of the restart (construction + local search)
    //   elapsed_s            wall time since the search started, at the end of the restart
    //   improved             1 when the restart replaced the incumbent (fleet first, then cost)
    //   incumbent_cost, incumbent_routes, incumbent_restart  the best solution over restarts 0..restart
    public sealed class RestartWriter : IDisposable
    {
        private readonly StreamWriter writer;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public string Path { get; }

        public RestartWriter(RunOptions run, Instance instance)
        {
            Path = System.IO.Path.Combine(run.ResultsDir, run.Tag, "restarts", instance.RelativeName + ".csv");
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            writer = new StreamWriter(Path, append: false, new UTF8Encoding(false));
            writer.WriteLine("restart,best_cost,best_routes,found_iteration,restart_time_s,elapsed_s,improved,incumbent_cost,incumbent_routes,incumbent_restart");
        }

        public void Row(int restart, Solution restartBest, TimeSpan restartTime, TimeSpan elapsed, bool improved, Solution incumbent)
        {
            var line = new StringBuilder();
            line.Append(restart.ToString(Inv)).Append(',');
            if (restartBest.Cost < double.MaxValue)
            {
                line.Append(restartBest.Cost.ToString("R", Inv)).Append(',')
                    .Append(restartBest.Routes.Count(r => !r.IsEmpty).ToString(Inv)).Append(',')
                    .Append(restartBest.Repetition.ToString(Inv)).Append(',');
            }
            else
            {
                line.Append(",,,");
            }
            line.Append(restartTime.TotalSeconds.ToString("F3", Inv)).Append(',')
                .Append(elapsed.TotalSeconds.ToString("F3", Inv)).Append(',')
                .Append(improved ? '1' : '0').Append(',');
            if (incumbent.Cost < Solver.MaxAcceptableCost)
            {
                line.Append(incumbent.Cost.ToString("R", Inv)).Append(',')
                    .Append(incumbent.Routes.Count(r => !r.IsEmpty).ToString(Inv)).Append(',')
                    .Append(incumbent.Restart.ToString(Inv));
            }
            else
            {
                line.Append(",,");
            }
            writer.WriteLine(line.ToString());
            writer.Flush();
        }

        public void Dispose()
        {
            writer.Dispose();
        }
    }
}
