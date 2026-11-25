namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>Profile gallery authorization and per-item reports in the community moderation queue.</summary>
    public class ProfileMediaTargetProvider : ContentTargetProvider
    {
        private readonly ApplicationServices _Services;
        /// <summary>Bind to one property.</summary>
        public ProfileMediaTargetProvider(ApplicationServices services) { _Services = services ?? throw new ArgumentNullException(nameof(services)); }
        /// <inheritdoc />
        public override IReadOnlyList<TargetTypeEnum> TargetTypes { get; } = new[] { TargetTypeEnum.ProfileMedia };
        /// <inheritdoc />
        public override string ScopeType(TargetTypeEnum targetType) => "Feed";
        /// <inheritdoc />
        public override bool Moderates(ContentActor actor, string scopeType) => scopeType == "Feed" && actor.CanModerateFeed;
        /// <inheritdoc />
        public override string PathOf(TargetTypeEnum targetType, string targetId) => "/members";
        /// <inheritdoc />
        public override Task<bool> IsVisibleAsync(ContentActor actor, TargetTypeEnum targetType, string targetId, CancellationToken token = default) => _Services.ProfileMedia.VisibleAsync(actor, targetId, token);
        /// <inheritdoc />
        public override bool AllowsAutoHide(TargetTypeEnum targetType) => true;
        /// <inheritdoc />
        public override async Task<ModerationTarget?> LoadModerationTargetAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            ProfileMediaItem? item = await _Services.Database.ProfileMedia.ReadAsync(targetId, token).ConfigureAwait(false);
            if (item == null) return null;
            return new ModerationTarget { TargetType = TargetTypeEnum.ProfileMedia, TargetId = item.Id, AuthorUserId = item.UploaderUserId, State = item.ModerationState, Deleted = item.DeletedUtc != null, Title = "Profile multimedia", Excerpt = item.Kind == MediaKindEnum.Video ? "Video" : "Photo", HasMedia = true, ExtensionPath = "/members/" + item.ProfileUserId };
        }
        /// <inheritdoc />
        public override async Task SetModerationStateAsync(ModerationTarget target, ModerationStateEnum state, string? moderatorUserId, string? reason, CancellationToken token = default)
        {
            await _Services.Database.ProfileMedia.SetModerationAsync(target.TargetId, state, moderatorUserId, reason, DateTime.UtcNow, token).ConfigureAwait(false);
        }
        /// <inheritdoc />
        public override async Task RecomputeReportsAsync(ModerationTarget target, CancellationToken token = default)
        {
            long count = await _Services.Database.Reports.CountOpenOnTargetAsync(target.TargetType, target.TargetId, null, token).ConfigureAwait(false);
            await _Services.Database.ProfileMedia.SetOpenReportCountAsync(target.TargetId, (int)Math.Min(Int32.MaxValue, count), token).ConfigureAwait(false);
        }
        /// <inheritdoc />
        public override async Task<object> ModerationViewAsync(ContentActor actor, ModerationTarget target, CancellationToken token = default)
        {
            ProfileMediaItem? item = await _Services.Database.ProfileMedia.ReadAsync(target.TargetId, token).ConfigureAwait(false);
            if (item == null) return await base.ModerationViewAsync(actor, target, token).ConfigureAwait(false);
            return (await _Services.ProfileMedia.ViewsAsync(actor, new[] { item }, token).ConfigureAwait(false)).First();
        }
    }
}
