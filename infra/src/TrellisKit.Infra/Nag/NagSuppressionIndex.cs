namespace TrellisKit.Infra.Nag
{
    using System;
    using System.Collections.Generic;
    using Constructs;

    /// <summary>
    /// The suppressions recorded by <see cref="NagHelper"/> in a construct tree, by construct path.
    /// A suppression applies to its construct and every construct below it.
    /// Thread safety: not thread safe.
    /// </summary>
    public class NagSuppressionIndex
    {
        private readonly Dictionary<string, List<NagSuppression>> _ByPath = new Dictionary<string, List<NagSuppression>>(StringComparer.Ordinal);

        /// <summary>
        /// Read every recorded suppression under a root construct.
        /// </summary>
        /// <param name="root">Root of the tree, usually the app.</param>
        /// <exception cref="ArgumentNullException">The root is null.</exception>
        public NagSuppressionIndex(IConstruct root)
        {
            ArgumentNullException.ThrowIfNull(root);

            foreach (IConstruct construct in root.Node.FindAll())
            {
                foreach (IMetadataEntry entry in construct.Node.Metadata)
                {
                    if (!String.Equals(entry.Type, NagHelper.SuppressionMetadataType, StringComparison.Ordinal)) continue;
                    if (entry.Data is not string json) continue;

                    string path = construct.Node.Path;
                    if (!_ByPath.TryGetValue(path, out List<NagSuppression>? list))
                    {
                        list = new List<NagSuppression>();
                        _ByPath[path] = list;
                    }

                    list.Add(NagSuppression.FromJson(json));
                }
            }
        }

        /// <summary>
        /// Number of constructs that carry at least one suppression.
        /// </summary>
        public int Count
        {
            get { return _ByPath.Count; }
        }

        /// <summary>
        /// Suppressions that apply to a construct path, outermost construct first.
        /// </summary>
        /// <param name="path">Construct path, for example Stack/Role/DefaultPolicy/Resource.</param>
        /// <returns>Suppressions; empty when none apply.</returns>
        /// <exception cref="ArgumentNullException">The path is null.</exception>
        public List<NagSuppression> For(string path)
        {
            ArgumentNullException.ThrowIfNull(path);

            List<List<NagSuppression>> levels = new List<List<NagSuppression>>();
            string current = path;
            while (true)
            {
                if (_ByPath.TryGetValue(current, out List<NagSuppression>? found)) levels.Add(found);
                if (current.Length == 0) break;
                int slash = current.LastIndexOf('/');
                current = slash < 0 ? String.Empty : current.Substring(0, slash);
            }

            List<NagSuppression> result = new List<NagSuppression>();
            for (int i = levels.Count - 1; i >= 0; i--) result.AddRange(levels[i]);
            return result;
        }

        /// <summary>
        /// Whether a finding on a construct is suppressed.
        /// </summary>
        /// <param name="path">Construct path of the violating resource.</param>
        /// <param name="ruleId">Rule id, for example AwsSolutions-IAM5.</param>
        /// <param name="qualifier">Finding qualifier, for example Resource::*, or null.</param>
        /// <returns>True when a suppression covers it.</returns>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public bool Covers(string path, string ruleId, string? qualifier)
        {
            ArgumentNullException.ThrowIfNull(path);
            ArgumentNullException.ThrowIfNull(ruleId);

            foreach (NagSuppression suppression in For(path))
            {
                if (suppression.Covers(ruleId, qualifier)) return true;
            }

            return false;
        }
    }
}
