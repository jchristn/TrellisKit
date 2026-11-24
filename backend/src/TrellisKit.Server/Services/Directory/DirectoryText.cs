namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;

    /// <summary>
    /// Text rules shared by venues, instructors, submissions, and imports: trimming with bounds, https URLs, social
    /// links, slugs, country codes, IANA time zones, and the normalized name similarity used for duplicate detection.
    /// Thread safe (stateless).
    /// </summary>
    public static class DirectoryText
    {
        #region Private-Members

        private static readonly Regex _Slug = new Regex("^[a-z0-9](?:[a-z0-9-]{0,78}[a-z0-9])?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Trim a value; empty becomes null; longer than max adds an error.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <param name="max">Most characters.</param>
        /// <param name="field">Field name.</param>
        /// <param name="errors">Field errors.</param>
        /// <returns>Trimmed value or null.</returns>
        /// <exception cref="ArgumentNullException">The errors are null.</exception>
        public static string? Text(string? value, int max, string field, Dictionary<string, string> errors)
        {
            ArgumentNullException.ThrowIfNull(errors);
            if (value == null) return null;
            string text = value.Trim();
            if (text.Length == 0) return null;
            if (text.Length > max) errors[field] = "At most " + max.ToString(CultureInfo.InvariantCulture) + " characters.";
            return text;
        }

        /// <summary>
        /// Validate an absolute https URL (up to 500 characters); empty becomes null.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <param name="field">Field name.</param>
        /// <param name="errors">Field errors.</param>
        /// <returns>URL or null.</returns>
        /// <exception cref="ArgumentNullException">The errors are null.</exception>
        public static string? HttpsUrl(string? value, string field, Dictionary<string, string> errors)
        {
            ArgumentNullException.ThrowIfNull(errors);
            if (String.IsNullOrWhiteSpace(value)) return null;
            string text = value.Trim();
            if (text.Length > 500 || !Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps || String.IsNullOrEmpty(uri.Host))
            {
                errors[field] = "Use an absolute https URL (at most 500 characters).";
                return null;
            }
            return uri.ToString();
        }

        /// <summary>
        /// Validate social links (https only, at most max).
        /// </summary>
        /// <param name="links">Requested links.</param>
        /// <param name="max">Most links.</param>
        /// <param name="errors">Field errors.</param>
        /// <returns>Links in order.</returns>
        /// <exception cref="ArgumentNullException">The errors are null.</exception>
        public static List<DirectorySocialLink> SocialLinks(List<SocialLinkRequest>? links, int max, Dictionary<string, string> errors)
        {
            ArgumentNullException.ThrowIfNull(errors);
            List<DirectorySocialLink> result = new List<DirectorySocialLink>();
            if (links == null) return result;
            if (links.Count > max) errors["socialLinks"] = "At most " + max.ToString(CultureInfo.InvariantCulture) + " links.";
            for (int i = 0; i < links.Count && i < max; i++)
            {
                SocialLinkRequest? link = links[i];
                if (link == null) continue;
                string? url = HttpsUrl(link.Url, "socialLinks[" + i + "].url", errors);
                if (url == null)
                {
                    errors.TryAdd("socialLinks[" + i + "].url", "Required (https).");
                    continue;
                }
                result.Add(new DirectorySocialLink { Id = IdGenerator.Generate(DirectoryIdPrefixes.SocialLink), Kind = link.Kind, Url = url, Position = result.Count });
            }
            return result;
        }

        /// <summary>
        /// Derive a slug from a name: lowercase letters and digits, other runs become one hyphen (2 to 80 characters).
        /// </summary>
        /// <param name="name">Name.</param>
        /// <param name="fallbackPrefix">Prefix of a random slug when the name has no letters or digits.</param>
        /// <returns>Slug.</returns>
        public static string DeriveSlug(string? name, string fallbackPrefix)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char ch in (name ?? String.Empty).Normalize(NormalizationForm.FormD).ToLowerInvariant())
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
                if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9')) sb.Append(ch);
                else if (sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-');
            }
            string slug = sb.ToString().Trim('-');
            if (slug.Length > 80) slug = slug.Substring(0, 80).Trim('-');
            if (slug.Length < 2) slug = fallbackPrefix + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            return slug;
        }

        /// <summary>
        /// Whether a slug is valid.
        /// </summary>
        /// <param name="slug">Slug.</param>
        /// <returns>True when valid.</returns>
        public static bool IsSlug(string? slug)
        {
            return slug != null && slug.Length >= 2 && _Slug.IsMatch(slug);
        }

        /// <summary>
        /// Validate an IANA time zone id; empty becomes null.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <param name="field">Field.</param>
        /// <param name="errors">Field errors.</param>
        /// <returns>Id or null.</returns>
        /// <exception cref="ArgumentNullException">The errors are null.</exception>
        public static string? TimeZone(string? value, string field, Dictionary<string, string> errors)
        {
            ArgumentNullException.ThrowIfNull(errors);
            if (String.IsNullOrWhiteSpace(value)) return null;
            string text = value.Trim();
            try
            {
                TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(text);
                if (!zone.HasIanaId && !TimeZoneInfo.TryConvertWindowsIdToIanaId(text, out string? _)) errors[field] = "Use an IANA time zone id, for example America/Los_Angeles.";
                return text;
            }
            catch (Exception e) when (e is TimeZoneNotFoundException || e is InvalidTimeZoneException)
            {
                errors[field] = "Unknown time zone; use an IANA id, for example America/Los_Angeles.";
                return null;
            }
        }

        /// <summary>
        /// Normalize a name for duplicate detection: lowercase letters and digits with single spaces.
        /// </summary>
        /// <param name="name">Name.</param>
        /// <returns>Normalized name.</returns>
        public static string NormalizeName(string? name)
        {
            StringBuilder sb = new StringBuilder();
            bool space = false;
            foreach (char ch in (name ?? String.Empty).Normalize(NormalizationForm.FormD).ToLowerInvariant())
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
                if (Char.IsLetterOrDigit(ch))
                {
                    if (space && sb.Length > 0) sb.Append(' ');
                    sb.Append(ch);
                    space = false;
                }
                else space = true;
            }
            return sb.ToString();
        }

        /// <summary>
        /// Name similarity from 0 to 1: the larger of the Levenshtein ratio of the normalized names and the ratio of
        /// their sorted word sets (so word order does not matter).
        /// </summary>
        /// <param name="a">First name.</param>
        /// <param name="b">Second name.</param>
        /// <returns>Similarity.</returns>
        public static double Similarity(string? a, string? b)
        {
            string x = NormalizeName(a);
            string y = NormalizeName(b);
            if (x.Length == 0 || y.Length == 0) return 0;
            if (x == y) return 1;
            string xs = String.Join(" ", x.Split(' ').OrderBy(w => w, StringComparer.Ordinal));
            string ys = String.Join(" ", y.Split(' ').OrderBy(w => w, StringComparer.Ordinal));
            return Math.Max(Ratio(x, y), Ratio(xs, ys));
        }

        /// <summary>
        /// The words of a name with at least 3 characters (for the duplicate candidate query), longest first.
        /// </summary>
        /// <param name="name">Name.</param>
        /// <returns>Words.</returns>
        public static List<string> Words(string? name)
        {
            return NormalizeName(name).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 3).Distinct(StringComparer.Ordinal).OrderByDescending(w => w.Length).ThenBy(w => w, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// The longest word of a name (for the duplicate candidate query), or null.
        /// </summary>
        /// <param name="name">Name.</param>
        /// <returns>Word or null.</returns>
        public static string? LongestWord(string? name)
        {
            return NormalizeName(name).Split(' ', StringSplitOptions.RemoveEmptyEntries).OrderByDescending(w => w.Length).ThenBy(w => w, StringComparer.Ordinal).FirstOrDefault();
        }

        #endregion

        #region Private-Methods

        private static double Ratio(string a, string b)
        {
            int max = Math.Max(a.Length, b.Length);
            if (max == 0) return 1;
            return 1.0 - (double)Levenshtein(a, b) / max;
        }

        private static int Levenshtein(string a, string b)
        {
            int[] previous = new int[b.Length + 1];
            int[] current = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) previous[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                current[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
                }
                int[] swap = previous;
                previous = current;
                current = swap;
            }
            return previous[b.Length];
        }

        #endregion
    }
}
