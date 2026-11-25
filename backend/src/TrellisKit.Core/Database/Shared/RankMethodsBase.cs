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

    internal abstract class RankMethodsBase : MethodsBase, IRankMethods
    {
        protected RankMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<List<MemberRank>> ReadMemberRanksAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("SELECT * FROM memberranks WHERE " + Scope() + " AND userid = " + D.Str(userId) + " ORDER BY fieldkey LIMIT 50;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MapRank).ToList();
        }

        public async Task<Dictionary<string, MemberRank>> ReadMemberRanksAsync(string fieldKey, IEnumerable<string> userIds, CancellationToken token = default)
        {
            RequireText(fieldKey, nameof(fieldKey));
            ArgumentNullException.ThrowIfNull(userIds);
            List<string> ids = DirectorySql.Distinct(userIds).Take(1000).ToList();
            Dictionary<string, MemberRank> result = new Dictionary<string, MemberRank>(StringComparer.Ordinal);
            if (ids.Count == 0) return result;
            DataTable table = await QueryAsync("SELECT * FROM memberranks WHERE " + Scope() + " AND lower(fieldkey) = " + D.Str(fieldKey.ToLowerInvariant()) +
                " AND userid IN " + D.InList(ids) + ";", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows)
            {
                MemberRank rank = MapRank(row);
                result[rank.UserId] = rank;
            }
            return result;
        }

        public async Task AwardAsync(RankPromotion award, MemberRank rank, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(award);
            ArgumentNullException.ThrowIfNull(rank);
            List<string> statements = new List<string>
            {
                InsertPromotion(award),
                "INSERT INTO memberranks (propertyid, id, userid, fieldkey, optionkey, level, status, promotionid, awardedby, awardedutc, createdutc, updatedutc) VALUES (" +
                    Pid + ", " + D.Str(rank.Id) + ", " + D.Str(rank.UserId) + ", " + D.Str(rank.FieldKey) + ", " + D.Str(rank.OptionKey) + ", " + D.Num(rank.Level) + ", " +
                    D.Enum(rank.Status) + ", " + D.Str(rank.PromotionId) + ", " + D.Str(rank.AwardedBy) + ", " + D.Ts(rank.AwardedUtc) + ", " + D.Ts(rank.CreatedUtc) + ", " + D.Ts(rank.UpdatedUtc) +
                    ") ON CONFLICT (propertyid, userid, lower(fieldkey)) DO UPDATE SET optionkey = EXCLUDED.optionkey, level = EXCLUDED.level, status = EXCLUDED.status, " +
                    "promotionid = EXCLUDED.promotionid, awardedby = EXCLUDED.awardedby, awardedutc = EXCLUDED.awardedutc, updatedutc = EXCLUDED.updatedutc;"
            };
            await TransactionAsync(statements, token).ConfigureAwait(false);
        }

        public async Task<bool> RevokeAsync(string awardId, RankPromotion revoke, CancellationToken token = default)
        {
            RequireText(awardId, nameof(awardId));
            ArgumentNullException.ThrowIfNull(revoke);
            List<string> statements = new List<string>
            {
                "DO $rank_revoke$ BEGIN UPDATE rankpromotions SET revokedutc = " + D.Ts(revoke.CreatedUtc) + ", updatedutc = " + D.Ts(revoke.CreatedUtc) + " WHERE " + Scope() +
                    " AND id = " + D.Str(awardId) + " AND action = 'Award' AND revokedutc IS NULL; IF NOT FOUND THEN RAISE EXCEPTION 'Award already revoked' USING ERRCODE = '23505'; END IF; END $rank_revoke$;",
                InsertPromotion(revoke),
                "UPDATE memberranks SET status = " + D.Enum(RankStatusEnum.Revoked) + ", updatedutc = " + D.Ts(revoke.CreatedUtc) + " WHERE " + Scope() + " AND promotionid = " + D.Str(awardId) + ";"
            };
            try
            {
                await TransactionAsync(statements, token).ConfigureAwait(false);
                return true;
            }
            catch (Exception e) when (D.IsUniqueViolation(e))
            {
                return false;
            }
        }

        public async Task<RankPromotion?> ReadPromotionAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) return null;
            DataTable table = await QueryAsync("SELECT * FROM rankpromotions WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : MapPromotion(table.Rows[0]);
        }

        public async Task<EnumerationResult<RankPromotion>> EnumeratePromotionsAsync(EnumerationRequest request, RankPromotionFilter filter, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            List<string> conditions = new List<string>();
            if (!String.IsNullOrEmpty(filter.UserId)) conditions.Add("p.userid = " + D.Str(filter.UserId));
            if (!String.IsNullOrEmpty(filter.FieldKey)) conditions.Add("lower(p.fieldkey) = " + D.Str(filter.FieldKey.ToLowerInvariant()));
            if (!String.IsNullOrEmpty(filter.OptionKey)) conditions.Add("lower(p.optionkey) = " + D.Str(filter.OptionKey.ToLowerInvariant()));
            if (!String.IsNullOrEmpty(filter.ActorId)) conditions.Add("p.actorid = " + D.Str(filter.ActorId));
            if (filter.Action != null) conditions.Add("p.action = " + D.Enum(filter.Action.Value));
            if (filter.Revoked != null) conditions.Add("p.action = 'Award' AND p.revokedutc IS " + (filter.Revoked.Value ? "NOT NULL" : "NULL"));
            if (filter.FromUtc != null) conditions.Add("p.createdutc >= " + D.Ts(filter.FromUtc.Value));
            if (filter.ToUtc != null) conditions.Add("p.createdutc < " + D.Ts(filter.ToUtc.Value));
            return await EnumerateAsync(request, "p.*", "rankpromotions p", conditions, MapPromotion, token).ConfigureAwait(false);
        }

        public async Task<List<RankPromotion>> ListPromotionsForUserAsync(string userId, string? afterId, int limit, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            string after = String.IsNullOrEmpty(afterId) ? String.Empty : " AND id > " + D.Str(afterId);
            DataTable table = await QueryAsync("SELECT * FROM rankpromotions WHERE " + Scope() + " AND userid = " + D.Str(userId) + after + " ORDER BY id LIMIT " + D.Num(Math.Clamp(limit, 1, 1000)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MapPromotion).ToList();
        }

        public async Task<List<RankPromotion>> RecentPromotionsForUserAsync(string userId, int limit, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("SELECT * FROM rankpromotions WHERE " + Scope() + " AND userid = " + D.Str(userId) + " ORDER BY createdutc DESC, id DESC LIMIT " + D.Num(Math.Clamp(limit, 1, 100)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MapPromotion).ToList();
        }

        public Task<long> CountPromotionsForUserAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            return CountAsync("SELECT COUNT(*) AS cnt FROM rankpromotions WHERE " + Scope() + " AND userid = " + D.Str(userId) + ";", token);
        }

        public async Task<List<RankRequirement>> ListRequirementsAsync(string fieldKey, CancellationToken token = default)
        {
            RequireText(fieldKey, nameof(fieldKey));
            DataTable table = await QueryAsync("SELECT * FROM rankrequirements WHERE " + Scope() + " AND lower(fieldkey) = " + D.Str(fieldKey.ToLowerInvariant()) +
                " ORDER BY position, id LIMIT 200;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MapRequirement).ToList();
        }

        public async Task<EnumerationResult<RankRequirement>> EnumerateRequirementsAsync(EnumerationRequest request, string? fieldKey, string? optionKey, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            List<string> conditions = new List<string>();
            if (!String.IsNullOrEmpty(fieldKey)) conditions.Add("lower(q.fieldkey) = " + D.Str(fieldKey.ToLowerInvariant()));
            if (!String.IsNullOrEmpty(optionKey)) conditions.Add("lower(q.optionkey) = " + D.Str(optionKey.ToLowerInvariant()));
            return await EnumerateAsync(request, "q.*", "rankrequirements q", conditions, MapRequirement, token).ConfigureAwait(false);
        }

        public async Task ReplaceRequirementsAsync(string fieldKey, List<RankRequirement> requirements, CancellationToken token = default)
        {
            RequireText(fieldKey, nameof(fieldKey));
            ArgumentNullException.ThrowIfNull(requirements);
            string field = "lower(fieldkey) = " + D.Str(fieldKey.ToLowerInvariant());
            List<string> statements = new List<string>();
            statements.AddRange(Driver.Metadata.DeleteWhereStatements(TargetTypeEnum.RankRequirement, "SELECT id FROM rankrequirements WHERE " + Scope() + " AND " + field));
            statements.Add("DELETE FROM rankrequirements WHERE " + Scope() + " AND " + field + ";");
            for (int i = 0; i < requirements.Count; i++)
            {
                RankRequirement r = requirements[i];
                statements.Add("INSERT INTO rankrequirements (propertyid, id, fieldkey, optionkey, level, minimumdaysingrade, minimumattendance, notes, curriculumid, position, createdutc, updatedutc) VALUES (" +
                    Pid + ", " + D.Str(r.Id) + ", " + D.Str(r.FieldKey) + ", " + D.Str(r.OptionKey) + ", " + D.Num(r.Level) + ", " + D.Num(r.MinimumDaysInGrade) + ", " + D.Num(r.MinimumAttendance) + ", " +
                    D.Str(r.Notes) + ", " + D.Str(r.CurriculumId) + ", " + D.Num(i) + ", " + D.Ts(r.CreatedUtc) + ", " + D.Ts(r.UpdatedUtc) + ");");
            }
            await UniqueTransactionAsync(statements, "A requirement for this rank", token).ConfigureAwait(false);
        }

        public async Task<List<LineageEntry>> ListLineageAsync(string userId, string? fieldKey, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            string field = String.IsNullOrEmpty(fieldKey) ? String.Empty : " AND lower(fieldkey) = " + D.Str(fieldKey.ToLowerInvariant());
            DataTable table = await QueryAsync("SELECT * FROM lineageentries WHERE " + Scope() + " AND userid = " + D.Str(userId) + field + " ORDER BY fieldkey, position, id LIMIT 200;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MapLineage).ToList();
        }

        public async Task<LineageEntry?> ReadLineageAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) return null;
            DataTable table = await QueryAsync("SELECT * FROM lineageentries WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : MapLineage(table.Rows[0]);
        }

        public async Task ReplaceLineageAsync(string userId, string fieldKey, List<LineageEntry> entries, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            RequireText(fieldKey, nameof(fieldKey));
            ArgumentNullException.ThrowIfNull(entries);
            string where = Scope() + " AND userid = " + D.Str(userId) + " AND lower(fieldkey) = " + D.Str(fieldKey.ToLowerInvariant());
            List<string> keep = entries.Where(e => !String.IsNullOrEmpty(e.Id)).Select(e => e.Id).ToList();
            string removed = "SELECT id FROM lineageentries WHERE " + where + (keep.Count == 0 ? String.Empty : " AND id NOT IN " + D.InList(keep));
            List<string> statements = new List<string>();
            statements.AddRange(Driver.Metadata.DeleteWhereStatements(TargetTypeEnum.LineageEntry, removed));
            statements.Add("DELETE FROM lineageentries WHERE " + where + (keep.Count == 0 ? String.Empty : " AND id NOT IN " + D.InList(keep)) + ";");
            for (int i = 0; i < entries.Count; i++)
            {
                LineageEntry e = entries[i];
                statements.Add("INSERT INTO lineageentries (propertyid, id, userid, fieldkey, optionkey, promotedbyuserid, promotedbyname, promotedutc, verified, verifiedby, verifiedutc, position, createdutc, updatedutc) VALUES (" +
                    Pid + ", " + D.Str(e.Id) + ", " + D.Str(userId) + ", " + D.Str(e.FieldKey) + ", " + D.Str(e.OptionKey) + ", " + D.Str(e.PromotedByUserId) + ", " + D.Str(e.PromotedByName) + ", " +
                    D.Ts(e.PromotedUtc) + ", " + D.Bool(e.Verified) + ", " + D.Str(e.VerifiedBy) + ", " + D.Ts(e.VerifiedUtc) + ", " + D.Num(i) + ", " + D.Ts(e.CreatedUtc) + ", " + D.Ts(e.UpdatedUtc) +
                    ") ON CONFLICT (id) DO UPDATE SET optionkey = EXCLUDED.optionkey, promotedbyuserid = EXCLUDED.promotedbyuserid, promotedbyname = EXCLUDED.promotedbyname, " +
                    "promotedutc = EXCLUDED.promotedutc, verified = EXCLUDED.verified, verifiedby = EXCLUDED.verifiedby, verifiedutc = EXCLUDED.verifiedutc, position = EXCLUDED.position, updatedutc = EXCLUDED.updatedutc " +
                    "WHERE lineageentries.propertyid = " + Pid + " AND lineageentries.userid = " + D.Str(userId) + ";");
            }
            await TransactionAsync(statements, token).ConfigureAwait(false);
        }

        public async Task<bool> VerifyLineageAsync(string id, string? verifierId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("UPDATE lineageentries SET verified = TRUE, verifiedby = " + D.Str(verifierId) + ", verifiedutc = " + D.Ts(nowUtc) + ", updatedutc = " + D.Ts(nowUtc) +
                " WHERE " + Scope() + " AND id = " + D.Str(id) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<EnumerationResult<string>> EnumerateCandidatesAsync(EnumerationRequest request, string fieldId, string fieldKey, string? optionKey, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            RequireText(fieldId, nameof(fieldId));
            RequireText(fieldKey, nameof(fieldKey));
            string key = D.Str(fieldKey.ToLowerInvariant());
            string from = "users u LEFT JOIN profilevalues pv ON pv.propertyid = u.propertyid AND pv.userid = u.id AND pv.fieldid = " + D.Str(fieldId) +
                " LEFT JOIN memberranks mr ON mr.propertyid = u.propertyid AND mr.userid = u.id AND lower(mr.fieldkey) = " + key;
            List<string> conditions = new List<string>
            {
                "u.isdeleted = " + D.Bool(false),
                "u.isbanned = " + D.Bool(false),
                "(pv.valuetext IS NOT NULL OR mr.id IS NOT NULL)"
            };
            if (!String.IsNullOrEmpty(optionKey))
            {
                string option = D.Str(optionKey.ToLowerInvariant());
                conditions.Add("((mr.status = 'Verified' AND lower(mr.optionkey) = " + option + ") OR ((mr.id IS NULL OR mr.status <> 'Verified') AND lower(pv.valuetext) = " + option + "))");
            }
            return await EnumerateAsync(request, "u.id", from, conditions, row => RowReader.GetString(row, "id"), token).ConfigureAwait(false);
        }

        public async Task EraseForUserAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            string user = D.Str(userId);
            List<string> statements = new List<string>();
            statements.AddRange(Driver.Metadata.DeleteWhereStatements(TargetTypeEnum.MemberRank, "SELECT id FROM memberranks WHERE " + Scope() + " AND userid = " + user));
            statements.AddRange(Driver.Metadata.DeleteWhereStatements(TargetTypeEnum.RankPromotion, "SELECT id FROM rankpromotions WHERE " + Scope() + " AND userid = " + user));
            statements.AddRange(Driver.Metadata.DeleteWhereStatements(TargetTypeEnum.LineageEntry, "SELECT id FROM lineageentries WHERE " + Scope() + " AND userid = " + user));
            statements.Add("DELETE FROM memberranks WHERE " + Scope() + " AND userid = " + user + ";");
            statements.Add("DELETE FROM rankpromotions WHERE " + Scope() + " AND userid = " + user + ";");
            statements.Add("DELETE FROM lineageentries WHERE " + Scope() + " AND userid = " + user + ";");
            statements.Add("UPDATE lineageentries SET promotedbyuserid = NULL, updatedutc = " + D.Ts(DateTime.UtcNow) + " WHERE " + Scope() + " AND promotedbyuserid = " + user + ";");
            await TransactionAsync(statements, token).ConfigureAwait(false);
        }

        private string InsertPromotion(RankPromotion p)
        {
            return "INSERT INTO rankpromotions (propertyid, id, userid, fieldkey, optionkey, level, action, actorid, note, eventid, reason, awardid, revokedutc, createdutc, updatedutc) VALUES (" +
                Pid + ", " + D.Str(p.Id) + ", " + D.Str(p.UserId) + ", " + D.Str(p.FieldKey) + ", " + D.Str(p.OptionKey) + ", " + D.Num(p.Level) + ", " + D.Enum(p.Action) + ", " +
                D.Str(p.ActorId) + ", " + D.Str(p.Note) + ", " + D.Str(p.EventId) + ", " + D.Str(p.Reason) + ", " + D.Str(p.AwardId) + ", " + D.Ts(p.RevokedUtc) + ", " +
                D.Ts(p.CreatedUtc) + ", " + D.Ts(p.UpdatedUtc) + ");";
        }

        private static MemberRank MapRank(DataRow row)
        {
            return new MemberRank
            {
                Id = RowReader.GetString(row, "id"),
                UserId = RowReader.GetString(row, "userid"),
                FieldKey = RowReader.GetString(row, "fieldkey"),
                OptionKey = RowReader.GetString(row, "optionkey"),
                Level = RowReader.GetInt(row, "level"),
                Status = RowReader.GetEnum(row, "status", RankStatusEnum.Verified),
                PromotionId = RowReader.GetNullableString(row, "promotionid"),
                AwardedBy = RowReader.GetNullableString(row, "awardedby"),
                AwardedUtc = RowReader.GetDateTime(row, "awardedutc"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }

        private static RankPromotion MapPromotion(DataRow row)
        {
            return new RankPromotion
            {
                Id = RowReader.GetString(row, "id"),
                UserId = RowReader.GetString(row, "userid"),
                FieldKey = RowReader.GetString(row, "fieldkey"),
                OptionKey = RowReader.GetString(row, "optionkey"),
                Level = RowReader.GetInt(row, "level"),
                Action = RowReader.GetEnum(row, "action", RankPromotionActionEnum.Award),
                ActorId = RowReader.GetNullableString(row, "actorid"),
                Note = RowReader.GetNullableString(row, "note"),
                EventId = RowReader.GetNullableString(row, "eventid"),
                Reason = RowReader.GetNullableString(row, "reason"),
                AwardId = RowReader.GetNullableString(row, "awardid"),
                RevokedUtc = RowReader.GetNullableDateTime(row, "revokedutc"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }

        private static RankRequirement MapRequirement(DataRow row)
        {
            return new RankRequirement
            {
                Id = RowReader.GetString(row, "id"),
                FieldKey = RowReader.GetString(row, "fieldkey"),
                OptionKey = RowReader.GetString(row, "optionkey"),
                Level = RowReader.GetNullableInt(row, "level"),
                MinimumDaysInGrade = RowReader.GetInt(row, "minimumdaysingrade"),
                MinimumAttendance = RowReader.GetInt(row, "minimumattendance"),
                Notes = RowReader.GetNullableString(row, "notes"),
                CurriculumId = RowReader.GetNullableString(row, "curriculumid"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }

        private static LineageEntry MapLineage(DataRow row)
        {
            return new LineageEntry
            {
                Id = RowReader.GetString(row, "id"),
                UserId = RowReader.GetString(row, "userid"),
                FieldKey = RowReader.GetString(row, "fieldkey"),
                OptionKey = RowReader.GetString(row, "optionkey"),
                PromotedByUserId = RowReader.GetNullableString(row, "promotedbyuserid"),
                PromotedByName = RowReader.GetNullableString(row, "promotedbyname"),
                PromotedUtc = RowReader.GetNullableDateTime(row, "promotedutc"),
                Verified = RowReader.GetBool(row, "verified"),
                VerifiedBy = RowReader.GetNullableString(row, "verifiedby"),
                VerifiedUtc = RowReader.GetNullableDateTime(row, "verifiedutc"),
                Position = RowReader.GetInt(row, "position"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }
    }
}
