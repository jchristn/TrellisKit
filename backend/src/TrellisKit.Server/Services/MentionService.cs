namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Mentions (contract section 3.3): resolve @handles of a body against active accounts (case-insensitive; at most
    /// mentions.maxNotifiedPerItem distinct members per item, the rest stay plain text), store them per source, and turn
    /// mentions not yet notified into Mention notification drafts. Self-mentions are stored but never notify; blocks in
    /// either direction suppress the notification (NotificationService); an edit notifies only newly added members
    /// (existing rows keep notifiedutc); a scheduled post notifies when it publishes. A member removed by an edit and
    /// added again later is notified again (the removed row is deleted). Thread safety: safe to call concurrently.
    /// </summary>
    public class MentionService
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly AppSettings _Settings;
        private readonly UserSummaryService _Summaries;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Running settings (read live).</param>
        /// <param name="summaries">UserSummary builder.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public MentionService(DatabaseDriverBase database, AppSettings settings, UserSummaryService summaries)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Summaries = summaries ?? throw new ArgumentNullException(nameof(summaries));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Resolve the mentions of a body (in order of first appearance, distinct members, at most
        /// mentions.maxNotifiedPerItem).
        /// </summary>
        /// <param name="body">Normalized body.</param>
        /// <param name="sourceType">Source type.</param>
        /// <param name="sourceId">Source identifier.</param>
        /// <param name="authorUserId">Author.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Mentions to store.</returns>
        /// <exception cref="ArgumentNullException">The source or author is null or empty.</exception>
        public async Task<List<Mention>> ResolveAsync(string? body, TargetTypeEnum sourceType, string sourceId, string authorUserId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(sourceId)) throw new ArgumentNullException(nameof(sourceId));
            if (String.IsNullOrEmpty(authorUserId)) throw new ArgumentNullException(nameof(authorUserId));
            List<Mention> result = new List<Mention>();
            int max = _Settings.Mentions.MaxNotifiedPerItem;
            if (max <= 0) return result;
            List<string> handles = MarkdownNormalizer.MentionHandles(body);
            if (handles.Count == 0) return result;
            Dictionary<string, string> resolved = await _Database.Mentions.ResolveHandlesAsync(handles, token).ConfigureAwait(false);
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            DateTime now = DateTime.UtcNow;
            foreach (string handle in handles)
            {
                if (!resolved.TryGetValue(handle.ToLowerInvariant(), out string? userId) || !seen.Add(userId)) continue;
                result.Add(new Mention
                {
                    Id = IdGenerator.Generate(Constants.MentionIdPrefix),
                    SourceType = sourceType,
                    SourceId = sourceId,
                    MentionedUserId = userId,
                    Handle = handle,
                    AuthorUserId = authorUserId,
                    CreatedUtc = now.AddMilliseconds(result.Count)
                });
                if (result.Count >= max) break;
            }
            return result;
        }

        /// <summary>
        /// Resolve and store the mentions of a body (replace semantics for the source).
        /// </summary>
        /// <param name="body">Normalized body.</param>
        /// <param name="sourceType">Source type.</param>
        /// <param name="sourceId">Source identifier.</param>
        /// <param name="authorUserId">Author.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored mentions.</returns>
        /// <exception cref="ArgumentNullException">The source or author is null or empty.</exception>
        public async Task<List<Mention>> StoreAsync(string? body, TargetTypeEnum sourceType, string sourceId, string authorUserId, CancellationToken token = default)
        {
            List<Mention> mentions = await ResolveAsync(body, sourceType, sourceId, authorUserId, token).ConfigureAwait(false);
            await _Database.Mentions.ReplaceAsync(sourceType, sourceId, mentions, token).ConfigureAwait(false);
            return mentions;
        }

        /// <summary>
        /// Claim the source's mentions not notified yet and build Mention drafts for them (self-mentions are claimed
        /// without a draft).
        /// </summary>
        /// <param name="sourceType">FeedPost or Comment.</param>
        /// <param name="sourceId">Source identifier.</param>
        /// <param name="authorUserId">Author (the notification actor).</param>
        /// <param name="feedPostId">Post for the deep link.</param>
        /// <param name="excerpt">Excerpt for the notification body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Drafts.</returns>
        public async Task<List<NotificationDraft>> ClaimDraftsAsync(TargetTypeEnum sourceType, string sourceId, string authorUserId, string? feedPostId, string? excerpt, CancellationToken token = default)
        {
            List<Mention> claimed = await _Database.Mentions.ClaimUnnotifiedAsync(sourceType, sourceId, DateTime.UtcNow, token).ConfigureAwait(false);
            return claimed.Where(m => !String.Equals(m.MentionedUserId, authorUserId, StringComparison.Ordinal)).Select(m => new NotificationDraft
            {
                RecipientUserId = m.MentionedUserId,
                Type = NotificationTypeEnum.Mention,
                ActorUserId = authorUserId,
                TargetType = sourceType,
                TargetId = sourceId,
                FeedPostId = feedPostId,
                CommentId = sourceType == TargetTypeEnum.Comment ? sourceId : null,
                Body = excerpt
            }).ToList();
        }

        /// <summary>
        /// Mention payloads of a bounded set of sources (handles as written, current display names).
        /// </summary>
        /// <param name="sourceType">Source type.</param>
        /// <param name="sourceIds">Source identifiers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Views by source identifier.</returns>
        /// <exception cref="ArgumentNullException">The identifiers are null.</exception>
        public async Task<Dictionary<string, List<MentionView>>> LoadViewsAsync(TargetTypeEnum sourceType, IEnumerable<string> sourceIds, CancellationToken token = default)
        {
            Dictionary<string, List<Mention>> rows = await _Database.Mentions.ListBySourcesAsync(sourceType, sourceIds, token).ConfigureAwait(false);
            Dictionary<string, UserSummary> users = await _Summaries.BuildAsync(rows.Values.SelectMany(l => l).Select(m => (string?)m.MentionedUserId), token).ConfigureAwait(false);
            Dictionary<string, List<MentionView>> result = new Dictionary<string, List<MentionView>>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, List<Mention>> pair in rows)
            {
                result[pair.Key] = pair.Value.Select(m => new MentionView
                {
                    UserId = m.MentionedUserId,
                    Handle = m.Handle,
                    DisplayName = users.TryGetValue(m.MentionedUserId, out UserSummary? u) ? u.DisplayName : String.Empty
                }).ToList();
            }
            return result;
        }

        #endregion
    }
}
