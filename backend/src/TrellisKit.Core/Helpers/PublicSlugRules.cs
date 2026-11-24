namespace TrellisKit.Core.Helpers
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Slug rules of public landing pages (docs/PHASE4_CONTRACT.md section 18.1), shared by venues, events, and member
    /// profile pages: lower case letters and digits in groups joined by single hyphens, 3 to 80 characters, not a
    /// reserved word. Mirrored for clients by shared/src/publicPages.js.
    /// Thread safety: stateless; safe to call concurrently.
    /// </summary>
    public static class PublicSlugRules
    {
        #region Public-Members

        /// <summary>
        /// Shortest slug.
        /// </summary>
        public const int MinLength = 3;

        /// <summary>
        /// Longest slug.
        /// </summary>
        public const int MaxLength = 80;

        /// <summary>
        /// Slugs no page may use (they collide with routes or read as system words).
        /// </summary>
        public static readonly IReadOnlyList<string> Reserved = new[]
        {
            "about", "account", "add", "admin", "all", "api", "app", "assets", "auth", "contact", "create", "delete", "edit", "embed", "help", "index", "join", "login", "logout",
            "map", "me", "new", "null", "platform", "privacy", "register", "search", "settings", "sign-in", "sign-out", "sign-up", "signin", "signup", "sitemap", "support", "terms",
            "undefined"
        };

        /// <summary>
        /// The message shown for a slug that breaks the rules.
        /// </summary>
        public const string Message = "3 to 80 lowercase letters and digits, with single hyphens between words, and not a reserved word.";

        #endregion

        #region Private-Members

        private static readonly Regex _Pattern = new Regex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly HashSet<string> _Reserved = new HashSet<string>(Reserved, StringComparer.Ordinal);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Why a slug is not usable: required, length, format, or reserved; null when it is usable.
        /// </summary>
        /// <param name="slug">Slug.</param>
        /// <returns>Problem code, or null.</returns>
        public static string? Problem(string? slug)
        {
            string s = (slug ?? String.Empty).Trim();
            if (s.Length == 0) return "required";
            if (s.Length < MinLength || s.Length > MaxLength) return "length";
            if (!_Pattern.IsMatch(s)) return "format";
            if (_Reserved.Contains(s)) return "reserved";
            return null;
        }

        /// <summary>
        /// Whether a slug is usable.
        /// </summary>
        /// <param name="slug">Slug.</param>
        /// <returns>True when usable.</returns>
        public static bool IsValid(string? slug)
        {
            return Problem(slug) == null;
        }

        /// <summary>
        /// A slug from free text parts (name, locality, region; empty parts are skipped): accents removed, lower case,
        /// every run of other characters becomes one hyphen, at most 80 characters (cut at a hyphen when possible). A
        /// result that is too short, reserved, or empty becomes the fallback (or the text plus the fallback).
        /// </summary>
        /// <param name="fallback">Word used when nothing usable remains, for example venue (must itself be valid).</param>
        /// <param name="parts">Text parts.</param>
        /// <returns>A valid slug.</returns>
        /// <exception cref="ArgumentException">The fallback is not a valid slug.</exception>
        public static string Derive(string fallback, params string?[] parts)
        {
            if (!IsValid(fallback)) throw new ArgumentException("The fallback must be a valid slug.", nameof(fallback));
            string text = String.Join(" ", (parts ?? Array.Empty<string?>()).Where(p => !String.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
            string slug = Slugify(text);
            if (slug.Length > MaxLength)
            {
                slug = slug.Substring(0, MaxLength);
                int cut = slug.LastIndexOf('-');
                if (cut >= MinLength) slug = slug.Substring(0, cut);
                slug = slug.TrimEnd('-');
            }
            if (slug.Length == 0) return fallback;
            if (!IsValid(slug))
            {
                string combined = slug + "-" + fallback;
                return IsValid(combined) ? combined : fallback;
            }
            return slug;
        }

        /// <summary>
        /// Append a numeric suffix (-2, -3, ...) to a slug, shortening it so the result stays within 80 characters.
        /// </summary>
        /// <param name="slug">Valid slug.</param>
        /// <param name="number">Suffix number (2 or more).</param>
        /// <returns>Suffixed slug.</returns>
        public static string WithSuffix(string slug, int number)
        {
            string suffix = "-" + number.ToString(CultureInfo.InvariantCulture);
            string head = slug.Length + suffix.Length > MaxLength ? slug.Substring(0, MaxLength - suffix.Length).TrimEnd('-') : slug;
            return head + suffix;
        }

        #endregion

        #region Private-Methods

        private static string Slugify(string text)
        {
            string normalized = text.Normalize(NormalizationForm.FormD);
            StringBuilder builder = new StringBuilder(normalized.Length);
            bool hyphen = false;
            foreach (char c in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                char lower = Char.ToLowerInvariant(c);
                if ((lower >= 'a' && lower <= 'z') || (lower >= '0' && lower <= '9'))
                {
                    builder.Append(lower);
                    hyphen = false;
                }
                else if (!hyphen && builder.Length > 0)
                {
                    builder.Append('-');
                    hyphen = true;
                }
            }
            return builder.ToString().Trim('-');
        }

        #endregion
    }
}
