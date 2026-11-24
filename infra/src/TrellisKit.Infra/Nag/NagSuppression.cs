namespace TrellisKit.Infra.Nag
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.RegularExpressions;

    /// <summary>
    /// One recorded cdk-nag suppression: a rule, the reason, and optionally the findings it covers.
    /// With neither <see cref="AppliesTo"/> nor <see cref="Patterns"/> it covers every finding of the rule.
    /// Thread safety: not thread safe.
    /// </summary>
    public class NagSuppression
    {
        /// <summary>
        /// Rule id, for example AwsSolutions-IAM5.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null or empty.</exception>
        public string RuleId
        {
            get { return _RuleId; }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(RuleId));
                _RuleId = value;
            }
        }

        /// <summary>
        /// Why the findings are acceptable.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null or empty.</exception>
        public string Reason
        {
            get { return _Reason; }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Reason));
                _Reason = value;
            }
        }

        /// <summary>
        /// Exact finding qualifiers, for example Resource::*. Default empty.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public List<string> AppliesTo
        {
            get { return _AppliesTo; }
            set { _AppliesTo = value ?? throw new ArgumentNullException(nameof(AppliesTo)); }
        }

        /// <summary>
        /// JavaScript regular expression literals matched against the finding qualifier, for example /^Resource::\*$/g.
        /// Default empty.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public List<string> Patterns
        {
            get { return _Patterns; }
            set { _Patterns = value ?? throw new ArgumentNullException(nameof(Patterns)); }
        }

        private string _RuleId = "AwsSolutions-IAM5";
        private string _Reason = "Not specified.";
        private List<string> _AppliesTo = new List<string>();
        private List<string> _Patterns = new List<string>();

        /// <summary>
        /// Whether this suppression covers a finding.
        /// </summary>
        /// <param name="ruleId">Rule id of the finding, for example AwsSolutions-IAM5.</param>
        /// <param name="qualifier">Finding qualifier inside the brackets, for example Resource::*, or null for a rule without findings.</param>
        /// <returns>True when suppressed.</returns>
        /// <exception cref="ArgumentNullException">The rule id is null or empty.</exception>
        public bool Covers(string ruleId, string? qualifier)
        {
            if (String.IsNullOrWhiteSpace(ruleId)) throw new ArgumentNullException(nameof(ruleId));
            if (!String.Equals(ruleId, RuleId, StringComparison.Ordinal)) return false;
            if (AppliesTo.Count == 0 && Patterns.Count == 0) return true;
            if (qualifier == null) return false;

            foreach (string item in AppliesTo)
            {
                if (String.Equals(item, qualifier, StringComparison.Ordinal)) return true;
            }

            foreach (string pattern in Patterns)
            {
                if (ToRegex(pattern).IsMatch(qualifier)) return true;
            }

            return false;
        }

        /// <summary>
        /// Serialize to JSON (the construct metadata form).
        /// </summary>
        /// <returns>JSON text.</returns>
        public string ToJson()
        {
            return JsonSerializer.Serialize(this);
        }

        /// <summary>
        /// Parse the JSON form.
        /// </summary>
        /// <param name="json">JSON text.</param>
        /// <returns>The suppression.</returns>
        /// <exception cref="ArgumentNullException">The text is null or empty.</exception>
        /// <exception cref="ArgumentException">The text is not a suppression.</exception>
        public static NagSuppression FromJson(string json)
        {
            if (String.IsNullOrWhiteSpace(json)) throw new ArgumentNullException(nameof(json));
            NagSuppression? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<NagSuppression>(json);
            }
            catch (JsonException e)
            {
                throw new ArgumentException("Not a cdk-nag suppression: " + e.Message, nameof(json), e);
            }

            if (parsed == null) throw new ArgumentException("Not a cdk-nag suppression.", nameof(json));
            return parsed;
        }

        /// <summary>
        /// Convert a JavaScript regular expression literal (/body/flags) to a .NET regular expression.
        /// The g flag is ignored and i maps to IgnoreCase.
        /// </summary>
        /// <param name="literal">Literal, for example /^Resource::\*$/g.</param>
        /// <returns>Regular expression.</returns>
        /// <exception cref="ArgumentNullException">The literal is null or empty.</exception>
        /// <exception cref="ArgumentException">The literal is malformed or uses an unsupported flag.</exception>
        public static Regex ToRegex(string literal)
        {
            if (String.IsNullOrWhiteSpace(literal)) throw new ArgumentNullException(nameof(literal));
            int end = literal.LastIndexOf('/');
            if (!literal.StartsWith('/') || end <= 0) throw new ArgumentException("Not a regular expression literal: " + literal, nameof(literal));

            string body = literal.Substring(1, end - 1);
            string flags = literal.Substring(end + 1);
            RegexOptions options = RegexOptions.CultureInvariant;
            foreach (char flag in flags)
            {
                if (flag == 'g') continue;
                if (flag == 'i')
                {
                    options |= RegexOptions.IgnoreCase;
                    continue;
                }

                throw new ArgumentException("Unsupported regular expression flag '" + flag + "' in " + literal, nameof(literal));
            }

            try
            {
                return new Regex(body, options, TimeSpan.FromSeconds(1));
            }
            catch (ArgumentException e)
            {
                throw new ArgumentException("Invalid regular expression " + literal + ": " + e.Message, nameof(literal), e);
            }
        }
    }
}
