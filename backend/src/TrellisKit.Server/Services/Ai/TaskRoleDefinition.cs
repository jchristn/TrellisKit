namespace TrellisKit.Server.Services.Ai
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One task role of the catalog (docs/INTEGRATION.md).
    /// </summary>
    public class TaskRoleDefinition
    {
        #region Public-Members

        /// <summary>
        /// Role key (PascalCase, for example Extraction).
        /// </summary>
        public string Role { get; set; } = String.Empty;

        /// <summary>
        /// Title.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// What the role is used for.
        /// </summary>
        public string Description { get; set; } = String.Empty;

        /// <summary>
        /// Endpoint kind the role needs. Default Completion.
        /// </summary>
        public ModelEndpointKindEnum Kind { get; set; } = ModelEndpointKindEnum.Completion;

        /// <summary>
        /// Whether CLI agent harnesses may be executors (agentic background work only). Default false.
        /// </summary>
        public bool AllowsHarness { get; set; } = false;

        /// <summary>
        /// Whether members wait on the role's replies (harnesses never allowed). Default false.
        /// </summary>
        public bool Interactive { get; set; } = false;

        /// <summary>
        /// Suggested hierarchy from the plan, as guidance.
        /// </summary>
        public string Suggested { get; set; } = String.Empty;

        /// <summary>
        /// Reasoning effort used until an administrator sets one, or null for each endpoint's own setting.
        /// </summary>
        public ModelReasoningEffortEnum? DefaultEffort { get; set; } = null;

        #endregion
    }
}
