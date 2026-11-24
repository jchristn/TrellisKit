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
    /// Shared sign-up code methods. The atomic redemption statement is provider-specific (see BuildRedeemStatements).
    /// </summary>
    internal abstract class SignupCodeMethodsBase : MethodsBase, ISignupCodeMethods
    {
        private const string _Columns =
            "id, code, name, description, notes, granttype, grantmonths, grantendutc, maxredemptions, redemptioncount, startsutc, expiresutc, " +
            "isactive, batchid, createdbyuserid, createdutc, updatedutc, lastredeemedutc, discountpercent, discountamountcents, discountduration, stripecouponid";

        protected const string RedemptionColumns =
            "propertyid, id, signupcodeid, userid, redeemedutc, membershipstartsutc, membershipendsutc, entitlementid, sourceip, useragent, isnewaccount, " +
            "revokedutc, revokedbyuserid, revokereason, reminderstage";

        protected const string EntitlementColumns =
            "propertyid, id, userid, source, sourceid, startsutc, endsutc, revokedutc, revokereason, reason, granttype, grantedbyuserid, createdutc, updatedutc";

        protected SignupCodeMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<SignupCode> CreateAsync(SignupCode code, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(code);
            await UniqueAsync(Insert(code), "That code", token).ConfigureAwait(false);
            return code;
        }

        public async Task<SignupCodeBatch> CreateBatchAsync(SignupCodeBatch batch, List<SignupCode> codes, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(codes);
            List<string> statements = new List<string>
            {
                "INSERT INTO signupcodebatches (propertyid, id, name, quantity, granttype, grantmonths, grantendutc, expiresutc, createdbyuserid, createdutc, discountpercent, discountamountcents, discountduration) VALUES (" +
                Pid + ", " + D.Str(batch.Id) + ", " + D.Str(batch.Name) + ", " + D.Num(batch.Quantity) + ", " + D.Enum(batch.GrantType) + ", " + D.Num(batch.GrantMonths) + ", " +
                D.Ts(batch.GrantEndUtc) + ", " + D.Ts(batch.ExpiresUtc) + ", " + D.Str(batch.CreatedByUserId) + ", " + D.Ts(batch.CreatedUtc) + ", " +
                D.Num(batch.DiscountPercent) + ", " + D.Num(batch.DiscountAmountCents) + ", " + D.Enum(batch.DiscountDuration) + ");"
            };
            foreach (SignupCode code in codes)
            {
                code.BatchId = batch.Id;
                statements.Add(Insert(code));
            }
            await UniqueTransactionAsync(statements, "A generated code", token).ConfigureAwait(false);
            return batch;
        }

        public Task<SignupCode?> ReadAsync(string id, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            return ReadOneAsync("id = " + D.Str(id), token);
        }

        public Task<SignupCode?> ReadByCodeAsync(string code, CancellationToken token = default)
        {
            RequireText(code, nameof(code));
            return ReadOneAsync(D.EqualsIgnoreCase("code", code.Trim()), token);
        }

        public async Task<List<SignupCode>> ReadManyAsync(IEnumerable<string> ids, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ids);
            List<string> list = ids.Where(i => !String.IsNullOrEmpty(i)).Distinct().ToList();
            if (list.Count == 0) return new List<SignupCode>();
            DataTable table = await QueryAsync("SELECT " + _Columns + " FROM signupcodes WHERE " + Scope() + " AND id IN " + D.InList(list) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task<bool> DeleteUnredeemedAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            DataTable table = await QueryAsync(
                "DELETE FROM signupcodes WHERE " + Scope() + " AND id = " + D.Str(id) + " AND redemptioncount = 0" +
                " AND NOT EXISTS (SELECT 1 FROM signupcoderedemptions x WHERE " + Scope("x") + " AND x.signupcodeid = " + D.Str(id) + ") RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        /// <inheritdoc />
        public async Task<SignupCode> UpdateAsync(SignupCode code, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(code);
            SignupCode c = code;
            string sql =
                "UPDATE signupcodes SET code = " + D.Str(c.Code) + ", name = " + D.Str(c.Name) + ", description = " + D.Str(c.Description) +
                ", notes = " + D.Str(c.Notes) + ", granttype = " + D.Enum(c.GrantType) + ", grantmonths = " + D.Num(c.GrantMonths) +
                ", grantendutc = " + D.Ts(c.GrantEndUtc) + ", maxredemptions = " + D.Num(c.MaxRedemptions) + ", startsutc = " + D.Ts(c.StartsUtc) +
                ", expiresutc = " + D.Ts(c.ExpiresUtc) + ", isactive = " + D.Bool(c.IsActive) + ", updatedutc = " + D.Ts(c.UpdatedUtc) +
                ", discountpercent = " + D.Num(c.DiscountPercent) + ", discountamountcents = " + D.Num(c.DiscountAmountCents) +
                ", discountduration = " + D.Enum(c.DiscountDuration) + ", stripecouponid = " + D.Str(c.StripeCouponId) +
                " WHERE " + Scope() + " AND id = " + D.Str(c.Id) + ";";
            await UniqueAsync(sql, "That code", token).ConfigureAwait(false);
            return code;
        }

        public Task<EnumerationResult<SignupCode>> EnumerateAsync(EnumerationRequest request, SignupCodeFilter filter, DateTime nowUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            List<string> conditions = new List<string>();
            if (filter.Status != null) conditions.Add(StatusCondition(filter.Status.Value, nowUtc));
            if (filter.BatchId != null) conditions.Add("c.batchid = " + D.Str(filter.BatchId));
            string select = String.Join(", ", _Columns.Split(',').Select(c => "c." + c.Trim()));
            return EnumerateAsync(request, select, "signupcodes c", conditions, Map, token);
        }

        public Task<long> CountByStatusAsync(SignupCodeStatusEnum status, DateTime nowUtc, CancellationToken token = default)
        {
            return CountAsync("SELECT COUNT(*) AS cnt FROM signupcodes WHERE " + Scope() + " AND " + StatusCondition(status, nowUtc) + ";", token);
        }

        public async Task<bool> TryRedeemAsync(SignupCodeRedemption redemption, Entitlement entitlement, DateTime nowUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(redemption);
            ArgumentNullException.ThrowIfNull(entitlement);
            List<string> statements = BuildRedeemStatements(redemption, entitlement, nowUtc);
            DataTable result = await UniqueTransactionAsync(statements, "A redemption of that code by this member", token).ConfigureAwait(false);
            return result.Rows.Count > 0;
        }

        public async Task<bool> TryRecordDiscountAsync(SignupCodeRedemption redemption, DateTime nowUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(redemption);
            List<string> statements = BuildDiscountStatements(redemption, nowUtc);
            DataTable result = await UniqueTransactionAsync(statements, "A redemption of that code by this member", token).ConfigureAwait(false);
            return result.Rows.Count > 0;
        }

        /// <summary>
        /// Statements that atomically increment the redemption count when the discount code is still redeemable and the
        /// member has not used it, then insert the redemption only when the increment happened. The last statement must
        /// return one row when the redemption was recorded and none otherwise.
        /// </summary>
        protected abstract List<string> BuildDiscountStatements(SignupCodeRedemption redemption, DateTime nowUtc);

        /// <summary>
        /// Statements that atomically increment the redemption count when the code is still redeemable and the member has
        /// not redeemed it, then insert the entitlement and redemption only when the increment happened. The last statement
        /// must return one row when the redemption was recorded and none otherwise.
        /// </summary>
        protected abstract List<string> BuildRedeemStatements(SignupCodeRedemption redemption, Entitlement entitlement, DateTime nowUtc);

        protected string RedeemUpdateSql(SignupCodeRedemption r, DateTime nowUtc)
        {
            return "UPDATE signupcodes SET redemptioncount = redemptioncount + 1, lastredeemedutc = " + D.Ts(nowUtc) + ", updatedutc = " + D.Ts(nowUtc) +
                " WHERE " + Scope() + " AND id = " + D.Str(r.SignupCodeId) + " AND " + StatusCondition(SignupCodeStatusEnum.Active, nowUtc) +
                " AND NOT EXISTS (SELECT 1 FROM signupcoderedemptions x WHERE " + Scope("x") + " AND x.signupcodeid = " + D.Str(r.SignupCodeId) + " AND x.userid = " + D.Str(r.UserId) + ")";
        }

        protected string EntitlementValues(Entitlement e)
        {
            return Pid + ", " + D.Str(e.Id) + ", " + D.Str(e.UserId) + ", " + D.Enum(e.Source) + ", " + D.Str(e.SourceId) + ", " + D.Ts(e.StartsUtc) + ", " + D.Ts(e.EndsUtc) + ", " +
                D.Ts(e.RevokedUtc) + ", " + D.Str(e.RevokeReason) + ", " + D.Str(e.Reason) + ", " + D.Enum(e.GrantType) + ", " + D.Str(e.GrantedByUserId) + ", " + D.Ts(e.CreatedUtc) + ", " + D.Ts(e.UpdatedUtc);
        }

        protected string RedemptionValues(SignupCodeRedemption r)
        {
            return Pid + ", " + D.Str(r.Id) + ", " + D.Str(r.SignupCodeId) + ", " + D.Str(r.UserId) + ", " + D.Ts(r.RedeemedUtc) + ", " + D.Ts(r.MembershipStartsUtc) + ", " +
                D.Ts(r.MembershipEndsUtc) + ", " + D.Str(String.IsNullOrEmpty(r.EntitlementId) ? null : r.EntitlementId) + ", " + D.Str(r.SourceIp) + ", " + D.Str(r.UserAgent) + ", " + D.Bool(r.IsNewAccount) + ", " +
                D.Ts(r.RevokedUtc) + ", " + D.Str(r.RevokedByUserId) + ", " + D.Str(r.RevokeReason) + ", " + D.Num(r.ReminderStage);
        }

        private string StatusCondition(SignupCodeStatusEnum status, DateTime nowUtc)
        {
            string n = D.Ts(nowUtc);
            string active = "isactive = " + D.Bool(true);
            string started = "(startsutc IS NULL OR startsutc <= " + n + ")";
            string unexpired = "(expiresutc IS NULL OR expiresutc > " + n + ")";
            switch (status)
            {
                case SignupCodeStatusEnum.Inactive:
                    return "isactive = " + D.Bool(false);
                case SignupCodeStatusEnum.Scheduled:
                    return "(" + active + " AND startsutc IS NOT NULL AND startsutc > " + n + ")";
                case SignupCodeStatusEnum.Expired:
                    return "(" + active + " AND " + started + " AND expiresutc IS NOT NULL AND expiresutc <= " + n + ")";
                case SignupCodeStatusEnum.Exhausted:
                    return "(" + active + " AND " + started + " AND " + unexpired + " AND maxredemptions IS NOT NULL AND redemptioncount >= maxredemptions)";
                default:
                    return "(" + active + " AND " + started + " AND " + unexpired + " AND (maxredemptions IS NULL OR redemptioncount < maxredemptions))";
            }
        }

        private string Insert(SignupCode c)
        {
            return "INSERT INTO signupcodes (propertyid, " + _Columns + ") VALUES (" +
                Pid + ", " + D.Str(c.Id) + ", " + D.Str(c.Code) + ", " + D.Str(c.Name) + ", " + D.Str(c.Description) + ", " + D.Str(c.Notes) + ", " + D.Enum(c.GrantType) + ", " +
                D.Num(c.GrantMonths) + ", " + D.Ts(c.GrantEndUtc) + ", " + D.Num(c.MaxRedemptions) + ", " + D.Num(c.RedemptionCount) + ", " + D.Ts(c.StartsUtc) + ", " +
                D.Ts(c.ExpiresUtc) + ", " + D.Bool(c.IsActive) + ", " + D.Str(c.BatchId) + ", " + D.Str(c.CreatedByUserId) + ", " + D.Ts(c.CreatedUtc) + ", " +
                D.Ts(c.UpdatedUtc) + ", " + D.Ts(c.LastRedeemedUtc) + ", " + D.Num(c.DiscountPercent) + ", " + D.Num(c.DiscountAmountCents) + ", " +
                D.Enum(c.DiscountDuration) + ", " + D.Str(c.StripeCouponId) + ");";
        }

        private async Task<SignupCode?> ReadOneAsync(string condition, CancellationToken token)
        {
            DataTable table = await QueryAsync("SELECT " + _Columns + " FROM signupcodes WHERE " + Scope() + " AND (" + condition + ");", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        private static SignupCode Map(DataRow row)
        {
            return new SignupCode
            {
                Id = RowReader.GetString(row, "id"),
                Code = RowReader.GetString(row, "code"),
                Name = RowReader.GetString(row, "name"),
                Description = RowReader.GetNullableString(row, "description"),
                Notes = RowReader.GetNullableString(row, "notes"),
                GrantType = RowReader.GetEnum(row, "granttype", GrantTypeEnum.DurationMonths),
                GrantMonths = RowReader.GetNullableInt(row, "grantmonths"),
                GrantEndUtc = RowReader.GetNullableDateTime(row, "grantendutc"),
                MaxRedemptions = RowReader.GetNullableInt(row, "maxredemptions"),
                RedemptionCount = RowReader.GetInt(row, "redemptioncount"),
                StartsUtc = RowReader.GetNullableDateTime(row, "startsutc"),
                ExpiresUtc = RowReader.GetNullableDateTime(row, "expiresutc"),
                IsActive = RowReader.GetBool(row, "isactive"),
                BatchId = RowReader.GetNullableString(row, "batchid"),
                CreatedByUserId = RowReader.GetNullableString(row, "createdbyuserid"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc"),
                LastRedeemedUtc = RowReader.GetNullableDateTime(row, "lastredeemedutc"),
                DiscountPercent = RowReader.GetNullableInt(row, "discountpercent"),
                DiscountAmountCents = RowReader.GetNullableInt(row, "discountamountcents"),
                DiscountDuration = RowReader.GetNullableEnum<DiscountDurationEnum>(row, "discountduration"),
                StripeCouponId = RowReader.GetNullableString(row, "stripecouponid")
            };
        }
    }
}
