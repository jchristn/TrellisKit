namespace TrellisKit.Core.Enumeration
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Validates and clamps an <see cref="EnumerationQuery"/> for a collection (docs/ENUMERATION_AND_METADATA.md
    /// section 2.2): maxResults clamping (route maximum from settings, hard cap 1000), skip, ordering validation,
    /// search length, created window, label and tag filter limits, staff-only filters, unknown filter names, and the
    /// continuation token (whose ordering and position win). Every collection goes through this, so paging cannot
    /// differ between routes. Thread safety: stateless; safe to call concurrently.
    /// </summary>
    public static class EnumerationNormalizer
    {
        #region Public-Members

        /// <summary>
        /// Default page size.
        /// </summary>
        public const int DefaultMaxResults = 25;

        /// <summary>
        /// Largest skip.
        /// </summary>
        public const int MaxSkip = 100000;

        /// <summary>
        /// Most labels or tags in one query.
        /// </summary>
        public const int MaxFilterEntries = 20;

        /// <summary>
        /// Longest search text.
        /// </summary>
        public const int MaxSearchLength = 200;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Normalize a query.
        /// </summary>
        /// <param name="query">Query, or null for defaults.</param>
        /// <param name="collection">Collection.</param>
        /// <param name="settings">Enumeration settings (live).</param>
        /// <param name="isStaff">Whether the caller may see and filter on staff metadata.</param>
        /// <param name="scope">Extra scope bound into continuation tokens (for example a parent id), or empty.</param>
        /// <param name="maxResultsCap">Optional lower route-specific maximum (for example 20 for a typeahead), or null.</param>
        /// <returns>Request.</returns>
        /// <exception cref="ArgumentNullException">The collection or settings are null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed or 400 InvalidContinuationToken.</exception>
        public static EnumerationRequest Normalize(EnumerationQuery? query, EnumerationCollection collection, EnumerationSettings settings, bool isStaff, string scope = "", int? maxResultsCap = null)
        {
            ArgumentNullException.ThrowIfNull(collection);
            ArgumentNullException.ThrowIfNull(settings);
            EnumerationQuery q = query ?? new EnumerationQuery();
            Dictionary<string, string> errors = new Dictionary<string, string>();

            int maximum = Math.Min(EnumerationSettings.HardMaximum, collection.IsAdmin ? settings.MaxResultsAdmin : settings.MaxResultsMember);
            if (maxResultsCap != null) maximum = Math.Min(maximum, Math.Max(1, maxResultsCap.Value));
            int maxResults = Math.Clamp(q.MaxResults ?? Math.Min(DefaultMaxResults, maximum), 1, maximum);

            if (q.Skip < 0 || q.Skip > MaxSkip) errors["skip"] = "Must be 0 to " + MaxSkip + ".";
            if (q.Search != null)
            {
                q.Search = q.Search.Trim();
                if (q.Search.Length == 0) q.Search = null;
                else if (q.Search.Length > MaxSearchLength) errors["search"] = "At most " + MaxSearchLength + " characters.";
            }
            if (q.CreatedAfterUtc != null && q.CreatedBeforeUtc != null && q.CreatedAfterUtc.Value >= q.CreatedBeforeUtc.Value)
                errors["createdBeforeUtc"] = "Must be after createdAfterUtc.";

            if (q.Labels.Count > MaxFilterEntries) errors["labels"] = "At most " + MaxFilterEntries + " labels.";
            if (q.Tags.Count > MaxFilterEntries) errors["tags"] = "At most " + MaxFilterEntries + " tags.";
            if (q.Labels.Count > 0 && collection.TargetType == null && !collection.HasInlineMetadata) errors["labels"] = "This list does not support label filters.";
            if (q.Tags.Count > 0 && collection.TargetType == null && !collection.HasInlineMetadata) errors["tags"] = "This list does not support tag filters.";
            for (int i = 0; i < q.Labels.Count; i++)
            {
                if (!MetadataRules.IsValidKey(q.Labels[i])) errors.TryAdd("labels[" + i + "]", "Use 1 to 64 letters, digits, spaces, or _ - . : /.");
                else if (!isStaff && MetadataRules.IsStaff(q.Labels[i])) errors.TryAdd("labels[" + i + "]", "Staff labels cannot be used here.");
            }
            foreach (KeyValuePair<string, string> pair in q.Tags)
            {
                if (!MetadataRules.IsValidKey(pair.Key)) errors.TryAdd("tags." + pair.Key, "Keys use 1 to 64 letters, digits, spaces, or _ - . : /.");
                else if (!isStaff && MetadataRules.IsStaff(pair.Key)) errors.TryAdd("tags." + pair.Key, "Staff tags cannot be used here.");
                else if ((pair.Value ?? String.Empty).Length > MetadataRules.MaxValueLength) errors.TryAdd("tags." + pair.Key, "Values are at most " + MetadataRules.MaxValueLength + " characters.");
            }
            foreach (string name in q.Filters.Keys)
            {
                if (!collection.FilterNames.Contains(name))
                    errors.TryAdd("filters." + name, collection.FilterNames.Count == 0 ? "This list has no filters." : "Unknown filter; use " + String.Join(", ", collection.FilterNames) + ".");
            }

            string orderingName = String.IsNullOrWhiteSpace(q.Ordering) ? collection.DefaultOrdering : q.Ordering.Trim();
            if (!collection.Orderings.TryGetValue(orderingName, out EnumerationOrdering? ordering))
            {
                errors["ordering"] = "Use one of " + collection.OrderingNames() + ".";
                ordering = collection.Orderings[collection.DefaultOrdering];
            }
            if (errors.Count > 0) throw ApiErrorException.Validation(errors, "One or more enumeration fields are invalid.");

            EnumerationRequest request = new EnumerationRequest
            {
                Collection = collection,
                Query = q,
                MaxResults = maxResults,
                Skip = q.Skip,
                Ordering = ordering,
                Fingerprint = Fingerprint(q, collection, scope),
                CountTotals = collection.IsAdmin || settings.CountMemberTotals,
                IncludeStaffMetadata = isStaff
            };

            if (!String.IsNullOrWhiteSpace(q.ContinuationToken))
            {
                ContinuationTokens.Decode(q.ContinuationToken, request.Fingerprint, out string tokenOrdering, out string? key, out string id);
                if (!collection.Orderings.TryGetValue(tokenOrdering, out EnumerationOrdering? fromToken)) throw ContinuationTokens.Invalid();
                request.Ordering = fromToken;
                request.AfterKey = key;
                request.AfterId = id;
                request.Skip = 0;
            }
            return request;
        }

        #endregion

        #region Private-Methods

        private static string Fingerprint(EnumerationQuery q, EnumerationCollection collection, string scope)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(collection.Name).Append('|').Append(scope ?? String.Empty).Append('|').Append(q.Search ?? String.Empty).Append('|');
            sb.Append(q.CreatedAfterUtc?.ToString("O", CultureInfo.InvariantCulture) ?? String.Empty).Append('|');
            sb.Append(q.CreatedBeforeUtc?.ToString("O", CultureInfo.InvariantCulture) ?? String.Empty).Append('|');
            sb.Append(String.Join(",", q.Labels.Select(MetadataRules.Normalize).OrderBy(l => l, StringComparer.Ordinal))).Append('|').Append(q.LabelMatch).Append('|');
            sb.Append(String.Join(",", q.Tags.Select(t => MetadataRules.Normalize(t.Key) + "=" + t.Value).OrderBy(t => t, StringComparer.Ordinal))).Append('|');
            sb.Append(String.Join(",", q.Filters.Select(f => f.Key.ToLowerInvariant() + "=" + FilterText(f.Value)).OrderBy(t => t, StringComparer.Ordinal)));
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
            return Convert.ToHexString(hash, 0, 12).ToLowerInvariant();
        }

        private static string FilterText(JsonElement value)
        {
            return value.ValueKind == JsonValueKind.String ? (value.GetString() ?? String.Empty) : value.GetRawText();
        }

        #endregion
    }
}
