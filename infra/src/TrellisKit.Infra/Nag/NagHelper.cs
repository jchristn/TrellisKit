namespace TrellisKit.Infra.Nag
{
    using System;
    using System.Collections.Generic;
    using Amazon.CDK;
    using Constructs;

    /// <summary>
    /// Records cdk-nag suppressions. Every suppression carries a reason so the audit trail lives in the template.
    /// cdk-nag 3 acknowledges findings through CDK's Validations API by exact finding id only, so each suppression is
    /// also recorded as construct metadata (<see cref="SuppressionMetadataType"/>). <see cref="AwsSolutionsNagPlugin"/>
    /// reads that metadata to apply rule-wide and regular expression suppressions, and
    /// <see cref="NagSuppressionMetadataAspect"/> writes it into the template in the cdk-nag 2 format.
    /// Thread safety: stateless.
    /// </summary>
    public static class NagHelper
    {
        /// <summary>
        /// Construct metadata type of a recorded suppression. The data is the JSON form of a <see cref="NagSuppression"/>.
        /// </summary>
        public static readonly string SuppressionMetadataType = "trelliskit:nag-suppression";

        /// <summary>
        /// Suppress a rule on a construct and its children.
        /// </summary>
        /// <param name="construct">Construct to annotate.</param>
        /// <param name="ruleId">Rule id, for example AwsSolutions-IAM5.</param>
        /// <param name="reason">Why the finding is acceptable. At least ten characters.</param>
        /// <param name="appliesTo">Optional finding qualifiers, for example Resource::*. Empty suppresses every finding of the rule.</param>
        /// <exception cref="ArgumentNullException">A required argument is null or empty.</exception>
        public static void Suppress(IConstruct construct, string ruleId, string reason, params string[] appliesTo)
        {
            ArgumentNullException.ThrowIfNull(construct);
            if (String.IsNullOrWhiteSpace(ruleId)) throw new ArgumentNullException(nameof(ruleId));
            if (String.IsNullOrWhiteSpace(reason)) throw new ArgumentNullException(nameof(reason));

            NagSuppression suppression = new NagSuppression();
            suppression.RuleId = ruleId;
            suppression.Reason = reason;

            List<string> ids = new List<string>();
            if (appliesTo != null && appliesTo.Length > 0)
            {
                foreach (string item in appliesTo)
                {
                    if (String.IsNullOrWhiteSpace(item)) throw new ArgumentNullException(nameof(appliesTo));
                    suppression.AppliesTo.Add(item);
                    ids.Add(ruleId + "[" + item + "]");
                }
            }
            else
            {
                ids.Add(ruleId);
            }

            foreach (string id in ids)
            {
                Validations.Of(construct).Acknowledge(new Acknowledgment { Id = id, Reason = reason });
            }

            Record(construct, suppression);
        }

        /// <summary>
        /// Build an IAM5 finding pattern for a resource attribute (usually an ARN) followed by a wildcard suffix.
        /// The attribute may appear as a same-stack reference (&lt;LogicalId.Arn&gt;) or, once cross-stack
        /// references are resolved, as an export name (Stack:ExportsOutputFnGetAttLogicalIdArn...), so both forms match.
        /// </summary>
        /// <param name="logicalIdPrefix">Construct id at the start of the logical id, for example MediaIngestBucket.</param>
        /// <param name="attribute">Attribute name, for example Arn.</param>
        /// <param name="suffix">Regular expression for the text after the attribute, for example \/\*.</param>
        /// <returns>Regular expression literal.</returns>
        /// <exception cref="ArgumentNullException">A required argument is empty.</exception>
        public static string ResourcePattern(string logicalIdPrefix, string attribute, string suffix)
        {
            if (String.IsNullOrWhiteSpace(logicalIdPrefix)) throw new ArgumentNullException(nameof(logicalIdPrefix));
            if (String.IsNullOrWhiteSpace(attribute)) throw new ArgumentNullException(nameof(attribute));
            if (String.IsNullOrWhiteSpace(suffix)) throw new ArgumentNullException(nameof(suffix));

            return "/^Resource::(<" + logicalIdPrefix + "[A-Za-z0-9]+\\." + attribute + ">|[A-Za-z0-9-]+:ExportsOutputFnGetAtt"
                + logicalIdPrefix + "[A-Za-z0-9]+" + attribute + "[A-Za-z0-9]+)" + suffix + "$/g";
        }

        /// <summary>
        /// Pattern of the Resource::* finding.
        /// </summary>
        public static readonly string AnyResourcePattern = "/^Resource::\\*$/g";

        /// <summary>
        /// Suppress specific AwsSolutions-IAM5 wildcard findings on a construct and its children. Each pattern is a
        /// JavaScript regular expression literal matched against the finding, for example
        /// /^Resource::&lt;MediaIngestBucket[A-Za-z0-9]+\.Arn&gt;\/\*$/g. Findings that match none of the patterns still fail.
        /// </summary>
        /// <param name="construct">Construct to annotate.</param>
        /// <param name="reason">Why the wildcards are acceptable. At least ten characters.</param>
        /// <param name="patterns">One or more regular expression literals.</param>
        /// <exception cref="ArgumentNullException">A required argument is null or empty.</exception>
        /// <exception cref="ArgumentException">A pattern is not a regular expression literal.</exception>
        public static void SuppressWildcards(IConstruct construct, string reason, params string[] patterns)
        {
            ArgumentNullException.ThrowIfNull(construct);
            if (String.IsNullOrWhiteSpace(reason)) throw new ArgumentNullException(nameof(reason));
            if (patterns == null || patterns.Length == 0) throw new ArgumentNullException(nameof(patterns));

            NagSuppression suppression = new NagSuppression();
            suppression.RuleId = "AwsSolutions-IAM5";
            suppression.Reason = reason;
            foreach (string pattern in patterns)
            {
                if (String.IsNullOrWhiteSpace(pattern)) throw new ArgumentNullException(nameof(patterns));
                NagSuppression.ToRegex(pattern);
                suppression.Patterns.Add(pattern);
            }

            Record(construct, suppression);
        }

        private static void Record(IConstruct construct, NagSuppression suppression)
        {
            construct.Node.AddMetadata(SuppressionMetadataType, suppression.ToJson());
        }
    }
}
