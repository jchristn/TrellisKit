namespace TrellisKit.Core.Enumeration
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Validation and visibility rules for labels and tags (docs/ENUMERATION_AND_METADATA.md section 1): limits,
    /// allowed characters, case-insensitive de-duplication, and staff-only ("staff:") entries.
    /// Thread safety: stateless; safe to call concurrently.
    /// </summary>
    public static class MetadataRules
    {
        #region Public-Members

        /// <summary>
        /// Maximum labels per object, counted separately for public labels and for staff: labels (so members cannot infer
        /// hidden staff labels from limit errors).
        /// </summary>
        public const int MaxLabels = 50;

        /// <summary>
        /// Maximum tag keys per object, counted separately for public keys and for staff: keys.
        /// </summary>
        public const int MaxTags = 50;

        /// <summary>
        /// Maximum length of a label or tag key.
        /// </summary>
        public const int MaxKeyLength = 64;

        /// <summary>
        /// Maximum length of a tag value.
        /// </summary>
        public const int MaxValueLength = 512;

        /// <summary>
        /// Prefix of staff-only labels and tag keys.
        /// </summary>
        public const string StaffPrefix = "staff:";

        #endregion

        #region Private-Members

        private static readonly Regex _Allowed = new Regex("^[\\p{L}\\p{N} _\\-.:/]{1,64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a label or tag key is staff-only.
        /// </summary>
        /// <param name="value">Label or key.</param>
        /// <returns>True when it starts with "staff:" (case-insensitive).</returns>
        public static bool IsStaff(string? value)
        {
            return value != null && value.Trim().StartsWith(StaffPrefix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Whether a label or tag key uses only allowed characters and is 1 to 64 characters after trimming.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>True when valid.</returns>
        public static bool IsValidKey(string? value)
        {
            return value != null && _Allowed.IsMatch(value.Trim());
        }

        /// <summary>
        /// Normalized (comparison) form of a label or tag key.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>Trimmed, lowercased value.</returns>
        public static string Normalize(string value)
        {
            return (value ?? String.Empty).Trim().ToLowerInvariant();
        }

        /// <summary>
        /// Validate and de-duplicate labels (case-insensitive, first casing kept).
        /// </summary>
        /// <param name="labels">Labels, or null.</param>
        /// <param name="field">Field name for errors, for example "labels".</param>
        /// <param name="errors">Field errors to add to.</param>
        /// <returns>Clean labels (empty for null).</returns>
        /// <exception cref="ArgumentNullException">The errors collection is null.</exception>
        public static List<string> CleanLabels(IEnumerable<string>? labels, string field, Dictionary<string, string> errors)
        {
            ArgumentNullException.ThrowIfNull(errors);
            List<string> result = new List<string>();
            if (labels == null) return result;
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            int index = 0;
            foreach (string? raw in labels)
            {
                string label = (raw ?? String.Empty).Trim();
                if (!IsValidKey(label)) errors.TryAdd(field + "[" + index + "]", "Use 1 to 64 letters, digits, spaces, or _ - . : /.");
                else if (seen.Add(Normalize(label))) result.Add(label);
                index++;
            }
            if (OverLabelLimit(result)) errors.TryAdd(field, "At most " + MaxLabels + " labels.");
            return result;
        }

        /// <summary>
        /// Validate tags (case-insensitive keys must be unique).
        /// </summary>
        /// <param name="tags">Tags, or null.</param>
        /// <param name="field">Field name for errors, for example "tags".</param>
        /// <param name="errors">Field errors to add to.</param>
        /// <returns>Clean tags (empty for null), keys trimmed, null values as empty.</returns>
        /// <exception cref="ArgumentNullException">The errors collection is null.</exception>
        public static Dictionary<string, string> CleanTags(Dictionary<string, string>? tags, string field, Dictionary<string, string> errors)
        {
            ArgumentNullException.ThrowIfNull(errors);
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (tags == null) return result;
            foreach (KeyValuePair<string, string> pair in tags)
            {
                string key = (pair.Key ?? String.Empty).Trim();
                string value = pair.Value ?? String.Empty;
                if (!IsValidKey(key)) { errors.TryAdd(field + "." + key, "Keys use 1 to 64 letters, digits, spaces, or _ - . : /."); continue; }
                if (value.Length > MaxValueLength) { errors.TryAdd(field + "." + key, "Values are at most " + MaxValueLength + " characters."); continue; }
                if (result.ContainsKey(key)) { errors.TryAdd(field + "." + key, "Duplicate key (keys are case-insensitive)."); continue; }
                result[key] = value;
            }
            if (OverTagLimit(result.Keys)) errors.TryAdd(field, "At most " + MaxTags + " tags.");
            return result;
        }

        /// <summary>
        /// Whether labels exceed <see cref="MaxLabels"/> public labels or <see cref="MaxLabels"/> staff: labels.
        /// </summary>
        /// <param name="labels">Labels.</param>
        /// <returns>True when over a limit.</returns>
        /// <exception cref="ArgumentNullException">The labels are null.</exception>
        public static bool OverLabelLimit(IEnumerable<string> labels)
        {
            ArgumentNullException.ThrowIfNull(labels);
            int staff = 0;
            int other = 0;
            foreach (string label in labels)
            {
                if (IsStaff(label)) staff++;
                else other++;
            }
            return staff > MaxLabels || other > MaxLabels;
        }

        /// <summary>
        /// Whether tag keys exceed <see cref="MaxTags"/> public keys or <see cref="MaxTags"/> staff: keys.
        /// </summary>
        /// <param name="keys">Tag keys.</param>
        /// <returns>True when over a limit.</returns>
        /// <exception cref="ArgumentNullException">The keys are null.</exception>
        public static bool OverTagLimit(IEnumerable<string> keys)
        {
            ArgumentNullException.ThrowIfNull(keys);
            int staff = 0;
            int other = 0;
            foreach (string key in keys)
            {
                if (IsStaff(key)) staff++;
                else other++;
            }
            return staff > MaxTags || other > MaxTags;
        }

        /// <summary>
        /// The metadata a caller may see: staff entries removed for non-staff. Never null collections.
        /// </summary>
        /// <param name="metadata">Stored metadata, or null.</param>
        /// <param name="includeStaff">Whether the caller is staff for this object.</param>
        /// <returns>Visible metadata (a copy).</returns>
        public static ObjectMetadata ForViewer(ObjectMetadata? metadata, bool includeStaff)
        {
            ObjectMetadata result = new ObjectMetadata();
            if (metadata == null) return result;
            result.Labels = metadata.Labels.Where(l => includeStaff || !IsStaff(l)).ToList();
            foreach (KeyValuePair<string, string> pair in metadata.Tags)
            {
                if (includeStaff || !IsStaff(pair.Key)) result.Tags[pair.Key] = pair.Value;
            }
            return result;
        }

        /// <summary>
        /// Combine a caller's replacement with what is stored: non-staff callers' staff entries are ignored and the
        /// stored staff entries are kept, so a member replacing their labels never touches staff metadata.
        /// </summary>
        /// <param name="existing">Stored metadata.</param>
        /// <param name="labels">Replacement labels, or null to keep the stored labels.</param>
        /// <param name="tags">Replacement tags, or null to keep the stored tags.</param>
        /// <param name="isStaff">Whether the caller is staff for this object.</param>
        /// <returns>Metadata to store.</returns>
        /// <exception cref="ArgumentNullException">The stored metadata is null.</exception>
        public static ObjectMetadata Replace(ObjectMetadata existing, List<string>? labels, Dictionary<string, string>? tags, bool isStaff)
        {
            ArgumentNullException.ThrowIfNull(existing);
            ObjectMetadata result = new ObjectMetadata();
            if (labels == null) result.Labels = new List<string>(existing.Labels);
            else
            {
                List<string> next = labels.Where(l => isStaff || !IsStaff(l)).ToList();
                if (!isStaff) next.AddRange(existing.Labels.Where(IsStaff));
                result.Labels = next.GroupBy(Normalize).Select(g => g.First()).ToList();
            }
            if (tags == null) foreach (KeyValuePair<string, string> pair in existing.Tags) result.Tags[pair.Key] = pair.Value;
            else
            {
                foreach (KeyValuePair<string, string> pair in tags) if (isStaff || !IsStaff(pair.Key)) result.Tags[pair.Key] = pair.Value;
                if (!isStaff) foreach (KeyValuePair<string, string> pair in existing.Tags) if (IsStaff(pair.Key)) result.Tags[pair.Key] = pair.Value;
            }
            return result;
        }

        /// <summary>
        /// Apply an incremental change (PATCH or bulk): add and remove labels, set and remove tag keys. Staff entries in
        /// the change are ignored for non-staff callers.
        /// </summary>
        /// <param name="existing">Stored metadata.</param>
        /// <param name="addLabels">Labels to add, or null.</param>
        /// <param name="removeLabels">Labels to remove, or null.</param>
        /// <param name="setTags">Tags to set, or null.</param>
        /// <param name="removeTagKeys">Tag keys to remove, or null.</param>
        /// <param name="isStaff">Whether the caller is staff for this object.</param>
        /// <returns>Metadata to store.</returns>
        /// <exception cref="ArgumentNullException">The stored metadata is null.</exception>
        public static ObjectMetadata Patch(ObjectMetadata existing, List<string>? addLabels, List<string>? removeLabels, Dictionary<string, string>? setTags, List<string>? removeTagKeys, bool isStaff)
        {
            ArgumentNullException.ThrowIfNull(existing);
            HashSet<string> remove = new HashSet<string>((removeLabels ?? new List<string>()).Where(l => isStaff || !IsStaff(l)).Select(Normalize), StringComparer.Ordinal);
            List<string> labels = existing.Labels.Where(l => !remove.Contains(Normalize(l))).ToList();
            foreach (string label in addLabels ?? new List<string>())
            {
                if (!isStaff && IsStaff(label)) continue;
                if (!labels.Any(l => Normalize(l) == Normalize(label))) labels.Add(label.Trim());
            }
            Dictionary<string, string> tags = new Dictionary<string, string>(existing.Tags, StringComparer.OrdinalIgnoreCase);
            foreach (string key in removeTagKeys ?? new List<string>()) if (isStaff || !IsStaff(key)) tags.Remove(key.Trim());
            foreach (KeyValuePair<string, string> pair in setTags ?? new Dictionary<string, string>()) if (isStaff || !IsStaff(pair.Key)) tags[pair.Key.Trim()] = pair.Value ?? String.Empty;
            return new ObjectMetadata { Labels = labels, Tags = tags };
        }

        #endregion
    }
}
