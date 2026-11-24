namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// UpdateGroup payload (docs/LIVE_UPDATES_CONTRACT.md section 10.4).
    /// </summary>
    public class UpdateGroupView
    {
        /// <summary>
        /// Identifier (upg_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Kind.
        /// </summary>
        public UpdateGroupKindEnum Kind { get; set; } = UpdateGroupKindEnum.Publish;

        /// <summary>
        /// Channel.
        /// </summary>
        public string Channel { get; set; } = String.Empty;

        /// <summary>
        /// Message.
        /// </summary>
        public string Message { get; set; } = String.Empty;

        /// <summary>
        /// Status.
        /// </summary>
        public UpdateGroupStatusEnum Status { get; set; } = UpdateGroupStatusEnum.Draft;

        /// <summary>
        /// Rollout percentage.
        /// </summary>
        public decimal RolloutPercentage { get; set; } = 0m;

        /// <summary>
        /// Critical.
        /// </summary>
        public bool Critical { get; set; } = false;

        /// <summary>
        /// Critical prompt text, or null.
        /// </summary>
        public string? CriticalMessage { get; set; } = null;

        /// <summary>
        /// Git commit, or null.
        /// </summary>
        public string? GitCommit { get; set; } = null;

        /// <summary>
        /// Uncommitted changes at export.
        /// </summary>
        public bool GitDirty { get; set; } = false;

        /// <summary>
        /// Group a rollback republished, or null.
        /// </summary>
        public string? RepublishOf { get; set; } = null;

        /// <summary>
        /// Group promoted from another channel, or null.
        /// </summary>
        public string? PromotedFrom { get; set; } = null;

        /// <summary>
        /// The publish tool's updateGroupId, or null.
        /// </summary>
        public string? UpdateGroupUuid { get; set; } = null;

        /// <summary>
        /// Platforms (at most 2).
        /// </summary>
        public List<string> Platforms { get; set; } = new List<string>();

        /// <summary>
        /// One update per platform (fixed bound 2).
        /// </summary>
        public List<UpdateView> Updates { get; set; } = new List<UpdateView>();

        /// <summary>
        /// Creator.
        /// </summary>
        public UpdateCreatorView CreatedBy { get; set; } = new UpdateCreatorView();

        /// <summary>
        /// First publish, or null.
        /// </summary>
        public DateTime? PublishedUtc { get; set; } = null;

        /// <summary>
        /// Pause time, or null.
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
        /// Rollout plan stages (at most 10).
        /// </summary>
        public List<UpdateRolloutStageView> RolloutPlan { get; set; } = new List<UpdateRolloutStageView>();

        /// <summary>
        /// Automatic halt.
        /// </summary>
        public UpdateAutoHaltView AutoHalt { get; set; } = new UpdateAutoHaltView();

        /// <summary>
        /// Adoption (detail only), or null.
        /// </summary>
        public UpdateAdoptionSummaryView? Adoption { get; set; } = null;

        /// <summary>
        /// Asset preview (detail only), or null.
        /// </summary>
        public UpdateAssetPreview? Assets { get; set; } = null;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Created time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last change.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    }
}
