namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A live update group: the iOS and Android updates of one publish, sharing rollout, critical flag, and lifecycle (table updategroups).
    /// </summary>
    public class UpdateGroup
    {
        /// <summary>
        /// Owning property.
        /// </summary>
        public string PropertyId { get; set; } = String.Empty;

        /// <summary>
        /// Identifier (upg_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// How the group was created.
        /// </summary>
        public UpdateGroupKindEnum Kind { get; set; } = UpdateGroupKindEnum.Publish;

        /// <summary>
        /// Channel the group's updates are served on.
        /// </summary>
        public string Channel { get; set; } = String.Empty;

        /// <summary>
        /// Release message.
        /// </summary>
        public string Message { get; set; } = String.Empty;

        /// <summary>
        /// Lifecycle status.
        /// </summary>
        public UpdateGroupStatusEnum Status { get; set; } = UpdateGroupStatusEnum.Draft;

        /// <summary>
        /// Rollout in basis points (1 to 10000; 10000 is 100 percent).
        /// </summary>
        public int RolloutBasisPoints { get; set; } = 0;

        /// <summary>
        /// Whether apps prompt to restart at once.
        /// </summary>
        public bool Critical { get; set; } = false;

        /// <summary>
        /// Critical prompt text (up to 200 characters), or null.
        /// </summary>
        public string? CriticalMessage { get; set; } = null;

        /// <summary>
        /// Git commit of the export, or null.
        /// </summary>
        public string? GitCommit { get; set; } = null;

        /// <summary>
        /// Whether the working tree had uncommitted changes.
        /// </summary>
        public bool GitDirty { get; set; } = false;

        /// <summary>
        /// Group whose content a rollback republished, or null.
        /// </summary>
        public string? RepublishOf { get; set; } = null;

        /// <summary>
        /// Group promoted from another channel, or null.
        /// </summary>
        public string? PromotedFrom { get; set; } = null;

        /// <summary>
        /// The publish tool's updateGroupId (a UUID shared by the platforms of one run), or null.
        /// </summary>
        public string? ClientGroupId { get; set; } = null;

        /// <summary>
        /// Credential type that created the group.
        /// </summary>
        public UpdateCreatorTypeEnum CreatedByType { get; set; } = UpdateCreatorTypeEnum.Member;

        /// <summary>
        /// Member, deploy key, or null for the admin token.
        /// </summary>
        public string? CreatedById { get; set; } = null;

        /// <summary>
        /// Display name of the creator.
        /// </summary>
        public string CreatedByName { get; set; } = String.Empty;

        /// <summary>
        /// Whether the automatic halt watches this group's failure rate.
        /// </summary>
        public bool AutoHaltEnabled { get; set; } = true;

        /// <summary>
        /// Failure percentage that halts the rollout (1 to 100).
        /// </summary>
        public int AutoHaltFailurePercent { get; set; } = 5;

        /// <summary>
        /// Installs needed before the failure rate is judged.
        /// </summary>
        public int AutoHaltMinInstalls { get; set; } = 20;

        /// <summary>
        /// When the automatic halt paused the group, or null.
        /// </summary>
        public DateTime? HaltedUtc { get; set; } = null;

        /// <summary>
        /// Why the group was halted, or null.
        /// </summary>
        public string? HaltReason { get; set; } = null;

        /// <summary>
        /// First publish time, or null for drafts.
        /// </summary>
        public DateTime? PublishedUtc { get; set; } = null;

        /// <summary>
        /// Last pause time, or null.
        /// </summary>
        public DateTime? PausedUtc { get; set; } = null;

        /// <summary>
        /// Rollback time, or null.
        /// </summary>
        public DateTime? RolledBackUtc { get; set; } = null;

        /// <summary>
        /// Supersede time, or null.
        /// </summary>
        public DateTime? SupersededUtc { get; set; } = null;

        /// <summary>
        /// Created time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last change time.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    }
}
