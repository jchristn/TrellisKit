namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Data loaded once for a batch of thread payloads (internal to <see cref="ForumViewBuilder"/>).
    /// </summary>
    internal sealed class ThreadBundle
    {
        internal Dictionary<string, UserSummary> Users { get; set; } = new Dictionary<string, UserSummary>(StringComparer.Ordinal);

        internal Dictionary<string, List<MediaView>> Media { get; set; } = new Dictionary<string, List<MediaView>>(StringComparer.Ordinal);

        internal Dictionary<string, List<MediaView>> PostMedia { get; set; } = new Dictionary<string, List<MediaView>>(StringComparer.Ordinal);

        internal Dictionary<string, List<MentionView>> Mentions { get; set; } = new Dictionary<string, List<MentionView>>(StringComparer.Ordinal);

        internal Dictionary<string, List<MentionView>> PostMentions { get; set; } = new Dictionary<string, List<MentionView>>(StringComparer.Ordinal);

        internal Dictionary<string, LinkPreviewView> Previews { get; set; } = new Dictionary<string, LinkPreviewView>(StringComparer.Ordinal);

        internal Dictionary<string, ReactionState> Reactions { get; set; } = new Dictionary<string, ReactionState>(StringComparer.Ordinal);

        internal Dictionary<string, ReactionState> PostReactions { get; set; } = new Dictionary<string, ReactionState>(StringComparer.Ordinal);

        internal Dictionary<string, ObjectMetadata> Metadata { get; set; } = new Dictionary<string, ObjectMetadata>(StringComparer.Ordinal);

        internal Dictionary<string, ObjectMetadata> PollMeta { get; set; } = new Dictionary<string, ObjectMetadata>(StringComparer.Ordinal);

        internal HashSet<string> Admins { get; set; } = new HashSet<string>(StringComparer.Ordinal);

        internal Dictionary<string, Poll> Polls { get; set; } = new Dictionary<string, Poll>(StringComparer.Ordinal);

        internal Dictionary<string, List<string>> MyVotes { get; set; } = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        internal HashSet<string> Followed { get; set; } = new HashSet<string>(StringComparer.Ordinal);

        internal Dictionary<string, DateTime> ThreadReads { get; set; } = new Dictionary<string, DateTime>(StringComparer.Ordinal);

        internal Dictionary<string, DateTime> ForumReads { get; set; } = new Dictionary<string, DateTime>(StringComparer.Ordinal);

        internal HashSet<string> Blockers { get; set; } = new HashSet<string>(StringComparer.Ordinal);

        internal HashSet<string> Muted { get; set; } = new HashSet<string>(StringComparer.Ordinal);

        internal DateTime UnreadWindowStart { get; set; } = DateTime.UtcNow;
    }
}
