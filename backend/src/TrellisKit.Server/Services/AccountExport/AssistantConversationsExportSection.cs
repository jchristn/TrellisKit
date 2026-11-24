namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Serialization;

    /// <summary>
    /// The assistantConversations section of the account export (docs/AI_CONTRACT.md section 9.6): every conversation
    /// the member owns with its messages, citations, and feedback. Conversations are read in pages of
    /// <see cref="ConversationBatchSize"/> and each conversation's messages in pages of <see cref="MessageBatchSize"/>,
    /// written to the stream as they are read, so memory stays bounded whatever the history size. Model telemetry
    /// (endpoints, tokens, latency) is not exported. Written even when the property has since turned AI off.
    /// </summary>
    public sealed class AssistantConversationsExportSection : IAccountExportSection
    {
        #region Public-Members

        /// <summary>
        /// Section name.
        /// </summary>
        public const string SectionName = "assistantConversations";

        /// <summary>
        /// Conversations per read. Default 50, clamped 1 to 500.
        /// </summary>
        public int ConversationBatchSize
        {
            get { return _ConversationBatchSize; }
            set { _ConversationBatchSize = Math.Clamp(value, 1, 500); }
        }

        /// <summary>
        /// Messages per read. Default 200, clamped 1 to 500.
        /// </summary>
        public int MessageBatchSize
        {
            get { return _MessageBatchSize; }
            set { _MessageBatchSize = Math.Clamp(value, 1, 500); }
        }

        /// <inheritdoc />
        public string Name
        {
            get { return SectionName; }
        }

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;
        private int _ConversationBatchSize = 50;
        private int _MessageBatchSize = 200;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public AssistantConversationsExportSection(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task WriteAsync(Utf8JsonWriter writer, string userId, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(writer);
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            writer.WriteStartArray();
            Dictionary<string, string?> titles = new Dictionary<string, string?>(StringComparer.Ordinal);
            string? after = null;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                List<AssistantConversation> conversations = await _Services.Database.Ai.Conversations.ListByUserAsync(userId, after, ConversationBatchSize, token).ConfigureAwait(false);
                if (conversations.Count == 0) break;
                Dictionary<string, ObjectMetadata> metadata = await _Services.Metadata.LoadAsync(TargetTypeEnum.AssistantConversation, conversations.Select(c => c.Id).ToList(), false, token).ConfigureAwait(false);
                foreach (AssistantConversation conversation in conversations)
                {
                    if (!titles.ContainsKey(conversation.AssistantId))
                    {
                        Assistant? assistant = await _Services.Database.Ai.Assistants.ReadAsync(conversation.AssistantId, token).ConfigureAwait(false);
                        titles[conversation.AssistantId] = assistant == null || assistant.DeletedUtc != null ? null : assistant.Title;
                    }
                    ObjectMetadata meta = metadata.TryGetValue(conversation.Id, out ObjectMetadata? m) ? m : new ObjectMetadata();
                    AssistantConversationExportItem header = new AssistantConversationExportItem
                    {
                        Id = conversation.Id,
                        AssistantId = conversation.AssistantId,
                        AssistantTitle = titles[conversation.AssistantId],
                        Title = conversation.Title,
                        Status = conversation.Status,
                        IsArchived = conversation.IsArchived,
                        Surface = conversation.Surface,
                        MessageCount = conversation.MessageCount,
                        Labels = meta.Labels,
                        Tags = meta.Tags,
                        CreatedUtc = conversation.CreatedUtc,
                        LastMessageUtc = conversation.LastMessageUtc
                    };
                    writer.WriteStartObject();
                    foreach (JsonProperty property in JsonSerializer.SerializeToElement(header, ApiJson.Api).EnumerateObject()) property.WriteTo(writer);
                    writer.WritePropertyName("messages");
                    await WriteMessagesAsync(writer, conversation.Id, token).ConfigureAwait(false);
                    writer.WriteEndObject();
                }
                await writer.FlushAsync(token).ConfigureAwait(false);
                if (conversations.Count < ConversationBatchSize) break;
                after = conversations[conversations.Count - 1].Id;
            }
            writer.WriteEndArray();
        }

        #endregion

        #region Private-Methods

        private async Task WriteMessagesAsync(Utf8JsonWriter writer, string conversationId, CancellationToken token)
        {
            writer.WriteStartArray();
            string? after = null;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                List<AssistantMessage> messages = await _Services.Database.Ai.Conversations.ListMessagesAfterAsync(conversationId, after, MessageBatchSize, token).ConfigureAwait(false);
                if (messages.Count == 0) break;
                Dictionary<string, AssistantFeedback> feedback = await _Services.Database.Ai.Conversations.ReadFeedbackByMessagesAsync(
                    messages.Where(x => x.Role == AssistantMessageRoleEnum.Assistant).Select(x => x.Id), token).ConfigureAwait(false);
                foreach (AssistantMessage message in messages)
                {
                    AssistantMessageExportItem item = new AssistantMessageExportItem
                    {
                        Id = message.Id,
                        Role = message.Role,
                        Content = message.Content,
                        Status = message.Status,
                        IsSuperseded = message.IsSuperseded,
                        Citations = message.Citations.Select(c => new CitationView { Index = c.Index, TargetType = c.TargetType, TargetId = c.TargetId, Title = c.Title, Path = c.Path }).ToList(),
                        Feedback = feedback.TryGetValue(message.Id, out AssistantFeedback? f) ? new MessageFeedbackView { Rating = f.Rating, Note = f.Note, Reasons = f.Reasons, CreatedUtc = f.CreatedUtc } : null,
                        ToolCalls = message.ToolCalls.Take(TrellisKit.Core.AiConstants.MaxToolCallsPerReply).Select(c => new AssistantToolCallExportItem
                        {
                            Id = c.Id,
                            ToolName = c.ToolName,
                            ArgumentsJson = c.ArgumentsJson,
                            Status = c.Status,
                            ResultCount = c.ResultCount,
                            Round = c.Round,
                            Results = c.Results.Select(r => new ToolCallResultView { TargetType = r.TargetType, TargetId = r.TargetId, Title = r.Title, Path = r.Path }).ToList(),
                            CreatedUtc = c.CreatedUtc
                        }).ToList(),
                        CreatedUtc = message.CreatedUtc,
                        CompletedUtc = message.CompletedUtc
                    };
                    JsonSerializer.Serialize(writer, item, ApiJson.Api);
                }
                await writer.FlushAsync(token).ConfigureAwait(false);
                if (messages.Count < MessageBatchSize) break;
                after = messages[messages.Count - 1].Id;
            }
            writer.WriteEndArray();
        }

        #endregion
    }
}
