namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// The attachmentSummary text of a revision (contract sections 5.5 and 7.6), for example
    /// "Removed 1 image, added 1 video", or "Reordered attachments" when only the order changed. Thread safety:
    /// stateless.
    /// </summary>
    public static class AttachmentSummaryText
    {
        #region Public-Methods

        /// <summary>
        /// Describe the change from one ordered attachment list to another.
        /// </summary>
        /// <param name="before">Attachments before the edit, in order.</param>
        /// <param name="after">Attachments after the edit, in order.</param>
        /// <returns>Text, or null when nothing changed.</returns>
        /// <exception cref="ArgumentNullException">A list is null.</exception>
        public static string? Describe(IReadOnlyList<MediaAttachment> before, IReadOnlyList<MediaAttachment> after)
        {
            ArgumentNullException.ThrowIfNull(before);
            ArgumentNullException.ThrowIfNull(after);
            HashSet<string> beforeIds = new HashSet<string>(before.Select(m => m.Id), StringComparer.Ordinal);
            HashSet<string> afterIds = new HashSet<string>(after.Select(m => m.Id), StringComparer.Ordinal);
            List<MediaAttachment> removed = before.Where(m => !afterIds.Contains(m.Id)).ToList();
            List<MediaAttachment> added = after.Where(m => !beforeIds.Contains(m.Id)).ToList();
            List<string> parts = new List<string>();
            AddPart(parts, "removed", removed);
            AddPart(parts, "added", added);
            if (parts.Count == 0)
            {
                bool reordered = !before.Select(m => m.Id).SequenceEqual(after.Select(m => m.Id), StringComparer.Ordinal);
                return reordered ? "Reordered attachments" : null;
            }
            string text = String.Join(", ", parts);
            return Char.ToUpperInvariant(text[0]) + text.Substring(1);
        }

        /// <summary>
        /// Whether two ordered attachment lists differ.
        /// </summary>
        /// <param name="before">Before.</param>
        /// <param name="after">After.</param>
        /// <returns>True when the ids or their order differ.</returns>
        /// <exception cref="ArgumentNullException">A list is null.</exception>
        public static bool Differs(IReadOnlyList<MediaAttachment> before, IReadOnlyList<MediaAttachment> after)
        {
            ArgumentNullException.ThrowIfNull(before);
            ArgumentNullException.ThrowIfNull(after);
            return !before.Select(m => m.Id).SequenceEqual(after.Select(m => m.Id), StringComparer.Ordinal);
        }

        #endregion

        #region Private-Methods

        private static void AddPart(List<string> parts, string verb, List<MediaAttachment> items)
        {
            int images = items.Count(m => m.Kind == MediaKindEnum.Image);
            int videos = items.Count(m => m.Kind == MediaKindEnum.Video);
            List<string> nouns = new List<string>();
            if (images > 0) nouns.Add(images + (images == 1 ? " image" : " images"));
            if (videos > 0) nouns.Add(videos + (videos == 1 ? " video" : " videos"));
            if (nouns.Count > 0) parts.Add(verb + " " + String.Join(" and ", nouns));
        }

        #endregion
    }
}
