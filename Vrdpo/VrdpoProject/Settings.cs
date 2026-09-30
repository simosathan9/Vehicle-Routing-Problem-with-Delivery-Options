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
        // settings.json: when absent it stays 2000 (the value that used to be hard-coded), so existing settings files
        // behave exactly as before. A value <= 0 disables the rule (the restart then runs for `repetitions` iterations).
        // Note that until the first acceptable solution has been found the counter is measured from iteration 0, so a
        // large instance whose service level is still climbing at iteration 2000 needs a larger value to get a solution.
        public int noImprovementLimit { get; set; } = 2000;
    }
}