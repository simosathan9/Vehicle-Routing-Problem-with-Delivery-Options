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
        }
    }
}
