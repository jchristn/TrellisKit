namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Forum moderation (docs/PHASE3_CONTRACT.md section 11): moderation actions on threads through the shared
    /// moderation service (as its target extension), moderation action records for pin, lock, move, and metadata edits,
    /// and forum mutes.
    /// </summary>
    public class ForumModerationService : IModerationTargetExtension
    {
        #region Public-Members

        /// <summary>
        /// Longest forum mute reason.
        /// </summary>
        public const int MaxReasonLength = 500;

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public ForumModerationService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public bool Handles(TargetTypeEnum targetType)
        {
            return targetType == TargetTypeEnum.Thread;
        }

        /// <inheritdoc />
        public async Task<ModerationTarget?> LoadAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            if (targetType != TargetTypeEnum.Thread || String.IsNullOrWhiteSpace(targetId)) return null;
            ForumThread? thread = await _Services.Database.Threads.ReadAsync(targetId.Trim(), token).ConfigureAwait(false);
            if (thread == null) return null;
            return new ModerationTarget { TargetType = TargetTypeEnum.Thread, TargetId = thread.Id, AuthorUserId = thread.AuthorUserId, Thread = thread, ForumId = thread.ForumId };
        }

        /// <inheritdoc />
        public async Task SetStateAsync(ModerationTarget target, ModerationStateEnum state, string? moderatorUserId, string? reason, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(target);
            if (!await _Services.Database.Threads.SetModerationStateAsync(target.TargetId, state, moderatorUserId, reason, DateTime.UtcNow, token).ConfigureAwait(false))
                throw ApiErrorException.NotFound("Thread");
            ThreadContext? context = await _Services.Forums.Access.LoadAsync(target.TargetId, token).ConfigureAwait(false);
            if (context != null)
            {
                target.Thread = context.Thread;
                await _Services.Forums.Promotions.SyncPublicMediaAsync(context, token).ConfigureAwait(false);
                if (state == ModerationStateEnum.None && context.Thread.PublishedUtc == null) await _Services.Forums.Threads.TryPublishAsync(context.Thread.Id, token).ConfigureAwait(false);
            }
            await _Services.Forums.Search.EnqueueAsync(TargetTypeEnum.Thread, target.TargetId, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<object> ViewAsync(ContentActor actor, ModerationTarget target, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(target);
            ThreadContext? context = await _Services.Forums.Access.LoadAsync(target.TargetId, token).ConfigureAwait(false);
            if (context == null) throw ApiErrorException.NotFound("Thread");
            return (await _Services.Forums.Views.ThreadsAsync(new[] { context }, actor, true, token).ConfigureAwait(false))[0];
        }

        /// <inheritdoc />
        public async Task<BoundedPreview<object>> RevisionsAsync(ModerationTarget target, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(target);
            EnumerationResult<ThreadRevisionView> page = await _Services.Forums.Threads.RevisionPageAsync(target.TargetId, new EnumerationQuery { MaxResults = BoundedPreview<object>.MaxObjects }, true, BoundedPreview<object>.MaxObjects, token).ConfigureAwait(false);
            return new BoundedPreview<object>
            {
                Objects = page.Objects.Take(BoundedPreview<object>.MaxObjects).Select(v => (object)v).ToList(),
                TotalRecords = page.TotalRecords ?? page.Objects.Count,
                Enumerate = Constants.ApiPrefix + "/threads/" + target.TargetId + "/revisions/enumerate"
            };
        }

        /// <inheritdoc />
        public Task RecomputeReportsAsync(ModerationTarget target, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(target);
            return _Services.Database.Threads.RecomputeOpenReportCountAsync(target.TargetId, token);
        }

        /// <inheritdoc />
        public Task<bool> PurgeAsync(string targetId, DateTime nowUtc, CancellationToken token = default)
        {
            return _Services.Database.Threads.PurgeAsync(targetId, nowUtc, token);
        }

        /// <inheritdoc />
        public async Task<string?> ForumOfCommentAsync(ModerationTarget target, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(target);
            if (target.Comment == null || target.Comment.TargetType != TargetTypeEnum.Thread) return null;
            ForumThread? thread = await _Services.Database.Threads.ReadAsync(target.Comment.TargetId, token).ConfigureAwait(false);
            return thread?.ForumId;
        }

        /// <inheritdoc />
        public async Task<string?> ForumNameAsync(string forumId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(forumId)) return null;
            List<Forum> forums = await _Services.Database.Forums.ReadManyAsync(new[] { forumId }, token).ConfigureAwait(false);
            return forums.FirstOrDefault()?.Name;
        }

        /// <inheritdoc />
        public Task<List<string>> ModeratorIdsAsync(string forumId, CancellationToken token = default)
        {
            return _Services.Database.Forums.ModeratorIdsForForumAsync(forumId, token);
        }

        /// <summary>
        /// Record a forum moderation action (Pin, Unpin, Lock, Unlock, Move, EditMetadata) in the shared history.
        /// </summary>
        /// <param name="actor">Moderator.</param>
        /// <param name="thread">Thread.</param>
        /// <param name="kind">Action.</param>
        /// <param name="reason">Reason.</param>
        /// <param name="fromForumId">Source forum of a Move.</param>
        /// <param name="toForumId">Destination forum of a Move.</param>
        /// <param name="note">Note, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored action.</returns>
        public async Task<ModerationAction> RecordAsync(ContentActor actor, ForumThread thread, ModerationActionTypeEnum kind, string reason, string? fromForumId, string? toForumId, string? note, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(thread);
            ModerationAction action = await _Services.Database.ModerationActions.CreateAsync(new ModerationAction
            {
                Id = IdGenerator.Generate(Constants.ModerationActionIdPrefix),
                ModeratorUserId = actor.UserId,
                BreakGlass = actor.IsBreakGlass,
                TargetType = TargetTypeEnum.Thread,
                TargetId = thread.Id,
                TargetAuthorUserId = thread.AuthorUserId,
                Action = kind,
                Reason = String.IsNullOrWhiteSpace(reason) ? kind.ToString() : (reason.Length > ModerationService.MaxReasonLength ? reason.Substring(0, ModerationService.MaxReasonLength) : reason),
                Note = note,
                FromForumId = fromForumId,
                ToForumId = toForumId,
                CreatedUtc = DateTime.UtcNow
            }, token).ConfigureAwait(false);
            TrellisKitTelemetry.Add(TrellisKitTelemetry.ModerationActions, 1, "action", kind.ToString().ToLowerInvariant(), "target_type", TargetTypeEnum.Thread.ToString());
            return action;
        }

        #region Forum-Mutes

        /// <summary>
        /// POST /api/v1/admin/moderation/forum-mutes: mute a member in one forum (replacing an active mute there).
        /// </summary>
        /// <param name="actor">Moderator in scope.</param>
        /// <param name="info">Request information.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The mute.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotFound.</exception>
        public async Task<ForumMuteView> CreateMuteAsync(ContentActor actor, RequestInfo info, ForumMuteRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            _Services.Forums.Access.RequireEnabled();
            RequireModerator(actor);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            string userId = (request.UserId ?? String.Empty).Trim();
            string forumId = (request.ForumId ?? String.Empty).Trim();
            if (userId.Length == 0) errors["userId"] = "Required.";
            if (forumId.Length == 0) errors["forumId"] = "Required.";
            string? reason = String.IsNullOrWhiteSpace(request.Reason) ? null : MarkdownNormalizer.NormalizeSingleLine(request.Reason);
            if (reason != null && reason.Length > MaxReasonLength) errors["reason"] = "At most " + MaxReasonLength + " characters.";
            DateTime now = DateTime.UtcNow;
            DateTime? until = request.UntilUtc == null ? null : (request.UntilUtc.Value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(request.UntilUtc.Value, DateTimeKind.Utc) : request.UntilUtc.Value.ToUniversalTime());
            int cap = _Services.Settings.Forums.MaxForumMuteDays;
            if (until != null && until.Value <= now) errors["untilUtc"] = "Must be in the future, or null to mute until lifted.";
            else if (until != null && cap > 0 && until.Value > now.AddDays(cap) && !actor.IsSystemAdmin) errors["untilUtc"] = "At most " + cap + " days ahead (forums.maxForumMuteDays).";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            Forum? forum = await _Services.Database.Forums.ReadAsync(forumId, token).ConfigureAwait(false);
            if (forum == null) throw ApiErrorException.NotFound("Forum");
            if (!actor.CanModerateForum(forum.Id)) throw ApiErrorException.Forbidden("You do not moderate this forum.");
            User? user = await _Services.Database.Users.ReadAsync(userId, token).ConfigureAwait(false);
            if (user == null || user.IsDeleted) throw ApiErrorException.NotFound("Member");
            HashSet<string> staff = await _Services.Summaries.StaffAsync(new[] { user.Id }, token).ConfigureAwait(false);
            if (staff.Contains(user.Id) && !actor.IsSystemAdmin) throw ApiErrorException.Forbidden("Staff cannot be muted in a forum by a moderator.");
            if (String.Equals(user.Id, actor.UserId, StringComparison.Ordinal)) throw ApiErrorException.Field("userId", "You cannot mute yourself.");
            await _Services.Metadata.ValidateAsync(actor, request.Labels, request.Tags, true, token, TargetTypeEnum.ForumMute).ConfigureAwait(false);
            ForumMute mute = await _Services.Database.Forums.CreateMuteAsync(new ForumMute
            {
                Id = IdGenerator.Generate(ForumIdPrefixes.ForumMute),
                ForumId = forum.Id,
                UserId = user.Id,
                UntilUtc = until,
                Reason = reason,
                MutedByUserId = actor.UserId,
                CreatedUtc = now
            }, token).ConfigureAwait(false);
            if (request.Labels != null || request.Tags != null) await _Services.Metadata.ApplyAsync(actor, TargetTypeEnum.ForumMute, mute.Id, request.Labels, request.Tags, token).ConfigureAwait(false);
            await _Services.Database.ModerationActions.CreateAsync(new ModerationAction
            {
                Id = IdGenerator.Generate(Constants.ModerationActionIdPrefix),
                ModeratorUserId = actor.UserId,
                BreakGlass = actor.IsBreakGlass,
                TargetType = TargetTypeEnum.ForumMute,
                TargetId = mute.Id,
                TargetAuthorUserId = user.Id,
                Action = ModerationActionTypeEnum.ForumMute,
                Reason = reason ?? "Muted in " + forum.Name,
                ToForumId = forum.Id,
                NotifiedAuthor = request.NotifyMember ?? true,
                CreatedUtc = now
            }, token).ConfigureAwait(false);
            if (request.NotifyMember ?? true)
            {
                string untilText = until == null ? "until a moderator lifts it" : "until " + until.Value.ToString("MMMM d, yyyy HH:mm", CultureInfo.InvariantCulture) + " UTC";
                await _Services.Notifications.CreateAsync(new NotificationDraft
                {
                    RecipientUserId = user.Id,
                    Type = NotificationTypeEnum.ModerationAction,
                    TargetType = TargetTypeEnum.Forum,
                    TargetId = forum.Id,
                    ForumId = forum.Id,
                    TitleText = forum.Name,
                    ModerationAction = ModerationActionTypeEnum.ForumMute,
                    ModerationReason = reason,
                    Body = "A moderator muted you in " + forum.Name + " " + untilText + ".",
                    Path = "/forums/" + forum.Id
                }, token).ConfigureAwait(false);
            }
            await _Services.Audit.WriteAsync(info, "forum.mute", TargetTypeEnum.User.ToString(), user.Id, forum.Name + (until == null ? " until lifted" : " until " + until.Value.ToString("O", CultureInfo.InvariantCulture)), token).ConfigureAwait(false);
            ForumTelemetry.Count(ForumTelemetry.Mutes, "action", "mute");
            return (await ViewsAsync(new List<ForumMute> { mute }, actor, token).ConfigureAwait(false))[0];
        }

        /// <summary>
        /// POST /api/v1/admin/moderation/forum-mutes/enumerate (filtered to the caller's scope).
        /// </summary>
        /// <param name="actor">Moderator.</param>
        /// <param name="query">Query (filters forumId, userId, activeOnly default true).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of mutes.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden.</exception>
        public async Task<EnumerationResult<ForumMuteView>> ListMutesAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Services.Forums.Access.RequireEnabled();
            RequireModerator(actor);
            EnumerationQuery q = query ?? new EnumerationQuery();
            Dictionary<string, string> errors = new Dictionary<string, string>();
            ForumMuteFilter filter = new ForumMuteFilter
            {
                ScopeForumIds = actor.ForumScope,
                ForumId = q.FilterString("forumId"),
                UserId = q.FilterString("userId"),
                ActiveOnly = q.FilterBool("activeOnly", errors) ?? true,
                NowUtc = DateTime.UtcNow
            };
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            EnumerationRequest request = _Services.Enumeration.Prepare(q, ForumCollections.ForumMutes, true, "forum-mutes|" + (filter.ScopeForumIds == null ? "*" : String.Join(",", filter.ScopeForumIds)));
            EnumerationResult<ForumMute> page = await _Services.Database.Forums.EnumerateMutesAsync(request, filter, token).ConfigureAwait(false);
            return page.With(await ViewsAsync(page.Objects, actor, token).ConfigureAwait(false));
        }

        /// <summary>
        /// DELETE /api/v1/admin/moderation/forum-mutes/{id}: lift a mute.
        /// </summary>
        /// <param name="actor">Moderator in scope.</param>
        /// <param name="info">Request information.</param>
        /// <param name="muteId">Mute.</param>
        /// <param name="reason">Optional reason.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound.</exception>
        public async Task EndMuteAsync(ContentActor actor, RequestInfo info, string muteId, string? reason, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            _Services.Forums.Access.RequireEnabled();
            RequireModerator(actor);
            ForumMute? mute = String.IsNullOrWhiteSpace(muteId) ? null : await _Services.Database.Forums.ReadMuteAsync(muteId.Trim(), token).ConfigureAwait(false);
            if (mute == null || !actor.CanModerateForum(mute.ForumId)) throw ApiErrorException.NotFound("Forum mute");
            DateTime now = DateTime.UtcNow;
            if (await _Services.Database.Forums.EndMuteAsync(mute.Id, actor.UserId, now, token).ConfigureAwait(false))
            {
                string? text = String.IsNullOrWhiteSpace(reason) ? null : MarkdownNormalizer.NormalizeSingleLine(reason);
                await _Services.Database.ModerationActions.CreateAsync(new ModerationAction
                {
                    Id = IdGenerator.Generate(Constants.ModerationActionIdPrefix),
                    ModeratorUserId = actor.UserId,
                    BreakGlass = actor.IsBreakGlass,
                    TargetType = TargetTypeEnum.ForumMute,
                    TargetId = mute.Id,
                    TargetAuthorUserId = mute.UserId,
                    Action = ModerationActionTypeEnum.ForumUnmute,
                    Reason = text == null ? "Mute lifted" : (text.Length > MaxReasonLength ? text.Substring(0, MaxReasonLength) : text),
                    ToForumId = mute.ForumId,
                    CreatedUtc = now
                }, token).ConfigureAwait(false);
                await _Services.Audit.WriteAsync(info, "forum.unmute", TargetTypeEnum.User.ToString(), mute.UserId, mute.ForumId, token).ConfigureAwait(false);
                ForumTelemetry.Count(ForumTelemetry.Mutes, "action", "unmute");
            }
        }

        #endregion

        #endregion

        #region Private-Methods

        private static void RequireModerator(ContentActor actor)
        {
            if (!actor.ModeratesAnyForum) throw ApiErrorException.Forbidden("A forum moderator role is required.");
        }

        private async Task<List<ForumMuteView>> ViewsAsync(List<ForumMute> mutes, ContentActor actor, CancellationToken token)
        {
            Dictionary<string, UserSummary> users = await _Services.Summaries.BuildAsync(mutes.Select(m => (string?)m.UserId).Concat(mutes.Select(m => m.MutedByUserId)), token).ConfigureAwait(false);
            Dictionary<string, Forum> forums = (await _Services.Database.Forums.ReadManyAsync(mutes.Select(m => m.ForumId), token).ConfigureAwait(false)).ToDictionary(f => f.Id, StringComparer.Ordinal);
            Dictionary<string, ObjectMetadata> metadata = await _Services.Metadata.LoadAsync(TargetTypeEnum.ForumMute, mutes.Select(m => m.Id), actor.IsStaffFor(TargetTypeEnum.ForumMute), token).ConfigureAwait(false);
            return mutes.Select(m => new ForumMuteView
            {
                Id = m.Id,
                User = users.TryGetValue(m.UserId, out UserSummary? u) ? u : UserSummary.Deleted(),
                Forum = new ForumMuteForumRef { Id = m.ForumId, Name = forums.TryGetValue(m.ForumId, out Forum? f) ? f.Name : String.Empty },
                UntilUtc = m.UntilUtc,
                Reason = m.Reason,
                MutedBy = m.MutedByUserId != null && users.TryGetValue(m.MutedByUserId, out UserSummary? by) ? by : null,
                EndedUtc = m.EndedUtc,
                CreatedUtc = m.CreatedUtc,
                Labels = metadata.TryGetValue(m.Id, out ObjectMetadata? md) ? md.Labels : new List<string>(),
                Tags = md != null ? md.Tags : new Dictionary<string, string>()
            }).ToList();
        }

        #endregion
    }
}
