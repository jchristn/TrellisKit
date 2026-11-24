namespace TrellisKit.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Requests;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Moderation console routes (Phase 2 contract sections 9.3 and 10.2, Moderator group: SystemAdmin, or any Moderator
    /// or FeedModerator assignment; per-item scope is checked in the service): actions, the queue, queue detail and
    /// dismiss, a target's reports and actions, the actions log, a member's content history, and the open and overdue
    /// counts.
    /// </summary>
    public class ModerationRoutes
    {
        #region Private-Members

        private const string _Tag = "Moderation";
        private const string _Base = Constants.ApiPrefix + "/admin/moderation";
        private const string _Target = _Base + "/queue/{targetType}/{targetId}";
        private const string _QueueItem = "{ targetType, targetId, targetState, preview: { title, excerpt, hasMedia }, author: UserSummary, reportCount, reporterCount, reasons: { reason: count }, firstReportedUtc, lastReportedUtc, dueUtc, isOverdue, status, scope: { type: Feed | Forum | Assistant, id, name }, labels, tags }";
        private const string _ActionItem = "ModerationActionView { id, moderator: UserSummary | null, targetType, targetId, targetAuthor: UserSummary, action, reason, note, notifiedAuthor, createdUtc, labels, tags }";
        private const string _ReportItem = "ReportView { id, reporter: UserSummary | null, targetType, targetId, reason, details, status, createdUtc, dueUtc, resolvedUtc, resolvedByName, resolutionNote, moderationActionId, labels, tags }";
        private const string _SnapshotItem = "AssistantSnapshotMessage { id, position, role, content, status, isReported, createdUtc, labels, tags }";
        private const string _ContentItem = "{ targetType, targetId, state, feedPostId, excerpt, createdUtc, reactionTotal, reportCount, labels, tags }";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory (resolves each request's property services).</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public ModerationRoutes(PropertyDirectory directory)
        {
            _Directory = directory ?? throw new ArgumentNullException(nameof(directory));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="server">Watson server.</param>
        /// <exception cref="ArgumentNullException">The server is null.</exception>
        public void Register(Webserver server)
        {
            ArgumentNullException.ThrowIfNull(server);
            WatsonWebserver.Core.Routing.RoutingGroup post = server.Routes.PostAuthentication;

            post.Static.Add(HttpMethod.POST, _Base + "/actions", ActAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Take a moderation action", _Tag, "Body { targetType: FeedPost | Comment, targetId, action: Hide | Unhide | Remove | Restore, reason (3 to 500), note?, notifyAuthor? (default true for Remove and Restore), resolveReports? (default true), labels?, tags? (stored on the action) }. " +
                    "Hide needs visible content, Unhide hidden, Remove visible or hidden, Restore removed and not purged. Remove moves served media to the retained bucket; Restore moves it back. FeedModerators act on member content in the Feed but never on SystemAdmin content. Audited as moderation.hide, unhide, remove, restore. Moderator.")
                    .Body("ModerationActionRequest").Returns("{ action: ModerationActionView, target: FeedPost | Comment }").Resp(400, "ValidationFailed").Resp(403, "Forbidden")
                    .Resp(404, "NotFound").Resp(409, "InvalidModerationState"));
            EnumerationRoutes.Add(post, _Base + "/actions", ModerationCollections.Actions, _ActionItem,
                () => Doc("List moderation actions", _Tag, "The actions log, newest first. Filters moderatorUserId (\"system\" for actions without a moderator), targetType, targetId, targetAuthorUserId, action, fromUtc and toUtc (aliases of the created window). Search covers reason and note. Labels and tags filter the actions' own metadata (type ModerationAction). Moderator."),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Moderation.ActionsAsync(actor, query, ctx.Token)));

            EnumerationRoutes.Add(post, _Base + "/queue", ModerationCollections.Queue, _QueueItem,
                () => Doc("List the moderation queue", _Tag, "One row per reported target in the caller's scope. Filters status (Open, the default, or Resolved), targetType (FeedPost, Thread, Comment, User, AssistantMessage, and the Phase 4 types Venue, Instructor, Event, Series, Lesson), reason, overdue (true or false), scopeType (Feed, Forum, Assistant, Directory, Events, or Library; Assistant rows are SystemAdmin only; DirectoryModerator, EventManager, and LibraryEditor holders see only their scope), forumIds (at most 50; comma-separated in the GET form). Each row carries scope { type, id, name } (name is the forum or assistant name). Orderings: DueAscending (default; overdue first), LastReportedDescending, FirstReportedAscending, ReportCountDescending, and the Created orderings (first report). Labels and tags filter the target's own metadata. Exact totals. Moderator."),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Moderation.QueueAsync(actor, query, ctx.Token)));
            post.Parameter.Add(HttpMethod.GET, _Target, DetailAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: TargetDoc(Doc("Read a moderation target", _Tag, "{ targetType, targetId, target: FeedPost | Comment | Thread | UserSummary | AssistantModerationSnapshot (full content, any state; an AssistantMessage target is its retained conversation snapshot, SystemAdmin only), context: { feedPostId, parentCommentId, threadId, forumId }, revisions, reports, actions (previews: { objects (at most 5), totalRecords, enumerate }), authorHistory: { removedCount90d, hiddenCount90d, reportsAgainst90d, isMuted, mutedUntilUtc }, openReports, labels, tags }. Moderator."))
                    .Bounded("ModerationQueueDetail", "revisions, reports, and actions previews of at most 5 objects each with totalRecords and the enumerate path").Resp(400, "ValidationFailed").Resp(403, "Forbidden").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.POST, _Target + "/dismiss", DismissAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: TargetDoc(Doc("Dismiss a target's reports", _Tag, "Body { note?, labels?, tags? } (labels and tags are stored on the Dismiss action). Marks the Open reports in the caller's scope Dismissed (content unchanged; unhide auto-hidden content with an action) and records a Dismiss action and the moderation.dismiss audit record. Returns the detail. Moderator."))
                    .Body("ModerationDismissRequest", false).Returns("ModerationQueueDetail").Resp(400, "ValidationFailed").Resp(403, "Forbidden").Resp(404, "NotFound"));
            EnumerationRoutes.Add(post, _Target + "/reports", ModerationCollections.Reports, _ReportItem,
                () => Doc("List a target's reports", _Tag, "Reports on the target in the caller's scope, newest first. Filters status and reason. Search covers details. Reporters of deleted accounts show as the Deleted member; system reports have a null reporter. Moderator."),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Moderation.ReportsAsync(actor, RouteHelper.Param(ctx, "targetType"), RouteHelper.Param(ctx, "targetId"), query, ctx.Token)),
                "targetType", "targetId");
            EnumerationRoutes.Add(post, _Target + "/snapshot", AiCollections.SnapshotMessages, _SnapshotItem,
                () => Doc("List a reported assistant message's snapshot", _Tag, "targetType must be AssistantMessage. The retained copy of the conversation up to and including the reported or withheld message (docs/AI_CONTRACT.md section 12.3), oldest first; it outlives the conversation until moderation.retainRemovedDays after the report. SystemAdmin."),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Moderation.SnapshotMessagesAsync(actor, RouteHelper.Param(ctx, "targetType"), RouteHelper.Param(ctx, "targetId"), query, ctx.Token)),
                "targetType", "targetId");
            EnumerationRoutes.Add(post, _Target + "/actions", ModerationCollections.Actions, _ActionItem,
                () => Doc("List a target's moderation actions", _Tag, "Moderation actions on the target, newest first (same filters as the actions log except the target). Moderator."),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Moderation.TargetActionsAsync(actor, RouteHelper.Param(ctx, "targetType"), RouteHelper.Param(ctx, "targetId"), query, ctx.Token)),
                "targetType", "targetId");

            EnumerationRoutes.Add(post, _Base + "/content", ModerationCollections.Content, _ContentItem,
                () => Doc("List a member's content", _Tag, "A member's feed posts and comments in any state. Filters authorUserId (required), targetType (FeedPost or Comment), state (Visible, Hidden, Removed, Deleted). Search covers title and body. Labels and tags filter each item's own metadata. Moderator."),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Moderation.ContentAsync(actor, query, ctx.Token)));
            post.Static.Add(HttpMethod.GET, _Base + "/summary", SummaryAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Moderation counts", _Tag, "{ open, overdue, openTargets }: Open reports, Open reports past moderation.reviewTargetHours, and targets with Open reports, in the caller's scope (admin Home tiles). Moderator.")
                    .Returns("ModerationSummary").Resp(403, "Forbidden"));
        }

        #endregion

        #region Private-Methods

        private static OpenApiRouteMetadata TargetDoc(OpenApiRouteMetadata metadata)
        {
            return metadata.PathParam("targetType", "FeedPost, Comment, Thread, User, or AssistantMessage").PathParam("targetId", "Target identifier");
        }

        private async Task<object> RunAsync<T>(HttpContextBase ctx, Func<ContentActor, Task<T>> body) where T : class
        {
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            return await body(actor).ConfigureAwait(false);
        }

        private RequestInfo Info(HttpContextBase ctx)
        {
            return RequestInfo.From(ctx, _Directory.For(ctx).Settings.Rest);
        }

        private async Task ActAsync(HttpContextBase ctx)
        {
            ModerationActionRequest request = RouteHelper.ReadJson<ModerationActionRequest>(ctx);
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Moderation.ActAsync(actor, Info(ctx), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DetailAsync(HttpContextBase ctx)
        {
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Moderation.DetailAsync(actor, RouteHelper.Param(ctx, "targetType"), RouteHelper.Param(ctx, "targetId"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DismissAsync(HttpContextBase ctx)
        {
            ModerationDismissRequest request = RouteHelper.ReadJson<ModerationDismissRequest>(ctx);
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Moderation.DismissAsync(actor, Info(ctx), RouteHelper.Param(ctx, "targetType"), RouteHelper.Param(ctx, "targetId"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task SummaryAsync(HttpContextBase ctx)
        {
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Moderation.SummaryAsync(actor, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        #endregion
    }
}
