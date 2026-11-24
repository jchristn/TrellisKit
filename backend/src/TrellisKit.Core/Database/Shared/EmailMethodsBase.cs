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
    using TrellisKit.Core.Security;

    /// <summary>
    /// Shared administrative email methods.
    /// </summary>
    internal abstract class EmailMethodsBase : MethodsBase, IEmailMethods
    {
        private const string _Columns = "id, template, category, recipient, recipientnormalized, userid, subject, status, provider, providermessageid, attempts, nextattemptutc, lasterror, idempotencykey, payloadciphertext, payloadnonce, payloadkeyid, createdutc, sentutc, deliveredutc, failedutc, updatedutc";

        protected EmailMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<bool> CreateMessageAsync(EmailMessageRecord record, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(record);
            string sql = "INSERT INTO emailmessages (propertyid, " + _Columns + ") VALUES (" + Pid + ", " + D.Str(record.Id) + ", " + D.Str(record.Template) + ", " + D.Enum(record.Category) + ", " +
                D.Str(record.Recipient) + ", " + D.Str(record.Recipient.Trim().ToLowerInvariant()) + ", " + D.Str(record.UserId) + ", " + D.Str(record.Subject) + ", " + D.Enum(record.Status) + ", " +
                D.Str(record.Provider) + ", " + D.Str(record.ProviderMessageId) + ", " + D.Num(record.Attempts) + ", " + D.Ts(record.NextAttemptUtc) + ", " + D.Str(record.LastError) + ", " +
                D.Str(record.IdempotencyKey) + ", " + D.Str(record.Payload?.Ciphertext) + ", " + D.Str(record.Payload?.Nonce) + ", " + D.Str(record.Payload?.KeyId) + ", " +
                D.Ts(record.CreatedUtc) + ", " + D.Ts(record.SentUtc) + ", " + D.Ts(record.DeliveredUtc) + ", " + D.Ts(record.FailedUtc) + ", " + D.Ts(record.UpdatedUtc) +
                ") ON CONFLICT DO NOTHING RETURNING id;";
            DataTable table = await QueryAsync(sql, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<EmailMessageRecord?> ReadMessageAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) return null;
            DataTable table = await QueryAsync("SELECT " + _Columns + " FROM emailmessages WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        public async Task UpdateMessageAsync(EmailMessageRecord record, bool clearPayload, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(record);
            await QueryAsync("UPDATE emailmessages SET subject = " + D.Str(record.Subject) + ", status = " + D.Enum(record.Status) + ", provider = " + D.Str(record.Provider) +
                ", providermessageid = " + D.Str(record.ProviderMessageId) + ", attempts = " + D.Num(record.Attempts) + ", nextattemptutc = " + D.Ts(record.NextAttemptUtc) +
                ", lasterror = " + D.Str(record.LastError) + ", sentutc = " + D.Ts(record.SentUtc) + ", deliveredutc = " + D.Ts(record.DeliveredUtc) + ", failedutc = " + D.Ts(record.FailedUtc) +
                ", updatedutc = " + D.Ts(record.UpdatedUtc) + ", claimedby = NULL, claimuntilutc = NULL" +
                (clearPayload ? ", payloadciphertext = NULL, payloadnonce = NULL, payloadkeyid = NULL" : String.Empty) +
                " WHERE " + Scope() + " AND id = " + D.Str(record.Id) + ";", token).ConfigureAwait(false);
        }

        public async Task<List<EmailMessageRecord>> ClaimDueAsync(DateTime nowUtc, string holder, int claimSeconds, int limit, CancellationToken token = default)
        {
            RequireText(holder, nameof(holder));
            string now = D.Ts(nowUtc);
            string sql = "UPDATE emailmessages SET claimedby = " + D.Str(holder) + ", claimuntilutc = " + D.Ts(nowUtc.AddSeconds(Math.Clamp(claimSeconds, 10, 3600))) +
                " WHERE " + Scope() + " AND id IN (SELECT id FROM emailmessages WHERE " + Scope() + " AND status = " + D.Enum(EmailStatusEnum.Queued) +
                " AND (nextattemptutc IS NULL OR nextattemptutc <= " + now + ") AND (claimuntilutc IS NULL OR claimuntilutc < " + now + ") ORDER BY nextattemptutc NULLS FIRST, id LIMIT " +
                D.Num(Math.Clamp(limit, 1, 100)) + " FOR UPDATE SKIP LOCKED) RETURNING " + _Columns + ";";
            DataTable table = await QueryAsync(sql, token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task<EmailMessageRecord?> FindByProviderIdAsync(string providerMessageId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(providerMessageId)) return null;
            DataTable table = await QueryAsync("SELECT " + _Columns + " FROM emailmessages WHERE " + Scope() + " AND providermessageid = " + D.Str(providerMessageId.Trim()) + " LIMIT 1;", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        public async Task<bool> ExistsSinceAsync(string keyPrefix, DateTime sinceUtc, CancellationToken token = default)
        {
            RequireText(keyPrefix, nameof(keyPrefix));
            long count = await CountAsync("SELECT COUNT(*) AS cnt FROM emailmessages WHERE " + Scope() + " AND " + D.StartsWith("idempotencykey", keyPrefix) + " AND createdutc >= " + D.Ts(sinceUtc) + ";", token).ConfigureAwait(false);
            return count > 0;
        }

        public Task<EnumerationResult<EmailMessageRecord>> EnumerateMessagesAsync(EnumerationRequest request, EmailMessageFilter filter, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            List<string> conditions = new List<string>();
            if (filter.Status != null) conditions.Add("m.status = " + D.Enum(filter.Status.Value));
            if (filter.Category != null) conditions.Add("m.category = " + D.Enum(filter.Category.Value));
            if (!String.IsNullOrWhiteSpace(filter.Template)) conditions.Add("m.template = " + D.Str(filter.Template.Trim()));
            if (!String.IsNullOrWhiteSpace(filter.Recipient)) conditions.Add("m.recipientnormalized = " + D.Str(filter.Recipient.Trim().ToLowerInvariant()));
            if (!String.IsNullOrWhiteSpace(filter.UserId)) conditions.Add("m.userid = " + D.Str(filter.UserId.Trim()));
            string select = String.Join(", ", _Columns.Split(", ").Select(c => "m." + c));
            return EnumerateAsync(request, select, "emailmessages m", conditions, Map, token);
        }

        public async Task<int> PruneMessagesAsync(DateTime beforeUtc, int batchSize, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("DELETE FROM emailmessages WHERE " + Scope() + " AND id IN (SELECT id FROM emailmessages WHERE " + Scope() + " AND createdutc < " + D.Ts(beforeUtc) +
                " AND status <> " + D.Enum(EmailStatusEnum.Queued) + " LIMIT " + D.Num(Math.Clamp(batchSize, 1, 10000)) + ") RETURNING 1 AS n;", token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        public async Task<bool> IsSuppressedAsync(string email, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(email)) return false;
            long count = await CountAsync("SELECT COUNT(*) AS cnt FROM emailsuppressions WHERE " + Scope() + " AND emailnormalized = " + D.Str(email.Trim().ToLowerInvariant()) + ";", token).ConfigureAwait(false);
            return count > 0;
        }

        public async Task<EmailSuppression> AddSuppressionAsync(EmailSuppression suppression, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(suppression);
            string normalized = suppression.Email.Trim().ToLowerInvariant();
            await QueryAsync("INSERT INTO emailsuppressions (propertyid, id, email, emailnormalized, reason, source, note, createdutc) VALUES (" + Pid + ", " + D.Str(suppression.Id) + ", " +
                D.Str(suppression.Email.Trim()) + ", " + D.Str(normalized) + ", " + D.Enum(suppression.Reason) + ", " + D.Str(suppression.Source) + ", " + D.Str(suppression.Note) + ", " +
                D.Ts(suppression.CreatedUtc) + ") ON CONFLICT (propertyid, emailnormalized) DO NOTHING;", token).ConfigureAwait(false);
            DataTable table = await QueryAsync("SELECT * FROM emailsuppressions WHERE " + Scope() + " AND emailnormalized = " + D.Str(normalized) + ";", token).ConfigureAwait(false);
            return MapSuppression(table.Rows[0]);
        }

        public async Task<EmailSuppression?> ReadSuppressionAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) return null;
            DataTable table = await QueryAsync("SELECT * FROM emailsuppressions WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : MapSuppression(table.Rows[0]);
        }

        public async Task DeleteSuppressionAsync(string id, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            string type = D.Enum(TargetTypeEnum.EmailSuppression);
            await TransactionAsync(new List<string>
            {
                "DELETE FROM labels WHERE " + Scope() + " AND targettype = " + type + " AND targetid = " + D.Str(id) + ";",
                "DELETE FROM tags WHERE " + Scope() + " AND targettype = " + type + " AND targetid = " + D.Str(id) + ";",
                "DELETE FROM emailsuppressions WHERE " + Scope() + " AND id = " + D.Str(id) + ";"
            }, token).ConfigureAwait(false);
        }

        public Task<EnumerationResult<EmailSuppression>> EnumerateSuppressionsAsync(EnumerationRequest request, EmailSuppressionReasonEnum? reason, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            List<string> conditions = new List<string>();
            if (reason != null) conditions.Add("s.reason = " + D.Enum(reason.Value));
            return EnumerateAsync(request, "s.*", "emailsuppressions s", conditions, MapSuppression, token);
        }

        public async Task<List<EmailTemplateOverride>> ReadOverridesAsync(CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT * FROM emailtemplateoverrides WHERE " + Scope() + " ORDER BY template LIMIT 200;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(row => new EmailTemplateOverride
            {
                Template = RowReader.GetString(row, "template"),
                Subject = RowReader.GetNullableString(row, "subject"),
                Preheader = RowReader.GetNullableString(row, "preheader"),
                Heading = RowReader.GetNullableString(row, "heading"),
                Body = RowReader.GetNullableString(row, "body"),
                UpdatedByUserId = RowReader.GetNullableString(row, "updatedbyuserid"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            }).ToList();
        }

        public async Task WriteOverrideAsync(string template, EmailTemplateOverride? value, CancellationToken token = default)
        {
            RequireText(template, nameof(template));
            if (value == null)
            {
                await QueryAsync("DELETE FROM emailtemplateoverrides WHERE " + Scope() + " AND template = " + D.Str(template) + ";", token).ConfigureAwait(false);
                return;
            }
            await QueryAsync("INSERT INTO emailtemplateoverrides (propertyid, template, subject, preheader, heading, body, updatedbyuserid, updatedutc) VALUES (" + Pid + ", " + D.Str(template) + ", " +
                D.Str(value.Subject) + ", " + D.Str(value.Preheader) + ", " + D.Str(value.Heading) + ", " + D.Str(value.Body) + ", " + D.Str(value.UpdatedByUserId) + ", " + D.Ts(value.UpdatedUtc) +
                ") ON CONFLICT (propertyid, template) DO UPDATE SET subject = EXCLUDED.subject, preheader = EXCLUDED.preheader, heading = EXCLUDED.heading, body = EXCLUDED.body, " +
                "updatedbyuserid = EXCLUDED.updatedbyuserid, updatedutc = EXCLUDED.updatedutc;", token).ConfigureAwait(false);
        }

        public async Task<List<EmailStaffEvent>> ReadStaffEventsAsync(CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT * FROM emailstaffevents WHERE " + Scope() + " ORDER BY template LIMIT 200;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(row => new EmailStaffEvent
            {
                Template = RowReader.GetString(row, "template"),
                Enabled = RowReader.GetBool(row, "enabled"),
                Recipients = (RowReader.GetNullableString(row, "recipients") ?? String.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            }).ToList();
        }

        public async Task WriteStaffEventAsync(EmailStaffEvent value, string? userId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(value);
            await QueryAsync("INSERT INTO emailstaffevents (propertyid, template, enabled, recipients, updatedbyuserid, updatedutc) VALUES (" + Pid + ", " + D.Str(value.Template) + ", " + D.Bool(value.Enabled) + ", " +
                D.Str(String.Join(",", value.Recipients)) + ", " + D.Str(userId) + ", " + D.Ts(value.UpdatedUtc) +
                ") ON CONFLICT (propertyid, template) DO UPDATE SET enabled = EXCLUDED.enabled, recipients = EXCLUDED.recipients, updatedbyuserid = EXCLUDED.updatedbyuserid, updatedutc = EXCLUDED.updatedutc;", token).ConfigureAwait(false);
        }

        private static EmailMessageRecord Map(DataRow row)
        {
            string? ciphertext = RowReader.GetNullableString(row, "payloadciphertext");
            return new EmailMessageRecord
            {
                Id = RowReader.GetString(row, "id"),
                Template = RowReader.GetString(row, "template"),
                Category = RowReader.GetEnum(row, "category", EmailCategoryEnum.Essential),
                Recipient = RowReader.GetString(row, "recipient"),
                UserId = RowReader.GetNullableString(row, "userid"),
                Subject = RowReader.GetString(row, "subject"),
                Status = RowReader.GetEnum(row, "status", EmailStatusEnum.Queued),
                Provider = RowReader.GetNullableString(row, "provider"),
                ProviderMessageId = RowReader.GetNullableString(row, "providermessageid"),
                Attempts = RowReader.GetInt(row, "attempts"),
                NextAttemptUtc = RowReader.GetNullableDateTime(row, "nextattemptutc"),
                LastError = RowReader.GetNullableString(row, "lasterror"),
                IdempotencyKey = RowReader.GetNullableString(row, "idempotencykey"),
                Payload = ciphertext == null ? null : new SecretCiphertext { Ciphertext = ciphertext, Nonce = RowReader.GetNullableString(row, "payloadnonce") ?? String.Empty, KeyId = RowReader.GetNullableString(row, "payloadkeyid") ?? String.Empty },
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                SentUtc = RowReader.GetNullableDateTime(row, "sentutc"),
                DeliveredUtc = RowReader.GetNullableDateTime(row, "deliveredutc"),
                FailedUtc = RowReader.GetNullableDateTime(row, "failedutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }

        private static EmailSuppression MapSuppression(DataRow row)
        {
            return new EmailSuppression
            {
                Id = RowReader.GetString(row, "id"),
                Email = RowReader.GetString(row, "email"),
                Reason = RowReader.GetEnum(row, "reason", EmailSuppressionReasonEnum.Manual),
                Source = RowReader.GetString(row, "source"),
                Note = RowReader.GetNullableString(row, "note"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc")
            };
        }
    }
}
