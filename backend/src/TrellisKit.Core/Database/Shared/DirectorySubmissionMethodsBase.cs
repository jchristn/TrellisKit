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
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    internal abstract class DirectorySubmissionMethodsBase : MethodsBase, IDirectorySubmissionMethods
    {
        protected DirectorySubmissionMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<DirectorySubmission> CreateAsync(DirectorySubmission submission, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(submission);
            RequireText(submission.Id, nameof(submission.Id));
            List<string> statements = new List<string>
            {
                "INSERT INTO directorysubmissions (propertyid, id, type, status, targettype, targetid, targetname, targetupdatedutc, submitteruserid, note, reviewernote, revieweruserid, decidedutc, dueutc, " +
                    "overduenotifiedutc, proposedlat, proposedlng, resultid, createdutc, updatedutc) VALUES (" + Pid + ", " + D.Str(submission.Id) + ", " + D.Enum(submission.Type) + ", " + D.Enum(submission.Status) + ", " +
                    D.Enum(submission.TargetType) + ", " + D.Str(submission.TargetId) + ", " + D.Str(submission.TargetName) + ", " + D.Ts(submission.TargetUpdatedUtc) + ", " + D.Str(submission.SubmitterUserId) + ", " +
                    D.Str(submission.Note) + ", NULL, NULL, NULL, " + D.Ts(submission.DueUtc) + ", NULL, " + DirectorySql.NullableNum(D, submission.ProposedLat) + ", " + DirectorySql.NullableNum(D, submission.ProposedLng) +
                    ", NULL, " + D.Ts(submission.CreatedUtc) + ", " + D.Ts(submission.UpdatedUtc) + ");"
            };
            string? fields = FieldInsert(submission.Id, submission.Fields);
            if (fields != null) statements.Add(fields);
            await UniqueTransactionAsync(statements, "A pending claim", token).ConfigureAwait(false);
            return (await ReadAsync(submission.Id, token).ConfigureAwait(false))!;
        }

        public async Task<DirectorySubmission?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) return null;
            DataTable table = await QueryAsync("SELECT s.* FROM directorysubmissions s WHERE " + Scope("s") + " AND s.id = " + D.Str(id) + ";", token).ConfigureAwait(false);
            List<DirectorySubmission> list = table.Rows.Cast<DataRow>().Select(Map).ToList();
            await FillAsync(list, token).ConfigureAwait(false);
            return list.FirstOrDefault();
        }

        public async Task<EnumerationResult<DirectorySubmission>> EnumerateAsync(EnumerationRequest request, DirectorySubmissionFilter filter, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            List<string> conditions = new List<string>();
            if (filter.Statuses.Count > 0) conditions.Add("s.status IN " + D.InList(filter.Statuses.Select(x => x.ToString())));
            if (filter.Types.Count > 0) conditions.Add("s.type IN " + D.InList(filter.Types.Select(x => x.ToString())));
            if (!String.IsNullOrEmpty(filter.SubmitterUserId)) conditions.Add("s.submitteruserid = " + D.Str(filter.SubmitterUserId));
            if (!String.IsNullOrEmpty(filter.TargetId)) conditions.Add("s.targetid = " + D.Str(filter.TargetId));
            EnumerationResult<DirectorySubmission> page = await EnumerateAsync(request, "s.*", "directorysubmissions s", conditions, Map, token).ConfigureAwait(false);
            await FillAsync(page.Objects, token).ConfigureAwait(false);
            return page;
        }

        public async Task<Dictionary<string, long>> StatusCountsByUserSinceAsync(string userId, DateTime sinceUtc, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("SELECT status, COUNT(*) AS cnt FROM directorysubmissions WHERE " + Scope() + " AND submitteruserid = " + D.Str(userId) +
                " AND createdutc >= " + D.Ts(sinceUtc) + " GROUP BY status ORDER BY status LIMIT 20;", token).ConfigureAwait(false);
            Dictionary<string, long> counts = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (DataRow row in table.Rows) counts[RowReader.GetString(row, "status")] = Convert.ToInt64(row["cnt"], System.Globalization.CultureInfo.InvariantCulture);
            return counts;
        }

        public Task<long> CountPendingByUserAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            return CountAsync("SELECT COUNT(*) AS cnt FROM directorysubmissions WHERE " + Scope() + " AND submitteruserid = " + D.Str(userId) + " AND status = 'Pending';", token);
        }

        public async Task<bool> HasPendingClaimAsync(string userId, string instructorId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            RequireText(instructorId, nameof(instructorId));
            long count = await CountAsync("SELECT COUNT(*) AS cnt FROM directorysubmissions WHERE " + Scope() + " AND submitteruserid = " + D.Str(userId) + " AND targetid = " + D.Str(instructorId) +
                " AND type = 'ClaimInstructor' AND status = 'Pending';", token).ConfigureAwait(false);
            return count > 0;
        }

        public async Task<bool> OverrideAsync(string id, List<DirectorySubmissionField> fields, bool setGeo, double? lat, double? lng, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            ArgumentNullException.ThrowIfNull(fields);
            string sid = D.Str(id);
            string pending = "EXISTS (SELECT 1 FROM directorysubmissions ps WHERE " + Scope("ps") + " AND ps.id = " + sid + " AND ps.status = 'Pending')";
            List<string> statements = new List<string>();
            List<string> names = fields.Select(f => f.Field).Distinct(StringComparer.Ordinal).ToList();
            if (names.Count > 0)
            {
                statements.Add("DELETE FROM directorysubmissionfields WHERE " + Scope() + " AND submissionid = " + sid + " AND field IN " + D.InList(names) + " AND " + pending + ";");
                statements.Add("INSERT INTO directorysubmissionfields (propertyid, id, submissionid, field, proposedvalue, currentvalue, position, isoverride) SELECT " + Pid + ", v.id, " + sid +
                    ", v.field, v.proposed, v.current, v.pos, " + D.Bool(true) + " FROM (VALUES " + String.Join(", ", fields.Select(f => "(" + D.Str(IdGenerator.Generate(DirectoryIdPrefixes.SubmissionField)) + ", " +
                    D.Str(f.Field) + ", " + D.Str(f.ProposedValue) + ", " + D.Str(f.CurrentValue) + ", " + D.Num(f.Position) + ")")) + ") AS v(id, field, proposed, current, pos) WHERE " + pending + ";");
            }
            string geo = setGeo ? ", proposedlat = " + DirectorySql.NullableNum(D, lat) + ", proposedlng = " + DirectorySql.NullableNum(D, lng) : String.Empty;
            statements.Add("UPDATE directorysubmissions SET updatedutc = " + D.Ts(nowUtc) + geo + " WHERE " + Scope() + " AND id = " + sid + " AND status = 'Pending' RETURNING id;");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task RefreshAsync(string id, List<DirectorySubmissionField> fields, DateTime? targetUpdatedUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            ArgumentNullException.ThrowIfNull(fields);
            string sid = D.Str(id);
            List<string> statements = new List<string>();
            foreach (DirectorySubmissionField field in fields.Where(f => !String.IsNullOrEmpty(f.Id)))
                statements.Add("UPDATE directorysubmissionfields SET currentvalue = " + D.Str(field.CurrentValue) + " WHERE " + Scope() + " AND submissionid = " + sid + " AND id = " + D.Str(field.Id) + ";");
            statements.Add("UPDATE directorysubmissions SET targetupdatedutc = " + D.Ts(targetUpdatedUtc) + " WHERE " + Scope() + " AND id = " + sid + " AND status = 'Pending';");
            await TransactionAsync(statements, token).ConfigureAwait(false);
        }

        public async Task ApproveAsync(DirectorySubmission submission, List<string> apply, string? targetTable, string? reviewerUserId, string? reviewNote, string? resultId, DateTime nowUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(submission);
            ArgumentNullException.ThrowIfNull(apply);
            string sid = D.Str(submission.Id);
            List<string> statements = new List<string>
            {
                "SELECT id FROM directorysubmissions WHERE " + Scope() + " AND id = " + sid + " FOR UPDATE;",
                DirectorySql.Guard("EXISTS (SELECT 1 FROM directorysubmissions g WHERE " + Scope("g") + " AND g.id = " + sid + " AND g.status = 'Pending')", "notpending")
            };
            if (targetTable != null && !String.IsNullOrEmpty(submission.TargetId))
            {
                string stale = "EXISTS (SELECT 1 FROM " + targetTable + " t WHERE " + Scope("t") + " AND t.id = " + D.Str(submission.TargetId) + " AND t.deletedutc IS NULL AND t.updatedutc = " +
                    (submission.TargetUpdatedUtc == null ? "t.updatedutc" : D.Ts(submission.TargetUpdatedUtc.Value)) + ")";
                statements.Add("SELECT id FROM " + targetTable + " WHERE " + Scope() + " AND id = " + D.Str(submission.TargetId) + " FOR UPDATE;");
                statements.Add(DirectorySql.Guard(stale, "stale"));
            }
            statements.AddRange(apply);
            statements.Add("UPDATE directorysubmissions SET status = 'Approved', revieweruserid = " + D.Str(reviewerUserId) + ", reviewernote = " + D.Str(reviewNote) + ", decidedutc = " + D.Ts(nowUtc) +
                ", resultid = " + D.Str(resultId) + ", targetid = COALESCE(targetid, " + D.Str(resultId) + "), updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + sid + ";");
            try
            {
                await UniqueTransactionAsync(statements, "An entry with this slug", token).ConfigureAwait(false);
            }
            catch (Exception e) when (DirectorySql.GuardCode(e) != null)
            {
                string? code = DirectorySql.GuardCode(e);
                if (code == "stale") throw new ApiErrorException(409, "SubmissionStale", "The target changed after this submission's diff was loaded; reload the submission and review the changes again.");
                throw new ApiErrorException(409, "NotPending", "This submission is no longer pending.");
            }
        }

        public async Task<bool> DecideAsync(string id, DirectorySubmissionStatusEnum status, string? reviewerUserId, string? reviewNote, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("UPDATE directorysubmissions SET status = " + D.Enum(status) + ", revieweruserid = " + D.Str(reviewerUserId) + ", reviewernote = " + D.Str(reviewNote) + ", decidedutc = " +
                D.Ts(nowUtc) + ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(id) + " AND status = 'Pending' RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<Dictionary<string, int>> PendingCountsAsync(IEnumerable<string> targetIds, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(targetIds);
            Dictionary<string, int> result = new Dictionary<string, int>(StringComparer.Ordinal);
            List<string> ids = DirectorySql.Distinct(targetIds);
            if (ids.Count == 0) return result;
            DataTable table = await QueryAsync("SELECT targetid, COUNT(*) AS cnt FROM directorysubmissions WHERE " + Scope() + " AND status = 'Pending' AND targetid IN " + D.InList(ids) + " GROUP BY targetid;", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows) result[RowReader.GetString(row, "targetid")] = (int)RowReader.GetLong(row, "cnt");
            return result;
        }

        public async Task<List<DirectorySubmission>> ClaimOverdueAsync(DateTime nowUtc, int limit, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("UPDATE directorysubmissions SET overduenotifiedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id IN (SELECT o.id FROM directorysubmissions o WHERE " + Scope("o") +
                " AND o.status = 'Pending' AND o.overduenotifiedutc IS NULL AND o.dueutc <= " + D.Ts(nowUtc) + " ORDER BY o.dueutc, o.id LIMIT " + D.Num(Math.Max(1, limit)) + ") AND overduenotifiedutc IS NULL RETURNING *;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public Task<long> CountPendingAsync(CancellationToken token = default)
        {
            return CountAsync("SELECT COUNT(*) AS cnt FROM directorysubmissions WHERE " + Scope() + " AND status = 'Pending';", token);
        }

        public async Task<List<DirectorySubmission>> ListByUserAsync(string userId, bool pendingOnly, string? afterId, int limit, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("SELECT s.* FROM directorysubmissions s WHERE " + Scope("s") + " AND s.submitteruserid = " + D.Str(userId) + (pendingOnly ? " AND s.status = 'Pending'" : String.Empty) +
                (afterId == null ? String.Empty : " AND s.id > " + D.Str(afterId)) + " ORDER BY s.id LIMIT " + D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            List<DirectorySubmission> list = table.Rows.Cast<DataRow>().Select(Map).ToList();
            await FillAsync(list, token).ConfigureAwait(false);
            return list;
        }

        public async Task<int> AnonymizeAsync(string userId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("UPDATE directorysubmissions SET submitteruserid = NULL, updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND submitteruserid = " + D.Str(userId) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        private string? FieldInsert(string submissionId, List<DirectorySubmissionField> fields)
        {
            if (fields.Count == 0) return null;
            return "INSERT INTO directorysubmissionfields (propertyid, id, submissionid, field, proposedvalue, currentvalue, position, isoverride) VALUES " + String.Join(", ", fields.Select(f => "(" + Pid + ", " +
                D.Str(String.IsNullOrEmpty(f.Id) ? IdGenerator.Generate(DirectoryIdPrefixes.SubmissionField) : f.Id) + ", " + D.Str(submissionId) + ", " + D.Str(f.Field) + ", " + D.Str(f.ProposedValue) + ", " +
                D.Str(f.CurrentValue) + ", " + D.Num(f.Position) + ", " + D.Bool(f.IsOverride) + ")")) + ";";
        }

        private async Task FillAsync(List<DirectorySubmission> submissions, CancellationToken token)
        {
            if (submissions.Count == 0) return;
            Dictionary<string, DirectorySubmission> byId = submissions.ToDictionary(s => s.Id, StringComparer.Ordinal);
            DataTable table = await QueryAsync("SELECT * FROM directorysubmissionfields WHERE " + Scope() + " AND submissionid IN " + D.InList(byId.Keys) + " ORDER BY submissionid, field, position, id;", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows)
            {
                if (!byId.TryGetValue(RowReader.GetString(row, "submissionid"), out DirectorySubmission? s)) continue;
                s.Fields.Add(new DirectorySubmissionField
                {
                    Id = RowReader.GetString(row, "id"),
                    Field = RowReader.GetString(row, "field"),
                    ProposedValue = RowReader.GetNullableString(row, "proposedvalue"),
                    CurrentValue = RowReader.GetNullableString(row, "currentvalue"),
                    Position = RowReader.GetInt(row, "position"),
                    IsOverride = RowReader.GetBool(row, "isoverride")
                });
            }
        }

        private static DirectorySubmission Map(DataRow row)
        {
            return new DirectorySubmission
            {
                Id = RowReader.GetString(row, "id"),
                Type = RowReader.GetEnum(row, "type", DirectorySubmissionTypeEnum.NewVenue),
                Status = RowReader.GetEnum(row, "status", DirectorySubmissionStatusEnum.Pending),
                TargetType = RowReader.GetEnum(row, "targettype", TargetTypeEnum.Venue),
                TargetId = RowReader.GetNullableString(row, "targetid"),
                TargetName = RowReader.GetNullableString(row, "targetname"),
                TargetUpdatedUtc = RowReader.GetNullableDateTime(row, "targetupdatedutc"),
                SubmitterUserId = RowReader.GetNullableString(row, "submitteruserid"),
                Note = RowReader.GetNullableString(row, "note"),
                ReviewerNote = RowReader.GetNullableString(row, "reviewernote"),
                ReviewerUserId = RowReader.GetNullableString(row, "revieweruserid"),
                DecidedUtc = RowReader.GetNullableDateTime(row, "decidedutc"),
                DueUtc = RowReader.GetDateTime(row, "dueutc"),
                OverdueNotifiedUtc = RowReader.GetNullableDateTime(row, "overduenotifiedutc"),
                ProposedLat = DirectorySql.NullableDouble(row, "proposedlat"),
                ProposedLng = DirectorySql.NullableDouble(row, "proposedlng"),
                ResultId = RowReader.GetNullableString(row, "resultid"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }
    }
}
