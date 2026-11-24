namespace TrellisKit.Core.Database.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Requests;

    /// <summary>
    /// Helpers shared by the moderation method groups: polymorphic label and tag filters (each row's own target type)
    /// and moderation scope conditions.
    /// </summary>
    internal abstract class ModerationMethodsBase : MethodsBase
    {
        protected ModerationMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        /// <summary>
        /// A copy of the request without label and tag filters (the caller applies them itself with
        /// <see cref="TargetMetadataConditions"/>), so the shared helper does not filter on the collection's fixed type.
        /// </summary>
        protected static EnumerationRequest WithoutMetadata(EnumerationRequest request)
        {
            EnumerationQuery q = request.Query;
            EnumerationQuery copy = new EnumerationQuery
            {
                MaxResults = q.MaxResults,
                ContinuationToken = q.ContinuationToken,
                Skip = q.Skip,
                Ordering = q.Ordering,
                Search = q.Search,
                CreatedAfterUtc = q.CreatedAfterUtc,
                CreatedBeforeUtc = q.CreatedBeforeUtc,
                LabelMatch = q.LabelMatch
            };
            return new EnumerationRequest
            {
                Collection = request.Collection,
                Query = copy,
                MaxResults = request.MaxResults,
                Skip = request.Skip,
                Ordering = request.Ordering,
                AfterKey = request.AfterKey,
                AfterId = request.AfterId,
                Fingerprint = request.Fingerprint,
                CountTotals = request.CountTotals,
                IncludeStaffMetadata = request.IncludeStaffMetadata
            };
        }

        /// <summary>
        /// Label and tag EXISTS conditions against a polymorphic target (type and id columns of the row).
        /// </summary>
        protected List<string> TargetMetadataConditions(EnumerationQuery q, string typeColumn, string idColumn)
        {
            List<string> conditions = new List<string>();
            List<string> labels = q.Labels.Select(MetadataRules.Normalize).Distinct(StringComparer.Ordinal).ToList();
            string match = Scope("ml") + " AND ml.targettype = " + typeColumn + " AND ml.targetid = " + idColumn;
            if (labels.Count > 0)
            {
                if (q.LabelMatch == LabelMatchEnum.Any)
                    conditions.Add("EXISTS (SELECT 1 FROM labels ml WHERE " + match + " AND ml.labelnormalized IN " + D.InList(labels) + ")");
                else
                    foreach (string label in labels)
                        conditions.Add("EXISTS (SELECT 1 FROM labels ml WHERE " + match + " AND ml.labelnormalized = " + D.Str(label) + ")");
            }
            foreach (KeyValuePair<string, string> tag in q.Tags)
            {
                string condition = "EXISTS (SELECT 1 FROM tags mt WHERE " + Scope("mt") + " AND mt.targettype = " + typeColumn + " AND mt.targetid = " + idColumn +
                    " AND mt.tagkeynormalized = " + D.Str(MetadataRules.Normalize(tag.Key));
                if (tag.Value != "*") condition += " AND mt.tagvalue = " + D.Str(tag.Value ?? String.Empty);
                conditions.Add(condition + ")");
            }
            return conditions;
        }

        /// <summary>
        /// Scope condition on a reports alias, or null when every scope is visible (SystemAdmin). An empty list matches
        /// nothing. A scoped moderator sees only content targets (never User or AssistantMessage, and Message only with the Messaging scope) in the listed scopes,
        /// and never content authored by a SystemAdmin (Phase 2 contract sections 1.1 and 9.3). Entries are a scope type
        /// ("Feed", "Forum", "Directory", "Events", "Library"), "Forum:&lt;forumId&gt;" for one forum, or
        /// "&lt;scopeType&gt;/Comment" for only the comments of a scope (docs/PHASE4_CONTRACT.md section 6.3).
        /// </summary>
        protected string? ScopeCondition(string alias, List<string>? scopeTypes)
        {
            if (scopeTypes == null) return null;
            if (scopeTypes.Count == 0) return "1 = 0";
            // Entries are a scope type ("Feed", "Forum" for every forum) or "Forum:<forumId>" for one forum (Phase 3 section 11.4).
            List<string> whole = scopeTypes.Where(s => s.IndexOf(':') < 0 && s.IndexOf('/') < 0).Distinct(StringComparer.Ordinal).ToList();
            List<string> commentsOnly = scopeTypes.Where(s => s.EndsWith("/Comment", StringComparison.Ordinal)).Select(s => s.Substring(0, s.Length - 8)).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).ToList();
            List<string> forums = scopeTypes.Where(s => s.StartsWith("Forum:", StringComparison.Ordinal)).Select(s => s.Substring(6)).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).ToList();
            List<string> parts = new List<string>();
            if (whole.Count > 0) parts.Add(alias + ".scopetype IN " + D.InList(whole));
            if (forums.Count > 0) parts.Add("(" + alias + ".scopetype = 'Forum' AND " + alias + ".scopeid IN " + D.InList(forums) + ")");
            if (commentsOnly.Count > 0) parts.Add("(" + alias + ".scopetype IN " + D.InList(commentsOnly) + " AND " + alias + ".targettype = " + D.Enum(TargetTypeEnum.Comment) + ")");
            // Other "<scopeType>:<scopeId>" entries (for example "Ideas:Comment") cover one scope id of that scope type.
            foreach (IGrouping<string, string> scoped in scopeTypes.Where(s => s.IndexOf(':') > 0 && !s.StartsWith("Forum:", StringComparison.Ordinal))
                .GroupBy(s => s.Substring(0, s.IndexOf(':')), StringComparer.Ordinal))
            {
                List<string> ids = scoped.Select(s => s.Substring(s.IndexOf(':') + 1)).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).ToList();
                if (ids.Count > 0) parts.Add("(" + alias + ".scopetype = " + D.Str(scoped.Key) + " AND " + alias + ".scopeid IN " + D.InList(ids) + ")");
            }
            if (parts.Count == 0) return "1 = 0";
            // Scoped moderators never see member, assistant reply, or direct message reports, except that a Messaging
            // moderator (scope "Messaging") sees the direct message reports of that scope (PHASE5_CONTRACT.md section 3.9).
            List<string> hidden = new List<string> { TargetTypeEnum.User.ToString(), TargetTypeEnum.AssistantMessage.ToString() };
            if (!whole.Contains("Messaging", StringComparer.Ordinal)) hidden.Add(TargetTypeEnum.Message.ToString());
            return "((" + String.Join(" OR ", parts) + ")" +
                " AND " + alias + ".targettype NOT IN " + D.InList(hidden) +
                " AND NOT EXISTS (SELECT 1 FROM roleassignments sa WHERE " + Scope("sa") + " AND sa.userid = " + alias + ".targetauthoruserid AND sa.role = " + D.Enum(RoleEnum.SystemAdmin) + "))";
        }
    }
}
