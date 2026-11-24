namespace TrellisKit.Infra.Nag
{
    using System;
    using System.Collections.Generic;
    using Amazon.CDK;
    using Amazon.JSII.Runtime.Deputy;
    using Constructs;

    /// <summary>
    /// Writes the suppressions recorded by <see cref="NagHelper"/> into the CloudFormation Metadata of every resource
    /// they apply to, in the cdk-nag 2 format (cdk_nag.rules_to_suppress with id, reason, and applies_to), so the
    /// audit trail stays in the template. Add one instance to the app; it reads the tree once, on its first visit.
    /// Thread safety: not thread safe.
    /// </summary>
    public class NagSuppressionMetadataAspect : DeputyBase, IAspect
    {
        private NagSuppressionIndex? _Index = null;

        /// <summary>
        /// Visit a construct.
        /// </summary>
        /// <param name="node">Construct.</param>
        /// <exception cref="ArgumentNullException">The node is null.</exception>
        public void Visit(IConstruct node)
        {
            ArgumentNullException.ThrowIfNull(node);
            if (!CfnResource.IsCfnResource(node) || node is not CfnResource resource) return;

            if (_Index == null) _Index = new NagSuppressionIndex(node.Node.Root);
            if (_Index.Count == 0) return;

            List<NagSuppression> suppressions = _Index.For(node.Node.Path);
            if (suppressions.Count == 0) return;

            List<object> rules = new List<object>();
            foreach (NagSuppression suppression in suppressions)
            {
                Dictionary<string, object> rule = new Dictionary<string, object>();
                rule["reason"] = suppression.Reason;
                rule["id"] = suppression.RuleId;

                List<object> appliesTo = new List<object>();
                foreach (string item in suppression.AppliesTo) appliesTo.Add(item);
                foreach (string pattern in suppression.Patterns)
                {
                    Dictionary<string, object> regex = new Dictionary<string, object>();
                    regex["regex"] = pattern;
                    appliesTo.Add(regex);
                }

                if (appliesTo.Count > 0) rule["applies_to"] = appliesTo.ToArray();
                rules.Add(rule);
            }

            Dictionary<string, object> metadata = new Dictionary<string, object>();
            metadata["rules_to_suppress"] = rules.ToArray();
            resource.AddMetadata("cdk_nag", metadata);
        }
    }
}
