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
    /// Builds the attachment payloads of direct messages (docs/PHASE5_CONTRACT.md section 3.7). This is the one place
    /// that turns attached media into URLs for messages, so the private media pipeline (per-object signed URLs for
    /// purpose Message, scoped to the conversation) replaces only <see cref="BuildAsync"/>. Today the payloads use the
    /// media service's standard views. Recipients of a pending request get a placeholder instead of the media.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class MessageAttachmentViews
    {
        #region Public-Members

        /// <summary>
        /// Placeholder text of an attachment hidden from the recipient of a pending request.
        /// </summary>
        public const string HiddenPlaceholder = "Attachment hidden until you accept";

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the builder.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public MessageAttachmentViews(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Attachment payloads of a page of Visible messages for one viewer, keyed by message id (messages without
        /// attachments are absent). Deleted and removed messages never carry attachments; the caller passes only the
        /// messages whose attachments the viewer may see listed.
        /// </summary>
        /// <param name="messages">Messages (a page).</param>
        /// <param name="hidden">Whether a message's media is hidden from the viewer (pending request recipient).</param>
        /// <param name="viewer">Viewer: the per-object private URLs (messaging.mediaUrlMinutes) work only for this viewer
        /// while they remain a member of the conversation.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Payloads by message id, in attachment order.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task<Dictionary<string, List<MessageAttachmentView>>> BuildAsync(IReadOnlyList<Message> messages, Func<Message, bool> hidden, ContentActor viewer, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(messages);
            ArgumentNullException.ThrowIfNull(hidden);
            ArgumentNullException.ThrowIfNull(viewer);
            Dictionary<string, List<MessageAttachmentView>> result = new Dictionary<string, List<MessageAttachmentView>>(StringComparer.Ordinal);
            List<Message> withMedia = messages.Where(m => m.State == MessageStateEnum.Visible && m.MediaIds.Count > 0).ToList();
            if (withMedia.Count == 0) return result;
            Dictionary<string, List<MediaView>> views = await _Services.Media.ListPrivateViewsForTargetsAsync(TargetTypeEnum.Message, withMedia.Select(m => m.Id), viewer, token).ConfigureAwait(false);
            foreach (Message message in withMedia)
            {
                Dictionary<string, MediaView> byId = views.TryGetValue(message.Id, out List<MediaView>? list)
                    ? list.GroupBy(v => v.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal)
                    : new Dictionary<string, MediaView>(StringComparer.Ordinal);
                bool isHidden = hidden(message);
                List<MessageAttachmentView> items = new List<MessageAttachmentView>();
                foreach (string mediaId in message.MediaIds)
                {
                    byId.TryGetValue(mediaId, out MediaView? view);
                    items.Add(new MessageAttachmentView
                    {
                        MediaId = mediaId,
                        Kind = view?.Kind ?? (message.HasVideo ? MediaKindEnum.Video : MediaKindEnum.Image),
                        IsHidden = isHidden,
                        Placeholder = isHidden ? HiddenPlaceholder : null,
                        Media = isHidden ? null : view
                    });
                }
                result[message.Id] = items;
            }
            return result;
        }

        #endregion
    }
}
