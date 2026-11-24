namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Text rules of library writes: slugs (2 to 80 of a-z, 0-9, and -), titles, and optional plain or Markdown text.
    /// Thread safety: stateless; safe to call concurrently.
    /// </summary>
    public static class LibraryText
    {
        #region Public-Members

        /// <summary>
        /// Longest slug.
        /// </summary>
        public const int MaxSlugLength = 80;

        /// <summary>
        /// Longest title.
        /// </summary>
        public const int MaxTitleLength = 120;

        /// <summary>
        /// Longest summary.
        /// </summary>
        public const int MaxSummaryLength = 300;

        /// <summary>
        /// Longest series description.
        /// </summary>
        public const int MaxSeriesDescriptionLength = 10000;

        #endregion

        #region Private-Members

        private static readonly Regex _Slug = new Regex("^[a-z0-9](?:[a-z0-9-]{0,78}[a-z0-9])?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a slug is valid (2 to 80 of a-z, 0-9, and -, not starting or ending with -).
        /// </summary>
        /// <param name="slug">Slug.</param>
        /// <returns>True when valid.</returns>
        public static bool IsSlug(string? slug)
        {
            return slug != null && slug.Length >= 2 && slug.Length <= MaxSlugLength && _Slug.IsMatch(slug);
        }

        /// <summary>
        /// Derive a slug from a title (lowercase letters and digits, other runs become one hyphen).
        /// </summary>
        /// <param name="title">Title.</param>
        /// <param name="fallbackPrefix">Prefix of a random slug when the title has too few usable characters.</param>
        /// <returns>Slug.</returns>
        public static string DeriveSlug(string title, string fallbackPrefix)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char ch in (title ?? String.Empty).Normalize(NormalizationForm.FormD).ToLowerInvariant())
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
                if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9')) sb.Append(ch);
                else if (sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-');
            }
            string slug = sb.ToString().Trim('-');
            if (slug.Length > MaxSlugLength) slug = slug.Substring(0, MaxSlugLength).Trim('-');
            if (slug.Length < 2) slug = fallbackPrefix + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            return slug;
        }

        /// <summary>
        /// Validate a required single-line title.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <param name="field">Field name.</param>
        /// <param name="errors">Field errors.</param>
        /// <returns>Normalized title, or null when invalid.</returns>
        public static string? Title(string? value, string field, Dictionary<string, string> errors)
        {
            ArgumentNullException.ThrowIfNull(errors);
            string title = value != null && MarkdownNormalizer.IsWellFormed(value) ? MarkdownNormalizer.NormalizeSingleLine(value) : String.Empty;
            int length = MarkdownNormalizer.Length(title);
            if (length < 2 || length > MaxTitleLength)
            {
                errors[field] = "Use 2 to " + MaxTitleLength + " characters.";
                return null;
            }
            return title;
        }

        /// <summary>
        /// Validate optional text (single line or Markdown subset).
        /// </summary>
        /// <param name="value">Value, or null.</param>
        /// <param name="max">Longest length.</param>
        /// <param name="field">Field name.</param>
        /// <param name="errors">Field errors.</param>
        /// <param name="markdown">Markdown (multi-line) text.</param>
        /// <returns>Normalized text, or null when empty or invalid.</returns>
        public static string? Optional(string? value, int max, string field, Dictionary<string, string> errors, bool markdown)
        {
            ArgumentNullException.ThrowIfNull(errors);
            if (value == null) return null;
            if (!MarkdownNormalizer.IsWellFormed(value))
            {
                errors[field] = "Must be valid UTF-8 text.";
                return null;
            }
            string normalized = markdown ? MarkdownNormalizer.Normalize(value) : MarkdownNormalizer.NormalizeSingleLine(value);
            if (MarkdownNormalizer.Length(normalized) > max) errors[field] = "At most " + max + " characters.";
            return normalized.Length == 0 ? null : normalized;
        }

        #endregion
    }
}
