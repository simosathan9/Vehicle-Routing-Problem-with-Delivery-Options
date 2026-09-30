using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VrdpoProject
{
    public class Settings
    {
        public int restarts { get; set; }
        public int repetitions { get; set; }
        public bool verbal { get; set; }
        public double promisesRestartRatio { get; set; }
        public bool multiRestart { get; set; }
        public string schema { get; set; }
        public string type { get; set; }
        public double randomness { get; set; }
        // Number of consecutive iterations without a new best solution after which a restart stops. Optional in
        // settings.json: when absent (or <= 0) the rule is off and every restart runs for `repetitions` iterations, as on this
        // branch before the setting existed. A value such as 2000 stops a restart early (about 5x faster, roughly 0.7 pp
        // worse mean gap on the 50-customer benchmarks). Until the first acceptable solution has been found the counter is
        // measured from iteration 0, so a large instance whose service level is still climbing at iteration 2000 needs a
        // larger value to get a solution.
        public int noImprovementLimit { get; set; } = 0;
    }
}