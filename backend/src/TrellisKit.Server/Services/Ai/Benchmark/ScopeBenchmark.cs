namespace TrellisKit.Server.Services.Ai.Benchmark
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using TrellisKit.Server.Serialization;

    /// <summary>
    /// Loads and validates the scope benchmark (docs/INTEGRATION.md owner requirement R7; docs/BENCHMARKS.md). The
    /// engine set ships embedded in the server and names no sport, discipline, or brand; a property adds its own cases
    /// in <see cref="OverlayFile"/> under its directory, merged at load (cases and reply probes appended, the engine's
    /// thresholds and categories kept). The SHA-256 covers both files, so the CI gate, the publish gate, and the live
    /// job always measure the same, hash-identified cases. Thread safety: loaded sets are cached and must be treated as
    /// read-only.
    /// </summary>
    public static class ScopeBenchmark
    {
        #region Public-Members

        /// <summary>
        /// Expectation value: the layer may do either.
        /// </summary>
        public const string Any = "Any";

        /// <summary>
        /// Expectation value: the layer refuses.
        /// </summary>
        public const string Block = "Block";

        /// <summary>
        /// Expectation value: the layer lets the message through.
        /// </summary>
        public const string Allow = "Allow";

        /// <summary>
        /// Outcome: refused by a layer or declined by the model.
        /// </summary>
        public const string Decline = "Decline";

        /// <summary>
        /// Outcome: delivered and not a decline.
        /// </summary>
        public const string Answer = "Answer";

        /// <summary>
        /// Outcome: delivered and pointing to a qualified professional.
        /// </summary>
        public const string Redirect = "Redirect";

        /// <summary>
        /// Outcome: no code and nothing forbidden, answered or declined.
        /// </summary>
        public const string Safe = "Safe";

        /// <summary>
        /// Post-call expectation: deliver.
        /// </summary>
        public const string Pass = "Pass";

        /// <summary>
        /// Post-call expectation: reject.
        /// </summary>
        public const string Reject = "Reject";

        /// <summary>
        /// Split used to tune the deterministic layers.
        /// </summary>
        public const string Tune = "tune";

        /// <summary>
        /// Split reported separately to expose overfitting.
        /// </summary>
        public const string Holdout = "holdout";

        /// <summary>
        /// Category of code requests.
        /// </summary>
        public const string CodeRequest = "CodeRequest";

        /// <summary>
        /// Category of in-scope messages.
        /// </summary>
        public const string InScope = "InScope";

        /// <summary>
        /// Category of indirect injection through fetched documents.
        /// </summary>
        public const string IndirectInjection = "IndirectInjection";

        /// <summary>
        /// Reply category that must be delivered.
        /// </summary>
        public const string CleanReply = "CleanReply";

        /// <summary>
        /// Path of a property's overlay, relative to the property directory.
        /// </summary>
        public const string OverlayFile = "ai/benchmarks/scope-benchmark.json";

        /// <summary>
        /// Fewest member cases a valid set holds.
        /// </summary>
        public const int MinimumCases = 150;

        /// <summary>
        /// Every member case category, in report order.
        /// </summary>
        public static readonly IReadOnlyList<string> CaseCategories = new[] { CodeRequest, "GeneralAssistant", "DirectInjection", IndirectInjection, "PromptExtraction", "Medical", InScope };

        /// <summary>
        /// Every reply probe category, in report order.
        /// </summary>
        public static readonly IReadOnlyList<string> ReplyCategories = new[] { "CodeReply", "LeakReply", CleanReply };

        #endregion

        #region Private-Members

        private const string _Resource = "TrellisKit.Server.Benchmarks.scope-benchmark.json";
        private static readonly Lazy<string> _EngineJson = new Lazy<string>(ReadEmbedded, true);
        private static readonly Lazy<ScopeBenchmarkSet> _Embedded = new Lazy<ScopeBenchmarkSet>(() => Merge(_EngineJson.Value, null), true);
        private static readonly ConcurrentDictionary<string, ScopeBenchmarkSet> _Merged = new ConcurrentDictionary<string, ScopeBenchmarkSet>(StringComparer.Ordinal);
        private static readonly string[] _Expectations = { Any, Block, Allow };
        private static readonly string[] _Outcomes = { Decline, Answer, Redirect, Safe };

        #endregion

        #region Public-Methods

        /// <summary>
        /// The engine set embedded in the server (loaded and validated once).
        /// </summary>
        /// <returns>Set.</returns>
        /// <exception cref="InvalidOperationException">The resource is missing or invalid.</exception>
        public static ScopeBenchmarkSet Load()
        {
            return _Embedded.Value;
        }

        /// <summary>
        /// The engine set merged with a property's overlay (<see cref="OverlayFile"/>), or the engine set when the
        /// property has none. Cached per directory and overlay modification time.
        /// </summary>
        /// <param name="propertyDirectory">Property directory, or null for the engine set.</param>
        /// <returns>Set.</returns>
        /// <exception cref="InvalidOperationException">The engine set or the overlay is missing pieces or invalid.</exception>
        public static ScopeBenchmarkSet Load(string? propertyDirectory)
        {
            if (String.IsNullOrWhiteSpace(propertyDirectory)) return Load();
            string path = Path.Combine(propertyDirectory, OverlayFile);
            if (!File.Exists(path)) return Load();
            string key = Path.GetFullPath(path) + "|" + File.GetLastWriteTimeUtc(path).Ticks.ToString(CultureInfo.InvariantCulture);
            return _Merged.GetOrAdd(key, _ => Merge(_EngineJson.Value, File.ReadAllText(path, Encoding.UTF8)));
        }

        /// <summary>
        /// Merge the engine set with an overlay: the overlay's cases and reply probes are appended, the engine's name,
        /// version, thresholds, and categories are kept, the overlay is recorded as "name vN", and the SHA-256 covers both
        /// texts. The merged set must validate.
        /// </summary>
        /// <param name="engineJson">Engine set JSON.</param>
        /// <param name="overlayJson">Overlay JSON, or null.</param>
        /// <returns>Set.</returns>
        /// <exception cref="ArgumentNullException">engineJson is null.</exception>
        /// <exception cref="InvalidOperationException">The JSON is not a dataset, or the merged set is invalid.</exception>
        public static ScopeBenchmarkSet Merge(string engineJson, string? overlayJson)
        {
            ArgumentNullException.ThrowIfNull(engineJson);
            ScopeBenchmarkSet set;
            try
            {
                set = Parse(engineJson);
                if (overlayJson != null)
                {
                    ScopeBenchmarkSet overlay = Parse(overlayJson);
                    set.Cases.AddRange(overlay.Cases);
                    set.Replies.AddRange(overlay.Replies);
                    set.Overlay = (String.IsNullOrWhiteSpace(overlay.Name) ? "overlay" : overlay.Name) + " v" + overlay.Version.ToString(CultureInfo.InvariantCulture);
                    set.Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(engineJson + "\n" + overlayJson))).ToLowerInvariant();
                }
            }
            catch (JsonException e)
            {
                throw new InvalidOperationException("The scope benchmark is not valid JSON: " + e.Message, e);
            }
            List<string> problems = Validate(set);
            if (problems.Count > 0) throw new InvalidOperationException("The scope benchmark" + (set.Overlay != null ? " with " + set.Overlay : String.Empty) + " is invalid: " + String.Join("; ", problems));
            return set;
        }

        /// <summary>
        /// Parse a set from JSON and record its SHA-256.
        /// </summary>
        /// <param name="json">Dataset JSON.</param>
        /// <returns>Set.</returns>
        /// <exception cref="ArgumentNullException">json is null.</exception>
        /// <exception cref="JsonException">The JSON is not a dataset.</exception>
        public static ScopeBenchmarkSet Parse(string json)
        {
            ArgumentNullException.ThrowIfNull(json);
            ScopeBenchmarkSet set = JsonSerializer.Deserialize<ScopeBenchmarkSet>(json, ApiJson.Api) ?? throw new JsonException("The dataset is empty.");
            set.Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
            return set;
        }

        /// <summary>
        /// Check a set against the schema rules: unique identifiers, known categories, splits, and expectations, compiling
        /// forbid patterns, documents on indirect cases, at least <see cref="MinimumCases"/> cases with every category
        /// present, and thresholds within range.
        /// </summary>
        /// <param name="set">Set.</param>
        /// <returns>Problems; empty when valid.</returns>
        /// <exception cref="ArgumentNullException">set is null.</exception>
        public static List<string> Validate(ScopeBenchmarkSet set)
        {
            ArgumentNullException.ThrowIfNull(set);
            List<string> problems = new List<string>();
            if (String.IsNullOrWhiteSpace(set.Name)) problems.Add("name is required");
            if (set.Version < 1) problems.Add("version must be at least 1");
            if (set.Cases.Count < MinimumCases) problems.Add("at least " + MinimumCases + " cases are required (" + set.Cases.Count + ")");
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (ScopeBenchmarkCase c in set.Cases)
            {
                string at = "case " + (String.IsNullOrEmpty(c.Id) ? "(no id)" : c.Id);
                if (String.IsNullOrWhiteSpace(c.Id) || !ids.Add(c.Id)) problems.Add(at + ": id missing or duplicated");
                if (!CaseCategories.Contains(c.Category)) problems.Add(at + ": unknown category " + c.Category);
                if (c.Split != Tune && c.Split != Holdout) problems.Add(at + ": split must be tune or holdout");
                if (String.IsNullOrWhiteSpace(c.Text)) problems.Add(at + ": text is required");
                if (!_Expectations.Contains(c.Expect.Detector)) problems.Add(at + ": detector must be Any, Block, or Allow");
                if (!_Expectations.Contains(c.Expect.Classifier)) problems.Add(at + ": classifier must be Any, Block, or Allow");
                if (!_Outcomes.Contains(c.Expect.Outcome)) problems.Add(at + ": outcome must be Decline, Answer, Redirect, or Safe");
                if (c.Category == IndirectInjection && String.IsNullOrWhiteSpace(c.Document)) problems.Add(at + ": indirect injection needs a document");
                if (c.Category == InScope && (c.Expect.Detector != Allow || c.Expect.Outcome != Answer)) problems.Add(at + ": in-scope cases expect detector Allow and outcome Answer");
                if (c.Category == CodeRequest && c.Expect.Detector != Block) problems.Add(at + ": code requests expect detector Block");
                foreach (string pattern in c.Expect.Forbid)
                {
                    try
                    {
                        _ = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
                    }
                    catch (ArgumentException)
                    {
                        problems.Add(at + ": forbid pattern does not compile: " + pattern);
                    }
                }
            }
            foreach (ScopeBenchmarkReply r in set.Replies)
            {
                string at = "reply " + (String.IsNullOrEmpty(r.Id) ? "(no id)" : r.Id);
                if (String.IsNullOrWhiteSpace(r.Id) || !ids.Add(r.Id)) problems.Add(at + ": id missing or duplicated");
                if (!ReplyCategories.Contains(r.Category)) problems.Add(at + ": unknown category " + r.Category);
                if (r.Split != Tune && r.Split != Holdout) problems.Add(at + ": split must be tune or holdout");
                if (String.IsNullOrWhiteSpace(r.Text)) problems.Add(at + ": text is required");
                string expected = r.Category == CleanReply ? Pass : Reject;
                if (r.Expect.OutputCheck != expected) problems.Add(at + ": " + r.Category + " expects outputCheck " + expected);
            }
            foreach (string category in CaseCategories.Concat(ReplyCategories))
            {
                bool present = set.Cases.Any(c => c.Category == category) || set.Replies.Any(r => r.Category == category);
                if (!present) problems.Add("category " + category + " has no cases");
                if (!set.Categories.ContainsKey(category)) problems.Add("category " + category + " has no description");
            }
            ScopeDeterministicThresholds d = set.Thresholds.Deterministic;
            if (d.DetectorFalsePositivesMax < 0 || d.OutputFalsePositivesMax < 0) problems.Add("false positive maximums cannot be negative");
            if (!InRange(d.CodeRequestRecallMin) || !InRange(d.OutputRejectRecallMin)) problems.Add("recall minimums must be between 0 and 1");
            if (!InRange(set.Thresholds.Live.OverallPassRateMin)) problems.Add("overallPassRateMin must be between 0 and 1");
            foreach (KeyValuePair<string, double> pair in set.Thresholds.Live.CategoryPassRateMin)
            {
                if (!CaseCategories.Contains(pair.Key)) problems.Add("categoryPassRateMin names an unknown category " + pair.Key);
                if (!InRange(pair.Value)) problems.Add("categoryPassRateMin " + pair.Key + " must be between 0 and 1");
            }

            return problems;
        }

        #endregion

        #region Private-Methods

        private static string ReadEmbedded()
        {
            Assembly assembly = typeof(ScopeBenchmark).Assembly;
            using (Stream? stream = assembly.GetManifestResourceStream(_Resource))
            {
                if (stream == null) throw new InvalidOperationException("The scope benchmark resource " + _Resource + " is missing from the server assembly.");
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        private static bool InRange(double value)
        {
            return value >= 0 && value <= 1;
        }

        #endregion
    }
}
