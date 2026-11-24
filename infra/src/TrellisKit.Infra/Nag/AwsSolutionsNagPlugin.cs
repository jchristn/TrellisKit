namespace TrellisKit.Infra.Nag
{
    using System;
    using System.Collections.Generic;
    using Amazon.CDK;
    using Amazon.JSII.Runtime.Deputy;
    using Cdklabs.CdkNag;
    using Constructs;

    /// <summary>
    /// Policy validation plugin that runs the cdk-nag AwsSolutions pack and removes the findings suppressed through
    /// <see cref="NagHelper"/>. cdk-nag 3 only honors acknowledgments of exact finding ids, so rule-wide suppressions
    /// of multi-finding rules and the regular expression patterns of <see cref="NagHelper.SuppressWildcards"/> are
    /// applied here. Register with Validations.Of(app).AddPlugins; any finding left fails synthesis.
    /// Thread safety: not thread safe.
    /// </summary>
    public class AwsSolutionsNagPlugin : DeputyBase, IPolicyValidationPlugin
    {
        private readonly AwsSolutionsChecks _Checks;

        /// <summary>
        /// Create the plugin.
        /// </summary>
        /// <param name="scope">Scope the pack is registered on, usually the app.</param>
        /// <exception cref="ArgumentNullException">The scope is null.</exception>
        public AwsSolutionsNagPlugin(IConstruct scope)
        {
            ArgumentNullException.ThrowIfNull(scope);
            _Checks = new AwsSolutionsChecks(scope, new NagPackProps { Verbose = true });
        }

        /// <summary>
        /// Plugin name shown in the validation report.
        /// </summary>
        public string Name
        {
            get { return _Checks.Name; }
        }

        /// <summary>
        /// Plugin version; none.
        /// </summary>
        public string? Version
        {
            get { return null; }
        }

        /// <summary>
        /// Rule ids evaluated; not listed.
        /// </summary>
        public string[]? RuleIds
        {
            get { return null; }
        }

        /// <summary>
        /// Called by the CDK validation framework after synthesis.
        /// </summary>
        /// <param name="context">Validation context; carries the construct tree.</param>
        /// <returns>Report of the findings left after suppressions.</returns>
        /// <exception cref="ArgumentNullException">The context is null.</exception>
        /// <exception cref="InvalidOperationException">The context carries no construct tree.</exception>
        public IPolicyValidationPluginReport Validate(IPolicyValidationContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            IConstruct root = context.AppConstruct ?? throw new InvalidOperationException("The validation context carries no construct tree.");
            return Filter(_Checks.Validate(context), new NagSuppressionIndex(root));
        }

        /// <summary>
        /// Run the pack over a construct tree directly and list the findings left after suppressions, as
        /// "RuleName at construct path" lines. Used by the tests.
        /// </summary>
        /// <param name="scope">Root of the tree, usually the app.</param>
        /// <returns>Unsuppressed findings; empty when clean.</returns>
        /// <exception cref="ArgumentNullException">The scope is null.</exception>
        public List<string> FindUnsuppressed(IConstruct scope)
        {
            ArgumentNullException.ThrowIfNull(scope);
            IPolicyValidationPluginReport report = Filter(_Checks.ValidateScope(scope), new NagSuppressionIndex(scope));
            List<string> findings = new List<string>();
            foreach (IPolicyViolation violation in report.Violations)
            {
                foreach (IPolicyViolatingResource resource in violation.ViolatingResources)
                {
                    findings.Add(violation.RuleName + " at " + (resource.ConstructPath ?? resource.ResourceLogicalId ?? "(unknown)"));
                }
            }

            return findings;
        }

        private static IPolicyValidationPluginReport Filter(IPolicyValidationPluginReport report, NagSuppressionIndex index)
        {
            List<IPolicyViolation> kept = new List<IPolicyViolation>();
            bool success = true;

            foreach (IPolicyViolation violation in report.Violations)
            {
                string ruleName = violation.RuleName;
                string ruleId = ruleName;
                string? qualifier = null;
                int open = ruleName.IndexOf('[');
                if (open > 0 && ruleName.EndsWith(']'))
                {
                    ruleId = ruleName.Substring(0, open);
                    qualifier = ruleName.Substring(open + 1, ruleName.Length - open - 2);
                }

                List<IPolicyViolatingResource> resources = new List<IPolicyViolatingResource>();
                foreach (IPolicyViolatingResource resource in violation.ViolatingResources)
                {
                    if (resource.ConstructPath != null && index.Covers(resource.ConstructPath, ruleId, qualifier)) continue;
                    resources.Add(new PolicyViolatingResource
                    {
                        ConstructPath = resource.ConstructPath,
                        Locations = resource.Locations,
                        ResourceLogicalId = resource.ResourceLogicalId,
                        TemplatePath = resource.TemplatePath
                    });
                }

                if (resources.Count == 0) continue;

                kept.Add(new PolicyViolation
                {
                    RuleName = ruleName,
                    Description = violation.Description,
                    Fix = violation.Fix,
                    RuleMetadata = violation.RuleMetadata,
                    Severity = violation.Severity,
                    ViolatingResources = resources.ToArray()
                });

                if (!String.Equals(violation.Severity, "warning", StringComparison.OrdinalIgnoreCase)
                    && !String.Equals(violation.Severity, "info", StringComparison.OrdinalIgnoreCase))
                {
                    success = false;
                }
            }

            return new PolicyValidationPluginReport
            {
                Success = success,
                Violations = kept.ToArray(),
                Metadata = report.Metadata,
                PluginVersion = report.PluginVersion
            };
        }
    }
}
