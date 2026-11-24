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
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Shared sign-up code redemption methods.
    /// </summary>
    internal abstract class SignupCodeRedemptionMethodsBase : MethodsBase, ISignupCodeRedemptionMethods
    {
        private string Fields
        {
            get
            {
                return "r.id, r.signupcodeid, r.userid, r.redeemedutc, r.membershipstartsutc, r.membershipendsutc, r.entitlementid, r.sourceip, r.useragent, " +
                    "r.isnewaccount, r.revokedutc, r.revokedbyuserid, r.revokereason, r.reminderstage, " +
                    "CASE WHEN EXISTS (SELECT 1 FROM subscriptions s WHERE " + Scope("s") + " AND s.userid = r.userid AND s.createdutc >= r.redeemedutc) THEN 1 ELSE 0 END AS convertedtopaid";
            }
        }

        private string SelectSql
        {
            get { return "SELECT " + Fields + " FROM signupcoderedemptions r WHERE " + Scope("r"); }
        }

        protected SignupCodeRedemptionMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<SignupCodeRedemption?> ReadAsync(string id, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync(SelectSql + " AND r.id = " + D.Str(id) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        public async Task<SignupCodeRedemption?> ReadByCodeAndUserAsync(string signupCodeId, string userId, CancellationToken token = default)
        {
            RequireText(signupCodeId, nameof(signupCodeId));
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync(SelectSql + " AND r.signupcodeid = " + D.Str(signupCodeId) + " AND r.userid = " + D.Str(userId) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        public Task<EnumerationResult<SignupCodeRedemption>> EnumerateAsync(EnumerationRequest request, RedemptionFilter filter, DateTime nowUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            List<string> conditions = new List<string>();
            if (filter.SignupCodeId != null) conditions.Add("r.signupcodeid = " + D.Str(filter.SignupCodeId));
            if (filter.UserId != null) conditions.Add("r.userid = " + D.Str(filter.UserId));
            if (filter.Status != null)
            {
                string n = D.Ts(nowUtc);
                switch (filter.Status.Value)
                {
                    case RedemptionStatusEnum.Revoked:
                        conditions.Add("r.revokedutc IS NOT NULL");
                        break;
                    case RedemptionStatusEnum.Expired:
                        conditions.Add("r.revokedutc IS NULL AND r.membershipendsutc IS NOT NULL AND r.membershipendsutc <= " + n);
                        break;
                    default:
                        conditions.Add("r.revokedutc IS NULL AND (r.membershipendsutc IS NULL OR r.membershipendsutc > " + n + ")");
                        break;
                }
            }
            return EnumerateAsync(request, Fields, "signupcoderedemptions r LEFT JOIN users u ON u.id = r.userid AND " + Scope("u"), conditions, Map, token);
        }

        public async Task<List<SignupCodeRedemption>> EnumerateByUserAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable rows = await QueryAsync(SelectSql + " AND r.userid = " + D.Str(userId) + " ORDER BY r.redeemedutc DESC, r.id DESC;", token).ConfigureAwait(false);
            return rows.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task<List<SignupCodeRedemption>> EnumerateByUserAfterAsync(string userId, string? afterId, int limit, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            string after = afterId == null ? String.Empty : " AND r.id > " + D.Str(afterId);
            DataTable table = await QueryAsync(SelectSql + " AND (r.userid = " + D.Str(userId) + ")" + after + " ORDER BY r.id LIMIT " + D.Num(Math.Clamp(limit, 1, 1000)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task UpdateAsync(SignupCodeRedemption redemption, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(redemption);
            SignupCodeRedemption r = redemption;
            await QueryAsync("UPDATE signupcoderedemptions SET revokedutc = " + D.Ts(r.RevokedUtc) + ", revokedbyuserid = " + D.Str(r.RevokedByUserId) +
                ", revokereason = " + D.Str(r.RevokeReason) + ", reminderstage = " + D.Num(r.ReminderStage) + ", membershipendsutc = " + D.Ts(r.MembershipEndsUtc) +
                " WHERE " + Scope() + " AND id = " + D.Str(r.Id) + ";", token).ConfigureAwait(false);
        }

        public async Task<List<SignupCodeRedemption>> EnumerateDueForReminderAsync(DateTime nowUtc, DateTime horizonUtc, int belowStage, int limit, CancellationToken token = default)
        {
            DataTable rows = await QueryAsync(SelectSql + " AND r.revokedutc IS NULL AND r.membershipendsutc IS NOT NULL AND r.membershipendsutc > " + D.Ts(nowUtc) +
                " AND r.membershipendsutc <= " + D.Ts(horizonUtc) + " AND r.reminderstage < " + D.Num(belowStage) +
                " ORDER BY r.membershipendsutc, r.id LIMIT " + D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            return rows.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task<bool> TrySetReminderStageAsync(string id, int stage, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("UPDATE signupcoderedemptions SET reminderstage = " + D.Num(stage) + " WHERE " + Scope() + " AND id = " + D.Str(id) +
                " AND reminderstage < " + D.Num(stage) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public Task<long> CountAsync(string? signupCodeId, DateTime? fromUtc, CancellationToken token = default)
        {
            List<string> conditions = new List<string> { Scope() };
            if (signupCodeId != null) conditions.Add("signupcodeid = " + D.Str(signupCodeId));
            if (fromUtc != null) conditions.Add("redeemedutc >= " + D.Ts(fromUtc.Value));
            return CountAsync("SELECT COUNT(*) AS cnt FROM signupcoderedemptions" + Where(conditions) + ";", token);
        }

        public async Task<List<DailyCount>> CountByDayAsync(string? signupCodeId, DateTime fromUtc, CancellationToken token = default)
        {
            string day = D.DayOf("redeemedutc");
            List<string> conditions = new List<string> { Scope(), "redeemedutc >= " + D.Ts(fromUtc) };
            if (signupCodeId != null) conditions.Add("signupcodeid = " + D.Str(signupCodeId));
            DataTable rows = await QueryAsync("SELECT " + day + " AS day, COUNT(*) AS cnt FROM signupcoderedemptions" + Where(conditions) +
                " GROUP BY " + day + " ORDER BY " + day + ";", token).ConfigureAwait(false);
            return rows.Rows.Cast<DataRow>().Select(r => new DailyCount { Date = RowReader.GetString(r, "day"), Count = RowReader.GetLong(r, "cnt") }).ToList();
        }

        public async Task<RedemptionOutcomes> CountOutcomesAsync(string? signupCodeId, DateTime nowUtc, CancellationToken token = default)
        {
            string n = D.Ts(nowUtc);
            string sql = "SELECT " +
                "SUM(CASE WHEN r.revokedutc IS NULL AND (r.membershipendsutc IS NULL OR r.membershipendsutc > " + n + ") THEN 1 ELSE 0 END) AS active, " +
                "SUM(CASE WHEN r.revokedutc IS NOT NULL THEN 1 ELSE 0 END) AS revoked, " +
                "SUM(CASE WHEN EXISTS (SELECT 1 FROM subscriptions s WHERE " + Scope("s") + " AND s.userid = r.userid AND s.createdutc >= r.redeemedutc) THEN 1 ELSE 0 END) AS converted, " +
                "SUM(CASE WHEN r.revokedutc IS NULL AND r.membershipendsutc IS NOT NULL AND r.membershipendsutc <= " + n + " AND NOT " +
                ActiveEntitlementExists("r.userid", nowUtc, null) + " THEN 1 ELSE 0 END) AS lapsed " +
                "FROM signupcoderedemptions r WHERE " + Scope("r") + (signupCodeId == null ? String.Empty : " AND r.signupcodeid = " + D.Str(signupCodeId)) + ";";
            DataTable table = await QueryAsync(sql, token).ConfigureAwait(false);
            RedemptionOutcomes outcomes = new RedemptionOutcomes();
            if (table.Rows.Count == 0) return outcomes;
            DataRow row = table.Rows[0];
            outcomes.Active = RowReader.GetLong(row, "active");
            outcomes.Revoked = RowReader.GetLong(row, "revoked");
            outcomes.ConvertedToPaid = RowReader.GetLong(row, "converted");
            outcomes.Lapsed = RowReader.GetLong(row, "lapsed");
            return outcomes;
        }

        private static SignupCodeRedemption Map(DataRow row)
        {
            return new SignupCodeRedemption
            {
                Id = RowReader.GetString(row, "id"),
                SignupCodeId = RowReader.GetString(row, "signupcodeid"),
                UserId = RowReader.GetString(row, "userid"),
                RedeemedUtc = RowReader.GetDateTime(row, "redeemedutc"),
                MembershipStartsUtc = RowReader.GetDateTime(row, "membershipstartsutc"),
                MembershipEndsUtc = RowReader.GetNullableDateTime(row, "membershipendsutc"),
                EntitlementId = RowReader.GetString(row, "entitlementid"),
                SourceIp = RowReader.GetNullableString(row, "sourceip"),
                UserAgent = RowReader.GetNullableString(row, "useragent"),
                IsNewAccount = RowReader.GetBool(row, "isnewaccount"),
                RevokedUtc = RowReader.GetNullableDateTime(row, "revokedutc"),
                RevokedByUserId = RowReader.GetNullableString(row, "revokedbyuserid"),
                RevokeReason = RowReader.GetNullableString(row, "revokereason"),
                ReminderStage = RowReader.GetInt(row, "reminderstage"),
                ConvertedToPaid = RowReader.GetBool(row, "convertedtopaid")
            };
        }
    }
}
