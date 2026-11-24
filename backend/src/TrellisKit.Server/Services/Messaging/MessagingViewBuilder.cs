namespace TrellisKit.Server.Services.Messaging
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Builds Conversation and Message payloads for one viewer with one batched load per page (docs/PHASE5_CONTRACT.md
    /// sections 1.4, 2.1, 2.2 and as-built note B): the other member's read position only when both members allow read
    /// receipts and no request is pending, tombstones without content, the original body as edit history, request media
    /// hidden from the recipient, and the caller's private labels and tags.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class MessagingViewBuilder
    {
        #region Public-Members

        /// <summary>
        /// Longest conversation list excerpt. Default 120.
        /// </summary>
        public const int ExcerptLength = 120;

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;
        private readonly MessagingModule _Module;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the builder.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <param name="module">Messaging module.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public MessagingViewBuilder(ApplicationServices services, MessagingModule module)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
            _Module = module ?? throw new ArgumentNullException(nameof(module));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The plain text excerpt of a body (whitespace collapsed, at most <see cref="ExcerptLength"/> characters).
        /// </summary>
        /// <param name="body">Body.</param>
        /// <param name="length">Maximum length.</param>
        /// <returns>Excerpt.</returns>
        public static string Excerpt(string? body, int length = ExcerptLength)
        {
            if (String.IsNullOrEmpty(body)) return String.Empty;
            string text = String.Join(" ", body.Split(new[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries));
            if (text.Length <= length) return text;
            int cut = length;
            if (Char.IsHighSurrogate(text[cut - 1])) cut--;
            return text.Substring(0, cut).TrimEnd() + "...";
        }

        /// <summary>
        /// One conversation payload.
        /// </summary>
        /// <param name="actor">Viewer.</param>
        /// <param name="entry">Conversation from the viewer's side.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Payload.</returns>
        public async Task<DirectConversationView> ConversationAsync(ContentActor actor, ConversationEntry entry, CancellationToken token = default)
        {
            List<DirectConversationView> views = await ConversationsAsync(actor, new List<ConversationEntry> { entry }, token).ConfigureAwait(false);
            return views[0];
        }

        /// <summary>
        /// Conversation payloads of a page, in order.
        /// </summary>
        /// <param name="actor">Viewer.</param>
        /// <param name="entries">Conversations from the viewer's side.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Payloads.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task<List<DirectConversationView>> ConversationsAsync(ContentActor actor, List<ConversationEntry> entries, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(entries);
            string me = actor.RequireUserId();
            if (entries.Count == 0) return new List<DirectConversationView>();
            DateTime now = _Module.Clock();
            List<string> otherIds = entries.SelectMany(e => e.Others).Select(o => o.UserId).Distinct(StringComparer.Ordinal).ToList();
            Dictionary<string, MessagingPeer> peers = await _Module.Policy.PeersAsync(me, otherIds, token).ConfigureAwait(false);
            Dictionary<string, UserSummary> summaries = await _Services.Summaries.BuildAsync(otherIds.Concat(new[] { me }).Select(i => (string?)i), token).ConfigureAwait(false);
            Dictionary<string, Message> last = await _Services.Database.Messages.ReadManyAsync(entries.Select(e => e.Conversation.LastMessageId).Where(i => i != null).Select(i => i!), token).ConfigureAwait(false);
            Dictionary<string, int> unread = await _Services.Database.Conversations.UnreadCountsAsync(me, entries.Select(e => e.Conversation.Id), token).ConfigureAwait(false);
            MemberMessagingSettings mine = await _Module.Policy.SettingsOfAsync(me, token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> metadata = await _Services.Metadata.LoadAsync(TargetTypeEnum.ConversationMember, entries.Select(e => e.Mine.Id), false, token).ConfigureAwait(false);
            List<DirectConversationView> result = new List<DirectConversationView>();
            foreach (ConversationEntry entry in entries)
            {
                ConversationMember? other = entry.Others.FirstOrDefault();
                MessagingPeer? peer = other != null && peers.TryGetValue(other.UserId, out MessagingPeer? p) ? p : null;
                List<ConversationMemberView> members = new List<ConversationMemberView>();
                foreach (ConversationMember member in new[] { entry.Mine }.Concat(entry.Others).OrderBy(m => m.JoinedUtc).ThenBy(m => m.Id, StringComparer.Ordinal))
                    members.Add(new ConversationMemberView { User = Summary(summaries, member.UserId), Role = member.Role, JoinedUtc = member.JoinedUtc });
                bool request = IsRequest(entry);
                bool receipts = other != null && peer != null && mine.ReadReceipts && peer.Settings.ReadReceipts && !request && !peer.BlockedByViewer && !peer.BlocksViewer;
                MessageSummaryView? lastMessage = null;
                if (entry.Conversation.LastMessageId != null && last.TryGetValue(entry.Conversation.LastMessageId, out Message? lm) && lm.Seq > entry.Mine.HiddenBeforeSeq)
                {
                    lastMessage = new MessageSummaryView
                    {
                        Id = lm.Id,
                        Seq = lm.Seq,
                        AuthorUserId = lm.AuthorUserId,
                        Excerpt = lm.State == MessageStateEnum.Visible ? lm.Excerpt : String.Empty,
                        HasAttachments = lm.State == MessageStateEnum.Visible && lm.AttachmentCount > 0,
                        State = lm.State,
                        CreatedUtc = lm.CreatedUtc
                    };
                }
                ObjectMetadata? meta = metadata.TryGetValue(entry.Mine.Id, out ObjectMetadata? m) ? m : null;
                result.Add(new DirectConversationView
                {
                    Id = entry.Conversation.Id,
                    Kind = entry.Conversation.Kind,
                    Members = members,
                    OtherMember = other == null ? UserSummary.Deleted() : Summary(summaries, other.UserId),
                    Folder = entry.Mine.Folder,
                    RequestState = entry.Mine.RequestState,
                    LastMessage = lastMessage,
                    LastSeq = entry.Conversation.LastSeq,
                    MyLastReadSeq = entry.Mine.LastReadSeq,
                    MyLastDeliveredSeq = entry.Mine.LastDeliveredSeq,
                    OtherLastReadSeq = receipts ? other!.LastReadSeq : null,
                    UnreadCount = unread.TryGetValue(entry.Conversation.Id, out int count) ? count : 0,
                    IsMuted = IsMuted(entry.Mine, now),
                    MutedUntilUtc = IsMuted(entry.Mine, now) ? entry.Mine.MutedUntilUtc : null,
                    MemberId = entry.Mine.Id,
                    Labels = meta?.Labels ?? new List<string>(),
                    Tags = meta?.Tags ?? new Dictionary<string, string>(),
                    Viewer = _Module.Policy.Viewer(actor, entry, peer),
                    CreatedUtc = entry.Conversation.CreatedUtc,
                    UpdatedUtc = entry.Mine.UpdatedUtc > entry.Conversation.UpdatedUtc ? entry.Mine.UpdatedUtc : entry.Conversation.UpdatedUtc
                });
            }
            return result;
        }

        /// <summary>
        /// Message payloads for one viewer, in order.
        /// </summary>
        /// <param name="actor">Viewer.</param>
        /// <param name="entries">The viewer's conversations keyed by id (every message's conversation must be present).</param>
        /// <param name="messages">Messages.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Payloads (messages of absent conversations are skipped).</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task<List<DirectMessageView>> MessagesAsync(ContentActor actor, IReadOnlyDictionary<string, ConversationEntry> entries, IReadOnlyList<Message> messages, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(entries);
            ArgumentNullException.ThrowIfNull(messages);
            string me = actor.RequireUserId();
            List<Message> list = messages.Where(m => entries.ContainsKey(m.ConversationId)).ToList();
            if (list.Count == 0) return new List<DirectMessageView>();
            DateTime now = _Module.Clock();
            Dictionary<string, Message> replies = await _Services.Database.Messages.ReadManyAsync(list.Where(m => m.ReplyToMessageId != null).Select(m => m.ReplyToMessageId!), token).ConfigureAwait(false);
            List<string?> authors = list.Select(m => m.AuthorUserId).Concat(replies.Values.Select(r => r.AuthorUserId)).Distinct().ToList();
            Dictionary<string, UserSummary> summaries = await _Services.Summaries.BuildAsync(authors, token).ConfigureAwait(false);
            Dictionary<string, ReactionState> reactions = await _Services.Reactions.LoadAsync(TargetTypeEnum.Message, list.Select(m => m.Id).ToList(), me, token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> metadata = await _Services.Metadata.LoadAsync(TargetTypeEnum.Message, list.Where(m => m.AuthorUserId == me).Select(m => m.Id), false, token).ConfigureAwait(false);
            Dictionary<string, List<MessageAttachmentView>> attachments = await _Module.Attachments.BuildAsync(list, m =>
                m.AuthorUserId != me && entries.TryGetValue(m.ConversationId, out ConversationEntry? e) && e.Mine.RequestState == ConversationRequestStateEnum.Pending, actor, token).ConfigureAwait(false);
            HashSet<string> related = await _Services.Blocks.RelatedAsync(me, token).ConfigureAwait(false);
            Dictionary<string, LinkPreviewView> previews = await _Services.LinkPreviews.LoadViewsAsync(list.Where(m => m.ShowLinkPreview && m.State == MessageStateEnum.Visible).Select(m => m.LinkPreviewId), token).ConfigureAwait(false);
            int window = _Services.Settings.Messaging.EditWindowMinutes;
            List<DirectMessageView> result = new List<DirectMessageView>();
            foreach (Message message in list)
            {
                ConversationEntry entry = entries[message.ConversationId];
                bool mine = message.AuthorUserId != null && message.AuthorUserId == me;
                bool visible = message.State == MessageStateEnum.Visible;
                bool blocked = entry.Others.Any(o => related.Contains(o.UserId));
                ReactionState state = reactions.TryGetValue(message.Id, out ReactionState? r) ? r : new ReactionState();
                ObjectMetadata? meta = mine && metadata.TryGetValue(message.Id, out ObjectMetadata? md) ? md : null;
                MessageReplyView? reply = null;
                if (message.ReplyToMessageId != null && replies.TryGetValue(message.ReplyToMessageId, out Message? quoted))
                {
                    bool quotedVisible = quoted.State == MessageStateEnum.Visible && quoted.Seq > entry.Mine.HiddenBeforeSeq;
                    reply = new MessageReplyView
                    {
                        Id = quoted.Id,
                        Seq = quoted.Seq,
                        Excerpt = quotedVisible ? quoted.Excerpt : String.Empty,
                        AuthorDisplayName = Summary(summaries, quoted.AuthorUserId).DisplayName,
                        IsDeleted = quoted.State != MessageStateEnum.Visible,
                        State = quoted.State
                    };
                }
                result.Add(new DirectMessageView
                {
                    Id = message.Id,
                    ConversationId = message.ConversationId,
                    Seq = message.Seq,
                    ClientMessageId = mine ? message.ClientMessageId : null,
                    Author = Summary(summaries, message.AuthorUserId),
                    Body = visible ? message.Body : String.Empty,
                    OriginalBody = visible ? message.OriginalBody : null,
                    IsEdited = message.EditedUtc != null,
                    EditedUtc = message.EditedUtc,
                    Attachments = visible && attachments.TryGetValue(message.Id, out List<MessageAttachmentView>? items) ? items : new List<MessageAttachmentView>(),
                    LinkPreview = visible && message.ShowLinkPreview && message.LinkPreviewId != null && previews.TryGetValue(message.LinkPreviewId, out LinkPreviewView? preview) ? preview : null,
                    ReplyTo = reply,
                    State = message.State,
                    ReactionCounts = visible ? state.ReactionCounts : new Dictionary<string, long>(),
                    ReactionTotal = visible ? state.ReactionTotal : 0,
                    MyReactions = visible ? state.MyReactions : new List<string>(),
                    Labels = meta?.Labels ?? new List<string>(),
                    Tags = meta?.Tags ?? new Dictionary<string, string>(),
                    Viewer = new MessageViewer
                    {
                        CanEdit = mine && visible && message.EditedUtc == null && window > 0 && message.CreatedUtc >= now.AddMinutes(-window),
                        CanDelete = mine && message.State != MessageStateEnum.Deleted,
                        CanReact = visible && !blocked,
                        CanReport = !mine && message.AuthorUserId != null && visible
                    },
                    CreatedUtc = message.CreatedUtc,
                    UpdatedUtc = message.UpdatedUtc
                });
            }
            return result;
        }

        /// <summary>
        /// Whether a member's mute is in effect.
        /// </summary>
        /// <param name="member">Member row.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <returns>True when muted now.</returns>
        public static bool IsMuted(ConversationMember member, DateTime nowUtc)
        {
            ArgumentNullException.ThrowIfNull(member);
            return member.IsMuted && (member.MutedUntilUtc == null || member.MutedUntilUtc.Value > nowUtc);
        }

        /// <summary>
        /// Whether a conversation is still a request (either side pending or awaiting acceptance).
        /// </summary>
        /// <param name="entry">Conversation.</param>
        /// <returns>True while a request is open.</returns>
        public static bool IsRequest(ConversationEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);
            return entry.Mine.RequestState != null || entry.Others.Any(o => o.RequestState != null);
        }

        #endregion

        #region Private-Methods

        private static UserSummary Summary(Dictionary<string, UserSummary> summaries, string? userId)
        {
            return userId != null && summaries.TryGetValue(userId, out UserSummary? summary) ? summary : UserSummary.Deleted();
        }

        #endregion
    }
}
