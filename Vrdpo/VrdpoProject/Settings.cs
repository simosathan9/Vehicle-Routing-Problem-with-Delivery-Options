using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VrdpoProject
{
    // Run configuration, read once from settings.json (keys are the JSON names below) and optionally
    // overridden from the command line with --set key=value.
    public sealed class Settings
    {
        // Independent restarts (construction + local search). A restart whose construction fails is repeated.
        [JsonPropertyName("restarts")]
        public int Restarts { get; set; } = 15;

        // Local search iterations per restart.
        [JsonPropertyName("repetitions")]
        public int Repetitions { get; set; } = 15000;

        // Per-iteration console trace.
        [JsonPropertyName("verbal")]
        public bool Verbal { get; set; } = false;

        // The promise matrix is reset every |Options| * ratio iterations.
        [JsonPropertyName("promisesRestartRatio")]
        public double PromisesRestartRatio { get; set; } = 1.5;

        // Distance metric. "int": travel time and cost are ceil(10 * Euclidean distance) (Tilk et al. instances).
        // "double": cost is the raw Euclidean distance and travel time is 10 times it (Dumez et al. instances).
        [JsonPropertyName("type")]
        public string Type { get; set; } = "double";

        // Consecutive iterations without a new restart-best after which the restart stops; <= 0 disables the rule.
        [JsonPropertyName("noImprovementLimit")]
        public int NoImprovementLimit { get; set; } = 0;

        // Exchange move: number of cheapest (by estimate) down/up flip pairs evaluated exactly per iteration; 0 = off.
        [JsonPropertyName("exchangeCandidates")]
        public int ExchangeCandidates { get; set; } = 20;

        // ---- Review experiments (all off by default; with all of them off the solver runs exactly as before) ----

        // VRPTW setup: every customer can only be served at its home option, i.e. its best-priority private
        // address (location type 2); a customer without a private address keeps its best-priority option.
        // Service-level constraints are not enforced (there is no option choice left). The construction adds
        // routes as needed.
        [JsonPropertyName("lockToHome")]
        public bool LockToHome { get; set; } = false;

        // Only the routing moves (relocation, swap, 2-opt): no flip, no exchange, no flip kick.
        [JsonPropertyName("routingMovesOnly")]
        public bool RoutingMovesOnly { get; set; } = false;

        // Writes one CSV row per local search iteration to <results>/<tag>/traces/<instance>.csv
        // (restart, iteration, applied move, move cost, current and restart-best cost and routes, service levels).
        [JsonPropertyName("traceIterations")]
        public bool TraceIterations { get; set; } = false;

        // Counts, per operator and iteration, the candidate moves evaluated, the feasible ones (whatever their
        // cost), the improving and the admissible ones, without the cost-first pruning of the scans (every
        // candidate is checked for feasibility). Does not change which move is applied. Needs traceIterations.
        [JsonPropertyName("countMoves")]
        public bool CountMoves { get; set; } = false;

        // Counts, per iteration, the inter-route relocation and swap candidates by the type of the transferred
        // delivery location(s) (private address vs shared locker) and how many of them pass the capacity and
        // time-window checks, whatever their cost (no cost-first pruning for the counters). Written to
        // <results>/<tag>/coupling/<instance>.csv and coupling_totals.csv. Does not change which move is applied.
        [JsonPropertyName("countCoupling")]
        public bool CountCoupling { get; set; } = false;

        // Writes one CSV row per restart to <results>/<tag>/restarts/<instance>.csv: the restart's best cost and
        // routes, the iteration it was found at, the restart's time, the elapsed time and the incumbent after the
        // restart (experiment 5: diminishing returns of the restarts). Tiny file; does not change the search.
        [JsonPropertyName("logRestarts")]
        public bool LogRestarts { get; set; } = false;

        // ---- Experiment 4 (promise memory vs. tabu tenure) ----

        // Arc memory of the local search. "promise": an arc the move creates is allowed only if the solution after the
        // move is better than the solution in which the arc was last created (the paper's rule; reset every
        // |Options| * promisesRestartRatio iterations). "tabu": an arc a move removes may not be re-created for
        // tabuTenure iterations (standard attribute tabu), unless the move beats the restart's best cost (aspiration).
        [JsonPropertyName("memory")]
        public string Memory { get; set; } = "promise";

        // Fixed tabu tenure in iterations; 0 = dynamic tenure, drawn per forbidden arc uniformly from
        // [tabuTenureMin * |N|, tabuTenureMax * |N|] (|N| = customers). -1 = no memory at all (plain best-improvement descent).
        [JsonPropertyName("tabuTenure")]
        public int TabuTenure { get; set; } = 10;

        [JsonPropertyName("tabuTenureMin")]
        public double TabuTenureMin { get; set; } = 0.1;

        [JsonPropertyName("tabuTenureMax")]
        public double TabuTenureMax { get; set; } = 0.2;

        // Aspiration criterion of the tabu memory: a tabu arc is allowed when the move's solution is cheaper than the
        // restart's best solution so far.
        [JsonPropertyName("tabuAspiration")]
        public bool TabuAspiration { get; set; } = true;

        // Counts the distinct solution states visited per restart (hash of the arc set) and the revisits; written to the
        // restart log (needs logRestarts). Search mobility measure of experiment 4.
        [JsonPropertyName("countStates")]
        public bool CountStates { get; set; } = false;

        [JsonIgnore]
        public bool TabuMemory => Memory == "tabu";

        [JsonIgnore]
        public bool IntegerMetric => Type == "int";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true,
        };

        public static Settings Load(string path, IReadOnlyDictionary<string, string> overrides)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Settings file not found: " + path);
            }
            Settings settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidDataException("Settings file is empty: " + path);
            foreach (var (key, value) in overrides)
            {
                settings.Set(key, value);
            }
            settings.Validate();
            return settings;
        }

        public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

        // Sets a property by its JSON name (case-insensitive) from its textual value.
        public void Set(string key, string value)
        {
            foreach (PropertyInfo prop in typeof(Settings).GetProperties())
            {
                string name = prop.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? prop.Name;
                if (!string.Equals(name, key, StringComparison.OrdinalIgnoreCase) || !prop.CanWrite)
                {
                    continue;
                }
                prop.SetValue(this, Convert.ChangeType(value, prop.PropertyType, CultureInfo.InvariantCulture));
                return;
            }
            throw new ArgumentException("Unknown setting: " + key);
        }

        private void Validate()
        {
            if (Type != "int" && Type != "double")
            {
                throw new InvalidDataException("settings.type must be \"int\" or \"double\", got \"" + Type + "\"");
            }
            if (Restarts <= 0 || Repetitions <= 0)
            {
                throw new InvalidDataException("settings.restarts and settings.repetitions must be positive");
            }
            if (ExchangeCandidates < 0)
            {
                throw new InvalidDataException("settings.exchangeCandidates must be >= 0");
            }
            if (Memory != "promise" && Memory != "tabu")
            {
                throw new InvalidDataException("settings.memory must be \"promise\" or \"tabu\", got \"" + Memory + "\"");
            }
            if (TabuTenure == 0 && (TabuTenureMin <= 0 || TabuTenureMax < TabuTenureMin))
            {
                throw new InvalidDataException("settings.tabuTenureMin/Max must satisfy 0 < min <= max for the dynamic tenure");
            }
            if (CountStates && !LogRestarts)
            {
                throw new InvalidDataException("settings.countStates needs settings.logRestarts = true (the counts are written to the restart log)");
            }
            if (CountMoves && !TraceIterations)
            {
                throw new InvalidDataException("settings.countMoves needs settings.traceIterations = true (the counts are written to the trace)");
            }
        }
    }
}
