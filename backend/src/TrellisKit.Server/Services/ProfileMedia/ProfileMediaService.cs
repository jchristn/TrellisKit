namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>Owner-managed profile galleries using the existing media pipeline and report moderation.</summary>
    public class ProfileMediaService
    {
        private readonly ApplicationServices _Services;

        /// <summary>Bind the gallery to one property's services.</summary>
        public ProfileMediaService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        /// <summary>Register visibility, moderation, exports, and account deletion.</summary>
        public void Attach()
        {
            _Services.ContentTargets.Register(new ProfileMediaTargetProvider(_Services));
            _Services.AccountExports.Register(new PagedExportSection<ProfileMediaItem>("profileMedia", async (userId, after, limit, token) =>
            {
                List<ProfileMediaItem> rows = await _Services.Database.ProfileMedia.ReadForUserAsync(userId, after, limit, token).ConfigureAwait(false);
                return new ContentExportPage<ProfileMediaItem> { Objects = rows, NextAfterId = rows.Count < limit ? null : rows[rows.Count - 1].Id };
            }));
            Func<string, CancellationToken, Task>? previous = _Services.Accounts.AccountClosed;
            _Services.Accounts.AccountClosed = async (userId, token) =>
            {
                while (true)
                {
                    List<ProfileMediaItem> items = await _Services.Database.ProfileMedia.EraseForUserAsync(userId, DateTime.UtcNow, 100, token).ConfigureAwait(false);
                    foreach (ProfileMediaItem item in items) await _Services.Media.PurgeTargetAsync(TargetTypeEnum.ProfileMedia, item.Id, token).ConfigureAwait(false);
                    if (items.Count < 100) break;
                }
                if (previous != null) await previous(userId, token).ConfigureAwait(false);
            };
        }

        /// <summary>Whether a caller can read a profile's gallery, including direct media authorization.</summary>
        public async Task<bool> CanReadAsync(ContentActor actor, string userId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (actor.IsAnonymous || !AccessDecider.Meets(actor.Tier, AccessDecider.WriteTier(actor.Access, FeatureKeyEnum.Members))) return false;
            User? owner = await _Services.Database.Users.ReadAsync(userId, token).ConfigureAwait(false);
            if (owner == null || owner.IsDeleted || owner.IsBanned) return false;
            if (!String.IsNullOrEmpty(actor.UserId) && actor.UserId != userId && (await _Services.Blocks.RelatedAsync(actor.UserId, token).ConfigureAwait(false)).Contains(userId)) return false;
            return true;
        }

        /// <summary>Whether an attached item is visible, preserving profile access and moderation gates.</summary>
        public async Task<bool> VisibleAsync(ContentActor actor, string id, CancellationToken token = default)
        {
            ProfileMediaItem? item = await _Services.Database.ProfileMedia.ReadAsync(id, token).ConfigureAwait(false);
            return item != null && item.DeletedUtc == null && await CanReadAsync(actor, item.ProfileUserId, token).ConfigureAwait(false) &&
                (item.IsLive || actor.CanModerateFeed || (item.ModerationState == ModerationStateEnum.Hidden && actor.UserId == item.UploaderUserId));
        }

        /// <summary>Enumerate a bounded gallery page with kind, label, and tag filters.</summary>
        public async Task<EnumerationResult<ProfileMediaItemView>> ListAsync(ContentActor actor, string userId, EnumerationQuery? query, CancellationToken token = default)
        {
            if (!await CanReadAsync(actor, userId, token).ConfigureAwait(false)) throw ApiErrorException.NotFound("Profile");
            _Services.Limits.EnforceMember(actor, MediaService.ReadBucket, _Services.Settings.RateLimits.MemberReadsPerMinute, RateLimitService.Minute);
            EnumerationQuery q = query ?? new EnumerationQuery();
            ProfileMediaFilter filter = new ProfileMediaFilter { ProfileUserId = userId, IncludeModerated = actor.CanModerateFeed };
            string? kind = q.FilterString("kind");
            if (!String.IsNullOrEmpty(kind))
            {
                if (String.Equals(kind, "Image", StringComparison.OrdinalIgnoreCase)) filter.Kind = MediaKindEnum.Image;
                else if (String.Equals(kind, "Video", StringComparison.OrdinalIgnoreCase)) filter.Kind = MediaKindEnum.Video;
                else throw ApiErrorException.Field("filters.kind", "Use Image or Video.");
            }
            EnumerationRequest request = _Services.Enumeration.Prepare(q, ProfileMediaCollections.Items, actor.CanModerateFeed, userId);
            EnumerationResult<ProfileMediaItem> page = await _Services.Database.ProfileMedia.EnumerateAsync(request, filter, token).ConfigureAwait(false);
            return page.With(await ViewsAsync(actor, page.Objects, token).ConfigureAwait(false));
        }

        /// <summary>Attach one to ten of the profile owner's own photo/video uploads.</summary>
        public async Task<ProfileMediaAddResponse> AddAsync(ContentActor actor, string userId, ProfileMediaAddRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            string uploader = actor.RequireUserId();
            if (uploader != userId) throw ApiErrorException.Forbidden("Only the profile owner can add media.");
            if (!await CanReadAsync(actor, userId, token).ConfigureAwait(false)) throw ApiErrorException.NotFound("Profile");
            actor.RequireCanPostMedia();
            List<string> ids = request.MediaIds ?? new List<string>();
            if (ids.Count < 1 || ids.Count > 10 || ids.Any(String.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
                throw ApiErrorException.Field("mediaIds", "Provide 1 to 10 distinct uploaded media ids.");
            List<MediaAttachment> media = await _Services.Media.ValidateAttachableAsync(actor, ids, 10, 10, null, null, token).ConfigureAwait(false);
            if (media.Any(m => m.Kind != MediaKindEnum.Image && m.Kind != MediaKindEnum.Video)) throw ApiErrorException.Field("mediaIds", "Only photos and videos can be added.");
            if (media.Any(m => m.OwnerUserId != uploader)) throw ApiErrorException.Forbidden("Only your own uploads can be added.");
            if (media.Any(m => m.Kind == MediaKindEnum.Video)) actor.RequireCanUploadVideo();
            await _Services.Metadata.ValidateAsync(actor, request.Labels, request.Tags, actor.CanModerateFeed, token, TargetTypeEnum.ProfileMedia).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            List<ProfileMediaItem> items = media.Select(m => new ProfileMediaItem { Id = IdGenerator.Generate("pmd_"), ProfileUserId = userId, UploaderUserId = uploader, MediaId = m.Id, Kind = m.Kind, CreatedUtc = now, UpdatedUtc = now }).ToList();
            try
            {
                await _Services.Database.ProfileMedia.CreateManyAsync(items, token).ConfigureAwait(false);
            }
            catch (DuplicateRecordException)
            {
                throw new ApiErrorException(409, "MediaNotAttachable", "One of the uploads is already attached to content.");
            }
            foreach (ProfileMediaItem item in items)
            {
                await _Services.Media.AttachAsync(TargetTypeEnum.ProfileMedia, item.Id, new List<string> { item.MediaId }, token).ConfigureAwait(false);
                if (request.Labels != null || request.Tags != null) await _Services.Metadata.ApplyAsync(actor, TargetTypeEnum.ProfileMedia, item.Id, request.Labels, request.Tags, token).ConfigureAwait(false);
            }
            return new ProfileMediaAddResponse { Objects = await ViewsAsync(actor, items, token).ConfigureAwait(false) };
        }

        /// <summary>Delete an owned gallery item. Staff use the standard audited moderation actions.</summary>
        public async Task DeleteAsync(ContentActor actor, string userId, string itemId, CancellationToken token = default)
        {
            if (actor.RequireUserId() != userId) throw ApiErrorException.Forbidden("Only the profile owner can delete this media.");
            ProfileMediaItem? item = await _Services.Database.ProfileMedia.ReadAsync(itemId, token).ConfigureAwait(false);
            if (item == null || item.ProfileUserId != userId || item.DeletedUtc != null) throw ApiErrorException.NotFound("Media item");
            if (!await _Services.Database.ProfileMedia.SoftDeleteAsync(item.Id, DateTime.UtcNow, token).ConfigureAwait(false)) throw ApiErrorException.NotFound("Media item");
            await _Services.Media.PurgeTargetAsync(TargetTypeEnum.ProfileMedia, item.Id, token).ConfigureAwait(false);
        }

        /// <summary>Build gallery items with signed media views and per-item capabilities.</summary>
        public async Task<List<ProfileMediaItemView>> ViewsAsync(ContentActor actor, IReadOnlyList<ProfileMediaItem> items, CancellationToken token = default)
        {
            Dictionary<string, List<MediaView>> media = await _Services.Media.ListViewsForTargetsAsync(TargetTypeEnum.ProfileMedia, items.Select(i => i.Id), false, actor.CanModerateFeed, token).ConfigureAwait(false);
            Dictionary<string, UserSummary> users = await _Services.Summaries.BuildAsync(items.Select(i => (string?)i.UploaderUserId), token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> metadata = await _Services.Metadata.LoadAsync(TargetTypeEnum.ProfileMedia, items.Select(i => i.Id), actor.CanModerateFeed, token).ConfigureAwait(false);
            return items.Select(item => new ProfileMediaItemView
            {
                Id = item.Id, ProfileUserId = item.ProfileUserId,
                Media = media.TryGetValue(item.Id, out List<MediaView>? views) ? views.FirstOrDefault() : null,
                Uploader = users.TryGetValue(item.UploaderUserId, out UserSummary? user) ? user : UserSummary.Deleted(),
                ModerationState = item.ModerationState,
                Labels = metadata.TryGetValue(item.Id, out ObjectMetadata? meta) ? meta.Labels : new List<string>(),
                Tags = metadata.TryGetValue(item.Id, out ObjectMetadata? tags) ? tags.Tags : new Dictionary<string, string>(),
                Viewer = new ProfileMediaItemViewer { IsUploader = actor.UserId == item.UploaderUserId, CanRemove = actor.UserId == item.UploaderUserId, CanReport = !actor.IsAnonymous && actor.UserId != item.UploaderUserId },
                CreatedUtc = item.CreatedUtc, UpdatedUtc = item.UpdatedUtc
            }).ToList();
        }
    }
}
