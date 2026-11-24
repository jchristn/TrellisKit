namespace TrellisKit.Core.Database.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database.Interfaces;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Shared rank history methods.
    /// </summary>
    internal abstract class RankHistoryMethodsBase : MethodsBase, IRankHistoryMethods
    {
        protected RankHistoryMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<List<RankHistoryEntry>> EnumerateByUserAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("SELECT * FROM rankhistoryentries WHERE " + Scope() + " AND userid = " + D.Str(userId) + " ORDER BY position, id;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(row => new RankHistoryEntry
            {
                Id = RowReader.GetString(row, "id"),
                UserId = RowReader.GetString(row, "userid"),
                Position = RowReader.GetInt(row, "position"),
                Rank = RowReader.GetString(row, "rank"),
                AwardedByName = RowReader.GetNullableString(row, "awardedbyname"),
                AwardedByUserId = RowReader.GetNullableString(row, "awardedbyuserid"),
                Affiliation = RowReader.GetNullableString(row, "affiliation"),
                DateAwarded = RowReader.GetNullableString(row, "dateawarded"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc")
            }).ToList();
        }

        public async Task ReplaceForUserAsync(string userId, List<RankHistoryEntry> entries, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            ArgumentNullException.ThrowIfNull(entries);
            List<string> statements = new List<string> { "DELETE FROM rankhistoryentries WHERE " + Scope() + " AND userid = " + D.Str(userId) + ";" };
            for (int i = 0; i < entries.Count; i++)
            {
                RankHistoryEntry e = entries[i];
                e.UserId = userId;
                e.Position = i;
                statements.Add("INSERT INTO rankhistoryentries (propertyid, id, userid, position, rank, awardedbyname, awardedbyuserid, affiliation, dateawarded, createdutc) VALUES (" +
                    Pid + ", " + D.Str(e.Id) + ", " + D.Str(userId) + ", " + D.Num(i) + ", " + D.Str(e.Rank) + ", " + D.Str(e.AwardedByName) + ", " +
                    D.Str(e.AwardedByUserId) + ", " + D.Str(e.Affiliation) + ", " + D.Str(e.DateAwarded) + ", " + D.Ts(e.CreatedUtc) + ");");
            }
            await TransactionAsync(statements, token).ConfigureAwait(false);
        }
    }
}
