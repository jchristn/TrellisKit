namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Collections.Generic;
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
    /// Conversations, messages, and feedback (docs/AI_CONTRACT.md sections 6, 9, and 14.3). Members and visitors see only
    /// their own conversations (404 otherwise, never 403). Administrators can read every conversation (resolved decision
    /// 7, disclosed in the chat UI and the privacy policy); every administrator read of a conversation is audited as
    /// ai.conversation.read.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class ConversationService
    {
        #region Private-Members

        private static readonly string[] _Reasons = new[] { "Incorrect", "Unhelpful", "Unsafe", "Other" };
        private readonly AiServices _Ai;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="ai">AI services of the property.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public ConversationService(AiServices ai)
        {
            _Ai = ai ?? throw new ArgumentNullException(nameof(ai));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create a conversation for the caller (rate limited by rateLimits.aiConversationsPerHour).
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="assistant">Assistant the caller may use.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Conversation.</returns>
        public async Task<AssistantConversation> CreateAsync(AssistantCaller caller, Assistant assistant, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(caller);
            ArgumentNullException.ThrowIfNull(assistant);
            if (!caller.IsSystemAdmin) _Ai.App.Limits.Enforce("ai-conversations:" + (caller.UserId ?? caller.VisitorId), _Ai.App.Settings.Ai.ConversationsPerHour, RateLimitService.Hour);
            DateTime now = DateTime.UtcNow;
            AssistantConversation conversation = new AssistantConversation
            {
                PropertyId = _Ai.App.PropertyId,
                Id = IdGenerator.Generate(AiConstants.ConversationIdPrefix),
                AssistantId = assistant.Id,
                UserId = caller.UserId,
                VisitorId = caller.UserId == null ? caller.VisitorId : null,
                Surface = caller.Surface,
                CreatedUtc = now,
                UpdatedUtc = now
            };
            await _Ai.App.Database.Ai.Conversations.CreateConversationAsync(conversation, token).ConfigureAwait(false);
            TrellisKitTelemetry.Add(AiTelemetry.ConversationsCreated, 1, "surface", AiTelemetry.Label(caller.Surface.ToString()));
            return conversation;
        }

        /// <summary>
        /// The caller's conversations (optionally for one assistant).
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="assistantId">Assistant, or null for every assistant (filters.assistantId).</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        public async Task<EnumerationResult<ConversationView>> EnumerateAsync(AssistantCaller caller, string? assistantId, EnumerationQuery? query, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(caller);
            _Ai.EnsureAvailable();
            if (caller.UserId != null) _Ai.App.Media.EnforceRead(caller.Actor);
            string owner = caller.UserId ?? caller.VisitorId ?? String.Empty;
            EnumerationRequest request = _Ai.App.Enumeration.Prepare(query, AiCollections.Conversations, false, owner + "|" + (assistantId ?? String.Empty));
            Dictionary<string, string> errors = new Dictionary<string, string>();
            AiEnumerationFilter filter = new AiEnumerationFilter
            {
                AssistantId = assistantId ?? request.Query.FilterString("assistantId"),
                Archived = request.Query.FilterBool("archived", errors),
                UserId = caller.UserId,
                VisitorId = caller.UserId == null ? caller.VisitorId ?? "none" : null
            };
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            EnumerationResult<AssistantConversation> page = await _Ai.App.Database.Ai.Conversations.EnumerateConversationsAsync(request, filter, token).ConfigureAwait(false);
            return page.With(await ViewsAsync(page.Objects, false, token).ConfigureAwait(false));
        }

        /// <summary>
        /// Read a conversation the caller owns, with its 20 newest messages.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="id">Conversation id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        public async Task<ConversationView> GetAsync(AssistantCaller caller, string id, CancellationToken token)
        {
            AssistantConversation conversation = await RequireOwnedAsync(caller, id, token).ConfigureAwait(false);
            if (caller.UserId != null) _Ai.App.Media.EnforceRead(caller.Actor);
            return await ViewAsync(conversation, true, false, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Read a conversation owned by the caller or throw 404.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="id">Conversation id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Conversation.</returns>
        public async Task<AssistantConversation> RequireOwnedAsync(AssistantCaller caller, string id, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(caller);
            _Ai.EnsureAvailable();
            if (String.IsNullOrWhiteSpace(id) || !id.StartsWith(AiConstants.ConversationIdPrefix, StringComparison.Ordinal)) throw ApiErrorException.NotFound("Conversation");
            AssistantConversation? conversation = await _Ai.App.Database.Ai.Conversations.ReadConversationAsync(id, token).ConfigureAwait(false);
            if (conversation == null) throw ApiErrorException.NotFound("Conversation");
            bool owner = (caller.UserId != null && conversation.UserId == caller.UserId) || (caller.UserId == null && caller.VisitorId != null && conversation.VisitorId == caller.VisitorId);
            if (!owner) throw ApiErrorException.NotFound("Conversation");
            return conversation;
        }

        /// <summary>
        /// Rename, archive, or relabel a conversation.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="id">Conversation id.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        public async Task<ConversationView> UpdateAsync(AssistantCaller caller, string id, ConversationUpdateRequest request, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(request);
            AssistantConversation conversation = await RequireOwnedAsync(caller, id, token).ConfigureAwait(false);
            if (request.Title != null)
            {
                string title = request.Title.Trim();
                if (title.Length < 1 || title.Length > 120) throw ApiErrorException.Field("title", "Must be 1 to 120 characters.");
                await _Ai.App.WordFilter.CheckAsync(caller.Actor, TargetTypeEnum.AssistantConversation, new Dictionary<string, string?> { { "title", title } }, token).ConfigureAwait(false);
                conversation.Title = title;
                conversation.TitleSource = AssistantTitleSourceEnum.Member;
            }
            if (request.IsArchived != null) conversation.IsArchived = request.IsArchived.Value;
            conversation.UpdatedUtc = DateTime.UtcNow;
            await _Ai.App.Database.Ai.Conversations.UpdateConversationAsync(conversation, token).ConfigureAwait(false);
            if (request.Labels != null || request.Tags != null)
            {
                if (caller.UserId == null) throw ApiErrorException.Field("labels", "Visitors cannot label conversations.");
                await _Ai.App.Metadata.ApplyAsync(caller.Actor, TargetTypeEnum.AssistantConversation, conversation.Id, request.Labels, request.Tags, token).ConfigureAwait(false);
            }
            return await ViewAsync(conversation, false, false, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Hard-delete a conversation (messages, citations, and stages; feedback keeps its copies).
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="id">Conversation id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task DeleteAsync(AssistantCaller caller, string id, CancellationToken token)
        {
            AssistantConversation conversation = await RequireOwnedAsync(caller, id, token).ConfigureAwait(false);
            await _Ai.App.Database.Ai.Conversations.DeleteConversationAsync(conversation.Id, token).ConfigureAwait(false);
            if (caller.UserId != null) await _Ai.App.Metadata.ApplyAsync(new ContentActor { IsSystemAdmin = true, IsStaff = true }, TargetTypeEnum.AssistantConversation, conversation.Id, new List<string>(), new Dictionary<string, string>(), token).ConfigureAwait(false);
        }

        /// <summary>
        /// Enumerate a conversation's messages for its owner.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="id">Conversation id.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        public async Task<EnumerationResult<MessageView>> EnumerateMessagesAsync(AssistantCaller caller, string id, EnumerationQuery? query, CancellationToken token)
        {
            AssistantConversation conversation = await RequireOwnedAsync(caller, id, token).ConfigureAwait(false);
            if (caller.UserId != null) _Ai.App.Media.EnforceRead(caller.Actor);
            EnumerationRequest request = _Ai.App.Enumeration.Prepare(query, AiCollections.Messages, false, conversation.Id);
            EnumerationResult<AssistantMessage> page = await _Ai.App.Database.Ai.Conversations.EnumerateMessagesAsync(request, conversation.Id, token).ConfigureAwait(false);
            return page.With(await MessageViewsAsync(page.Objects, false, token).ConfigureAwait(false));
        }

        /// <summary>
        /// Read one message of a conversation the caller owns (used to resume after a dropped stream).
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="id">Conversation id.</param>
        /// <param name="messageId">Message id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        public async Task<MessageView> GetMessageAsync(AssistantCaller caller, string id, string messageId, CancellationToken token)
        {
            AssistantMessage message = await RequireMessageAsync(caller, id, messageId, token).ConfigureAwait(false);
            return (await MessageViewsAsync(new List<AssistantMessage> { message }, false, token).ConfigureAwait(false))[0];
        }

        /// <summary>
        /// Read a message of an owned conversation or throw 404.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="id">Conversation id.</param>
        /// <param name="messageId">Message id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Message.</returns>
        public async Task<AssistantMessage> RequireMessageAsync(AssistantCaller caller, string id, string messageId, CancellationToken token)
        {
            AssistantConversation conversation = await RequireOwnedAsync(caller, id, token).ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(messageId) || !messageId.StartsWith(AiConstants.MessageIdPrefix, StringComparison.Ordinal)) throw ApiErrorException.NotFound("Message");
            AssistantMessage? message = await _Ai.App.Database.Ai.Conversations.ReadMessageAsync(messageId, token).ConfigureAwait(false);
            if (message == null || message.ConversationId != conversation.Id) throw ApiErrorException.NotFound("Message");
            return message;
        }

        /// <summary>
        /// Rate a reply (the rated pair is copied for administrators).
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="id">Conversation id.</param>
        /// <param name="messageId">Reply id.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The reply with its feedback.</returns>
        public async Task<MessageView> SetFeedbackAsync(AssistantCaller caller, string id, string messageId, MessageFeedbackRequest request, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(request);
            AssistantMessage reply = await RequireMessageAsync(caller, id, messageId, token).ConfigureAwait(false);
            if (reply.Role != AssistantMessageRoleEnum.Assistant) throw ApiErrorException.Field("messageId", "Only replies can be rated.");
            Dictionary<string, string> errors = new Dictionary<string, string>();
            if (request.Rating == null) errors["rating"] = "Up or Down.";
            string? note = String.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
            if (note != null && note.Length > 1000) errors["note"] = "Must be at most 1000 characters.";
            List<string> reasons = (request.Reasons ?? new List<string>()).Distinct().ToList();
            if (reasons.Any(r => !_Reasons.Contains(r))) errors["reasons"] = "Each reason must be one of: " + String.Join(", ", _Reasons) + ".";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            if (!caller.IsSystemAdmin) _Ai.App.Limits.Enforce("ai-feedback:" + (caller.UserId ?? caller.VisitorId), _Ai.App.Settings.Ai.FeedbackPerHour, RateLimitService.Hour);
            if (note != null) await _Ai.App.WordFilter.CheckAsync(caller.Actor, TargetTypeEnum.AssistantMessage, new Dictionary<string, string?> { { "note", note } }, token).ConfigureAwait(false);
            AssistantMessage? prompt = reply.ReplyToMessageId == null ? null : await _Ai.App.Database.Ai.Conversations.ReadMessageAsync(reply.ReplyToMessageId, token).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            AssistantFeedback? existing = await _Ai.App.Database.Ai.Conversations.ReadFeedbackByMessageAsync(reply.Id, token).ConfigureAwait(false);
            await _Ai.App.Database.Ai.Conversations.UpsertFeedbackAsync(new AssistantFeedback
            {
                Id = existing?.Id ?? IdGenerator.Generate(AiConstants.FeedbackIdPrefix),
                MessageId = reply.Id,
                ConversationId = reply.ConversationId,
                AssistantId = reply.AssistantId,
                UserId = caller.UserId,
                VisitorId = caller.UserId == null ? caller.VisitorId : null,
                Rating = request.Rating!.Value,
                Note = note,
                Reasons = reasons,
                PromptCopy = prompt?.Content ?? String.Empty,
                ReplyCopy = reply.Content,
                CreatedUtc = existing?.CreatedUtc ?? now,
                UpdatedUtc = now
            }, token).ConfigureAwait(false);
            TrellisKitTelemetry.Add(AiTelemetry.Feedback, 1, "rating", request.Rating.Value.ToString().ToLowerInvariant());
            return (await MessageViewsAsync(new List<AssistantMessage> { reply }, false, token).ConfigureAwait(false))[0];
        }

        /// <summary>
        /// Remove the caller's feedback on a reply.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="id">Conversation id.</param>
        /// <param name="messageId">Reply id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The reply.</returns>
        public async Task<MessageView> ClearFeedbackAsync(AssistantCaller caller, string id, string messageId, CancellationToken token)
        {
            AssistantMessage reply = await RequireMessageAsync(caller, id, messageId, token).ConfigureAwait(false);
            await _Ai.App.Database.Ai.Conversations.DeleteFeedbackByMessageAsync(reply.Id, token).ConfigureAwait(false);
            return (await MessageViewsAsync(new List<AssistantMessage> { reply }, false, token).ConfigureAwait(false))[0];
        }

        /// <summary>
        /// Administrator conversation browser (filters assistantId, userId, visitorId, ownerType, status, archived,
        /// surface).
        /// </summary>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page with owners.</returns>
        public async Task<EnumerationResult<ConversationView>> EnumerateAdminAsync(EnumerationQuery? query, CancellationToken token)
        {
            _Ai.EnsureAvailable();
            EnumerationRequest request = _Ai.App.Enumeration.Prepare(query, AiCollections.AdminConversations, true);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            AiEnumerationFilter filter = new AiEnumerationFilter
            {
                AssistantId = request.Query.FilterString("assistantId"),
                UserId = request.Query.FilterString("userId"),
                VisitorId = request.Query.FilterString("visitorId"),
                OwnerType = request.Query.FilterString("ownerType"),
                Status = request.Query.FilterEnum<AssistantConversationStatusEnum>("status", errors),
                Archived = request.Query.FilterBool("archived", errors),
                Surface = request.Query.FilterEnum<AiConversationSurfaceEnum>("surface", errors)
            };
            if (filter.OwnerType != null && filter.OwnerType != "Member" && filter.OwnerType != "Visitor") errors["filters.ownerType"] = "Must be Member or Visitor.";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            EnumerationResult<AssistantConversation> page = await _Ai.App.Database.Ai.Conversations.EnumerateConversationsAsync(request, filter, token).ConfigureAwait(false);
            return page.With(await ViewsAsync(page.Objects, true, token).ConfigureAwait(false));
        }

        /// <summary>
        /// Administrator read of a conversation with its newest messages and full telemetry. Audited as
        /// ai.conversation.read.
        /// </summary>
        /// <param name="id">Conversation id.</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        public async Task<ConversationView> GetAdminAsync(string id, RequestInfo info, CancellationToken token)
        {
            AssistantConversation conversation = await RequireAnyAsync(id, token).ConfigureAwait(false);
            await _Ai.App.Audit.WriteAsync(info, "ai.conversation.read", "AssistantConversation", conversation.Id, "owner " + (conversation.UserId ?? conversation.VisitorId), token).ConfigureAwait(false);
            TrellisKitTelemetry.Add(AiTelemetry.AdminConversationReads, 1);
            return await ViewAsync(conversation, true, true, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Administrator enumeration of a conversation's messages with full telemetry. Audited as
        /// ai.conversation.read on the first page.
        /// </summary>
        /// <param name="id">Conversation id.</param>
        /// <param name="query">Query.</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        public async Task<EnumerationResult<MessageView>> EnumerateAdminMessagesAsync(string id, EnumerationQuery? query, RequestInfo info, CancellationToken token)
        {
            AssistantConversation conversation = await RequireAnyAsync(id, token).ConfigureAwait(false);
            EnumerationRequest request = _Ai.App.Enumeration.Prepare(query, AiCollections.AdminMessages, true, conversation.Id);
            if (!request.HasPosition) await _Ai.App.Audit.WriteAsync(info, "ai.conversation.read", "AssistantConversation", conversation.Id, "messages", token).ConfigureAwait(false);
            EnumerationResult<AssistantMessage> page = await _Ai.App.Database.Ai.Conversations.EnumerateMessagesAsync(request, conversation.Id, token).ConfigureAwait(false);
            return page.With(await MessageViewsAsync(page.Objects, true, token).ConfigureAwait(false));
        }

        /// <summary>
        /// Feedback queue (filters assistantId, rating, hasNote, reviewed).
        /// </summary>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        public async Task<EnumerationResult<AdminFeedbackView>> EnumerateFeedbackAsync(EnumerationQuery? query, CancellationToken token)
        {
            _Ai.EnsureAvailable();
            EnumerationRequest request = _Ai.App.Enumeration.Prepare(query, AiCollections.Feedback, true);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            AiEnumerationFilter filter = new AiEnumerationFilter
            {
                AssistantId = request.Query.FilterString("assistantId"),
                Rating = request.Query.FilterEnum<AssistantFeedbackRatingEnum>("rating", errors),
                HasNote = request.Query.FilterBool("hasNote", errors),
                Reviewed = request.Query.FilterBool("reviewed", errors)
            };
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            EnumerationResult<AssistantFeedback> page = await _Ai.App.Database.Ai.Conversations.EnumerateFeedbackAsync(request, filter, token).ConfigureAwait(false);
            List<Assistant> assistants = await _Ai.App.Database.Ai.Assistants.ReadManyAsync(page.Objects.Select(f => f.AssistantId), token).ConfigureAwait(false);
            Dictionary<string, UserSummary> members = await _Ai.App.Summaries.BuildAsync(page.Objects.Select(f => f.UserId), token).ConfigureAwait(false);
            return page.With(page.Objects.Select(f =>
            {
                Assistant? a = assistants.FirstOrDefault(x => x.Id == f.AssistantId);
                return new AdminFeedbackView
                {
                    Id = f.Id,
                    Assistant = new AiAssistantRef { Id = f.AssistantId, Name = a?.Name ?? String.Empty, Deleted = a == null || a.DeletedUtc != null },
                    Rating = f.Rating,
                    Reasons = f.Reasons,
                    Note = f.Note,
                    Reviewed = f.Reviewed,
                    ReviewedBy = f.ReviewedByUserId,
                    ReviewedUtc = f.ReviewedUtc,
                    ConversationId = f.ConversationId,
                    MessageId = f.MessageId,
                    ReplyContent = f.ReplyCopy,
                    PromptContent = f.PromptCopy,
                    Member = f.UserId != null && members.TryGetValue(f.UserId, out UserSummary? s) ? s : null,
                    CreatedUtc = f.CreatedUtc
                };
            }).ToList());
        }

        /// <summary>
        /// Mark feedback reviewed. Audited as ai.feedback.review.
        /// </summary>
        /// <param name="actor">Reviewer.</param>
        /// <param name="id">Feedback id.</param>
        /// <param name="request">Optional note.</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task ReviewFeedbackAsync(ContentActor actor, string id, FeedbackReviewRequest? request, RequestInfo info, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Ai.EnsureAvailable();
            if (String.IsNullOrWhiteSpace(id) || !id.StartsWith(AiConstants.FeedbackIdPrefix, StringComparison.Ordinal) ||
                !await _Ai.App.Database.Ai.Conversations.ReviewFeedbackAsync(id, actor.UserId, DateTime.UtcNow, token).ConfigureAwait(false))
                throw ApiErrorException.NotFound("Feedback");
            string? note = request?.Note;
            await _Ai.App.Audit.WriteAsync(info, "ai.feedback.review", "AssistantFeedback", id, String.IsNullOrWhiteSpace(note) ? null : (note.Length > 500 ? note.Substring(0, 500) : note), token).ConfigureAwait(false);
        }

        /// <summary>
        /// View of one conversation.
        /// </summary>
        /// <param name="conversation">Conversation.</param>
        /// <param name="includeRecent">Include the 20 newest messages.</param>
        /// <param name="admin">Administrator view (owner and telemetry).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        public async Task<ConversationView> ViewAsync(AssistantConversation conversation, bool includeRecent, bool admin, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(conversation);
            ConversationView view = (await ViewsAsync(new List<AssistantConversation> { conversation }, admin, token).ConfigureAwait(false))[0];
            if (!includeRecent) return view;
            List<AssistantMessage> recent = await _Ai.App.Database.Ai.Conversations.RecentMessagesAsync(conversation.Id, AiConstants.RecentMessages, token).ConfigureAwait(false);
            view.RecentMessages = new MessagePageView
            {
                Objects = await MessageViewsAsync(recent, admin, token).ConfigureAwait(false),
                TotalRecords = conversation.MessageCount,
                Enumerate = Constants.ApiPrefix + (admin ? "/admin/ai/conversations/" : "/assistant-conversations/") + conversation.Id + "/messages/enumerate"
            };
            return view;
        }

        /// <summary>
        /// Views of messages with feedback (and telemetry for administrators).
        /// </summary>
        /// <param name="messages">Messages.</param>
        /// <param name="admin">Administrator view.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Views in the same order.</returns>
        public async Task<List<MessageView>> MessageViewsAsync(List<AssistantMessage> messages, bool admin, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(messages);
            List<MessageView> views = new List<MessageView>();
            bool details = admin || _Ai.App.Settings.Ai.ShowModelDetailsToMembers;
            foreach (AssistantMessage m in messages)
            {
                AssistantFeedback? feedback = m.Role == AssistantMessageRoleEnum.Assistant && m.Status == AssistantMessageStatusEnum.Completed
                    ? await _Ai.App.Database.Ai.Conversations.ReadFeedbackByMessageAsync(m.Id, token).ConfigureAwait(false)
                    : null;
                views.Add(MessageView(m, feedback, details, admin, _Ai.Tools));
            }
            return views;
        }

        /// <summary>
        /// View of one message.
        /// </summary>
        /// <param name="m">Message.</param>
        /// <param name="feedback">Feedback, or null.</param>
        /// <param name="details">Include model, usage, and timings.</param>
        /// <param name="admin">Include full telemetry (and tool arguments, output summaries, and errors).</param>
        /// <param name="tools">Tool registry for activity labels, or null.</param>
        /// <returns>View.</returns>
        public static MessageView MessageView(AssistantMessage m, AssistantFeedback? feedback, bool details, bool admin, AssistantToolRegistry? tools = null)
        {
            ArgumentNullException.ThrowIfNull(m);
            bool reply = m.Role == AssistantMessageRoleEnum.Assistant;
            MessageView view = new MessageView
            {
                Id = m.Id,
                ConversationId = m.ConversationId,
                Role = m.Role,
                Content = m.Content,
                Status = m.Status,
                IsAiGenerated = reply,
                Reasoning = m.Reasoning,
                Citations = m.Citations.Select(c => new CitationView { Index = c.Index, TargetType = c.TargetType, TargetId = c.TargetId, Title = c.Title, Path = c.Path }).ToList(),
                ToolCalls = ToolCallsView(m, admin, tools),
                Feedback = feedback == null ? null : new MessageFeedbackView { Rating = feedback.Rating, Note = feedback.Note, Reasons = feedback.Reasons, CreatedUtc = feedback.CreatedUtc },
                FinishReason = reply ? m.FinishReason : null,
                ErrorCode = m.ErrorCode,
                IsSuperseded = m.IsSuperseded,
                ClientMessageId = m.ClientMessageId,
                CreatedUtc = m.CreatedUtc,
                CompletedUtc = m.CompletedUtc
            };
            if (reply && details)
            {
                view.Model = new MessageModelView { EndpointId = m.EndpointId, EndpointName = m.EndpointName, Model = m.Model, UsedFallback = m.UsedFallback };
                view.Usage = new AiTokenUsageView { PromptTokens = m.PromptTokens, CompletionTokens = m.CompletionTokens, ReasoningTokens = m.ReasoningTokens, TotalTokens = m.PromptTokens + m.CompletionTokens + m.ReasoningTokens };
                view.LatencyMs = m.LatencyMs;
                view.TimeToFirstTokenMs = m.TimeToFirstTokenMs;
            }
            if (admin && reply)
            {
                view.Telemetry = new MessageTelemetryView
                {
                    TraceId = m.TraceId,
                    ReplyToMessageId = m.ReplyToMessageId,
                    RetrievalQuery = m.RetrievalQuery,
                    TimeToLastTokenMs = m.TimeToLastTokenMs,
                    TokensPerSecond = m.TokensPerSecond == null ? null : Math.Round(m.TokensPerSecond.Value, 2),
                    CostUsd = AiUsageService.Usd(m.CostMicros),
                    ModerationVerdict = m.ModerationVerdict,
                    ModerationCategory = m.ModerationCategory,
                    Stages = m.Stages.Select(s => new MessageStageView { Stage = s.Stage, EndpointId = s.EndpointId, Model = s.Model, StartedUtc = s.StartedUtc, DurationMs = s.DurationMs, Success = s.Success, Detail = s.Detail, PromptTokens = s.PromptTokens, CompletionTokens = s.CompletionTokens }).ToList(),
                    RetrievedItems = m.Citations.Select(c => new CitationScoreView { Index = c.Index, TargetType = c.TargetType, TargetId = c.TargetId, Title = c.Title, Score = c.Score }).ToList()
                };
            }
            if (admin && !reply && m.ModerationVerdict != null)
                view.Telemetry = new MessageTelemetryView { ModerationVerdict = m.ModerationVerdict, ModerationCategory = m.ModerationCategory };
            return view;
        }

        /// <summary>
        /// The bounded tool call preview of a reply (section 9.2): labels, status, counts, durations, and result links
        /// for everyone; arguments, the output summary, size, and errors for administrators.
        /// </summary>
        /// <param name="m">Message.</param>
        /// <param name="admin">Administrator view.</param>
        /// <param name="tools">Tool registry for labels, or null.</param>
        /// <returns>Preview.</returns>
        public static ToolCallPageView ToolCallsView(AssistantMessage m, bool admin, AssistantToolRegistry? tools)
        {
            ArgumentNullException.ThrowIfNull(m);
            List<ToolCallView> calls = m.ToolCalls.Take(AiConstants.MaxToolCallsPerReply).Select(c =>
            {
                AssistantTool? tool = tools?.Find(c.ToolName);
                return new ToolCallView
                {
                    Id = c.Id,
                    ToolName = c.ToolName,
                    Label = AssistantToolRegistry.RunningLabel(tool, c.ToolName),
                    CompletedLabel = AssistantToolRegistry.DoneLabel(tool, c.ToolName),
                    Status = c.Status,
                    ResultCount = c.ResultCount,
                    DurationMs = c.DurationMs,
                    Round = c.Round,
                    Results = c.Results.Select(r => new ToolCallResultView { TargetType = r.TargetType, TargetId = r.TargetId, Title = r.Title, Path = r.Path }).ToList(),
                    ArgumentsJson = admin ? c.ArgumentsJson : null,
                    ResultSummary = admin ? c.ResultSummary : null,
                    ResultCharacters = admin ? c.ResultCharacters : null,
                    Error = admin ? c.Error : null,
                    CreatedUtc = c.CreatedUtc
                };
            }).ToList();
            return new ToolCallPageView { Objects = calls, TotalRecords = m.ToolCalls.Count };
        }

        #endregion

        #region Private-Methods

        private async Task<AssistantConversation> RequireAnyAsync(string id, CancellationToken token)
        {
            _Ai.EnsureAvailable();
            if (String.IsNullOrWhiteSpace(id) || !id.StartsWith(AiConstants.ConversationIdPrefix, StringComparison.Ordinal)) throw ApiErrorException.NotFound("Conversation");
            AssistantConversation? conversation = await _Ai.App.Database.Ai.Conversations.ReadConversationAsync(id, token).ConfigureAwait(false);
            return conversation ?? throw ApiErrorException.NotFound("Conversation");
        }

        private async Task<List<ConversationView>> ViewsAsync(List<AssistantConversation> conversations, bool admin, CancellationToken token)
        {
            List<Assistant> assistants = await _Ai.App.Database.Ai.Assistants.ReadManyAsync(conversations.Select(c => c.AssistantId), token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> metadata = await _Ai.App.Metadata.LoadAsync(TargetTypeEnum.AssistantConversation, conversations.Select(c => c.Id), admin, token).ConfigureAwait(false);
            Dictionary<string, UserSummary> members = admin ? await _Ai.App.Summaries.BuildAsync(conversations.Select(c => c.UserId), token).ConfigureAwait(false) : new Dictionary<string, UserSummary>();
            List<ConversationView> views = new List<ConversationView>();
            foreach (AssistantConversation c in conversations)
            {
                Assistant? a = assistants.FirstOrDefault(x => x.Id == c.AssistantId);
                bool available = a != null && a.DeletedUtc == null && a.Enabled;
                ConversationView view = new ConversationView
                {
                    Id = c.Id,
                    Assistant = new ConversationAssistantView { Id = c.AssistantId, Title = a?.Title ?? String.Empty, Avatar = a == null ? new AssistantAvatarView() : AssistantService.Avatar(a), Available = available },
                    Title = c.Title,
                    MessageCount = c.MessageCount,
                    LastMessageUtc = c.LastMessageUtc,
                    LastMessagePreview = String.IsNullOrEmpty(c.LastMessagePreview) ? c.LastMessagePreview : MarkdownNormalizer.Excerpt(c.LastMessagePreview),
                    IsArchived = c.IsArchived,
                    Status = available ? c.Status : AssistantConversationStatusEnum.AssistantUnavailable,
                    Surface = c.Surface,
                    Labels = metadata.TryGetValue(c.Id, out ObjectMetadata? m) ? m.Labels : new List<string>(),
                    Tags = m?.Tags ?? new Dictionary<string, string>(),
                    CreatedUtc = c.CreatedUtc,
                    UpdatedUtc = c.UpdatedUtc
                };
                if (admin)
                    view.Owner = new ConversationOwnerView
                    {
                        Type = c.UserId != null ? "Member" : "Visitor",
                        UserId = c.UserId,
                        VisitorId = c.VisitorId,
                        Member = c.UserId != null && members.TryGetValue(c.UserId, out UserSummary? s) ? s : null
                    };
                views.Add(view);
            }
            return views;
        }

        #endregion
    }
}
