namespace TrellisKit.Core.Database.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database.Interfaces;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    internal abstract class ContentChangeMethodsBase : MethodsBase, IContentChangeMethods
    {
        private const int _MaxErrorLength = 500;
        private const int _MaxNoteLength = 2000;

        protected ContentChangeMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public Task<EnumerationResult<ContentChangeRecord>> EnumerateAsync(EnumerationRequest request, ContentChangeFilter filter, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            List<string> conditions = new List<string>();
            if (filter.Statuses.Count > 0) conditions.Add("c.reviewstatus IN " + D.InList(filter.Statuses.Select(s => s.ToString())));
            if (filter.Categories.Count > 0) conditions.Add("(" + String.Join(" OR ", filter.Categories.Distinct().Select(c => "position(" + D.Str("," + c + ",") + " in ',' || c.categories || ',') > 0")) + ")");
            if (filter.Severities.Count > 0) conditions.Add("c.severity IN " + D.InList(filter.Severities.Select(s => s.ToString())));
            if (filter.MinimumSeverity != null)
                conditions.Add("c.severity IN " + D.InList(Enum.GetValues<ContentReviewSeverityEnum>().Where(s => s >= filter.MinimumSeverity.Value).Select(s => s.ToString())));
            if (filter.TargetTypes.Count > 0) conditions.Add("c.targettype IN " + D.InList(filter.TargetTypes));
            if (!String.IsNullOrWhiteSpace(filter.TargetId)) conditions.Add("c.targetid = " + D.Str(filter.TargetId.Trim()));
            if (filter.Clients.Count > 0) conditions.Add("c.client IN " + D.InList(filter.Clients.Select(s => s.ToString())));
            if (filter.Actions.Count > 0) conditions.Add("c.action IN " + D.InList(filter.Actions.Select(s => s.ToString())));
            if (!String.IsNullOrWhiteSpace(filter.ActorUserId)) conditions.Add("c.actoruserid = " + D.Str(filter.ActorUserId.Trim()));
            if (!String.IsNullOrWhiteSpace(filter.AuthorUserId)) conditions.Add("c.authoruserid = " + D.Str(filter.AuthorUserId.Trim()));
            return EnumerateAsync(request, "c.*", "contentchanges c", conditions, Map, token);
        }

        public async Task<ContentChangeRecord?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) return null;
            DataTable table = await QueryAsync("SELECT * FROM contentchanges WHERE " + Scope() + " AND id = " + D.Str(id.Trim()) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        public async Task<List<ContentChangeRecord>> ListPendingAsync(int max, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT * FROM contentchanges WHERE " + Scope() + " AND reviewstatus = " + D.Enum(ContentReviewStatusEnum.Pending) +
                " ORDER BY createdutc, id LIMIT " + D.Num(Math.Clamp(max, 1, 5000)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task<bool> CompleteReviewAsync(string id, ContentReviewStatusEnum status, List<ContentReviewCategoryEnum> categories, ContentReviewSeverityEnum severity, string? note,
            string reviewedBy, ContentReviewerKindEnum kind, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            ArgumentNullException.ThrowIfNull(categories);
            DataTable table = await QueryAsync("UPDATE contentchanges SET reviewstatus = " + D.Enum(status) + ", categories = " + D.Str(Categories(categories)) + ", severity = " + D.Enum(severity) +
                ", reviewernote = " + D.Str(Cut(note, _MaxNoteLength)) + ", reviewedby = " + D.Str(reviewedBy) + ", reviewerkind = " + D.Enum(kind) + ", reviewedutc = " + D.Ts(nowUtc) +
                (kind == ContentReviewerKindEnum.Model ? ", lasterror = NULL" : String.Empty) + ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(id) + " AND reviewstatus = " + D.Enum(ContentReviewStatusEnum.Pending) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<int> RecordFailureAsync(string id, string error, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("UPDATE contentchanges SET reviewattempts = reviewattempts + 1, lasterror = " + D.Str(Cut(error, _MaxErrorLength)) + ", updatedutc = " + D.Ts(nowUtc) +
                " WHERE " + Scope() + " AND id = " + D.Str(id) + " AND reviewstatus = " + D.Enum(ContentReviewStatusEnum.Pending) + " RETURNING reviewattempts;", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? 0 : RowReader.GetInt(table.Rows[0], "reviewattempts");
        }

        public async Task<bool> ResolveAsync(string id, ContentReviewStatusEnum status, string? note, string reviewedBy, string? moderationActionId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            string? text = Cut(note, _MaxNoteLength);
            DataTable table = await QueryAsync("UPDATE contentchanges SET reviewstatus = " + D.Enum(status) + ", reviewedby = " + D.Str(reviewedBy) + ", reviewerkind = " + D.Enum(ContentReviewerKindEnum.Administrator) +
                ", reviewedutc = " + D.Ts(nowUtc) + (text == null ? String.Empty : ", reviewernote = " + D.Str(text)) + (moderationActionId == null ? String.Empty : ", moderationactionid = " + D.Str(moderationActionId)) +
                ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(id) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> MarkNotifiedAsync(string id, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("UPDATE contentchanges SET notifiedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(id) + " AND notifiedutc IS NULL RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<ContentReviewCounts> CountsAsync(DateTime flaggedSinceUtc, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT " +
                "COUNT(*) FILTER (WHERE reviewstatus = 'Pending') AS pending, " +
                "COUNT(*) FILTER (WHERE reviewstatus = 'Flagged') AS flagged, " +
                "COUNT(*) FILTER (WHERE reviewstatus = 'Flagged' AND reviewedutc >= " + D.Ts(flaggedSinceUtc) + ") AS flaggedsince, " +
                "COUNT(*) FILTER (WHERE reviewstatus = 'Pending' AND reviewattempts > 0) AS failing, " +
                "MIN(createdutc) FILTER (WHERE reviewstatus = 'Pending') AS oldestpending " +
                "FROM contentchanges WHERE " + Scope() + " AND reviewstatus IN ('Pending', 'Flagged');", token).ConfigureAwait(false);
            DataRow row = table.Rows[0];
            return new ContentReviewCounts
            {
                Pending = RowReader.GetLong(row, "pending"),
                Flagged = RowReader.GetLong(row, "flagged"),
                FlaggedSince = RowReader.GetLong(row, "flaggedsince"),
                Failing = RowReader.GetLong(row, "failing"),
                OldestPendingUtc = RowReader.GetNullableDateTime(row, "oldestpending")
            };
        }

        public async Task<int> PruneAsync(DateTime beforeUtc, int max, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("DELETE FROM contentchanges WHERE " + Scope() + " AND id IN (SELECT o.id FROM contentchanges o WHERE " + Scope("o") + " AND o.createdutc < " + D.Ts(beforeUtc) +
                " AND o.reviewstatus IN ('Clean', 'Dismissed', 'Actioned') ORDER BY o.createdutc LIMIT " + D.Num(Math.Clamp(max, 1, 50000)) + ") RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        public async Task<int> EraseForUserAsync(string userId, DateTime nowUtc, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            DataTable table = await QueryAsync("UPDATE contentchanges SET snapshot = '', mediarefs = '', " +
                "reviewstatus = CASE WHEN reviewstatus = 'Pending' THEN 'Dismissed' ELSE reviewstatus END, " +
                "reviewedby = CASE WHEN reviewstatus = 'Pending' THEN 'system' ELSE reviewedby END, " +
                "reviewerkind = CASE WHEN reviewstatus = 'Pending' THEN 'System' ELSE reviewerkind END, " +
                "reviewedutc = CASE WHEN reviewstatus = 'Pending' THEN " + D.Ts(nowUtc) + " ELSE reviewedutc END, " +
                "reviewernote = CASE WHEN reviewstatus = 'Pending' THEN 'The account was deleted before review.' ELSE reviewernote END, " +
                "updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND authoruserid = " + D.Str(userId) + " AND (snapshot <> '' OR mediarefs <> '' OR reviewstatus = 'Pending') RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        private static string Categories(List<ContentReviewCategoryEnum> categories)
        {
            return String.Join(",", categories.Distinct().OrderBy(c => c).Select(c => c.ToString()));
        }

        private static string? Cut(string? value, int max)
        {
            if (value == null) return null;
            string text = value.Trim();
            return text.Length > max ? text.Substring(0, max) : text;
        }

        private static ContentChangeRecord Map(DataRow row)
        {
            return new ContentChangeRecord
            {
                Id = RowReader.GetString(row, "id"),
                TargetType = RowReader.GetString(row, "targettype"),
                TargetId = RowReader.GetString(row, "targetid"),
                ContextType = RowReader.GetNullableString(row, "contexttype"),
                ContextId = RowReader.GetNullableString(row, "contextid"),
                Action = RowReader.GetEnum(row, "action", ContentChangeActionEnum.Create),
                ActorUserId = RowReader.GetNullableString(row, "actoruserid"),
                AuthorUserId = RowReader.GetNullableString(row, "authoruserid"),
                Client = RowReader.GetEnum(row, "client", ContentChangeClientEnum.System),
                Snapshot = RowReader.GetNullableString(row, "snapshot") ?? String.Empty,
                MediaRefs = (RowReader.GetNullableString(row, "mediarefs") ?? String.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                ReviewStatus = RowReader.GetEnum(row, "reviewstatus", ContentReviewStatusEnum.Pending),
                ReviewedUtc = RowReader.GetNullableDateTime(row, "reviewedutc"),
                ReviewedBy = RowReader.GetNullableString(row, "reviewedby"),
                ReviewerKind = RowReader.GetNullableEnum<ContentReviewerKindEnum>(row, "reviewerkind"),
                Categories = (RowReader.GetNullableString(row, "categories") ?? String.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(c => Enum.TryParse(c, out ContentReviewCategoryEnum parsed) ? (ContentReviewCategoryEnum?)parsed : null).Where(c => c != null).Select(c => c!.Value).ToList(),
                Severity = RowReader.GetEnum(row, "severity", ContentReviewSeverityEnum.None),
                ReviewerNote = RowReader.GetNullableString(row, "reviewernote"),
                ReviewAttempts = RowReader.GetInt(row, "reviewattempts"),
                LastError = RowReader.GetNullableString(row, "lasterror"),
                NotifiedUtc = RowReader.GetNullableDateTime(row, "notifiedutc"),
                ModerationActionId = RowReader.GetNullableString(row, "moderationactionid"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }
    }
}
