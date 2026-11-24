namespace TrellisKit.Core.Enumeration
{
    using System;
    using System.Globalization;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Keyset keys of the comment orderings (ContentCollections.CommentTree and CommentChildren). Top orders by score
    /// descending, then createdUtc ascending, then id ascending, so its key carries both the score and the time
    /// ("score|time"); CreatedDescending and CreatedAscending carry the time. The comment identifier is the tie breaker
    /// and travels separately in the continuation token. Thread safety: stateless.
    /// </summary>
    public static class CommentOrderingKeys
    {
        #region Public-Members

        /// <summary>
        /// Top ordering name.
        /// </summary>
        public const string Top = "Top";

        /// <summary>
        /// Newest first ordering name.
        /// </summary>
        public const string CreatedDescending = "CreatedDescending";

        /// <summary>
        /// Oldest first ordering name.
        /// </summary>
        public const string CreatedAscending = "CreatedAscending";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Key of a comment for an ordering.
        /// </summary>
        /// <param name="comment">Comment.</param>
        /// <param name="ordering">Ordering name.</param>
        /// <returns>Key text.</returns>
        /// <exception cref="ArgumentNullException">The comment is null.</exception>
        public static string KeyOf(Comment comment, string ordering)
        {
            ArgumentNullException.ThrowIfNull(comment);
            string time = comment.CreatedUtc.ToString("O", CultureInfo.InvariantCulture);
            if (String.Equals(ordering, Top, StringComparison.OrdinalIgnoreCase)) return comment.Score.ToString(CultureInfo.InvariantCulture) + "|" + time;
            return time;
        }

        /// <summary>
        /// Parse a key.
        /// </summary>
        /// <param name="key">Key text from a continuation token.</param>
        /// <param name="ordering">Ordering name.</param>
        /// <param name="score">Score (Top only; 0 otherwise).</param>
        /// <param name="createdUtc">Created time.</param>
        /// <exception cref="TrellisKit.Core.Exceptions.ApiErrorException">400 InvalidContinuationToken when malformed.</exception>
        public static void Parse(string? key, string ordering, out long score, out DateTime createdUtc)
        {
            score = 0;
            createdUtc = DateTime.MinValue;
            if (String.IsNullOrEmpty(key)) throw ContinuationTokens.Invalid();
            string time = key;
            if (String.Equals(ordering, Top, StringComparison.OrdinalIgnoreCase))
            {
                int bar = key.IndexOf('|', StringComparison.Ordinal);
                if (bar <= 0 || !Int64.TryParse(key.Substring(0, bar), NumberStyles.Integer, CultureInfo.InvariantCulture, out score)) throw ContinuationTokens.Invalid();
                time = key.Substring(bar + 1);
            }
            if (!DateTime.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime parsed)) throw ContinuationTokens.Invalid();
            createdUtc = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        }

        #endregion
    }
}
