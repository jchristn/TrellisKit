namespace TrellisKit.Core.Database.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database.Interfaces;

    /// <summary>
    /// Shared member goal methods.
    /// </summary>
    internal abstract class UserGoalMethodsBase : MethodsBase, IUserGoalMethods
    {
        protected UserGoalMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<List<string>> EnumerateByUserAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("SELECT goal FROM usergoals WHERE " + Scope() + " AND userid = " + D.Str(userId) + " ORDER BY position;", token).ConfigureAwait(false);
            List<string> goals = new List<string>();
            foreach (DataRow row in table.Rows)
            {
                string? goal = RowReader.GetNullableString(row, "goal");
                if (!String.IsNullOrEmpty(goal)) goals.Add(goal);
            }
            return goals;
        }

        public async Task ReplaceForUserAsync(string userId, List<string> goals, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            ArgumentNullException.ThrowIfNull(goals);
            List<string> statements = new List<string> { "DELETE FROM usergoals WHERE " + Scope() + " AND userid = " + D.Str(userId) + ";" };
            List<string> distinct = goals.Where(g => !String.IsNullOrWhiteSpace(g)).Distinct(StringComparer.Ordinal).ToList();
            for (int i = 0; i < distinct.Count; i++)
            {
                statements.Add("INSERT INTO usergoals (propertyid, userid, goal, position) VALUES (" + Pid + ", " + D.Str(userId) + ", " + D.Str(distinct[i]) + ", " + D.Num(i) + ");");
            }
            await TransactionAsync(statements, token).ConfigureAwait(false);
        }
    }
}
