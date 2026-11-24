namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Plugs events into comments, reactions, reports, moderation, and search (docs/PHASE4_CONTRACT.md sections 4.8,
    /// 6, and 7): scope "Events" moderated by event staff (SystemAdmin and EventManager); comments follow
    /// commentsEnabled, new comments on canceled events are 409 EventCanceled, PostComment goes to the hosts; a removed
    /// event is canceled and its RSVPed members get EventCanceled without the moderation reason; search documents carry
    /// the event's labels and tags. Thread safety: stateless.
    /// </summary>
    public class EventContentProvider : ContentTargetProvider
    {
        #region Public-Members

        /// <summary>
        /// Moderation and report scope of events.
        /// </summary>
        public const string Scope = "Events";

        /// <inheritdoc />
        public override IReadOnlyList<TargetTypeEnum> TargetTypes
        {
            get { return _Types; }
        }

        #endregion

        #region Private-Members

        private static readonly List<TargetTypeEnum> _Types = new List<TargetTypeEnum> { TargetTypeEnum.Event };
        private readonly ApplicationServices _Services;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public EventContentProvider(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override string ScopeType(TargetTypeEnum targetType)
        {
            return Scope;
        }

        /// <inheritdoc />
        public override bool Moderates(ContentActor actor, string scopeType)
        {
            ArgumentNullException.ThrowIfNull(actor);
            return String.Equals(scopeType, Scope, StringComparison.OrdinalIgnoreCase) && actor.IsEventStaff;
        }

        /// <inheritdoc />
        public override string PathOf(TargetTypeEnum targetType, string targetId)
        {
            return "/events/" + targetId;
        }

        /// <inheritdoc />
        public override async Task<bool> IsVisibleAsync(ContentActor actor, TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (targetType != TargetTypeEnum.Event) return false;
            EventRecord? e = await _Services.Database.Events.Records.ReadAsync(targetId, token).ConfigureAwait(false);
            return e != null && EventAccess.Read(e, actor) == EventReadEnum.Open;
        }

        /// <inheritdoc />
        public override async Task<CommentHost> CommentHostAsync(ContentActor actor, TargetTypeEnum targetType, string targetId, bool forWrite, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            EventRecord e = await RequireOpenAsync(actor, targetId, token).ConfigureAwait(false);
            if (forWrite)
            {
                if (e.Status == EventStatusEnum.Canceled) throw new ApiErrorException(409, "EventCanceled", "This event was canceled; existing comments stay readable.");
                if (e.Status != EventStatusEnum.Published || e.ModerationState != ModerationStateEnum.None) throw ApiErrorException.NotFound("Event");
                if (!e.CommentsEnabled) throw new ApiErrorException(403, "CommentsDisabled", "Comments are turned off for this event.");
                if (!EventAccess.MayParticipate(e, actor)) throw new ApiErrorException(402, "MembershipRequired", "A membership is required to comment on this event.");
            }
            bool media = _Services.Settings.Events.CommentMedia;
            return new CommentHost
            {
                TargetType = TargetTypeEnum.Event,
                TargetId = e.Id,
                AuthorUserId = e.HostUserIds.FirstOrDefault() ?? String.Empty,
                CommentCount = e.CommentCount,
                AllowMedia = media,
                AllowVideo = media,
                ModeratorInScope = actor.IsEventStaff
            };
        }

        /// <inheritdoc />
        public override async Task<int> CommentCountAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            EventRecord? e = await _Services.Database.Events.Records.ReadAsync(targetId, token).ConfigureAwait(false);
            return e?.CommentCount ?? 0;
        }

        /// <inheritdoc />
        public override async Task AfterCommentAsync(CommentHost host, Comment comment, Comment? parent, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(host);
            ArgumentNullException.ThrowIfNull(comment);
            await _Services.Database.Events.Records.RecountEngagementAsync(host.TargetId, token).ConfigureAwait(false);
            EventRecord? e = await _Services.Database.Events.Records.ReadAsync(host.TargetId, token).ConfigureAwait(false);
            if (e == null) return;
            string path = "/events/" + e.Id + "?comment=" + comment.Id;
            List<NotificationDraft> drafts = new List<NotificationDraft>();
            if (parent != null && !String.Equals(parent.AuthorUserId, comment.AuthorUserId, StringComparison.Ordinal))
            {
                drafts.Add(new NotificationDraft
                {
                    RecipientUserId = parent.AuthorUserId,
                    Type = NotificationTypeEnum.CommentReply,
                    ActorUserId = comment.AuthorUserId,
                    TargetType = TargetTypeEnum.Comment,
                    TargetId = comment.Id,
                    CommentId = comment.Id,
                    ParentCommentId = parent.Id,
                    Body = comment.Excerpt,
                    Path = path
                });
            }
            if (parent == null)
            {
                drafts.AddRange(e.HostUserIds.Where(h => !String.Equals(h, comment.AuthorUserId, StringComparison.Ordinal)).Select(h => new NotificationDraft
                {
                    RecipientUserId = h,
                    Type = NotificationTypeEnum.PostComment,
                    ActorUserId = comment.AuthorUserId,
                    TargetType = TargetTypeEnum.Comment,
                    TargetId = comment.Id,
                    CommentId = comment.Id,
                    SubjectNoun = "event",
                    Body = comment.Excerpt,
                    Path = path
                }));
            }
            if (drafts.Count > 0) await _Services.Notifications.CreateForSourceAsync(drafts, token).ConfigureAwait(false);
            await _Services.Forums.Search.EnqueueAsync(TargetTypeEnum.Comment, comment.Id, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public override async Task CommentChangedAsync(Comment comment, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(comment);
            if (comment.TargetType == TargetTypeEnum.Event) await _Services.Database.Events.Records.RecountEngagementAsync(comment.TargetId, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public override async Task<ReactionTarget?> ReactionTargetAsync(ContentActor actor, TargetTypeEnum targetType, string targetId, bool forWrite, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            EventRecord e = await RequireOpenAsync(actor, targetId, token).ConfigureAwait(false);
            if (forWrite)
            {
                if (e.Status != EventStatusEnum.Published && e.Status != EventStatusEnum.Canceled) throw ApiErrorException.NotFound("Event");
                if (!EventAccess.MayParticipate(e, actor)) throw new ApiErrorException(402, "MembershipRequired", "A membership is required to react to this event.");
                actor.RequireNotMuted();
            }
            return new ReactionTarget { AuthorUserId = e.HostUserIds.FirstOrDefault() ?? String.Empty, Excerpt = e.Title };
        }

        /// <inheritdoc />
        public override Task ReactionsChangedAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            return _Services.Database.Events.Records.RecountEngagementAsync(targetId, token);
        }

        /// <inheritdoc />
        public override async Task<ModerationTarget?> LoadModerationTargetAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            if (targetType != TargetTypeEnum.Event) return null;
            EventRecord? e = await _Services.Database.Events.Records.ReadAsync(targetId, token).ConfigureAwait(false);
            if (e == null || e.Status == EventStatusEnum.Template) return null;
            return new ModerationTarget { TargetType = TargetTypeEnum.Event, TargetId = e.Id, AuthorUserId = e.CreatedByUserId ?? e.HostUserIds.FirstOrDefault() ?? String.Empty, State = e.ModerationState, Deleted = e.DeletedUtc != null };
        }

        /// <inheritdoc />
        public override async Task SetModerationStateAsync(ModerationTarget target, ModerationStateEnum state, string? moderatorUserId, string? reason, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(target);
            EventRecord e = await _Services.Database.Events.Records.ReadAsync(target.TargetId, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Event");
            DateTime now = DateTime.UtcNow;
            EventRecord next = e.Clone();
            next.ModerationState = state;
            next.ModeratedUtc = now;
            next.ModeratedByUserId = moderatorUserId;
            next.ModerationReason = reason;
            await _Services.Database.Events.Records.UpdateAsync(next, false, now, token).ConfigureAwait(false);
            if (state == ModerationStateEnum.Removed && e.Status == EventStatusEnum.Published)
            {
                EventRecord moderated = await _Services.Database.Events.Records.ReadAsync(e.Id, token).ConfigureAwait(false) ?? next;
                await _Services.Events.Catalog.CancelOneAsync(null, moderated, "This event was removed.", now, token).ConfigureAwait(false);
                EventRecord canceled = await _Services.Database.Events.Records.ReadAsync(e.Id, token).ConfigureAwait(false) ?? moderated;
                await _Services.Events.Notifier.StartEventAsync(NotificationTypeEnum.EventCanceled, canceled, String.Empty, token).ConfigureAwait(false);
            }
            await _Services.Events.Catalog.SyncCoversAsync(new[] { e.CoverMediaId }, token).ConfigureAwait(false);
            await _Services.Forums.Search.EnqueueAsync(TargetTypeEnum.Event, e.Id, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public override Task RecomputeReportsAsync(ModerationTarget target, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(target);
            return _Services.Database.Events.Records.RecountEngagementAsync(target.TargetId, token);
        }

        /// <inheritdoc />
        public override async Task<object> ModerationViewAsync(ContentActor actor, ModerationTarget target, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(target);
            EventRecord? e = await _Services.Database.Events.Records.ReadAsync(target.TargetId, token).ConfigureAwait(false);
            if (e == null) return await base.ModerationViewAsync(actor, target, token).ConfigureAwait(false);
            return await _Services.Events.Views.AdminAsync(actor, e, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public override async Task<SearchDocument?> SearchDocumentAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            if (targetType != TargetTypeEnum.Event) return null;
            EventRecord? e = await _Services.Database.Events.Records.ReadAsync(targetId, token).ConfigureAwait(false);
            if (e == null || e.ModerationState != ModerationStateEnum.None || (e.Status != EventStatusEnum.Published && e.Status != EventStatusEnum.Canceled)) return null;
            ObjectMetadata metadata = await _Services.Database.Metadata.ReadAsync(TargetTypeEnum.Event, e.Id, token).ConfigureAwait(false);
            SearchDocument document = new SearchDocument
            {
                Id = "Event:" + e.Id,
                Content = String.Join(" ", e.Title, e.Title, e.Title, e.Summary ?? String.Empty, MarkdownNormalizer.ToPlainText(e.Description), e.LocationName ?? String.Empty, e.Locality ?? String.Empty),
                Labels = metadata.Labels.Select(TrellisKit.Core.Enumeration.MetadataRules.Normalize).Distinct(StringComparer.Ordinal).ToList()
            };
            foreach (KeyValuePair<string, string> tag in metadata.Tags) document.Tags[TrellisKit.Core.Enumeration.MetadataRules.Normalize(tag.Key)] = tag.Value ?? String.Empty;
            document.Tags["tk.type"] = "Event";
            return document;
        }

        /// <inheritdoc />
        public override Task<List<string>> ChangedIdsAsync(TargetTypeEnum targetType, DateTime? sinceUtc, int limit, CancellationToken token = default)
        {
            return _Services.Database.Events.Records.ChangedIdsAsync(sinceUtc, limit, token);
        }

        /// <inheritdoc />
        public override async Task<SearchResultItem?> SearchResultAsync(ContentActor actor, TargetTypeEnum targetType, string targetId, IReadOnlyList<string> terms, CancellationToken token = default)
        {
            EventRecord? e = await _Services.Database.Events.Records.ReadAsync(targetId, token).ConfigureAwait(false);
            if (e == null) return null;
            string text = String.Join(" ", e.Summary ?? String.Empty, MarkdownNormalizer.ToPlainText(e.Description)).Trim();
            return new SearchResultItem
            {
                Type = TargetTypeEnum.Event.ToString(),
                Id = e.Id,
                Title = e.Title,
                Path = PathOf(TargetTypeEnum.Event, e.Id),
                CreatedUtc = e.PublishedUtc ?? e.CreatedUtc,
                Snippet = SearchService.Snippet(text, terms, "description", null) ?? new SearchSnippetView { Field = "description", Text = text.Length > 200 ? text.Substring(0, 200) : text },
                Event = new SearchEventView
                {
                    StartUtc = e.StartUtc,
                    TimeZone = e.TimeZone,
                    IsCanceled = e.Status == EventStatusEnum.Canceled,
                    IsPast = e.EndUtc <= DateTime.UtcNow,
                    RecurrenceId = e.RecurrenceId
                }
            };
        }

        /// <inheritdoc />
        public override async Task<PromotionSource?> PromotionSourceAsync(ContentActor actor, TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (targetType != TargetTypeEnum.Event) return null;
            EventRecord e = await RequireOpenAsync(actor, targetId, token).ConfigureAwait(false);
            PromotionSource source = new PromotionSource
            {
                Title = e.Title,
                Excerpt = String.IsNullOrWhiteSpace(e.Summary) ? MarkdownNormalizer.Excerpt(e.Description) : e.Summary!,
                Audience = e.Audience,
                Path = PathOf(TargetTypeEnum.Event, e.Id),
                AuthorUserId = null,
                IsLive = e.Status == EventStatusEnum.Published && e.ModerationState == ModerationStateEnum.None,
                PromotedFeedPostId = e.PromotedFeedPostId,
                NotifyMembersDefault = false,
                CommentsOpen = e.CommentsEnabled && e.Status == EventStatusEnum.Published,
                ReactionsOpen = e.Status == EventStatusEnum.Published || e.Status == EventStatusEnum.Canceled
            };
            if (!String.IsNullOrEmpty(e.CoverMediaId)) source.MediaIds.Add(e.CoverMediaId);
            return source;
        }

        /// <inheritdoc />
        public override async Task PromotionChangedAsync(TargetTypeEnum targetType, string targetId, string? feedPostId, CancellationToken token = default)
        {
            EventRecord? e = await _Services.Database.Events.Records.ReadAsync(targetId, token).ConfigureAwait(false);
            if (e == null) return;
            e.PromotedFeedPostId = feedPostId;
            await _Services.Database.Events.Records.UpdateAsync(e, false, DateTime.UtcNow, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public override bool ProvidesKnowledge(TargetTypeEnum targetType)
        {
            return targetType == TargetTypeEnum.Event;
        }

        /// <inheritdoc />
        public override async Task<List<KnowledgeDocument>> KnowledgeDocumentsAsync(TargetTypeEnum targetType, AssistantKnowledgeSource source, int maxItems, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            EnumerationQuery query = new EnumerationQuery { MaxResults = Math.Clamp(maxItems, 1, 100), Labels = new List<string>(source.Labels), LabelMatch = source.LabelMatch, Tags = new Dictionary<string, string>(source.Tags) };
            EventListFilter filter = new EventListFilter { NowUtc = DateTime.UtcNow, When = EventWhenEnum.Upcoming };
            EnumerationRequest request = _Services.Enumeration.Prepare(query, EventCollections.Events, true, "knowledge");
            EnumerationResult<EventRecord> page = await _Services.Database.Events.Records.EnumerateAsync(filter, request, token).ConfigureAwait(false);
            return page.Objects.Select(Knowledge).ToList();
        }

        /// <inheritdoc />
        public override async Task<List<KnowledgeDocument>> KnowledgeSearchAsync(ContentActor actor, TargetTypeEnum targetType, AssistantKnowledgeSource source, string keyword, int maxResults, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(source);
            EnumerationQuery query = new EnumerationQuery { MaxResults = Math.Clamp(maxResults, 1, 25), Search = keyword, Labels = new List<string>(source.Labels), LabelMatch = source.LabelMatch, Tags = new Dictionary<string, string>(source.Tags) };
            EventListFilter filter = new EventListFilter { NowUtc = DateTime.UtcNow, When = EventWhenEnum.Upcoming, Audiences = actor.IsEventStaff ? null : ContentAudienceRule.VisibleAudiences(actor) };
            EnumerationRequest request = _Services.Enumeration.Prepare(query, EventCollections.Events, false, "knowledge-search");
            EnumerationResult<EventRecord> page = await _Services.Database.Events.Records.EnumerateAsync(filter, request, token).ConfigureAwait(false);
            return page.Objects.Where(e => EventAccess.Read(e, actor) == EventReadEnum.Open).Select(Knowledge).ToList();
        }

        /// <summary>
        /// The assistant tool findEvents: upcoming events the caller may read matching optional text, a category, and a
        /// window, at most 10, as plain text lines.
        /// </summary>
        /// <param name="caller">Chatting caller.</param>
        /// <param name="args">Arguments { query, category, startsAfter, startsBefore, maxResults }.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        public async Task<TrellisKit.Server.Services.Ai.AssistantToolResult> FindEventsAsync(TrellisKit.Server.Services.Ai.AssistantCaller caller, System.Text.Json.JsonElement args, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(caller);
            string? text = Arg(args, "query");
            string? category = Arg(args, "category");
            int max = 5;
            if (args.ValueKind == System.Text.Json.JsonValueKind.Object && args.TryGetProperty("maxResults", out System.Text.Json.JsonElement m) && m.TryGetInt32(out int n)) max = Math.Clamp(n, 1, 10);
            EnumerationQuery query = new EnumerationQuery { MaxResults = max, Search = text };
            if (category != null) query.SetFilter("category", category);
            string? after = Arg(args, "startsAfter");
            string? before = Arg(args, "startsBefore");
            if (after != null) query.SetFilter("startsAfterUtc", after);
            if (before != null) query.SetFilter("startsBeforeUtc", before);
            EnumerationResult<EventSummaryView> page = await _Services.Events.Catalog.ListAsync(caller.Actor, query, token).ConfigureAwait(false);
            List<EventSummaryView> open = page.Objects.Where(e => !e.Locked).ToList();
            List<string> lines = open.Select(e => "- " + e.Title + " (" + EventTimeZones.Friendly(e.StartUtc, e.TimeZone, e.IsAllDay) + ", " + e.TimeZone + ")" +
                (e.Location == null ? String.Empty : " at " + EventViewBuilder.LocationText(e.Location)) + " " + _Services.Events.Notifier.EventLink(e.Id)).ToList();
            return new TrellisKit.Server.Services.Ai.AssistantToolResult
            {
                Text = lines.Count == 0 ? "No upcoming events match." : String.Join("\n", lines),
                ResultCount = lines.Count,
                Results = open.Select(e => new TrellisKit.Core.Models.AssistantToolCallResult { TargetType = "Event", TargetId = e.Id, Title = e.Title, Path = "/events/" + e.Id }).ToList()
            };
        }

        #endregion

        #region Private-Methods

        private static string? Arg(System.Text.Json.JsonElement args, string name)
        {
            if (args.ValueKind != System.Text.Json.JsonValueKind.Object || !args.TryGetProperty(name, out System.Text.Json.JsonElement v) || v.ValueKind != System.Text.Json.JsonValueKind.String) return null;
            string text = (v.GetString() ?? String.Empty).Trim();
            return text.Length == 0 ? null : text.Length > 200 ? text.Substring(0, 200) : text;
        }

        private static KnowledgeDocument Knowledge(EventRecord e)
        {
            string when = EventTimeZones.Friendly(e.StartUtc, e.TimeZone, e.IsAllDay) + " (" + e.TimeZone + ")";
            string where = e.Format == EventFormatEnum.Online ? "Online" : String.Join(", ", new[] { e.LocationName, e.Locality, e.Region }.Where(x => !String.IsNullOrWhiteSpace(x)));
            return new KnowledgeDocument
            {
                TargetId = e.Id,
                Title = e.Title,
                Text = e.Title + ". " + when + ". " + where + ". " + (e.Summary ?? String.Empty) + " " + MarkdownNormalizer.ToPlainText(e.Description),
                Audience = e.Audience.ToString()
            };
        }

        private async Task<EventRecord> RequireOpenAsync(ContentActor actor, string targetId, CancellationToken token)
        {
            EventRecord? e = await _Services.Database.Events.Records.ReadAsync(targetId, token).ConfigureAwait(false);
            if (e == null || EventAccess.Read(e, actor) != EventReadEnum.Open) throw ApiErrorException.NotFound("Event");
            return e;
        }

        #endregion
    }
}
