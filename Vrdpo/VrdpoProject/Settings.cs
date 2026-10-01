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

        // "double" mode only: travel TIME = 10 * Euclidean distance, the same model as "int" mode without its per-arc rounding.
        // Windows and service times are stored x10, so the default (false) - travel time = raw distance - makes travel 10 times too fast
        // and solves a relaxed problem whose solutions often violate the real time windows.
        public bool consistentTravelTime { get; set; } = false;
        // The time window of a location limits when service STARTS in the Dumez et al. model (constraint 11), and the instance Readme adds the
        // delivery duration (5 minutes single, 2 minutes shared) to the due date to the same effect. The solver compares the COMPLETION time with
        // the due date, which is tighter by that duration at every stop; true adds it back to the due dates.
        public bool windowBindsServiceStart { get; set; } = false;

        // Corrections to the search. Both are off by default, which reproduces the published behaviour run for run.
        // Customer.Clone re-pointed the ORIGINAL customer's Options at the clone's list instead of the clone's own. With PrioritySwap off (the
        // case in this configuration) this has no visible effect on the result.
        public bool fixCloneSideEffect { get; set; } = false;
        // The same-route 2-opt time check rejected every candidate, so that move never did anything; with this on, the reversed route is
        // tested for time-window feasibility and the move can be applied.
        public bool fixSameRouteTwoOpt { get; set; } = false;
    }
}