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
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Shared member search methods.
    /// </summary>
    internal abstract class MemberSearchMethodsBase : MethodsBase, IMemberSearchMethods
    {
        protected MemberSearchMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<int> ReplaceWordsAsync(string userId, string? displayName, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            List<string> words = DisplayNameWords.Split(displayName);
            await TransactionAsync(WordStatements(userId, words), token).ConfigureAwait(false);
            return words.Count;
        }

        public async Task DeleteWordsAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            await QueryAsync("DELETE FROM displaynamewords WHERE " + Scope() + " AND userid = " + D.Str(userId) + ";", token).ConfigureAwait(false);
        }

        public async Task<List<string>> WordsAsync(string userId, CancellationToken token = default)
        {
            List<string> result = new List<string>();
            if (String.IsNullOrEmpty(userId)) return result;
            DataTable table = await QueryAsync("SELECT word FROM displaynamewords WHERE " + Scope() + " AND userid = " + D.Str(userId) + " ORDER BY word LIMIT 100;", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows) result.Add(RowReader.GetString(row, "word"));
            return result;
        }

        public async Task<string?> BackfillBatchAsync(string? afterUserId, int batchSize, CancellationToken token = default)
        {
            batchSize = Math.Clamp(batchSize, 1, 1000);
            string after = String.IsNullOrEmpty(afterUserId) ? String.Empty : " AND u.id > " + D.Str(afterUserId);
            DataTable table = await QueryAsync("SELECT u.id, u.displayname FROM users u WHERE " + Scope("u") + " AND u.isdeleted = " + D.Bool(false) + after +
                " AND NOT EXISTS (SELECT 1 FROM displaynamewords w WHERE " + Scope("w") + " AND w.userid = u.id) ORDER BY u.id LIMIT " + D.Num(batchSize) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            List<string> statements = new List<string>();
            string? last = null;
            foreach (DataRow row in table.Rows)
            {
                last = RowReader.GetString(row, "id");
                List<string> words = DisplayNameWords.Split(RowReader.GetNullableString(row, "displayname"));
                if (words.Count > 0) statements.Add(InsertWords(last, words));
            }
            if (statements.Count > 0) await TransactionAsync(statements, token).ConfigureAwait(false);
            return last;
        }

        public Task<EnumerationResult<string>> SearchAsync(MemberSearchCriteria query, EnumerationRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(query);
            ArgumentNullException.ThrowIfNull(request);
            RequireText(query.Prefix, nameof(query.Prefix));
            string prefix = query.Prefix;
            string handle = "lower(u.handle)";
            string context = "1 = 0";
            if (!String.IsNullOrEmpty(query.ContextFeedPostId))
            {
                string post = D.Str(query.ContextFeedPostId);
                context = "(u.id IN (SELECT f.authoruserid FROM feedposts f WHERE " + Scope("f") + " AND f.id = " + post + " AND f.deletedutc IS NULL) OR u.id IN (SELECT c.authoruserid FROM comments c WHERE " + Scope("c") + " AND c.targettype = " +
                    D.Enum(TargetTypeEnum.FeedPost) + " AND c.targetid = " + post + " AND c.deletedutc IS NULL))";
            }
            if (!String.IsNullOrEmpty(query.ContextThreadId))
            {
                string thread = D.Str(query.ContextThreadId);
                context = "(u.id IN (SELECT t.authoruserid FROM forumthreads t WHERE " + Scope("t") + " AND t.id = " + thread + " AND t.deletedutc IS NULL) OR u.id IN (SELECT c.authoruserid FROM comments c WHERE " + Scope("c") + " AND c.targettype = " +
                    D.Enum(TargetTypeEnum.Thread) + " AND c.targetid = " + thread + " AND c.deletedutc IS NULL))";
            }
            string rank = "CASE WHEN " + handle + " = " + D.Str(prefix) + " THEN 0 WHEN " + context + " THEN 1 WHEN " + D.StartsWith(handle, prefix) + " THEN 2 ELSE 3 END";
            List<string> conditions = new List<string>
            {
                Scope("u"),
                "u.id IN (SELECT hu.id FROM users hu WHERE " + Scope("hu") + " AND " + D.StartsWith("lower(hu.handle)", prefix) + " UNION SELECT w.userid FROM displaynamewords w WHERE " + Scope("w") + " AND " + D.StartsWith("w.word", prefix) + ")",
                "u.isdeleted = " + D.Bool(false),
                "u.isbanned = " + D.Bool(false),
                "u.ageattestedutc IS NOT NULL",
                "(" + ActiveEntitlementExists("u.id", query.NowUtc, null) + " OR EXISTS (SELECT 1 FROM roleassignments ra WHERE " + Scope("ra") + " AND ra.userid = u.id AND ra.role = " + D.Enum(RoleEnum.SystemAdmin) + "))"
            };
            if (!String.IsNullOrEmpty(query.CallerUserId))
            {
                string caller = D.Str(query.CallerUserId);
                conditions.Add("u.id <> " + caller);
                conditions.Add("NOT EXISTS (SELECT 1 FROM blocks b WHERE " + Scope("b") + " AND ((b.blockeruserid = " + caller + " AND b.blockeduserid = u.id) OR (b.blockeruserid = u.id AND b.blockeduserid = " + caller + ")))");
            }
            if (String.Equals(request.Ordering.Name, "Relevance", StringComparison.Ordinal))
            {
                string secondary = "CASE WHEN " + handle + " = " + D.Str(prefix) + " OR (" + D.StartsWith(handle, prefix) +
                    " AND NOT " + context + ") THEN " + handle + " ELSE lower(u.displayname) END";
                request.Ordering = new EnumerationOrdering
                {
                    Name = "Relevance",
                    Expression = "(CAST((" + rank + ") AS TEXT) || ':' || COALESCE((" + secondary + "), ''))",
                    Descending = false,
                    KeyKind = EnumerationKeyKindEnum.Text
                };
            }
            return EnumerateAsync(request, "u.id", "users u", conditions, row => RowReader.GetString(row, "id"), token);
        }

        private List<string> WordStatements(string userId, List<string> words)
        {
            List<string> statements = new List<string> { "DELETE FROM displaynamewords WHERE " + Scope() + " AND userid = " + D.Str(userId) + ";" };
            if (words.Count > 0) statements.Add(InsertWords(userId, words));
            return statements;
        }

        private string InsertWords(string userId, List<string> words)
        {
            return "INSERT INTO displaynamewords (propertyid, userid, word) VALUES " + String.Join(", ", words.Distinct(StringComparer.Ordinal).Select(w => "(" + Pid + ", " + D.Str(userId) + ", " + D.Str(w) + ")")) +
                " ON CONFLICT (userid, word) DO NOTHING;";
        }
    }
}
