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
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    internal abstract class PollMethodsBase : MethodsBase, IPollMethods
    {
        protected PollMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<Poll?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) return null;
            List<Poll> polls = await LoadAsync("p.id = " + D.Str(id), token).ConfigureAwait(false);
            return polls.FirstOrDefault();
        }

        public async Task<Poll?> ReadByThreadAsync(string threadId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(threadId)) return null;
            List<Poll> polls = await LoadAsync("p.threadid = " + D.Str(threadId), token).ConfigureAwait(false);
            return polls.FirstOrDefault();
        }

        public async Task<Dictionary<string, Poll>> ReadByThreadsAsync(IEnumerable<string> threadIds, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(threadIds);
            List<string> ids = threadIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) return new Dictionary<string, Poll>(StringComparer.Ordinal);
            List<Poll> polls = await LoadAsync("p.threadid IN " + D.InList(ids), token).ConfigureAwait(false);
            return polls.GroupBy(p => p.ThreadId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        }

        public async Task<Dictionary<string, List<string>>> VotesOfAsync(string userId, IEnumerable<string> pollIds, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            ArgumentNullException.ThrowIfNull(pollIds);
            Dictionary<string, List<string>> result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            List<string> ids = pollIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) return result;
            DataTable table = await QueryAsync("SELECT v.pollid, v.optionid FROM pollvotes v JOIN polloptions o ON " + Scope("o") + " AND o.id = v.optionid WHERE " + Scope("v") + " AND v.userid = " +
                D.Str(userId) + " AND v.pollid IN " + D.InList(ids) + " ORDER BY v.pollid, o.position;", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows)
            {
                string poll = RowReader.GetString(row, "pollid");
                if (!result.TryGetValue(poll, out List<string>? list))
                {
                    list = new List<string>();
                    result[poll] = list;
                }
                list.Add(RowReader.GetString(row, "optionid"));
            }
            return result;
        }

        public async Task<PollVoteOutcome> VoteAsync(string pollId, string userId, IReadOnlyCollection<string> optionIds, bool allowChange, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(pollId, nameof(pollId));
            RequireText(userId, nameof(userId));
            ArgumentNullException.ThrowIfNull(optionIds);
            string pid = D.Str(pollId);
            string user = D.Str(userId);
            List<string> options = optionIds.Distinct(StringComparer.Ordinal).ToList();
            string open = "EXISTS (SELECT 1 FROM polls op WHERE " + Scope("op") + " AND op.id = " + pid + " AND op.closedutc IS NULL AND (op.closesutc IS NULL OR op.closesutc > " + D.Ts(nowUtc) + "))";
            string mine = "SELECT mv.optionid FROM pollvotes mv WHERE " + Scope("mv") + " AND mv.pollid = " + pid + " AND mv.userid = " + user;
            string had = "EXISTS (" + mine + ")";
            string same = "NOT EXISTS (SELECT 1 FROM pollvotes sv WHERE " + Scope("sv") + " AND sv.pollid = " + pid + " AND sv.userid = " + user + " AND sv.optionid NOT IN " + D.InList(options.Count == 0 ? new List<string> { "" } : options) +
                ") AND (SELECT COUNT(*) FROM pollvotes cv WHERE " + Scope("cv") + " AND cv.pollid = " + pid + " AND cv.userid = " + user + ") = " + D.Num(options.Count);
            string permitted = allowChange ? open : open + " AND NOT " + had;
            List<string> statements = new List<string>
            {
                "UPDATE polls SET updatedutc = updatedutc WHERE " + Scope() + " AND id = " + pid + ";",
                "CREATE TEMP TABLE IF NOT EXISTS pollvotestate (pollid TEXT, hadvote BOOLEAN, samevote BOOLEAN, isopen BOOLEAN) ON COMMIT DROP;",
                "INSERT INTO pollvotestate (pollid, hadvote, samevote, isopen) SELECT " + pid + ", " + had + ", " + same + ", " + open + ";",
                "DELETE FROM pollvotes WHERE " + Scope() + " AND pollid = " + pid + " AND userid = " + user + " AND " + permitted + " AND NOT (" + same + ");"
            };
            if (options.Count > 0)
            {
                statements.Add("INSERT INTO pollvotes (propertyid, id, pollid, optionid, userid, createdutc) SELECT " + Pid + ", v.id, " + pid + ", v.optionid, " + user + ", " + D.Ts(nowUtc) + " FROM (VALUES " +
                    String.Join(", ", options.Select(o => "(" + D.Str(IdGenerator.Generate(ForumIdPrefixes.PollVote)) + ", " + D.Str(o) + ")")) + ") AS v(id, optionid) WHERE " + open +
                    " AND NOT EXISTS (" + mine + ") ON CONFLICT DO NOTHING;");
            }
            statements.AddRange(RecomputeStatements(pid, nowUtc));
            statements.Add("SELECT s.hadvote, s.samevote, s.isopen, (SELECT string_agg(cur.optionid, ',' ORDER BY cur.optionid) FROM pollvotes cur WHERE " + Scope("cur") + " AND cur.pollid = " + pid +
                " AND cur.userid = " + user + ") AS current FROM pollvotestate s;");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            DataRow row = table.Rows[0];
            bool hadVote = RowReader.GetBool(row, "hadvote");
            bool sameVote = RowReader.GetBool(row, "samevote");
            bool isOpen = RowReader.GetBool(row, "isopen");
            List<string> current = (RowReader.GetNullableString(row, "current") ?? String.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
            HashSet<string> wanted = new HashSet<string>(options, StringComparer.Ordinal);
            bool applied = current.Count == wanted.Count && current.All(wanted.Contains);
            return new PollVoteOutcome
            {
                Applied = applied && isOpen,
                Closed = !isOpen,
                Locked = isOpen && !allowChange && hadVote && !sameVote,
                Changed = hadVote && !sameVote,
                HadVote = hadVote,
                Current = current
            };
        }

        public async Task<bool> CloseAsync(string pollId, string? byUserId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(pollId, nameof(pollId));
            DataTable table = await QueryAsync("UPDATE polls SET closedutc = " + D.Ts(nowUtc) + ", closedbyuserid = " + D.Str(byUserId) + ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " +
                D.Str(pollId) + " AND closedutc IS NULL RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<List<Poll>> ClaimDueAsync(DateTime nowUtc, int limit, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("UPDATE polls SET closedutc = closesutc, updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id IN (SELECT d.id FROM polls d WHERE " + Scope("d") +
                " AND d.closedutc IS NULL AND d.closesutc IS NOT NULL AND d.closesutc <= " + D.Ts(nowUtc) + " ORDER BY d.closesutc, d.id LIMIT " + D.Num(Math.Max(1, limit)) + ") AND closedutc IS NULL RETURNING id;", token).ConfigureAwait(false);
            List<string> ids = table.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "id")).ToList();
            if (ids.Count == 0) return new List<Poll>();
            return await LoadAsync("p.id IN " + D.InList(ids), token).ConfigureAwait(false);
        }

        public async Task<bool> ReplaceOptionsAsync(Poll poll, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(poll);
            RequireText(poll.Id, nameof(poll.Id));
            string pid = D.Str(poll.Id);
            string unvoted = "NOT EXISTS (SELECT 1 FROM pollvotes uv WHERE " + Scope("uv") + " AND uv.pollid = " + pid + ")";
            List<string> statements = new List<string>
            {
                "UPDATE polls SET multiplechoice = " + D.Bool(poll.MultipleChoice) + ", maxchoices = " + D.Num(poll.MaxChoices) + ", resultsvisibility = " + D.Enum(poll.ResultsVisibility) +
                    ", votervisibility = " + D.Enum(poll.VoterVisibility) + ", allowvotechange = " + D.Bool(poll.AllowVoteChange) + ", closesutc = " + D.Ts(poll.ClosesUtc) + ", updatedutc = " + D.Ts(poll.UpdatedUtc) +
                    " WHERE " + Scope() + " AND id = " + pid + " AND closedutc IS NULL AND " + unvoted + ";",
                "DELETE FROM polloptions WHERE " + Scope() + " AND pollid = " + pid + " AND " + unvoted + " AND EXISTS (SELECT 1 FROM polls ok WHERE " + Scope("ok") + " AND ok.id = " + pid + " AND ok.updatedutc = " + D.Ts(poll.UpdatedUtc) + ");"
            };
            if (poll.Options.Count > 0)
            {
                statements.Add("INSERT INTO polloptions (propertyid, id, pollid, label, labelnormalized, position, votecount, createdutc) SELECT " + Pid + ", v.id, " + pid + ", v.label, v.norm, v.pos, 0, " + D.Ts(poll.UpdatedUtc) +
                    " FROM (VALUES " + String.Join(", ", poll.Options.Select(o => "(" + D.Str(o.Id) + ", " + D.Str(o.Label) + ", " + D.Str(o.LabelNormalized) + ", " + D.Num(o.Position) + ")")) +
                    ") AS v(id, label, norm, pos) WHERE EXISTS (SELECT 1 FROM polls ok WHERE " + Scope("ok") + " AND ok.id = " + pid + " AND ok.updatedutc = " + D.Ts(poll.UpdatedUtc) + ") AND " + unvoted + ";");
            }
            statements.Add("SELECT id FROM polls WHERE " + Scope() + " AND id = " + pid + " AND updatedutc = " + D.Ts(poll.UpdatedUtc) + ";");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> SetClosesAsync(string pollId, DateTime? closesUtc, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(pollId, nameof(pollId));
            DataTable table = await QueryAsync("UPDATE polls SET closesutc = " + D.Ts(closesUtc) + ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(pollId) + " AND closedutc IS NULL RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public Task<EnumerationResult<PollVoter>> EnumerateVotersAsync(string pollId, string? optionId, EnumerationRequest request, CancellationToken token = default)
        {
            RequireText(pollId, nameof(pollId));
            ArgumentNullException.ThrowIfNull(request);
            string pid = D.Str(pollId);
            string option = optionId == null ? String.Empty : " AND v.userid IN (SELECT ov.userid FROM pollvotes ov WHERE " + Scope("ov") + " AND ov.pollid = " + pid + " AND ov.optionid = " + D.Str(optionId) + ")";
            string from = "(SELECT v.propertyid AS propertyid, v.userid AS userid, MAX(v.createdutc) AS createdutc, string_agg(v.optionid, ',' ORDER BY v.optionid) AS optionids FROM pollvotes v WHERE " + Scope("v") +
                " AND v.pollid = " + pid + option + " GROUP BY v.propertyid, v.userid) pv";
            return EnumerateAsync(request, "pv.userid, pv.createdutc, pv.optionids", from, new List<string>(), row => new PollVoter
            {
                UserId = RowReader.GetString(row, "userid"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                OptionIds = (RowReader.GetNullableString(row, "optionids") ?? String.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).ToList()
            }, token);
        }

        public async Task<List<string>> VoterIdsAsync(string pollId, string? afterUserId, int limit, CancellationToken token = default)
        {
            RequireText(pollId, nameof(pollId));
            DataTable table = await QueryAsync("SELECT DISTINCT userid FROM pollvotes WHERE " + Scope() + " AND pollid = " + D.Str(pollId) + (afterUserId == null ? String.Empty : " AND userid > " + D.Str(afterUserId)) +
                " ORDER BY userid LIMIT " + D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "userid")).ToList();
        }

        public async Task<bool> VotedWithinAsync(string pollId, string userId, DateTime sinceUtc, CancellationToken token = default)
        {
            RequireText(pollId, nameof(pollId));
            RequireText(userId, nameof(userId));
            long count = await CountAsync("SELECT COUNT(*) AS cnt FROM pollvotes WHERE " + Scope() + " AND pollid = " + D.Str(pollId) + " AND userid = " + D.Str(userId) + " AND createdutc >= " + D.Ts(sinceUtc) + ";", token).ConfigureAwait(false);
            return count > 0;
        }

        public async Task<List<PollVoteRecord>> ListVotesByUserAsync(string userId, string? afterId, int limit, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("SELECT v.id, v.pollid, p.threadid, v.optionid, o.label, v.createdutc FROM pollvotes v JOIN polls p ON " + Scope("p") + " AND p.id = v.pollid JOIN polloptions o ON " +
                Scope("o") + " AND o.id = v.optionid WHERE " + Scope("v") + " AND v.userid = " + D.Str(userId) + (afterId == null ? String.Empty : " AND v.id > " + D.Str(afterId)) +
                " ORDER BY v.id LIMIT " + D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => new PollVoteRecord
            {
                Id = RowReader.GetString(r, "id"),
                PollId = RowReader.GetString(r, "pollid"),
                ThreadId = RowReader.GetString(r, "threadid"),
                OptionId = RowReader.GetString(r, "optionid"),
                OptionLabel = RowReader.GetString(r, "label"),
                CreatedUtc = RowReader.GetDateTime(r, "createdutc")
            }).ToList();
        }

        public async Task<int> DeleteVotesForUserAsync(string userId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable polls = await QueryAsync("SELECT DISTINCT pollid FROM pollvotes WHERE " + Scope() + " AND userid = " + D.Str(userId) + ";", token).ConfigureAwait(false);
            List<string> ids = polls.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "pollid")).ToList();
            if (ids.Count == 0) return 0;
            List<string> statements = new List<string> { "DELETE FROM pollvotes WHERE " + Scope() + " AND userid = " + D.Str(userId) + ";" };
            foreach (string id in ids) statements.AddRange(RecomputeStatements(D.Str(id), nowUtc));
            await TransactionAsync(statements, token).ConfigureAwait(false);
            return ids.Count;
        }

        private List<string> RecomputeStatements(string pollLiteral, DateTime nowUtc)
        {
            return new List<string>
            {
                "UPDATE polloptions SET votecount = (SELECT COUNT(*) FROM pollvotes c WHERE " + Scope("c") + " AND c.optionid = polloptions.id) WHERE " + Scope() + " AND pollid = " + pollLiteral + ";",
                "UPDATE polls SET totalvoters = (SELECT COUNT(DISTINCT c.userid) FROM pollvotes c WHERE " + Scope("c") + " AND c.pollid = polls.id), firstvoteutc = COALESCE(firstvoteutc, (SELECT MIN(c.createdutc) FROM pollvotes c WHERE " +
                    Scope("c") + " AND c.pollid = polls.id)), updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + pollLiteral + ";"
            };
        }

        private async Task<List<Poll>> LoadAsync(string condition, CancellationToken token)
        {
            DataTable table = await QueryAsync("SELECT p.* FROM polls p WHERE " + Scope("p") + " AND " + condition + ";", token).ConfigureAwait(false);
            List<Poll> polls = table.Rows.Cast<DataRow>().Select(Map).ToList();
            if (polls.Count == 0) return polls;
            DataTable options = await QueryAsync("SELECT o.* FROM polloptions o WHERE " + Scope("o") + " AND o.pollid IN " + D.InList(polls.Select(p => p.Id)) + " ORDER BY o.pollid, o.position;", token).ConfigureAwait(false);
            Dictionary<string, Poll> byId = polls.ToDictionary(p => p.Id, StringComparer.Ordinal);
            foreach (DataRow row in options.Rows)
            {
                PollOption option = new PollOption
                {
                    Id = RowReader.GetString(row, "id"),
                    PollId = RowReader.GetString(row, "pollid"),
                    Label = RowReader.GetString(row, "label"),
                    LabelNormalized = RowReader.GetString(row, "labelnormalized"),
                    Position = RowReader.GetInt(row, "position"),
                    VoteCount = RowReader.GetInt(row, "votecount"),
                    CreatedUtc = RowReader.GetDateTime(row, "createdutc")
                };
                if (byId.TryGetValue(option.PollId, out Poll? poll)) poll.Options.Add(option);
            }
            return polls;
        }

        private static Poll Map(DataRow row)
        {
            return new Poll
            {
                Id = RowReader.GetString(row, "id"),
                ThreadId = RowReader.GetString(row, "threadid"),
                MultipleChoice = RowReader.GetBool(row, "multiplechoice"),
                MaxChoices = RowReader.GetNullableInt(row, "maxchoices"),
                ResultsVisibility = RowReader.GetEnum(row, "resultsvisibility", PollResultsVisibilityEnum.AfterVote),
                VoterVisibility = RowReader.GetEnum(row, "votervisibility", PollVoterVisibilityEnum.Anonymous),
                AllowVoteChange = RowReader.GetBool(row, "allowvotechange"),
                ClosesUtc = RowReader.GetNullableDateTime(row, "closesutc"),
                ClosedUtc = RowReader.GetNullableDateTime(row, "closedutc"),
                ClosedByUserId = RowReader.GetNullableString(row, "closedbyuserid"),
                TotalVoters = RowReader.GetInt(row, "totalvoters"),
                FirstVoteUtc = RowReader.GetNullableDateTime(row, "firstvoteutc"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }
    }
}
