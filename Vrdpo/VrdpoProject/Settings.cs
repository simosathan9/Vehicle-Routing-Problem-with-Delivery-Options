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

        // The first two settings below are the model of Tilk et al., Dumez et al. and Yang et al.; the other four correct the search. All six are ON by
        // default. Set all six to false to reproduce the results of the published tables run for run.
        // "double" mode only: travel TIME = 10 * Euclidean distance, the same model as "int" mode without its per-arc rounding.
        // Windows and service times are stored x10, so false - travel time = raw distance - makes travel 10 times too fast
        // and solves a relaxed problem whose solutions often violate the real time windows.
        public bool consistentTravelTime { get; set; } = true;
        // The time window of a location limits when service STARTS in the Dumez et al. model (constraint 11), and the instance Readme adds the
        // delivery duration (5 minutes single, 2 minutes shared) to the due date to the same effect. With false the solver compares the COMPLETION
        // time with the due date, which is tighter by that duration at every stop; true adds it back to the due dates.
        public bool windowBindsServiceStart { get; set; } = true;

        // Corrections to the search.
        // Customer.Clone re-pointed the ORIGINAL customer's Options at the clone's list instead of the clone's own. With PrioritySwap off (the
        // case in this configuration) this has no visible effect on the result.
        public bool fixCloneSideEffect { get; set; } = true;
        // The same-route 2-opt time check rejected every candidate, so that move never did anything; with this on, the reversed route is
        // tested for time-window feasibility and the move can be applied.
        public bool fixSameRouteTwoOpt { get; set; } = true;
        // Relocation never used route 0 as a target (`targetRouteIndex == 0` was skipped), so no stop could be moved into the first route. With
        // this on, route 0 is a target like any other. The effect on the results is within noise.
        public bool relocateIntoFirstRoute { get; set; } = true;
        // Same-route relocation: the time windows were checked on the route that still contains the moved stop (so the stop was counted twice and
        // some valid moves were rejected), and the utilisation ratio subtracted that route's metric twice. With this on the route that results from
        // the move is checked and the ratio is 1. The effect on the results is within noise.
        public bool exactRelocationFeasibility { get; set; } = true;
    }
}