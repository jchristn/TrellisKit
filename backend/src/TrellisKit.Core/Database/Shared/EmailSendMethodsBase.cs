namespace TrellisKit.Core.Database.Shared
{
    using System;
    using System.Data;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database.Interfaces;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Shared category email idempotency methods.
    /// </summary>
    internal abstract class EmailSendMethodsBase : MethodsBase, IEmailSendMethods
    {
        private const string _Columns = "id, userid, category, sourceid, status, createdutc";

        protected EmailSendMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<bool> TryCreateAsync(EmailSend send, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(send);
            RequireText(send.Id, nameof(send.Id));
            RequireText(send.UserId, nameof(send.UserId));
            RequireText(send.SourceId, nameof(send.SourceId));
            DataTable table = await QueryAsync("INSERT INTO emailsends (propertyid, " + _Columns + ") VALUES (" + Pid + ", " + D.Str(send.Id) + ", " + D.Str(send.UserId) + ", " + D.Enum(send.Category) + ", " +
                D.Str(send.SourceId) + ", " + D.Enum(send.Status) + ", " + D.Ts(send.CreatedUtc) + ") ON CONFLICT (propertyid, userid, category, sourceid) DO NOTHING RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task UpdateStatusAsync(string id, EmailSendStatusEnum status, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            await QueryAsync("UPDATE emailsends SET status = " + D.Enum(status) + " WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
        }

        public async Task<bool> SentSinceAsync(string userId, NotificationCategoryEnum category, DateTime sinceUtc, string excludeSourceId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            long count = await CountAsync("SELECT COUNT(*) AS cnt FROM emailsends WHERE " + Scope() + " AND userid = " + D.Str(userId) + " AND category = " + D.Enum(category) + " AND status = " +
                D.Enum(EmailSendStatusEnum.Sent) + " AND createdutc >= " + D.Ts(sinceUtc) + " AND sourceid <> " + D.Str(excludeSourceId ?? String.Empty) + ";", token).ConfigureAwait(false);
            return count > 0;
        }

        public async Task<EmailSend?> ReadAsync(string userId, NotificationCategoryEnum category, string sourceId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            RequireText(sourceId, nameof(sourceId));
            DataTable table = await QueryAsync("SELECT " + _Columns + " FROM emailsends WHERE " + Scope() + " AND userid = " + D.Str(userId) + " AND category = " + D.Enum(category) + " AND sourceid = " + D.Str(sourceId) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            DataRow row = table.Rows[0];
            return new EmailSend
            {
                Id = RowReader.GetString(row, "id"),
                UserId = RowReader.GetString(row, "userid"),
                Category = RowReader.GetEnum(row, "category", NotificationCategoryEnum.FeedPosts),
                SourceId = RowReader.GetString(row, "sourceid"),
                Status = RowReader.GetEnum(row, "status", EmailSendStatusEnum.Sent),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc")
            };
        }

        public async Task<int> DeleteByUserAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("DELETE FROM emailsends WHERE " + Scope() + " AND userid = " + D.Str(userId) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count;
        }
    }
}
