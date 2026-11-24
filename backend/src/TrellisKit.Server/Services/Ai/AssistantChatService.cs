namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using PolyPrompt.Models;
    using TrellisKit.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// The chat pipeline (docs/AI_CONTRACT.md sections 6.3, 9, 10, 12, and 13): the checks in contract order (audience,
    /// mute, reply in progress, conversation full, daily allowance, budget, rate limits, length, word filter, moderation
    /// model), storage of the member message and a pending reply, then generation detached from the request (a dropped
    /// connection does not stop it; the cancel route does): query rewrite, knowledge retrieval, history trimmed to the
    /// context window, the chat model with fallback, output moderation, citations, persistence of the full telemetry,
    /// usage and cost, and the conversation title.
    /// Thread safety: safe to call concurrently; one reply generates per conversation at a time.
    /// </summary>
    public class AssistantChatService
    {
        #region Private-Members

        private const int _HistoryMessages = 200;
        private readonly AiServices _Ai;
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _InFlight = new ConcurrentDictionary<string, CancellationTokenSource>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, byte> _BusyConversations = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="ai">AI services of the property.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public AssistantChatService(AiServices ai)
        {
            _Ai = ai ?? throw new ArgumentNullException(nameof(ai));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run the checks and store the member message with a pending reply (section 6.3). Errors are thrown before any
        /// response is started, so they reach the client as JSON errors.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="conversationId">Conversation id.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Prepared exchange.</returns>
        /// <exception cref="ApiErrorException">401, 402, 403 Muted, 404, 409 ReplyInProgress, 409 ConversationFull, 429
        /// AssistantLimitReached, 429 RateLimited, 400 MessageTooLong, 400 ContentFiltered, 400 MessageBlocked, 400 OutOfScope, 503 BudgetExceeded.</exception>
        public async Task<PreparedExchange> PrepareAsync(AssistantCaller caller, string conversationId, MessageSendRequest request, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(caller);
            ArgumentNullException.ThrowIfNull(request);
            AssistantConversation conversation = await _Ai.Conversations.RequireOwnedAsync(caller, conversationId, token).ConfigureAwait(false);
            Assistant assistant = await _Ai.Assistants.RequireUsableAsync(caller, conversation.AssistantId, token).ConfigureAwait(false);
            return await PrepareCoreAsync(caller, assistant, conversation, request, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Create a conversation and prepare its first message in one call.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="assistant">Assistant the caller may use.</param>
        /// <param name="conversation">The new conversation.</param>
        /// <param name="request">Message.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Prepared exchange.</returns>
        public Task<PreparedExchange> PrepareFirstAsync(AssistantCaller caller, Assistant assistant, AssistantConversation conversation, MessageSendRequest request, CancellationToken token)
        {
            return PrepareCoreAsync(caller, assistant, conversation, request, token);
        }

        /// <summary>
        /// Prepare a regenerated reply: the latest reply of the conversation is superseded by a new pending reply to the
        /// same member message (section 6.2).
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="conversationId">Conversation id.</param>
        /// <param name="messageId">Reply to replace.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Prepared exchange.</returns>
        /// <exception cref="ApiErrorException">404, 409 when it is not the latest reply or a reply is in progress.</exception>
        public async Task<PreparedExchange> PrepareRegenerateAsync(AssistantCaller caller, string conversationId, string messageId, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(caller);
            AssistantMessage old = await _Ai.Conversations.RequireMessageAsync(caller, conversationId, messageId, token).ConfigureAwait(false);
            AssistantConversation conversation = await _Ai.Conversations.RequireOwnedAsync(caller, conversationId, token).ConfigureAwait(false);
            Assistant assistant = await _Ai.Assistants.RequireUsableAsync(caller, conversation.AssistantId, token).ConfigureAwait(false);
            EnsureNotMuted(caller);
            if (old.Role != AssistantMessageRoleEnum.Assistant || old.IsSuperseded || old.ReplyToMessageId == null) throw new ApiErrorException(409, "Conflict", "Only the latest reply can be regenerated.");
            List<AssistantMessage> recent = await _Ai.App.Database.Ai.Conversations.RecentMessagesAsync(conversation.Id, 4, token).ConfigureAwait(false);
            AssistantMessage? latest = recent.LastOrDefault(m => m.Role == AssistantMessageRoleEnum.Assistant && !m.IsSuperseded);
            if (latest == null || latest.Id != old.Id) throw new ApiErrorException(409, "Conflict", "Only the latest reply can be regenerated.");
            if (_BusyConversations.ContainsKey(conversation.Id) || await _Ai.App.Database.Ai.Conversations.HasInFlightAsync(conversation.Id, token).ConfigureAwait(false))
                throw new ApiErrorException(409, "ReplyInProgress", "A reply is still being written. Wait for it to finish.");
            await _Ai.Usage.EnforceBudgetAsync(assistant, token).ConfigureAwait(false);
            EnforceMessageRate(caller);
            AssistantMessage? user = await _Ai.App.Database.Ai.Conversations.ReadMessageAsync(old.ReplyToMessageId, token).ConfigureAwait(false);
            if (user == null) throw ApiErrorException.NotFound("Message");
            AssistantMessage reply = NewReply(conversation, assistant, user);
            await _Ai.App.Database.Ai.Conversations.AddRegeneratedReplyAsync(old.Id, reply, token).ConfigureAwait(false);
            return new PreparedExchange
            {
                Caller = caller,
                Assistant = assistant,
                Endpoints = await _Ai.Assistants.EndpointsAsync(assistant, token).ConfigureAwait(false),
                Conversation = conversation,
                UserMessage = user,
                Reply = reply,
                History = await HistoryAsync(conversation.Id, user.Id, token).ConfigureAwait(false)
            };
        }

        /// <summary>
        /// Prepare an admin test exchange (nothing is stored; usage is marked test).
        /// </summary>
        /// <param name="caller">Administrator caller.</param>
        /// <param name="assistant">Assistant (possibly a draft copy).</param>
        /// <param name="request">Messages.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Prepared exchange.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 429.</exception>
        public async Task<PreparedExchange> PrepareTestAsync(AssistantCaller caller, Assistant assistant, AssistantTestRequest request, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(caller);
            ArgumentNullException.ThrowIfNull(assistant);
            ArgumentNullException.ThrowIfNull(request);
            List<AssistantTestMessage> messages = (request.Messages ?? new List<AssistantTestMessage>()).Where(m => !String.IsNullOrWhiteSpace(m.Content)).ToList();
            if (messages.Count < 1 || messages.Count > AiConstants.MaxTestMessages) throw ApiErrorException.Field("messages", "1 to " + AiConstants.MaxTestMessages + " messages.");
            if (!String.Equals(messages[messages.Count - 1].Role, "user", StringComparison.OrdinalIgnoreCase)) throw ApiErrorException.Field("messages", "The last message must be from the user.");
            _Ai.App.Limits.Enforce("ai-assistant-tests:" + (caller.UserId ?? "admin-token"), _Ai.App.Settings.Ai.AssistantTestsPerHour, RateLimitService.Hour);
            DateTime now = DateTime.UtcNow;
            AssistantMessage user = new AssistantMessage { Id = "acm_test_user", ConversationId = "acv_test", AssistantId = assistant.Id, Role = AssistantMessageRoleEnum.User, Content = messages[messages.Count - 1].Content!.Trim(), CreatedUtc = now };
            AssistantMessage reply = new AssistantMessage { Id = IdGenerator.Generate(AiConstants.MessageIdPrefix), ConversationId = "acv_test", AssistantId = assistant.Id, Role = AssistantMessageRoleEnum.Assistant, Status = AssistantMessageStatusEnum.Pending, ReplyToMessageId = user.Id, CreatedUtc = now };
            return new PreparedExchange
            {
                Caller = caller,
                Assistant = assistant,
                Endpoints = await _Ai.Assistants.EndpointsAsync(assistant, token).ConfigureAwait(false),
                UserMessage = user,
                Reply = reply,
                History = messages.Take(messages.Count - 1).Select(m => new KeyValuePair<string, string>(String.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase) ? "assistant" : "user", m.Content!.Trim())).ToList(),
                Persist = false,
                IsTest = true
            };
        }

        /// <summary>
        /// Generate the reply of a prepared exchange, emitting the section 10.2 events to the sink. Runs detached from the
        /// request: a dropped connection does not stop it; the cancel route does.
        /// </summary>
        /// <param name="prepared">Prepared exchange.</param>
        /// <param name="sink">Event sink.</param>
        /// <param name="streamRequested">Whether the client asked for a stream.</param>
        /// <returns>The member message and the finished reply.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public Task<MessageExchangeView> RunAsync(PreparedExchange prepared, IChatEventSink sink, bool streamRequested)
        {
            ArgumentNullException.ThrowIfNull(prepared);
            ArgumentNullException.ThrowIfNull(sink);
            return Task.Run(() => RunCoreAsync(prepared, sink, streamRequested));
        }

        /// <summary>
        /// Cancel a reply that is still being written (partial content is kept).
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="conversationId">Conversation id.</param>
        /// <param name="messageId">Reply id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The reply.</returns>
        /// <exception cref="ApiErrorException">404, 409 when it already finished.</exception>
        public async Task<MessageView> CancelAsync(AssistantCaller caller, string conversationId, string messageId, CancellationToken token)
        {
            AssistantMessage reply = await _Ai.Conversations.RequireMessageAsync(caller, conversationId, messageId, token).ConfigureAwait(false);
            if (reply.Role != AssistantMessageRoleEnum.Assistant || (reply.Status != AssistantMessageStatusEnum.Pending && reply.Status != AssistantMessageStatusEnum.Streaming))
                throw new ApiErrorException(409, "Conflict", "The reply already finished.");
            if (_InFlight.TryGetValue(reply.Id, out CancellationTokenSource? cts))
            {
                cts.Cancel();
                for (int i = 0; i < 50 && _InFlight.ContainsKey(reply.Id); i++) await Task.Delay(100, token).ConfigureAwait(false);
            }
            else
            {
                reply.Status = AssistantMessageStatusEnum.Cancelled;
                reply.ErrorCode = "Cancelled";
                reply.CompletedUtc = DateTime.UtcNow;
                await _Ai.App.Database.Ai.Conversations.CompleteReplyAsync(reply, token).ConfigureAwait(false);
            }
            return await _Ai.Conversations.GetMessageAsync(caller, conversationId, messageId, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Client-facing message for an error code (section 10.2 error event).
        /// </summary>
        /// <param name="code">Error code.</param>
        /// <returns>Message.</returns>
        public static string ErrorMessage(string? code)
        {
            switch (code)
            {
                case "ModelTimeout": return "The assistant took too long to answer. Try again.";
                case "ModelBusy": return "The assistant is busy. Try again in a few seconds.";
                case "ModelUnavailable": return "The assistant is unavailable right now. Try again later.";
                case "Cancelled": return "The reply was stopped.";
                default: return "The assistant could not answer. Try again.";
            }
        }

        #endregion

        #region Private-Methods

        private async Task<PreparedExchange> PrepareCoreAsync(AssistantCaller caller, Assistant assistant, AssistantConversation conversation, MessageSendRequest request, CancellationToken token)
        {
            EnsureNotMuted(caller);
            string? clientMessageId = String.IsNullOrWhiteSpace(request.ClientMessageId) ? null : request.ClientMessageId.Trim();
            if (clientMessageId != null && (!Guid.TryParse(clientMessageId, out Guid _) || clientMessageId.Length > 64)) throw ApiErrorException.Field("clientMessageId", "Must be a UUID.");
            if (clientMessageId != null)
            {
                AssistantMessage? existing = await _Ai.App.Database.Ai.Conversations.ReadByClientMessageIdAsync(conversation.Id, clientMessageId, token).ConfigureAwait(false);
                if (existing != null && existing.CreatedUtc > DateTime.UtcNow.AddHours(-24))
                {
                    AssistantMessage? reply = await _Ai.App.Database.Ai.Conversations.ReadReplyToAsync(existing.Id, token).ConfigureAwait(false);
                    if (reply != null)
                        return new PreparedExchange { Caller = caller, Assistant = assistant, Conversation = conversation, UserMessage = existing, Reply = reply, Replay = true };
                }
            }
            if (_BusyConversations.ContainsKey(conversation.Id) || await _Ai.App.Database.Ai.Conversations.HasInFlightAsync(conversation.Id, token).ConfigureAwait(false))
                throw new ApiErrorException(409, "ReplyInProgress", "A reply is still being written. Wait for it to finish.");
            if (conversation.Status == AssistantConversationStatusEnum.Full || conversation.MessageCount + 2 > assistant.MaxMessagesPerConversation)
            {
                TrellisKitTelemetry.Add(AiTelemetry.LimitHits, 1, "kind", "conversation_full");
                throw new ApiErrorException(409, "ConversationFull", "This conversation is full. Start a new one.");
            }
            int? limit = AssistantService.DailyLimit(assistant, caller);
            if (limit != null)
            {
                int? remaining = await _Ai.Assistants.RemainingTodayAsync(assistant, caller, token).ConfigureAwait(false);
                if (remaining != null && remaining.Value <= 0)
                {
                    TrellisKitTelemetry.Add(AiTelemetry.LimitHits, 1, "kind", "daily");
                    throw AiErrors.LimitReached(DateTime.UtcNow);
                }
            }
            if (!caller.IsSystemAdmin) await _Ai.Usage.EnforceBudgetAsync(assistant, token).ConfigureAwait(false);
            EnforceMessageRate(caller);
            string content = MarkdownNormalizer.Normalize(request.Content ?? String.Empty).Trim();
            if (content.Length == 0) throw ApiErrorException.Field("content", "Required.");
            if (content.Length > assistant.MaxMessageCharacters)
                throw new ApiErrorException(400, "MessageTooLong", "Messages can be at most " + assistant.MaxMessageCharacters + " characters.", new Dictionary<string, string> { { "content", "At most " + assistant.MaxMessageCharacters + " characters." } });
            WordFilterResult flags = await _Ai.App.WordFilter.CheckAsync(caller.Actor, TargetTypeEnum.AssistantMessage, new Dictionary<string, string?> { { "content", content } }, token).ConfigureAwait(false);
            if (flags.HasFlags) TrellisKitTelemetry.Add(AiTelemetry.Moderation, 1, "stage", "input", "verdict", "flag", "source", "word_filter");
            // docs/INTEGRATION.md owner requirement R7: requests for code never reach a model.
            if (!_Ai.App.Property.Property.AiPolicy.AllowCode && OffScopeDetector.IsCodeRequest(content))
            {
                TrellisKitTelemetry.Add(AiTelemetry.Moderation, 1, "stage", "input", "verdict", "block", "source", "scope");
                throw new ApiErrorException(400, "OutOfScope", await _Ai.Prompts.GetAsync(PromptCatalog.AssistantRefusal, token).ConfigureAwait(false));
            }
            Dictionary<string, ModelEndpoint> endpoints = await _Ai.Assistants.EndpointsAsync(assistant, token).ConfigureAwait(false);
            List<AssistantMessageStage> stages = new List<AssistantMessageStage>();
            ModerationOutcome verdict = await _Ai.Moderation.ClassifyAsync(assistant, endpoints, content, "input", false, token).ConfigureAwait(false);
            if (verdict.Stage != null) stages.Add(verdict.Stage);
            if (verdict.Verdict == AssistantModerationVerdictEnum.Block && assistant.ModerationMode == AssistantModerationModeEnum.Block)
                throw new ApiErrorException(400, "MessageBlocked", "This message can't be sent to the assistant.");
            DateTime now = DateTime.UtcNow;
            AssistantMessage user = new AssistantMessage
            {
                Id = IdGenerator.Generate(AiConstants.MessageIdPrefix),
                ConversationId = conversation.Id,
                AssistantId = assistant.Id,
                Role = AssistantMessageRoleEnum.User,
                Content = content,
                Status = AssistantMessageStatusEnum.Completed,
                ClientMessageId = clientMessageId,
                ModerationVerdict = verdict.Stage == null || verdict.Skipped ? null : verdict.Verdict,
                ModerationCategory = verdict.Category,
                CreatedUtc = now,
                CompletedUtc = now
            };
            AssistantMessage pending = NewReply(conversation, assistant, user);
            pending.CreatedUtc = now.AddMilliseconds(1);
            try
            {
                await _Ai.App.Database.Ai.Conversations.AddExchangeAsync(user, pending, assistant.MaxMessagesPerConversation, token).ConfigureAwait(false);
            }
            catch (DuplicateRecordException)
            {
                throw new ApiErrorException(409, "ReplyInProgress", "That message was already sent.");
            }
            string? subject = caller.UserId ?? caller.VisitorId;
            if (subject != null)
                await _Ai.App.Database.Ai.Conversations.AddDailyUsageAsync(new AiDailyUsage { AssistantId = assistant.Id, SubjectType = caller.UserId != null ? "User" : "Visitor", SubjectId = subject, Date = AssistantService.Today(), Messages = 1 }, token).ConfigureAwait(false);
            // Flags on a member message are system reports in the Assistant scope (SystemAdmin only) with a snapshot.
            if (flags.HasFlags)
            {
                await _Ai.App.WordFilter.RecordFlagsAsync(flags, TargetTypeEnum.AssistantMessage, user.Id, caller.UserId, AssistantReportService.Scope, assistant.Id, token).ConfigureAwait(false);
                await _Ai.Reports.SnapshotAsync(user.Id, AssistantReportService.ReasonInputFlagged, null, null, token).ConfigureAwait(false);
            }
            if (verdict.Verdict == AssistantModerationVerdictEnum.Flag && assistant.ModerationMode == AssistantModerationModeEnum.Block)
                await _Ai.Reports.FileSystemReportAsync(user, caller.UserId, "Moderation model: Flag" + (verdict.Category != null ? " " + verdict.Category : String.Empty), AssistantReportService.ReasonInputFlagged, null, token).ConfigureAwait(false);
            bool first = conversation.MessageCount == 0;
            if (first && conversation.Title == null)
            {
                string fallback = content.Length <= 60 ? content : content.Substring(0, 60).TrimEnd();
                await _Ai.App.Database.Ai.Conversations.SetGeneratedTitleAsync(conversation.Id, fallback.Replace('\n', ' '), AssistantTitleSourceEnum.Fallback, now, token).ConfigureAwait(false);
                conversation.Title = fallback.Replace('\n', ' ');
            }
            conversation.MessageCount += 2;
            conversation.LastMessageUtc = now;
            if (conversation.MessageCount >= assistant.MaxMessagesPerConversation) conversation.Status = AssistantConversationStatusEnum.Full;
            TrellisKitTelemetry.Add(AiTelemetry.Messages, 1, "role", "user", "surface", AiTelemetry.Label(caller.Surface.ToString()), "audience", caller.UserId == null ? "visitor" : caller.HasMembership ? "member" : "signed_in");
            return new PreparedExchange
            {
                Caller = caller,
                Assistant = assistant,
                Endpoints = endpoints,
                Conversation = conversation,
                UserMessage = user,
                Reply = pending,
                History = await HistoryAsync(conversation.Id, user.Id, token).ConfigureAwait(false),
                FirstReply = first,
                Flags = flags,
                InputVerdict = user.ModerationVerdict,
                Stages = stages
            };
        }

        private async Task<MessageExchangeView> RunCoreAsync(PreparedExchange p, IChatEventSink sink, bool streamRequested)
        {
            bool admin = p.IsTest;
            MessageView userView = ConversationService.MessageView(p.UserMessage, null, false, false);
            if (p.Conversation != null || p.Replay)
            {
                ConversationView? conversationView = p.Conversation == null ? null : await _Ai.Conversations.ViewAsync(p.Conversation, false, false, CancellationToken.None).ConfigureAwait(false);
                await sink.EmitAsync("message.created", new { userMessage = userView, replyMessageId = p.Reply.Id, conversation = conversationView }).ConfigureAwait(false);
            }
            if (p.Replay) return await ReplayAsync(p, sink, userView).ConfigureAwait(false);
            if (p.Conversation != null) _BusyConversations[p.Conversation.Id] = 0;
            CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromMinutes(15));
            _InFlight[p.Reply.Id] = cts;
            try
            {
                AssistantMessage reply = await GenerateAsync(p, sink, streamRequested, cts.Token).ConfigureAwait(false);
                MessageView replyView = ConversationService.MessageView(reply, null, admin || _Ai.App.Settings.Ai.ShowModelDetailsToMembers, admin, _Ai.Tools);
                int? remaining = p.Persist ? await _Ai.Assistants.RemainingTodayAsync(p.Assistant, p.Caller, CancellationToken.None).ConfigureAwait(false) : null;
                if (reply.Status == AssistantMessageStatusEnum.Blocked)
                    await sink.EmitAsync("reply.blocked", new { reply = replyView, message = AssistantModerationService.BlockedNotice }).ConfigureAwait(false);
                else if (reply.Status == AssistantMessageStatusEnum.Completed)
                    await sink.EmitAsync("reply.completed", new { reply = replyView, remainingToday = remaining }).ConfigureAwait(false);
                else
                    await sink.EmitAsync("error", new { error = reply.ErrorCode ?? "ModelError", message = ErrorMessage(reply.ErrorCode), replyMessageId = reply.Id }).ConfigureAwait(false);
                return new MessageExchangeView { UserMessage = userView, Reply = replyView };
            }
            finally
            {
                _InFlight.TryRemove(p.Reply.Id, out CancellationTokenSource? _);
                cts.Dispose();
                if (p.Conversation != null) _BusyConversations.TryRemove(p.Conversation.Id, out byte _);
            }
        }

        private async Task<MessageExchangeView> ReplayAsync(PreparedExchange p, IChatEventSink sink, MessageView userView)
        {
            AssistantMessage reply = p.Reply;
            for (int i = 0; i < 600 && (reply.Status == AssistantMessageStatusEnum.Pending || reply.Status == AssistantMessageStatusEnum.Streaming); i++)
            {
                await Task.Delay(500).ConfigureAwait(false);
                reply = await _Ai.App.Database.Ai.Conversations.ReadMessageAsync(reply.Id, CancellationToken.None).ConfigureAwait(false) ?? reply;
            }
            MessageView replyView = (await _Ai.Conversations.MessageViewsAsync(new List<AssistantMessage> { reply }, false, CancellationToken.None).ConfigureAwait(false))[0];
            if (reply.Status == AssistantMessageStatusEnum.Completed || reply.Status == AssistantMessageStatusEnum.Blocked)
            {
                if (reply.Content.Length > 0) await sink.EmitAsync("reply.delta", new { text = reply.Content }).ConfigureAwait(false);
                await sink.EmitAsync(reply.Status == AssistantMessageStatusEnum.Blocked ? "reply.blocked" : "reply.completed", new { reply = replyView, remainingToday = await _Ai.Assistants.RemainingTodayAsync(p.Assistant, p.Caller, CancellationToken.None).ConfigureAwait(false) }).ConfigureAwait(false);
            }
            else
            {
                await sink.EmitAsync("error", new { error = reply.ErrorCode ?? "ModelError", message = ErrorMessage(reply.ErrorCode), replyMessageId = reply.Id }).ConfigureAwait(false);
            }
            return new MessageExchangeView { UserMessage = userView, Reply = replyView };
        }

        private async Task<AssistantMessage> GenerateAsync(PreparedExchange p, IChatEventSink sink, bool streamRequested, CancellationToken token)
        {
            Assistant assistant = p.Assistant;
            AssistantMessage reply = p.Reply;
            List<AssistantMessageStage> stages = new List<AssistantMessageStage>(p.Stages);
            Stopwatch total = Stopwatch.StartNew();
            using (Activity? span = AiTelemetry.Span("ai.chat"))
            {
                span?.SetTag("ai.assistant.id", assistant.Id);
                span?.SetTag("ai.message.id", reply.Id);
                reply.TraceId = span?.TraceId.ToString() ?? Activity.Current?.TraceId.ToString();
                KnowledgeResult knowledge = new KnowledgeResult();
                string query = p.UserMessage.Content;
                try
                {
                    if (assistant.KnowledgeEnabled)
                    {
                        query = await _Ai.Knowledge.RewriteAsync(assistant, p.Endpoints, p.History, p.UserMessage.Content, p.IsTest, stages, token).ConfigureAwait(false);
                        knowledge = await _Ai.Knowledge.RetrieveAsync(assistant, p.Endpoints, p.Caller, query, p.IsTest, stages, token).ConfigureAwait(false);
                        reply.RetrievalQuery = query;
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return await FinishAsync(p, reply, null, stages, total, "Cancelled", token).ConfigureAwait(false);
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    stages.Add(new AssistantMessageStage { Stage = "retrieval", StartedUtc = DateTime.UtcNow, Success = false, Detail = e is ApiErrorException api ? api.Error : "failed" });
                }

                ModelEndpoint? chat = assistant.Models.TryGetValue(AssistantPurposeEnum.Chat, out string? chatId) && p.Endpoints.TryGetValue(chatId, out ModelEndpoint? c) ? c : null;
                ModelEndpoint? fallback = assistant.Models.TryGetValue(AssistantPurposeEnum.ChatFallback, out string? fallbackId) && p.Endpoints.TryGetValue(fallbackId, out ModelEndpoint? f) ? f : null;
                if (chat == null && fallback == null) return await FinishAsync(p, reply, null, stages, total, "ModelUnavailable", token).ConfigureAwait(false);
                await _Ai.Prompts.EnsureLoadedAsync(CancellationToken.None).ConfigureAwait(false);
                string systemPrompt = _Ai.Assistants.RenderSystemPrompt(assistant, p.Caller);
                List<ChatMessage> messages = BuildMessages(systemPrompt, knowledge.ContextBlock, p.History, p.UserMessage.Content, chat ?? fallback!, assistant.MaxTokensPerReply);
                if (p.IsTest)
                    await sink.EmitAsync("debug", new
                    {
                        systemPrompt,
                        retrievalQuery = query,
                        knowledge = knowledge.Items.Select(i => new { index = i.Index, targetType = i.TargetType, targetId = i.TargetId, title = i.Title, score = Math.Round(i.Score, 6) }).ToList(),
                        historyMessages = messages.Count - 1 - (knowledge.ContextBlock == null ? 1 : 2)
                    }).ConfigureAwait(false);
                if (p.Persist) await _Ai.App.Database.Ai.Conversations.MarkStreamingAsync(reply.Id, CancellationToken.None).ConfigureAwait(false);
                await sink.EmitAsync("reply.started", new { replyMessageId = reply.Id, assistantTitle = assistant.Title }).ConfigureAwait(false);
                DateTime chatStarted = DateTime.UtcNow;
                CompletionCall call = new CompletionCall
                {
                    Endpoint = chat ?? fallback!,
                    Fallback = chat == null ? null : fallback,
                    Messages = messages,
                    MaxTokens = assistant.MaxTokensPerReply,
                    Temperature = assistant.Temperature,
                    TopP = assistant.TopP,
                    Stream = streamRequested && assistant.Streaming,
                    Purpose = AssistantPurposeEnum.Chat,
                    AssistantId = assistant.Id,
                    IsTest = p.IsTest,
                    GuardKey = p.Persist ? reply.Id : null
                };
                List<AssistantTool> tools = new List<AssistantTool>();
                ModelEndpoint? router = null;
                if (assistant.ToolsEnabled && assistant.AllowedTools.Count > 0)
                {
                    tools = _Ai.ToolService.Usable(assistant, p.Caller, await _Ai.ToolService.DisabledAsync(CancellationToken.None).ConfigureAwait(false));
                    if (tools.Count > 0 && assistant.Models.TryGetValue(AssistantPurposeEnum.ToolRouting, out string? routerId) && p.Endpoints.TryGetValue(routerId, out ModelEndpoint? r) && ModelInvoker.IsRoutable(r)) router = r;
                }
                List<KnowledgeCandidate> citationItems = new List<KnowledgeCandidate>(knowledge.Items);
                Func<string, Task> onText = text => sink.EmitAsync("reply.delta", new { text });
                Func<string, Task>? onReasoning = assistant.ShowReasoning ? text => sink.EmitAsync("reply.reasoning", new { text }) : null;
                CompletionOutcome outcome;
                try
                {
                    outcome = tools.Count > 0
                        ? await _Ai.ToolLoop.CompleteAsync(p, call, tools, router, citationItems, sink, onText, onReasoning, stages, token).ConfigureAwait(false)
                        : await _Ai.Invoker.CompleteAsync(call, onText, onReasoning, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    outcome = new CompletionOutcome { ErrorCode = "Cancelled", Endpoint = call.Endpoint, Text = String.Empty };
                }
                if (tools.Count == 0) stages.Add(new AssistantMessageStage
                {
                    Stage = "chat",
                    EndpointId = outcome.Endpoint?.Id,
                    Model = outcome.Endpoint?.Model,
                    StartedUtc = chatStarted,
                    DurationMs = outcome.LatencyMs,
                    Success = outcome.Success,
                    Detail = (outcome.Success ? "finish " + (outcome.FinishReason ?? "stop") : outcome.ErrorCode) + (outcome.UsedFallback ? "; fallback" : String.Empty) + (outcome.Retries > 0 ? "; retries " + outcome.Retries : String.Empty),
                    PromptTokens = outcome.PromptTokens,
                    CompletionTokens = outcome.CompletionTokens
                });
                if (p.IsTest && outcome.Error != null) await sink.EmitAsync("debug", new { error = outcome.Error, statusCode = outcome.StatusCode }).ConfigureAwait(false);
                reply.Content = outcome.Text ?? String.Empty;
                reply.Reasoning = assistant.ShowReasoning ? outcome.Reasoning : null;
                reply.EndpointId = outcome.Endpoint?.Id;
                reply.EndpointName = outcome.Endpoint?.Name;
                reply.Model = outcome.Endpoint?.Model;
                reply.UsedFallback = outcome.UsedFallback;
                reply.PromptTokens = outcome.PromptTokens;
                reply.CompletionTokens = outcome.CompletionTokens;
                reply.ReasoningTokens = outcome.ReasoningTokens;
                reply.TimeToFirstTokenMs = outcome.TimeToFirstTokenMs;
                reply.TimeToLastTokenMs = outcome.TimeToLastTokenMs;
                reply.TokensPerSecond = outcome.TokensPerSecond;
                reply.FinishReason = outcome.FinishReason;
                reply.CostMicros = outcome.CostMicros;
                if (!outcome.Success) return await FinishAsync(p, reply, knowledge, stages, total, outcome.ErrorCode ?? "ModelError", token).ConfigureAwait(false);

                string? withheld = reply.Content;
                string? outputFlag = await ModerateOutputAsync(p, reply, stages).ConfigureAwait(false);
                // docs/INTEGRATION.md owner requirement R7: a reply with code or the engine's rules is retried once with a
                // reminder (not streamed; the final reply replaces the streamed text), and withheld behind the refusal
                // when the retry is rejected too, whatever the moderation mode.
                string? reason = reply.Status == AssistantMessageStatusEnum.Blocked ? null : ScopeOutputCheck.RejectReason(reply.Content, _Ai.App.Property.Property.AiPolicy.AllowCode);
                if (reason != null)
                {
                    TrellisKitTelemetry.Add(AiTelemetry.Moderation, 1, "stage", "output", "verdict", "retry", "source", "scope");
                    CompletionOutcome retry = await RetryOutOfScopeAsync(call, messages, stages, token).ConfigureAwait(false);
                    reply.PromptTokens += retry.PromptTokens;
                    reply.CompletionTokens += retry.CompletionTokens;
                    reply.ReasoningTokens += retry.ReasoningTokens;
                    reply.CostMicros += retry.CostMicros;
                    if (retry.Success && ScopeOutputCheck.RejectReason(retry.Text, _Ai.App.Property.Property.AiPolicy.AllowCode) == null)
                    {
                        reply.Content = retry.Text ?? String.Empty;
                        reply.FinishReason = retry.FinishReason;
                        withheld = reply.Content;
                        outputFlag = await ModerateOutputAsync(p, reply, stages).ConfigureAwait(false);
                    }
                    else
                    {
                        TrellisKitTelemetry.Add(AiTelemetry.Moderation, 1, "stage", "output", "verdict", "block", "source", "scope");
                        outputFlag = reason == ScopeOutputCheck.ReasonCode ? "Scope: code in reply" : "Scope: rules in reply";
                        reply.Status = AssistantMessageStatusEnum.Blocked;
                        reply.Content = await _Ai.Prompts.GetAsync(PromptCatalog.AssistantRefusal, CancellationToken.None).ConfigureAwait(false);
                        reply.ErrorCode = "OutputOutOfScope";
                    }
                }
                if (reply.Status != AssistantMessageStatusEnum.Blocked)
                {
                    withheld = null;
                    reply.Status = AssistantMessageStatusEnum.Completed;
                    reply.Citations = assistant.Citations ? KnowledgeRetrievalService.Citations(reply.Content, citationItems) : new List<AssistantCitation>();
                    if (reply.Citations.Count > 0)
                        await sink.EmitAsync("reply.citations", new { citations = reply.Citations.Select(x => new CitationView { Index = x.Index, TargetType = x.TargetType, TargetId = x.TargetId, Title = x.Title, Path = x.Path }).ToList() }).ConfigureAwait(false);
                }
                span?.SetTag("gen_ai.usage.input_tokens", reply.PromptTokens);
                span?.SetTag("gen_ai.usage.output_tokens", reply.CompletionTokens);
                AssistantMessage finished = await FinishAsync(p, reply, knowledge, stages, total, null, CancellationToken.None).ConfigureAwait(false);
                // docs/AI_CONTRACT.md section 12.2: a withheld or flagged reply files a system report with a snapshot that
                // keeps the withheld text for moderators (the stored reply carries only the notice).
                if (outputFlag != null && p.Persist && !p.IsTest)
                    await _Ai.Reports.FileSystemReportAsync(finished, null, outputFlag, withheld != null ? AssistantReportService.ReasonOutputBlocked : AssistantReportService.ReasonOutputFlagged, withheld, CancellationToken.None).ConfigureAwait(false);
                return finished;
            }
        }

        private async Task<string?> ModerateOutputAsync(PreparedExchange p, AssistantMessage reply, List<AssistantMessageStage> stages)
        {
            if (p.Assistant.ModerationMode != AssistantModerationModeEnum.Block || reply.Content.Length == 0) return null;
            ModerationOutcome verdict = await _Ai.Moderation.ClassifyAsync(p.Assistant, p.Endpoints, reply.Content, "output", p.IsTest, CancellationToken.None).ConfigureAwait(false);
            if (verdict.Stage != null)
            {
                stages.Add(verdict.Stage);
                if (!verdict.Skipped)
                {
                    reply.ModerationVerdict = verdict.Verdict;
                    reply.ModerationCategory = verdict.Category;
                }
            }
            string category = verdict.Category != null ? " " + verdict.Category : String.Empty;
            if (verdict.Verdict == AssistantModerationVerdictEnum.Flag) return "Output moderation: Flag" + category;
            if (verdict.Verdict != AssistantModerationVerdictEnum.Block) return null;
            reply.Status = AssistantMessageStatusEnum.Blocked;
            reply.Content = AssistantModerationService.BlockedNotice;
            reply.ErrorCode = "OutputBlocked";
            return "Output moderation: Block" + category;
        }

        private async Task<CompletionOutcome> RetryOutOfScopeAsync(CompletionCall call, List<ChatMessage> messages, List<AssistantMessageStage> stages, CancellationToken token)
        {
            DateTime started = DateTime.UtcNow;
            CompletionCall retry = new CompletionCall
            {
                Endpoint = call.Endpoint,
                Fallback = call.Fallback,
                Messages = ScopeOutputCheck.WithReminder(messages, _Ai.App.Property.Property.AiPolicy.AllowCode),
                MaxTokens = call.MaxTokens,
                Temperature = call.Temperature,
                TopP = call.TopP,
                Stream = false,
                Purpose = call.Purpose,
                AssistantId = call.AssistantId,
                IsTest = call.IsTest,
                GuardKey = call.GuardKey == null ? null : call.GuardKey + ":scope-retry"
            };
            CompletionOutcome outcome;
            try
            {
                outcome = await _Ai.Invoker.CompleteAsync(retry, null, null, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                outcome = new CompletionOutcome { ErrorCode = "Cancelled", Endpoint = call.Endpoint, Text = String.Empty };
            }
            stages.Add(new AssistantMessageStage
            {
                Stage = "chat.retry",
                EndpointId = outcome.Endpoint?.Id,
                Model = outcome.Endpoint?.Model,
                StartedUtc = started,
                DurationMs = outcome.LatencyMs,
                Success = outcome.Success,
                Detail = outcome.Success ? "scope retry" : outcome.ErrorCode,
                PromptTokens = outcome.PromptTokens,
                CompletionTokens = outcome.CompletionTokens
            });
            return outcome;
        }

        private async Task<AssistantMessage> FinishAsync(PreparedExchange p, AssistantMessage reply, KnowledgeResult? knowledge, List<AssistantMessageStage> stages, Stopwatch total, string? errorCode, CancellationToken token)
        {
            if (errorCode != null)
            {
                reply.Status = errorCode == "Cancelled" ? AssistantMessageStatusEnum.Cancelled : AssistantMessageStatusEnum.Failed;
                reply.ErrorCode = errorCode;
            }
            reply.LatencyMs = total.ElapsedMilliseconds;
            reply.CompletedUtc = DateTime.UtcNow;
            for (int i = 0; i < stages.Count; i++) stages[i].Position = i;
            reply.Stages = stages;
            if (reply.Citations.Count == 0 && knowledge != null && reply.Status == AssistantMessageStatusEnum.Completed && !p.Assistant.Citations)
                reply.Citations = new List<AssistantCitation>();
            TrellisKitTelemetry.Add(AiTelemetry.Messages, 1, "role", "assistant", "surface", AiTelemetry.Label(p.IsTest ? "admin_test" : p.Caller.Surface.ToString()), "audience", p.Caller.UserId == null ? "visitor" : p.Caller.HasMembership ? "member" : "signed_in");
            if (!p.Persist) return reply;
            await _Ai.App.Database.Ai.Conversations.CompleteReplyAsync(reply, CancellationToken.None).ConfigureAwait(false);
            string? subject = p.Caller.UserId ?? p.Caller.VisitorId;
            if (subject != null && (reply.PromptTokens > 0 || reply.CompletionTokens > 0))
                await _Ai.App.Database.Ai.Conversations.AddDailyUsageAsync(new AiDailyUsage
                {
                    AssistantId = p.Assistant.Id,
                    SubjectType = p.Caller.UserId != null ? "User" : "Visitor",
                    SubjectId = subject,
                    Date = AssistantService.Today(),
                    PromptTokens = reply.PromptTokens,
                    CompletionTokens = reply.CompletionTokens + reply.ReasoningTokens,
                    CostMicros = reply.CostMicros
                }, CancellationToken.None).ConfigureAwait(false);
            if (p.FirstReply && reply.Status == AssistantMessageStatusEnum.Completed && p.Conversation != null)
            {
                AssistantConversation conversation = p.Conversation;
                _ = Task.Run(() => TitleAsync(p.Assistant, p.Endpoints, conversation, p.UserMessage.Content));
            }
            return reply;
        }

        private async Task TitleAsync(Assistant assistant, Dictionary<string, ModelEndpoint> endpoints, AssistantConversation conversation, string firstMessage)
        {
            try
            {
                if (!_Ai.App.Settings.Ai.GenerateTitles) return;
                string? id = assistant.Models.TryGetValue(AssistantPurposeEnum.TitleGeneration, out string? t) ? t : assistant.Models.TryGetValue(AssistantPurposeEnum.Chat, out string? c) ? c : null;
                if (id == null || !endpoints.TryGetValue(id, out ModelEndpoint? endpoint)) return;
                CompletionOutcome outcome = await _Ai.Invoker.PromptAsync(endpoint, "Write a short title of at most 8 words for a conversation that starts with this message. Reply with the title only.\n\n" + (firstMessage.Length > 1000 ? firstMessage.Substring(0, 1000) : firstMessage),
                    null, 24, 0.2, AssistantPurposeEnum.TitleGeneration, assistant.Id, false, CancellationToken.None).ConfigureAwait(false);
                if (!outcome.Success) return;
                string title = (outcome.Text ?? String.Empty).Trim().Trim('"', '\'', '*', '#').Replace('\n', ' ').Trim();
                string[] words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (words.Length > 8) title = String.Join(" ", words.Take(8));
                if (title.Length == 0 || title.Length > 120) return;
                if (await _Ai.App.WordFilter.IsBlockedAsync(title, CancellationToken.None).ConfigureAwait(false)) return;
                await _Ai.App.Database.Ai.Conversations.SetGeneratedTitleAsync(conversation.Id, title, AssistantTitleSourceEnum.Generated, DateTime.UtcNow, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                _Ai.App.Logger.Warn("[AssistantChatService] title generation failed for " + conversation.Id + ": " + e.Message);
            }
        }

        private List<ChatMessage> BuildMessages(string systemPrompt, string? context, List<KeyValuePair<string, string>> history, string message, ModelEndpoint endpoint, int maxTokensPerReply)
        {
            List<ChatMessage> messages = new List<ChatMessage> { ChatMessage.System(systemPrompt) };
            if (!String.IsNullOrEmpty(context)) messages.Add(ChatMessage.System(context));
            int window = endpoint.ContextWindow ?? _Ai.App.Settings.Ai.DefaultContextWindow;
            long budget = ((long)window - maxTokensPerReply) * 4 - systemPrompt.Length - (context?.Length ?? 0) - message.Length;
            List<ChatMessage> kept = new List<ChatMessage>();
            int i = history.Count - 1;
            while (i >= 0 && budget > 0)
            {
                // Drop whole turns: an assistant reply goes with the user message before it.
                int start = i;
                if (history[i].Key == "assistant" && i > 0 && history[i - 1].Key == "user") start = i - 1;
                long size = 0;
                for (int j = start; j <= i; j++) size += history[j].Value.Length + 16;
                if (size > budget) break;
                budget -= size;
                for (int j = i; j >= start; j--) kept.Insert(0, history[j].Key == "assistant" ? ChatMessage.Assistant(history[j].Value) : ChatMessage.User(history[j].Value));
                i = start - 1;
            }
            messages.AddRange(kept);
            messages.Add(ChatMessage.User(message));
            return messages;
        }

        private async Task<List<KeyValuePair<string, string>>> HistoryAsync(string conversationId, string currentUserMessageId, CancellationToken token)
        {
            List<AssistantMessage> recent = await _Ai.App.Database.Ai.Conversations.RecentMessagesAsync(conversationId, _HistoryMessages, token).ConfigureAwait(false);
            List<KeyValuePair<string, string>> history = new List<KeyValuePair<string, string>>();
            foreach (AssistantMessage m in recent)
            {
                if (m.Id == currentUserMessageId) break;
                if (m.Role == AssistantMessageRoleEnum.User) history.Add(new KeyValuePair<string, string>("user", m.Content));
                else if (m.Status == AssistantMessageStatusEnum.Completed && !m.IsSuperseded) history.Add(new KeyValuePair<string, string>("assistant", m.Content));
            }
            return history;
        }

        private AssistantMessage NewReply(AssistantConversation conversation, Assistant assistant, AssistantMessage user)
        {
            return new AssistantMessage
            {
                Id = IdGenerator.Generate(AiConstants.MessageIdPrefix),
                ConversationId = conversation.Id,
                AssistantId = assistant.Id,
                Role = AssistantMessageRoleEnum.Assistant,
                Status = AssistantMessageStatusEnum.Pending,
                ReplyToMessageId = user.Id,
                CreatedUtc = DateTime.UtcNow
            };
        }

        private static void EnsureNotMuted(AssistantCaller caller)
        {
            ContentActor actor = caller.Actor;
            if (actor.IsMuted && (actor.MutedUntilUtc == null || actor.MutedUntilUtc > DateTime.UtcNow))
                throw new ApiErrorException(403, "Muted", "Your account is muted.");
        }

        private void EnforceMessageRate(AssistantCaller caller)
        {
            if (caller.IsSystemAdmin) return;
            try
            {
                if (caller.UserId != null)
                {
                    _Ai.App.Limits.EnforceMember(caller.Actor, "ai-messages", _Ai.App.Settings.Ai.MessagesPerMinute, RateLimitService.Minute);
                }
                else
                {
                    _Ai.App.Limits.Enforce("ai-visitor-messages:" + caller.VisitorId, _Ai.App.Settings.Ai.VisitorMessagesPerMinute, RateLimitService.Minute);
                    _Ai.App.Limits.EnforceIp(caller.SourceIp, "ai-visitor-ip", _Ai.App.Settings.Ai.VisitorMessagesPerIpPerHour, RateLimitService.Hour);
                }
            }
            catch (ApiErrorException)
            {
                TrellisKitTelemetry.Add(AiTelemetry.LimitHits, 1, "kind", "rate");
                throw;
            }
        }

        #endregion
    }
}
