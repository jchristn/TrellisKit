namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Deterministic, side-effect free feature flag evaluation shared by the app manifest, server-side kill switch
    /// enforcement, and the admin Test as preview (docs/LIVE_UPDATES_CONTRACT.md section 12.3), and the targeting rule
    /// validation (section 12.2).
    /// Thread safety: stateless.
    /// </summary>
    public static class FeatureFlagEvaluator
    {
        #region Public-Members

        /// <summary>
        /// Operators allowed per attribute.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string[]> Operators = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            { "platform", new[] { "In", "NotIn" } },
            { "client", new[] { "In", "NotIn" } },
            { "appVersion", new[] { "SemverRange", "In", "NotIn" } },
            { "updateId", new[] { "In", "NotIn" } },
            { "runtimeVersion", new[] { "In", "NotIn" } },
            { "channel", new[] { "In", "NotIn" } },
            { "signedIn", new[] { "Is" } },
            { "membership", new[] { "In" } },
            { "userRole", new[] { "In", "NotIn" } },
            { "userLabels", new[] { "HasAny", "HasAll", "HasNone" } },
            { "userTag", new[] { "Equals", "HasKey" } },
            { "userId", new[] { "In" } },
            { "profileField", new[] { "Equals", "In" } },
            { "country", new[] { "In", "NotIn" } },
            { "date", new[] { "Before", "After" } }
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Evaluate a flag for a caller.
        /// </summary>
        /// <param name="flag">Flag definition.</param>
        /// <param name="context">Caller.</param>
        /// <returns>Result.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static FlagEvaluationResult Evaluate(FeatureFlag flag, FlagEvaluationContext context)
        {
            ArgumentNullException.ThrowIfNull(flag);
            ArgumentNullException.ThrowIfNull(context);
            if (!flag.Enabled) return new FlagEvaluationResult { Value = flag.OffValue, Reason = "Disabled" };
            int? lastBucket = null;
            bool outside = false;
            foreach (FeatureFlagRule rule in flag.Rules.OrderBy(r => r.Position))
            {
                if (!rule.Conditions.All(c => Matches(c, context))) continue;
                bool targetsAdmins = rule.Conditions.Any(c => c.Attribute == "userId" || (c.Attribute == "userRole" && c.Operator == "In" && c.Values.Contains("SystemAdmin")));
                if (rule.RolloutBasisPoints >= 10000) return new FlagEvaluationResult { Value = rule.Value, MatchedRuleId = rule.Id, Reason = "Rule", RuleTargetsAdmins = targetsAdmins };
                int bucket = LiveUpdateRules.Bucket(flag.Key + ":" + rule.Id, context.SubjectId ?? String.Empty);
                lastBucket = bucket;
                if (!String.IsNullOrEmpty(context.SubjectId) && bucket < rule.RolloutBasisPoints)
                    return new FlagEvaluationResult { Value = rule.Value, MatchedRuleId = rule.Id, Reason = "Rule", Bucket = bucket, RuleTargetsAdmins = targetsAdmins };
                outside = true;
            }
            return new FlagEvaluationResult { Value = flag.DefaultValue, Reason = outside ? "RuleOutsideRollout" : "Default", Bucket = lastBucket };
        }

        /// <summary>
        /// Validate the rules of a flag.
        /// </summary>
        /// <param name="rules">Rules.</param>
        /// <param name="maxRules">appConfig.maxRulesPerFlag.</param>
        /// <param name="maxConditions">appConfig.maxConditionsPerRule.</param>
        /// <param name="maxValues">appConfig.maxValuesPerCondition.</param>
        /// <returns>Field errors (empty when valid).</returns>
        public static Dictionary<string, string> Validate(List<FeatureFlagRule> rules, int maxRules, int maxConditions, int maxValues)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>();
            if (rules == null) return errors;
            if (rules.Count > maxRules) errors["rules"] = "At most " + maxRules + " rules (appConfig.maxRulesPerFlag).";
            for (int r = 0; r < rules.Count; r++)
            {
                FeatureFlagRule rule = rules[r];
                string prefix = "rules[" + r + "]";
                if (rule.Conditions.Count > maxConditions) errors[prefix + ".conditions"] = "At most " + maxConditions + " conditions (appConfig.maxConditionsPerRule).";
                if (rule.RolloutBasisPoints < 1 || rule.RolloutBasisPoints > 10000) errors[prefix + ".rolloutPercentage"] = "Must be 0.01 to 100.";
                for (int c = 0; c < rule.Conditions.Count; c++)
                {
                    FeatureFlagCondition condition = rule.Conditions[c];
                    string field = prefix + ".conditions[" + c + "]";
                    if (!Operators.TryGetValue(condition.Attribute ?? String.Empty, out string[]? allowed))
                    {
                        errors[field + ".attribute"] = "Unknown attribute.";
                        continue;
                    }
                    if (!allowed.Contains(condition.Operator)) errors[field + ".operator"] = "Must be one of " + String.Join(", ", allowed) + ".";
                    if (condition.Values.Count == 0 || condition.Values.Count > maxValues) errors[field + ".values"] = "Send 1 to " + maxValues + " values.";
                    else if (condition.Values.Any(v => v == null || v.Length > 500)) errors[field + ".values"] = "Values are 0 to 500 characters.";
                    if (condition.Attribute == "userId" && condition.Values.Count > 100) errors[field + ".values"] = "At most 100 user ids.";
                    if (condition.Operator == "SemverRange" && (condition.Values.Count != 1 || !SemverRange.TryParse(condition.Values[0], out SemverRange? _))) errors[field + ".values"] = "One valid semver range, for example >=1.2.0 <2.0.0.";
                    if (condition.Attribute == "signedIn" && condition.Values.Any(v => v != "true" && v != "false")) errors[field + ".values"] = "true or false.";
                    if (condition.Attribute == "date" && condition.Values.Any(v => !DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime _))) errors[field + ".values"] = "An ISO 8601 UTC time.";
                    if (condition.Attribute == "userTag" && condition.Operator == "Equals" && condition.Values.Any(v => !v.Contains('='))) errors[field + ".values"] = "key=value.";
                    if (condition.Attribute == "profileField" && (ProfileFieldKey(condition) == null || ProfileFieldCandidates(condition).Count == 0))
                        errors[field + ".values"] = "Name the profile field (argument, or the first value) and at least one value to compare.";
                }
            }
            return errors;
        }

        #endregion

        #region Private-Methods

        private static bool Matches(FeatureFlagCondition c, FlagEvaluationContext x)
        {
            switch (c.Attribute)
            {
                case "platform": return InOrNot(c, x.Platform);
                case "client": return InOrNot(c, x.Client);
                case "updateId": return InOrNot(c, x.UpdateId);
                case "runtimeVersion": return InOrNot(c, x.RuntimeVersion);
                case "channel": return InOrNot(c, x.Channel);
                case "country": return InOrNot(c, x.Country);
                case "appVersion":
                    if (String.IsNullOrEmpty(x.AppVersion)) return false;
                    if (c.Operator == "SemverRange") return SemverRange.TryParse(c.Values.FirstOrDefault(), out SemverRange? range) && range!.Satisfies(x.AppVersion);
                    return InOrNot(c, x.AppVersion);
                case "signedIn": return c.Values.FirstOrDefault() == (x.SignedIn ? "true" : "false");
                case "membership": return x.SignedIn && x.Membership != null && c.Values.Contains(x.Membership, StringComparer.OrdinalIgnoreCase);
                case "userRole":
                    if (!x.SignedIn && x.Roles.Count == 0) return false;
                    bool any = x.Roles.Any(r => c.Values.Contains(r, StringComparer.OrdinalIgnoreCase));
                    return c.Operator == "NotIn" ? !any : any;
                case "userLabels":
                    if (!x.SignedIn) return false;
                    HashSet<string> labels = new HashSet<string>(x.UserLabels, StringComparer.OrdinalIgnoreCase);
                    if (c.Operator == "HasAll") return c.Values.All(labels.Contains);
                    if (c.Operator == "HasNone") return !c.Values.Any(labels.Contains);
                    return c.Values.Any(labels.Contains);
                case "userTag":
                    if (!x.SignedIn) return false;
                    Dictionary<string, string> tags = new Dictionary<string, string>(x.UserTags, StringComparer.OrdinalIgnoreCase);
                    if (c.Operator == "HasKey") return c.Values.Any(tags.ContainsKey) || (c.Argument != null && tags.ContainsKey(c.Argument));
                    foreach (string pair in c.Values)
                    {
                        int eq = pair.IndexOf('=');
                        if (eq > 0 && tags.TryGetValue(pair.Substring(0, eq), out string? value) && value == pair.Substring(eq + 1)) return true;
                    }
                    return false;
                case "userId": return x.UserId != null && c.Values.Contains(x.UserId, StringComparer.Ordinal);
                case "profileField":
                    if (!x.SignedIn) return false;
                    string? field = ProfileFieldKey(c);
                    if (field == null || !x.ProfileFields.TryGetValue(field, out List<string>? held) || held.Count == 0) return false;
                    List<string> candidates = ProfileFieldCandidates(c);
                    if (candidates.Count == 0) return false;
                    if (c.Operator == "Equals") return held.Any(v => String.Equals(v, candidates[0], StringComparison.OrdinalIgnoreCase));
                    return held.Any(v => candidates.Contains(v, StringComparer.OrdinalIgnoreCase));
                case "date":
                    if (!DateTime.TryParse(c.Values.FirstOrDefault(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime when)) return false;
                    return c.Operator == "Before" ? x.NowUtc < when : x.NowUtc > when;
                default:
                    return false;
            }
        }

        /// <summary>
        /// The profile field key of a profileField condition: the argument when set, else the first value.
        /// </summary>
        /// <param name="c">Condition.</param>
        /// <returns>Field key, or null.</returns>
        public static string? ProfileFieldKey(FeatureFlagCondition c)
        {
            ArgumentNullException.ThrowIfNull(c);
            if (!String.IsNullOrWhiteSpace(c.Argument)) return c.Argument.Trim();
            return c.Values.Count > 0 && !String.IsNullOrWhiteSpace(c.Values[0]) ? c.Values[0].Trim() : null;
        }

        /// <summary>
        /// The candidate values of a profileField condition: every value when the argument names the field, else the values
        /// after the first.
        /// </summary>
        /// <param name="c">Condition.</param>
        /// <returns>Candidates.</returns>
        public static List<string> ProfileFieldCandidates(FeatureFlagCondition c)
        {
            ArgumentNullException.ThrowIfNull(c);
            return (!String.IsNullOrWhiteSpace(c.Argument) ? c.Values : c.Values.Skip(1)).Select(v => (v ?? String.Empty).Trim()).ToList();
        }

        private static bool InOrNot(FeatureFlagCondition c, string? value)
        {
            if (String.IsNullOrEmpty(value)) return false;
            bool contained = c.Values.Contains(value, StringComparer.OrdinalIgnoreCase);
            return c.Operator == "NotIn" ? !contained : contained;
        }

        #endregion
    }
}
