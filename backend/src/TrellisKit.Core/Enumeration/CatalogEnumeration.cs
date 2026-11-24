namespace TrellisKit.Core.Enumeration
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Standard keyset enumeration for a source whose size is independently bounded, such as a validated configuration
    /// catalog. Persisted collections must page in the database instead. Thread safety: stateless.
    /// </summary>
    public static class CatalogEnumeration
    {
        /// <summary>
        /// Filter and page a bounded source using a normalized enumeration request. Immutable entries without a creation
        /// timestamp use the Unix epoch. Source identifiers must be unique and stable. Collection filters are applied
        /// by the caller before this method; the normalizer validates their names and binds them into the cursor.
        /// </summary>
        /// <typeparam name="T">Entry type.</typeparam>
        /// <param name="request">Validated, property-scoped request.</param>
        /// <param name="source">Independently bounded source.</param>
        /// <param name="id">Stable unique identifier.</param>
        /// <param name="created">Creation time, or Unix epoch for immutable entries.</param>
        /// <param name="search">Searchable text, default identifier.</param>
        /// <param name="labels">Inline labels, default empty.</param>
        /// <param name="tags">Inline tags, default empty.</param>
        /// <param name="orderKey">Key for additional orderings; created orderings always use creation time.</param>
        /// <returns>A bounded page with exact totals when requested.</returns>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        /// <exception cref="ArgumentException">Identifiers are empty or duplicated.</exception>
        public static EnumerationResult<T> Page<T>(EnumerationRequest request, IReadOnlyCollection<T> source,
            Func<T, string> id, Func<T, DateTime>? created = null, Func<T, string>? search = null,
            Func<T, IReadOnlyCollection<string>>? labels = null, Func<T, IReadOnlyDictionary<string, string>>? tags = null,
            Func<T, string>? orderKey = null)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(id);
            HashSet<string> identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (T item in source)
            {
                string identity = id(item);
                if (String.IsNullOrEmpty(identity) || !identities.Add(identity))
                    throw new ArgumentException("Catalog identifiers must be nonempty and unique.", nameof(source));
            }
            DateTime Created(T item) => (created?.Invoke(item) ?? DateTime.UnixEpoch).ToUniversalTime();
            string Key(T item) => request.Ordering.Name.StartsWith("Created", StringComparison.OrdinalIgnoreCase)
                ? Created(item).ToString("O", CultureInfo.InvariantCulture) : orderKey?.Invoke(item) ?? id(item);
            bool Matches(T item)
            {
                if (request.Query.CreatedAfterUtc is DateTime after && Created(item) < after.ToUniversalTime()) return false;
                if (request.Query.CreatedBeforeUtc is DateTime before && Created(item) >= before.ToUniversalTime()) return false;
                if (request.Query.Search != null && !(search?.Invoke(item) ?? id(item)).Contains(request.Query.Search, StringComparison.OrdinalIgnoreCase)) return false;
                IReadOnlyCollection<string> itemLabels = labels?.Invoke(item) ?? Array.Empty<string>();
                if (request.Query.Labels.Count > 0)
                {
                    bool MatchLabel(string label) => itemLabels.Contains(label, StringComparer.OrdinalIgnoreCase);
                    if (request.Query.LabelMatch == LabelMatchEnum.Any ? !request.Query.Labels.Any(MatchLabel) : !request.Query.Labels.All(MatchLabel)) return false;
                }
                IReadOnlyDictionary<string, string>? itemTags = tags?.Invoke(item);
                foreach (KeyValuePair<string, string> tag in request.Query.Tags)
                {
                    if (itemTags == null || !itemTags.Any(pair => String.Equals(pair.Key, tag.Key, StringComparison.OrdinalIgnoreCase)
                        && (tag.Value == "*" || String.Equals(pair.Value, tag.Value, StringComparison.Ordinal)))) return false;
                }
                return true;
            }
            int CompareKeys(string? left, string? right)
            {
                if (request.Ordering.KeyKind == EnumerationKeyKindEnum.Number)
                {
                    if (!Int64.TryParse(left, NumberStyles.Integer, CultureInfo.InvariantCulture, out long leftNumber)
                        || !Int64.TryParse(right, NumberStyles.Integer, CultureInfo.InvariantCulture, out long rightNumber)) throw ContinuationTokens.Invalid();
                    return leftNumber.CompareTo(rightNumber);
                }
                if (request.Ordering.KeyKind == EnumerationKeyKindEnum.Time)
                {
                    if (!DateTime.TryParse(left, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime leftTime)
                        || !DateTime.TryParse(right, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime rightTime)) throw ContinuationTokens.Invalid();
                    return leftTime.CompareTo(rightTime);
                }
                return StringComparer.Ordinal.Compare(left, right);
            }
            IComparer<string> keyComparer = Comparer<string>.Create(CompareKeys);
            if (request.HasPosition) CompareKeys(request.AfterKey, request.AfterKey);
            List<T> matching = source.Where(Matches).ToList();
            IOrderedEnumerable<T> ordered = request.Ordering.Descending
                ? matching.OrderByDescending(Key, keyComparer).ThenByDescending(id, StringComparer.Ordinal)
                : matching.OrderBy(Key, keyComparer).ThenBy(id, StringComparer.Ordinal);
            IEnumerable<T> remaining = ordered;
            if (request.HasPosition)
            {
                remaining = remaining.Where(item =>
                {
                    int comparison = CompareKeys(Key(item), request.AfterKey);
                    if (comparison == 0) comparison = StringComparer.Ordinal.Compare(id(item), request.AfterId);
                    return request.Ordering.Descending ? comparison < 0 : comparison > 0;
                });
            }
            List<T> candidates = remaining.Skip(request.Skip).ToList();
            List<T> objects = candidates.Take(request.MaxResults).ToList();
            bool end = candidates.Count <= objects.Count;
            return new EnumerationResult<T>
            {
                MaxResults = request.MaxResults,
                Skip = request.Skip,
                Objects = objects,
                EndOfResults = end,
                ContinuationToken = end ? null : ContinuationTokens.Encode(request.Fingerprint, request.Ordering.Name, Key(objects[^1]), id(objects[^1])),
                TotalRecords = request.CountTotals ? matching.Count : null,
                RecordsRemaining = request.CountTotals ? Math.Max(0, candidates.Count - objects.Count) : null
            };
        }
    }
}
