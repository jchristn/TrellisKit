namespace TrellisKit.Core.Database.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database.Interfaces;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    internal abstract class SearchQueueMethodsBase : MethodsBase, ISearchQueueMethods
    {
        private const string _Watermark = "Watermark";

        protected SearchQueueMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task EnqueueAsync(TargetTypeEnum targetType, string targetId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(targetId, nameof(targetId));
            await QueryAsync("INSERT INTO searchqueue (propertyid, targettype, targetid, enqueuedutc, attempts) VALUES (" + Pid + ", " + D.Enum(targetType) + ", " + D.Str(targetId) + ", " + D.Ts(nowUtc) +
                ", 0) ON CONFLICT (propertyid, targettype, targetid) DO UPDATE SET enqueuedutc = excluded.enqueuedutc, attempts = 0;", token).ConfigureAwait(false);
        }

        public async Task<List<SearchQueueItem>> PeekAsync(int limit, int maxAttempts, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT targettype, targetid, enqueuedutc FROM searchqueue WHERE " + Scope() + " AND targettype <> " + D.Str(_Watermark) + " AND attempts < " + D.Num(Math.Max(1, maxAttempts)) +
                " ORDER BY enqueuedutc, targetid LIMIT " + D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => new SearchQueueItem
            {
                TargetType = RowReader.GetEnum(r, "targettype", TargetTypeEnum.Thread),
                TargetId = RowReader.GetString(r, "targetid"),
                EnqueuedUtc = RowReader.GetDateTime(r, "enqueuedutc")
            }).ToList();
        }

        public async Task<bool> CompleteAsync(SearchQueueItem item, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(item);
            DataTable table = await QueryAsync("DELETE FROM searchqueue WHERE " + Scope() + " AND targettype = " + D.Enum(item.TargetType) + " AND targetid = " + D.Str(item.TargetId) +
                " AND enqueuedutc = " + D.Ts(item.EnqueuedUtc) + " RETURNING targetid;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task FailAsync(SearchQueueItem item, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(item);
            await QueryAsync("UPDATE searchqueue SET attempts = attempts + 1 WHERE " + Scope() + " AND targettype = " + D.Enum(item.TargetType) + " AND targetid = " + D.Str(item.TargetId) + ";", token).ConfigureAwait(false);
        }

        public Task<long> CountAsync(CancellationToken token = default)
        {
            return CountAsync("SELECT COUNT(*) AS cnt FROM searchqueue WHERE " + Scope() + " AND targettype <> " + D.Str(_Watermark) + ";", token);
        }

        public async Task<int> EnqueueAllAsync(DateTime nowUtc, CancellationToken token = default)
        {
            string now = D.Ts(nowUtc);
            string conflict = " ON CONFLICT (propertyid, targettype, targetid) DO UPDATE SET enqueuedutc = excluded.enqueuedutc, attempts = 0;";
            List<string> statements = new List<string>
            {
                "INSERT INTO searchqueue (propertyid, targettype, targetid, enqueuedutc, attempts) SELECT " + Pid + ", " + D.Enum(TargetTypeEnum.Thread) + ", t.id, " + now + ", 0 FROM forumthreads t WHERE " +
                    Scope("t") + " AND t.deletedutc IS NULL" + conflict,
                "INSERT INTO searchqueue (propertyid, targettype, targetid, enqueuedutc, attempts) SELECT " + Pid + ", " + D.Enum(TargetTypeEnum.FeedPost) + ", f.id, " + now + ", 0 FROM feedposts f WHERE " +
                    Scope("f") + " AND f.deletedutc IS NULL" + conflict,
                "INSERT INTO searchqueue (propertyid, targettype, targetid, enqueuedutc, attempts) SELECT " + Pid + ", " + D.Enum(TargetTypeEnum.Comment) + ", c.id, " + now + ", 0 FROM comments c WHERE " +
                    Scope("c") + " AND c.deletedutc IS NULL" + conflict,
                "INSERT INTO searchqueue (propertyid, targettype, targetid, enqueuedutc, attempts) SELECT " + Pid + ", " + D.Enum(TargetTypeEnum.User) + ", u.id, " + now + ", 0 FROM users u WHERE " +
                    Scope("u") + " AND u.isdeleted = " + D.Bool(false) + conflict,
                "INSERT INTO searchqueue (propertyid, targettype, targetid, enqueuedutc, attempts) SELECT " + Pid + ", " + D.Enum(TargetTypeEnum.Idea) + ", i.id, " + now + ", 0 FROM ideas i WHERE " +
                    Scope("i") + " AND i.deletedutc IS NULL" + conflict,
                "SELECT COUNT(*) AS cnt FROM searchqueue WHERE " + Scope() + " AND enqueuedutc = " + now + ";"
            };
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? 0 : (int)RowReader.GetLong(table.Rows[0], "cnt");
        }

        public async Task<DateTime?> ReadWatermarkAsync(CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT enqueuedutc FROM searchqueue WHERE " + Scope() + " AND targettype = " + D.Str(_Watermark) + " AND targetid = 'sweep';", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : RowReader.GetDateTime(table.Rows[0], "enqueuedutc");
        }

        public async Task WriteWatermarkAsync(DateTime watermarkUtc, CancellationToken token = default)
        {
            await QueryAsync("INSERT INTO searchqueue (propertyid, targettype, targetid, enqueuedutc, attempts) VALUES (" + Pid + ", " + D.Str(_Watermark) + ", 'sweep', " + D.Ts(watermarkUtc) +
                ", 0) ON CONFLICT (propertyid, targettype, targetid) DO UPDATE SET enqueuedutc = excluded.enqueuedutc;", token).ConfigureAwait(false);
        }

        public async Task<int> EnqueueChangedAsync(DateTime sinceUtc, DateTime nowUtc, CancellationToken token = default)
        {
            string since = D.Ts(sinceUtc);
            string now = D.Ts(nowUtc);
            string conflict = " ON CONFLICT (propertyid, targettype, targetid) DO UPDATE SET enqueuedutc = excluded.enqueuedutc, attempts = 0;";
            List<string> statements = new List<string>
            {
                "INSERT INTO searchqueue (propertyid, targettype, targetid, enqueuedutc, attempts) SELECT " + Pid + ", " + D.Enum(TargetTypeEnum.Thread) + ", t.id, " + now + ", 0 FROM forumthreads t WHERE " +
                    Scope("t") + " AND t.updatedutc >= " + since + conflict,
                "INSERT INTO searchqueue (propertyid, targettype, targetid, enqueuedutc, attempts) SELECT " + Pid + ", " + D.Enum(TargetTypeEnum.FeedPost) + ", f.id, " + now + ", 0 FROM feedposts f WHERE " +
                    Scope("f") + " AND f.updatedutc >= " + since + conflict,
                "INSERT INTO searchqueue (propertyid, targettype, targetid, enqueuedutc, attempts) SELECT " + Pid + ", " + D.Enum(TargetTypeEnum.Comment) + ", c.id, " + now + ", 0 FROM comments c WHERE " +
                    Scope("c") + " AND c.updatedutc >= " + since + conflict,
                "INSERT INTO searchqueue (propertyid, targettype, targetid, enqueuedutc, attempts) SELECT " + Pid + ", " + D.Enum(TargetTypeEnum.User) + ", u.id, " + now + ", 0 FROM users u WHERE " +
                    Scope("u") + " AND u.updatedutc >= " + since + conflict,
                "INSERT INTO searchqueue (propertyid, targettype, targetid, enqueuedutc, attempts) SELECT " + Pid + ", " + D.Enum(TargetTypeEnum.Idea) + ", i.id, " + now + ", 0 FROM ideas i WHERE " +
                    Scope("i") + " AND i.updatedutc >= " + since + conflict,
                "SELECT COUNT(*) AS cnt FROM searchqueue WHERE " + Scope() + " AND enqueuedutc = " + now + " AND targettype <> " + D.Str(_Watermark) + ";"
            };
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? 0 : (int)RowReader.GetLong(table.Rows[0], "cnt");
        }
    }
}
