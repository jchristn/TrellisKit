namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A task role with its executor hierarchy and policies.
    /// </summary>
    public class AiTaskRoleView
    {
        #region Public-Members

        /// <summary>Immutable catalog creation anchor used by enumeration date filters.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UnixEpoch;


        /// <summary>
        /// Role key.
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
        /// Endpoint kind the role needs (Completion or Embedding).
        /// </summary>
        public string Kind { get; set; } = "Completion";

        /// <summary>
        /// Whether CLI agent harnesses may be executors (agentic background roles only).
        /// </summary>
        public bool AllowsHarness { get; set; } = false;

        /// <summary>
        /// Whether the role serves members interactively (harnesses are never allowed).
        /// </summary>
        public bool Interactive { get; set; } = false;

        /// <summary>
        /// The plan's suggested hierarchy, as guidance for administrators.
        /// </summary>
        public string Suggested { get; set; } = String.Empty;

        /// <summary>
        /// Reasoning effort, or null.
        /// </summary>
        public string? ReasoningEffort { get; set; } = null;

        /// <summary>
        /// FallThrough or Wait.
        /// </summary>
        public string AvailabilityPolicy { get; set; } = "FallThrough";

        /// <summary>
        /// Fail, Defer, or Wait.
        /// </summary>
        public string ExhaustionPolicy { get; set; } = "Fail";

        /// <summary>
        /// Executors in order.
        /// </summary>
        public List<AiExecutorView> Executors { get; set; } = new List<AiExecutorView>();

        /// <summary>
        /// True when at least one executor can run on this instance.
        /// </summary>
        public bool Ready { get; set; } = false;

        /// <summary>
        /// Administrator who last changed the role, or null.
        /// </summary>
        public string? UpdatedByUserId { get; set; } = null;

        /// <summary>
        /// UTC time of the last change, or null.
        /// </summary>
        public DateTime? UpdatedUtc { get; set; } = null;

        /// <summary>Catalog labels. Empty when none are declared.</summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>Catalog tags. Empty when none are declared.</summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        #endregion
    }
}
