namespace VrdpoProject
{
    // Command line of one solve.
    public sealed class RunOptions
    {
        public const string Usage =
            "usage: VrdpoProject <instance.txt> [--settings <file>] [--tag <name>] [--results <dir>] [--set <key>=<value>]...\n" +
            "  --settings  settings file (default: settings.json)\n" +
            "  --tag       experiment name; outputs go to <results>/<tag>/ (default: current timestamp)\n" +
            "  --results   results root directory (default: results)\n" +
            "  --set       override one setting, e.g. --set exchangeCandidates=0 (repeatable)";

        public string InstancePath { get; private set; }
        public string SettingsPath { get; private set; } = "settings.json";
        public string Tag { get; private set; } = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        public string ResultsDir { get; private set; } = "results";
        public Dictionary<string, string> Overrides { get; } = new(StringComparer.OrdinalIgnoreCase);

        public static RunOptions Parse(string[] args)
        {
            var run = new RunOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                switch (arg)
                {
                    case "--settings":
                        run.SettingsPath = Next(args, ref i);
                        break;
                    case "--tag":
                        run.Tag = Next(args, ref i);
                        break;
                    case "--results":
                        run.ResultsDir = Next(args, ref i);
                        break;
                    case "--set":
                    {
                        string pair = Next(args, ref i);
                        int eq = pair.IndexOf('=');
                        if (eq <= 0)
                        {
                            throw new ArgumentException("--set expects key=value, got: " + pair);
                        }
                        run.Overrides[pair[..eq]] = pair[(eq + 1)..];
                        break;
                    }
                    default:
                        if (arg.StartsWith("--"))
                        {
                            throw new ArgumentException("Unknown option: " + arg);
                        }
                        if (run.InstancePath != null)
                        {
                            throw new ArgumentException("Only one instance path is accepted, got a second one: " + arg);
                        }
                        run.InstancePath = arg;
                        break;
                }
            }
            if (run.InstancePath == null)
            {
                throw new ArgumentException("Missing instance path.");
            }
            if (!File.Exists(run.InstancePath))
            {
                throw new ArgumentException("Instance file not found: " + run.InstancePath);
            }
            return run;
        }

        private static string Next(string[] args, ref int i)
        {
            if (i + 1 >= args.Length)
            {
                throw new ArgumentException(args[i] + " needs a value");
            }
            return args[++i];
        }
    }
}
