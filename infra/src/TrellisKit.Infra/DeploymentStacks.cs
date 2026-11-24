namespace TrellisKit.Infra
{
    using System.Collections.Generic;
    using Amazon.CDK;
    using TrellisKit.Infra.Config;
    using TrellisKit.Infra.Stacks;

    /// <summary>
    /// Every stack of one synthesis. Per-property topology: one full <see cref="AppStacks"/> set per property.
    /// Shared topology: one <see cref="SharedStacks"/> set plus one <see cref="PropertyStacks"/> set per property.
    /// Returned by <see cref="InfraAppBuilder.BuildDeployment"/>.
    /// </summary>
    public class DeploymentStacks
    {
        /// <summary>
        /// Topology.
        /// </summary>
        public DeploymentTopologyEnum Topology { get; set; } = DeploymentTopologyEnum.PerProperty;

        /// <summary>
        /// Full stack sets, one per property (per-property topology only).
        /// </summary>
        public List<AppStacks> PerProperty { get; } = new List<AppStacks>();

        /// <summary>
        /// Shared stacks (shared topology only), or null.
        /// </summary>
        public SharedStacks? Shared { get; set; } = null;

        /// <summary>
        /// Property-owned stacks, one set per property (shared topology only).
        /// </summary>
        public List<PropertyStacks> Properties { get; } = new List<PropertyStacks>();

        /// <summary>
        /// Account-level GitHub OIDC provider stacks, one per account the synthesis deploys into.
        /// </summary>
        public List<GitHubOidcStack> GitHubOidc { get; } = new List<GitHubOidcStack>();

        /// <summary>
        /// Every stack of the synthesis, each once (property sets of one account share the GitHub OIDC stack).
        /// </summary>
        public List<Stack> All
        {
            get
            {
                List<Stack> stacks = new List<Stack>();
                foreach (AppStacks set in PerProperty) AddDistinct(stacks, set.All);
                if (Shared != null) AddDistinct(stacks, Shared.All);
                foreach (PropertyStacks set in Properties) AddDistinct(stacks, set.All);
                return stacks;
            }
        }

        private static void AddDistinct(List<Stack> stacks, List<Stack> more)
        {
            foreach (Stack stack in more)
            {
                if (!stacks.Contains(stack)) stacks.Add(stack);
            }
        }
    }
}
