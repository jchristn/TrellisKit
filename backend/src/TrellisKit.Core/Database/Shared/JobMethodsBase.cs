namespace TrellisKit.Core.Database.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database.Interfaces;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    internal abstract class JobMethodsBase : MethodsBase, IJobMethods
    {
        private const int _MaxInsert = 5000;

        protected JobMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public Task<EnumerationResult<JobDefinition>> EnumerateDefinitionsAsync(EnumerationRequest request, CancellationToken token = default)
        {
            return EnumerateAsync(request, "d.*", "jobdefinitions d", new List<string>(), MapDefinition, token);
        }

        public async Task<int> CountDefinitionsAsync(bool enabledOnly, CancellationToken token = default)
        {
            return checked((int)await CountAsync("SELECT COUNT(*) AS cnt FROM jobdefinitions WHERE " + Scope() + (enabledOnly ? " AND enabled" : String.Empty) + ";", token).ConfigureAwait(false));
        }

        public async Task<JobDefinition?> ReadDefinitionAsync(string key, CancellationToken token = default)
        {
            RequireText(key, nameof(key));
            DataTable table = await QueryAsync("SELECT * FROM jobdefinitions WHERE " + Scope() + " AND key = " + D.Str(key) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : MapDefinition(table.Rows[0]);
        }

        public async Task<bool> InsertDefinitionIfMissingAsync(JobDefinition definition, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(definition);
            RequireText(definition.Id, nameof(definition.Id));
            RequireText(definition.Key, nameof(definition.Key));
            DataTable table = await QueryAsync("INSERT INTO jobdefinitions (propertyid, id, key, name, description, enabled, intervaltype, intervalvalue, timeofdayminutesutc, daysofweek, " +
                "maxconcurrency, maxattempts, budgetmicrosperrun, requiresharness, nextrunutc, lastrunutc, lockeduntilutc, updatedbyuserid, createdutc, updatedutc) VALUES (" +
                Pid + ", " + D.Str(definition.Id) + ", " + D.Str(definition.Key) + ", " + D.Str(definition.Name) + ", " + D.Str(definition.Description) + ", " + D.Bool(definition.Enabled) + ", " +
                D.Enum(definition.IntervalType) + ", " + D.Num(definition.IntervalValue) + ", " + D.Num(definition.TimeOfDayMinutesUtc) + ", " + D.Str(Days(definition.DaysOfWeek)) + ", " +
                D.Num(definition.MaxConcurrency) + ", " + D.Num(definition.MaxAttempts) + ", " + Long(definition.BudgetMicrosPerRun) + ", " + D.Bool(definition.RequiresHarness) + ", " +
                D.Ts(definition.NextRunUtc) + ", NULL, NULL, NULL, " + D.Ts(definition.CreatedUtc) + ", " + D.Ts(definition.UpdatedUtc) + ") ON CONFLICT (propertyid, key) DO NOTHING RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public Task UpdateDefinitionAsync(JobDefinition definition, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(definition);
            return QueryAsync("UPDATE jobdefinitions SET name = " + D.Str(definition.Name) + ", description = " + D.Str(definition.Description) + ", enabled = " + D.Bool(definition.Enabled) +
                ", intervaltype = " + D.Enum(definition.IntervalType) + ", intervalvalue = " + D.Num(definition.IntervalValue) + ", timeofdayminutesutc = " + D.Num(definition.TimeOfDayMinutesUtc) +
                ", daysofweek = " + D.Str(Days(definition.DaysOfWeek)) + ", maxconcurrency = " + D.Num(definition.MaxConcurrency) + ", maxattempts = " + D.Num(definition.MaxAttempts) +
                ", budgetmicrosperrun = " + Long(definition.BudgetMicrosPerRun) + ", requiresharness = " + D.Bool(definition.RequiresHarness) + ", nextrunutc = " + D.Ts(definition.NextRunUtc) +
                ", lastrunutc = " + D.Ts(definition.LastRunUtc) + ", lockeduntilutc = " + D.Ts(definition.LockedUntilUtc) + ", updatedbyuserid = " + D.Str(definition.UpdatedByUserId) +
                ", updatedutc = " + D.Ts(definition.UpdatedUtc) + " WHERE " + Scope() + " AND key = " + D.Str(definition.Key) + ";", token);
        }

        public async Task<List<JobDefinition>> ClaimDueDefinitionsAsync(DateTime nowUtc, DateTime lockUntilUtc, int max, CancellationToken token = default)
        {
            int limit = Math.Clamp(max, 1, 100);
            // A locking CTE runs once; an IN (subquery LIMIT n FOR UPDATE SKIP LOCKED) may be re-evaluated and lock more than n rows.
            DataTable table = await QueryAsync("WITH due AS (SELECT d.id FROM jobdefinitions d WHERE " + Scope("d") + " AND d.enabled AND d.nextrunutc IS NOT NULL AND d.nextrunutc <= " + D.Ts(nowUtc) +
                " AND (d.lockeduntilutc IS NULL OR d.lockeduntilutc < " + D.Ts(nowUtc) + ") ORDER BY d.nextrunutc LIMIT " + limit + " FOR UPDATE SKIP LOCKED) " +
                "UPDATE jobdefinitions SET lockeduntilutc = " + D.Ts(lockUntilUtc) + " FROM due WHERE jobdefinitions." + Scope() + " AND jobdefinitions.id = due.id RETURNING jobdefinitions.*;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MapDefinition).ToList();
        }

        public async Task<bool> HasActiveRunAsync(string key, CancellationToken token = default)
        {
            RequireText(key, nameof(key));
            return await CountAsync("SELECT COUNT(*) AS cnt FROM jobruns WHERE " + Scope() + " AND key = " + D.Str(key) + " AND status IN ('Queued', 'Running');", token).ConfigureAwait(false) > 0;
        }

        public Task CreateRunAsync(JobRun run, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(run);
            RequireText(run.Id, nameof(run.Id));
            return QueryAsync("INSERT INTO jobruns (propertyid, id, definitionid, key, trigger, status, isdryrun, skippedreason, error, requestedbyuserid, workerid, createdutc, startedutc, completedutc, " +
                "itemstotal, itemssucceeded, itemsfailed, itemsdead, itemsskipped, costmicros, parameters, summary) VALUES (" + Pid + ", " + D.Str(run.Id) + ", " + D.Str(run.DefinitionId) + ", " + D.Str(run.Key) + ", " +
                D.Enum(run.Trigger) + ", " + D.Enum(run.Status) + ", " + D.Bool(run.IsDryRun) + ", " + D.Str(run.SkippedReason) + ", " + D.Str(run.Error) + ", " + D.Str(run.RequestedByUserId) + ", " +
                D.Str(run.WorkerId) + ", " + D.Ts(run.CreatedUtc) + ", " + D.Ts(run.StartedUtc) + ", " + D.Ts(run.CompletedUtc) + ", " + D.Num(run.ItemsTotal) + ", " + D.Num(run.ItemsSucceeded) + ", " +
                D.Num(run.ItemsFailed) + ", " + D.Num(run.ItemsDead) + ", " + D.Num(run.ItemsSkipped) + ", " + D.Num(run.CostMicros) + ", " + D.Str(run.Parameters) + ", " + D.Str(run.Summary) + ");", token);
        }

        public async Task<JobRun?> ReadRunAsync(string id, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("SELECT * FROM jobruns WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : MapRun(table.Rows[0]);
        }

        private string RunUpdate(JobRun run)
        {
            ArgumentNullException.ThrowIfNull(run);
            return "UPDATE jobruns SET status = " + D.Enum(run.Status) + ", skippedreason = " + D.Str(run.SkippedReason) + ", error = " + D.Str(run.Error) + ", workerid = " + D.Str(run.WorkerId) +
                ", startedutc = " + D.Ts(run.StartedUtc) + ", completedutc = " + D.Ts(run.CompletedUtc) + ", itemstotal = " + D.Num(run.ItemsTotal) + ", itemssucceeded = " + D.Num(run.ItemsSucceeded) +
                ", itemsfailed = " + D.Num(run.ItemsFailed) + ", itemsdead = " + D.Num(run.ItemsDead) + ", itemsskipped = " + D.Num(run.ItemsSkipped) + ", costmicros = " + D.Num(run.CostMicros) +
                ", summary = " + D.Str(run.Summary) + " WHERE " + Scope() + " AND id = " + D.Str(run.Id);
        }

        public Task UpdateRunAsync(JobRun run, CancellationToken token = default)
        {
            return QueryAsync(RunUpdate(run) + ";", token);
        }

        public async Task<bool> UpdateRunningRunAsync(JobRun run, CancellationToken token = default)
        {
            DataTable rows = await QueryAsync(RunUpdate(run) + " AND status='Running' RETURNING id;", token).ConfigureAwait(false);
            return rows.Rows.Count > 0;
        }

        public async Task<bool> CancelRunAsync(string id, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            string active = Scope() + " AND id=" + D.Str(id) + " AND status IN ('Queued','Running')";
            string itemScope = Scope() + " AND runid=" + D.Str(id);
            string count(string status) { return "(SELECT COUNT(*) FROM jobitems WHERE " + itemScope + (status.Length == 0 ? String.Empty : " AND status=" + D.Str(status)) + ")"; }
            List<string> statements = new List<string>
            {
                "SELECT id FROM jobruns WHERE " + active + " FOR UPDATE;",
                "UPDATE jobitems SET status='Skipped',error='The run was cancelled.',completedutc=" + D.Ts(nowUtc) + " WHERE " + itemScope + " AND status='Pending' AND EXISTS(SELECT 1 FROM jobruns WHERE " + active + ");",
                "UPDATE jobruns SET status='Cancelled',completedutc=" + D.Ts(nowUtc) + ",planningtoken=NULL,planninguntilutc=NULL,itemstotal=" + count("") + ",itemssucceeded=" + count("Succeeded") + ",itemsfailed=" + count("Failed") + ",itemsdead=" + count("Dead") + ",itemsskipped=" + count("Skipped") + ",costmicros=(SELECT COALESCE(SUM(costmicros),0) FROM jobitems WHERE " + itemScope + ") WHERE " + active + " RETURNING id;"
            };
            DataTable rows = await TransactionAsync(statements, token).ConfigureAwait(false);
            return rows.Rows.Count > 0;
        }

        public async Task<List<JobRun>> ClaimQueuedRunsAsync(string workerId, DateTime nowUtc, int max, CancellationToken token = default)
        {
            RequireText(workerId, nameof(workerId));
            int limit = Math.Clamp(max, 1, 100);
            DataTable table = await QueryAsync("WITH queued AS (SELECT q.id FROM jobruns q WHERE " + Scope("q") + " AND q.status = 'Queued' AND (q.planninguntilutc IS NULL OR q.planninguntilutc <= " + D.Ts(nowUtc) + ") ORDER BY q.createdutc, q.id LIMIT " + limit + " FOR UPDATE SKIP LOCKED) " +
                "UPDATE jobruns SET planningtoken = " + D.Str(Guid.NewGuid().ToString("N")) + ", planninguntilutc = " + D.Ts(nowUtc.AddMinutes(5)) + ", workerid = " + D.Str(workerId) + ", startedutc = " + D.Ts(nowUtc) + " FROM queued WHERE jobruns." + Scope() + " AND jobruns.id = queued.id RETURNING jobruns.*;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MapRun).OrderBy(r => r.CreatedUtc).ToList();
        }

        public async Task<List<JobRun>> ReadRunningRunsAsync(int max, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT * FROM jobruns WHERE " + Scope() + " AND status = 'Running' ORDER BY createdutc, id LIMIT " + Math.Clamp(max, 1, 500) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MapRun).ToList();
        }

        public async Task<bool> FinishPlanningAsync(JobRun run, List<JobItem> items, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(run);
            ArgumentNullException.ThrowIfNull(items);
            if (String.IsNullOrEmpty(run.PlanningToken) || (run.Status != JobRunStatusEnum.Running && run.Status != JobRunStatusEnum.Failed) || items.Count > 20000 || items.Any(i => i.RunId != run.Id)) throw new ArgumentException("A bounded plan and a fenced planning claim are required.");
            string claim = Scope() + " AND id = " + D.Str(run.Id) + " AND status = 'Queued' AND planningtoken = " + D.Str(run.PlanningToken);
            List<string> statements = new List<string> { "SELECT id FROM jobruns WHERE " + claim + " FOR UPDATE;" };
            foreach (JobItem[] chunk in items.Chunk(500))
            {
                string values = String.Join(", ", chunk.Select(i => "(" + Pid + ", " + D.Str(i.Id) + ", " + D.Str(i.RunId) + ", " + D.Str(i.Key) + ", " + D.Str(i.TargetKey) + ", " + D.Str(i.Payload) + ", " +
                    D.Enum(i.Status) + ", " + D.Num(i.Attempts) + ", " + D.Num(i.MaxAttempts) + ", " + D.Ts(i.NextAttemptUtc) + ", " + D.Bool(i.RequiresHarness) + ", " + D.Ts(i.CreatedUtc) + ")"));
                statements.Add("INSERT INTO jobitems (propertyid,id,runid,key,targetkey,payload,status,attempts,maxattempts,nextattemptutc,requiresharness,createdutc) SELECT * FROM (VALUES " + values + ") AS planned(propertyid,id,runid,key,targetkey,payload,status,attempts,maxattempts,nextattemptutc,requiresharness,createdutc) WHERE EXISTS (SELECT 1 FROM jobruns WHERE " + claim + ") ON CONFLICT(propertyid,runid,targetkey) DO NOTHING;");
            }
            statements.Add("UPDATE jobruns SET status=" + D.Enum(run.Status) + ", itemstotal=" + D.Num(items.Count) + ", error=" + D.Str(run.Error) + ", completedutc=" + D.Ts(run.CompletedUtc) + ", planningtoken=NULL, planninguntilutc=NULL WHERE " + claim + " RETURNING id;");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<int> InsertItemsAsync(List<JobItem> items, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(items);
            if (items.Count == 0) return 0;
            if (items.Count > _MaxInsert) throw new ArgumentOutOfRangeException(nameof(items), "At most " + _MaxInsert + " items per call.");
            int inserted = 0;
            foreach (JobItem[] chunk in items.Chunk(500))
            {
                string values = String.Join(", ", chunk.Select(i => "(" + Pid + ", " + D.Str(i.Id) + ", " + D.Str(i.RunId) + ", " + D.Str(i.Key) + ", " + D.Str(i.TargetKey) + ", " + D.Str(i.Payload) + ", " +
                    D.Enum(i.Status) + ", " + D.Num(i.Attempts) + ", " + D.Num(i.MaxAttempts) + ", " + D.Ts(i.NextAttemptUtc) + ", " + D.Bool(i.RequiresHarness) + ", " + D.Ts(i.CreatedUtc) + ")"));
                DataTable table = await QueryAsync("INSERT INTO jobitems (propertyid, id, runid, key, targetkey, payload, status, attempts, maxattempts, nextattemptutc, requiresharness, createdutc) VALUES " +
                    values + " ON CONFLICT (propertyid, runid, targetkey) DO NOTHING RETURNING id;", token).ConfigureAwait(false);
                inserted += table.Rows.Count;
            }

            return inserted;
        }

        public async Task<List<JobItem>> ClaimItemsAsync(string runId, string workerId, DateTime nowUtc, DateTime lockUntilUtc, int max, bool includeHarness, CancellationToken token = default)
        {
            RequireText(runId, nameof(runId));
            RequireText(workerId, nameof(workerId));
            int limit = Math.Clamp(max, 1, 64);
            // Lock the run row first so concurrent claimers of one run serialize, then take only the free slots
            // (limit minus items already running) through a locking CTE that runs once.
            List<string> statements = new List<string>
            {
                "SELECT id FROM jobruns WHERE " + Scope() + " AND id = " + D.Str(runId) + " FOR UPDATE;",
                "WITH free AS (SELECT p.id FROM jobitems p WHERE " + Scope("p") + " AND p.runid = " + D.Str(runId) + " AND p.status = 'Pending' AND p.nextattemptutc <= " + D.Ts(nowUtc) +
                    " AND EXISTS (SELECT 1 FROM jobruns WHERE " + Scope() + " AND id=" + D.Str(runId) + " AND status='Running')" +
                    (includeHarness ? String.Empty : " AND NOT p.requiresharness") + " ORDER BY p.nextattemptutc, p.id LIMIT GREATEST(0, " + limit + " - (SELECT COUNT(*) FROM jobitems r WHERE " + Scope("r") +
                    " AND r.runid = " + D.Str(runId) + " AND r.status = 'Running')) FOR UPDATE SKIP LOCKED) " +
                    "UPDATE jobitems SET status = 'Running', attempts = jobitems.attempts + 1, workerid = " + D.Str(workerId) + ", lockeduntilutc = " + D.Ts(lockUntilUtc) + ", startedutc = " + D.Ts(nowUtc) +
                    " FROM free WHERE jobitems." + Scope() + " AND jobitems.id = free.id RETURNING jobitems.*;"
            };
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MapItem).OrderBy(i => i.NextAttemptUtc).ThenBy(i => i.Id, StringComparer.Ordinal).ToList();
        }

        public async Task<int> CountRunningItemsAsync(string runId, CancellationToken token = default)
        {
            RequireText(runId, nameof(runId));
            return (int)await CountAsync("SELECT COUNT(*) AS cnt FROM jobitems WHERE " + Scope() + " AND runid = " + D.Str(runId) + " AND status = 'Running';", token).ConfigureAwait(false);
        }

        public async Task<bool> CompleteItemAsync(JobItem item, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(item);
            RequireText(item.Id, nameof(item.Id));
            DataTable table = await QueryAsync("UPDATE jobitems SET status = " + D.Enum(item.Status) + ", result = " + D.Str(item.Result) + ", error = " + D.Str(item.Error) + ", costmicros = " + D.Num(item.CostMicros) +
                ", nextattemptutc = " + D.Ts(item.NextAttemptUtc) + ", lockeduntilutc = NULL, workerid = NULL, completedutc = " + D.Ts(item.CompletedUtc) + " WHERE " + Scope() + " AND id = " + D.Str(item.Id) +
                " AND status = 'Running' AND workerid = " + D.Str(item.WorkerId) + " AND attempts = " + D.Num(item.Attempts) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<int> ReleaseExpiredItemsAsync(DateTime nowUtc, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("UPDATE jobitems SET status = CASE WHEN attempts >= maxattempts THEN 'Dead' ELSE 'Pending' END, error = 'The worker stopped before the attempt finished.', " +
                "lockeduntilutc = NULL, workerid = NULL, nextattemptutc = " + D.Ts(nowUtc) + ", completedutc = CASE WHEN attempts >= maxattempts THEN " + D.Ts(nowUtc) + " ELSE NULL END WHERE " + Scope() +
                " AND status = 'Running' AND lockeduntilutc < " + D.Ts(nowUtc) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        public async Task<int> SkipPendingItemsAsync(string runId, string reason, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(runId, nameof(runId));
            DataTable table = await QueryAsync("UPDATE jobitems SET status = 'Skipped', error = " + D.Str(reason) + ", completedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND runid = " + D.Str(runId) +
                " AND status = 'Pending' RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        public async Task<JobItemCounts> CountItemsAsync(string runId, CancellationToken token = default)
        {
            RequireText(runId, nameof(runId));
            DataTable table = await QueryAsync("SELECT status, COUNT(*) AS cnt, COALESCE(SUM(costmicros), 0) AS cost FROM jobitems WHERE " + Scope() + " AND runid = " + D.Str(runId) + " GROUP BY status;", token).ConfigureAwait(false);
            JobItemCounts counts = new JobItemCounts();
            foreach (DataRow row in table.Rows)
            {
                int n = (int)RowReader.GetLong(row, "cnt");
                counts.Total += n;
                counts.CostMicros += RowReader.GetLong(row, "cost");
                switch (RowReader.GetString(row, "status"))
                {
                    case "Pending":
                    case "Running":
                        counts.Open += n;
                        break;
                    case "Succeeded":
                        counts.Succeeded += n;
                        break;
                    case "Failed":
                        counts.Failed += n;
                        break;
                    case "Dead":
                        counts.Dead += n;
                        break;
                    case "Skipped":
                        counts.Skipped += n;
                        break;
                }
            }

            return counts;
        }

        public async Task<JobItem?> ReadItemAsync(string id, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("SELECT * FROM jobitems WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : MapItem(table.Rows[0]);
        }

        public async Task<bool> RetryItemAsync(string id, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            List<string> statements = new List<string>
            {
                "UPDATE jobruns SET status = 'Running', completedutc = NULL WHERE " + Scope() + " AND id = (SELECT runid FROM jobitems WHERE " + Scope() + " AND id = " + D.Str(id) +
                    " AND status IN ('Dead', 'Failed')) AND status IN ('Succeeded', 'PartiallySucceeded', 'Failed');",
                "UPDATE jobitems SET status = 'Pending', attempts = 0, error = NULL, nextattemptutc = " + D.Ts(nowUtc) + ", completedutc = NULL WHERE " + Scope() + " AND id = " + D.Str(id) +
                    " AND status IN ('Dead', 'Failed') AND runid IN (SELECT id FROM jobruns WHERE " + Scope() + " AND status = 'Running') RETURNING id;"
            };
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<long> CostMicrosSinceAsync(DateTime sinceUtc, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT COALESCE(SUM(costmicros), 0) AS cost FROM jobitems WHERE " + Scope() + " AND createdutc >= " + D.Ts(sinceUtc) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? 0 : RowReader.GetLong(table.Rows[0], "cost");
        }

        public Task<EnumerationResult<JobRun>> EnumerateRunsAsync(EnumerationRequest request, string? key, List<JobRunStatusEnum> statuses, List<JobTriggerEnum> triggers, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            List<string> conditions = new List<string>();
            if (!String.IsNullOrWhiteSpace(key)) conditions.Add("r.key = " + D.Str(key.Trim()));
            if (statuses != null && statuses.Count > 0) conditions.Add("r.status IN " + D.InList(statuses.Select(s => s.ToString())));
            if (triggers != null && triggers.Count > 0) conditions.Add("r.trigger IN " + D.InList(triggers.Select(t => t.ToString())));
            return EnumerateAsync(request, "r.*", "jobruns r", conditions, MapRun, token);
        }

        public Task<EnumerationResult<JobItem>> EnumerateItemsAsync(EnumerationRequest request, string? runId, string? key, List<JobItemStatusEnum> statuses, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            List<string> conditions = new List<string>();
            if (!String.IsNullOrWhiteSpace(runId)) conditions.Add("i.runid = " + D.Str(runId.Trim()));
            if (!String.IsNullOrWhiteSpace(key)) conditions.Add("i.key = " + D.Str(key.Trim()));
            if (statuses != null && statuses.Count > 0) conditions.Add("i.status IN " + D.InList(statuses.Select(s => s.ToString())));
            return EnumerateAsync(request, "i.*", "jobitems i", conditions, MapItem, token);
        }

        public async Task<int> CountOpenRunsAsync(CancellationToken token = default)
        {
            return (int)await CountAsync("SELECT COUNT(*) AS cnt FROM jobruns WHERE " + Scope() + " AND status IN ('Queued', 'Running');", token).ConfigureAwait(false);
        }

        public async Task<Dictionary<string, JobRun>> ReadLatestRunsAsync(IReadOnlyCollection<string> keys, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(keys);
            if (keys.Count > 1000) throw new ArgumentException("At most 1000 keys.", nameof(keys));
            if (keys.Count == 0) return new Dictionary<string, JobRun>(StringComparer.Ordinal);
            DataTable table = await QueryAsync("SELECT DISTINCT ON (key) * FROM jobruns WHERE " + Scope() + " AND key IN " + D.InList(keys) + " ORDER BY key, createdutc DESC, id DESC LIMIT 1000;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MapRun).ToDictionary(r => r.Key, StringComparer.Ordinal);
        }

        public async Task<JobRun?> ReadLatestRunWithParameterAsync(string key, string name, string value, CancellationToken token = default)
        {
            RequireText(key, nameof(key));
            RequireText(name, nameof(name));
            RequireText(value, nameof(value));
            string needle = "%" + JsonSerializer.Serialize(name) + ":" + JsonSerializer.Serialize(value) + "%";
            DataTable table = await QueryAsync("SELECT * FROM jobruns WHERE " + Scope() + " AND key = " + D.Str(key) + " AND parameters LIKE " + D.Str(needle) +
                " ORDER BY createdutc DESC, id DESC LIMIT 1;", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : MapRun(table.Rows[0]);
        }

        public async Task<int> PruneAsync(DateTime beforeUtc, int maxRuns, CancellationToken token = default)
        {
            string ids = "SELECT id FROM jobruns WHERE " + Scope() + " AND status NOT IN ('Queued', 'Running') AND COALESCE(completedutc, createdutc) < " + D.Ts(beforeUtc) +
                " ORDER BY createdutc LIMIT " + Math.Clamp(maxRuns, 1, 1000);
            List<string> statements = new List<string>
            {
                "DELETE FROM jobitems WHERE " + Scope() + " AND runid IN (" + ids + ");",
                "DELETE FROM jobruns WHERE " + Scope() + " AND id IN (" + ids + ") RETURNING id;"
            };
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        private string Long(long? value)
        {
            return value == null ? "NULL" : D.Num(value.Value);
        }

        private static string Days(List<DayOfWeek> days)
        {
            return days == null ? String.Empty : String.Join(",", days.Distinct().OrderBy(d => (int)d).Select(d => ((int)d).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        private static List<DayOfWeek> ParseDays(string? text)
        {
            List<DayOfWeek> days = new List<DayOfWeek>();
            if (String.IsNullOrWhiteSpace(text)) return days;
            foreach (string part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (Int32.TryParse(part, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int n) && n >= 0 && n <= 6) days.Add((DayOfWeek)n);
            }

            return days;
        }

        private static T ParseEnum<T>(string text, T fallback) where T : struct, System.Enum
        {
            return System.Enum.TryParse<T>(text, true, out T value) ? value : fallback;
        }

        private static JobDefinition MapDefinition(DataRow row)
        {
            return new JobDefinition
            {
                Id = RowReader.GetString(row, "id"),
                Key = RowReader.GetString(row, "key"),
                Name = RowReader.GetString(row, "name"),
                Description = RowReader.GetString(row, "description"),
                Enabled = RowReader.GetBool(row, "enabled"),
                IntervalType = ParseEnum(RowReader.GetString(row, "intervaltype"), JobIntervalTypeEnum.Manual),
                IntervalValue = RowReader.GetInt(row, "intervalvalue"),
                TimeOfDayMinutesUtc = RowReader.GetInt(row, "timeofdayminutesutc"),
                DaysOfWeek = ParseDays(RowReader.GetNullableString(row, "daysofweek")),
                MaxConcurrency = RowReader.GetInt(row, "maxconcurrency"),
                MaxAttempts = RowReader.GetInt(row, "maxattempts"),
                BudgetMicrosPerRun = RowReader.GetNullableLong(row, "budgetmicrosperrun"),
                RequiresHarness = RowReader.GetBool(row, "requiresharness"),
                NextRunUtc = RowReader.GetNullableDateTime(row, "nextrunutc"),
                LastRunUtc = RowReader.GetNullableDateTime(row, "lastrunutc"),
                LockedUntilUtc = RowReader.GetNullableDateTime(row, "lockeduntilutc"),
                UpdatedByUserId = RowReader.GetNullableString(row, "updatedbyuserid"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }

        private static JobRun MapRun(DataRow row)
        {
            return new JobRun
            {
                Id = RowReader.GetString(row, "id"),
                DefinitionId = RowReader.GetString(row, "definitionid"),
                Key = RowReader.GetString(row, "key"),
                Trigger = ParseEnum(RowReader.GetString(row, "trigger"), JobTriggerEnum.Schedule),
                Status = ParseEnum(RowReader.GetString(row, "status"), JobRunStatusEnum.Queued),
                IsDryRun = RowReader.GetBool(row, "isdryrun"),
                SkippedReason = RowReader.GetNullableString(row, "skippedreason"),
                Error = RowReader.GetNullableString(row, "error"),
                RequestedByUserId = RowReader.GetNullableString(row, "requestedbyuserid"),
                WorkerId = RowReader.GetNullableString(row, "workerid"),
                PlanningToken = RowReader.GetNullableString(row, "planningtoken"),
                PlanningUntilUtc = RowReader.GetNullableDateTime(row, "planninguntilutc"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                StartedUtc = RowReader.GetNullableDateTime(row, "startedutc"),
                CompletedUtc = RowReader.GetNullableDateTime(row, "completedutc"),
                ItemsTotal = RowReader.GetInt(row, "itemstotal"),
                ItemsSucceeded = RowReader.GetInt(row, "itemssucceeded"),
                ItemsFailed = RowReader.GetInt(row, "itemsfailed"),
                ItemsDead = RowReader.GetInt(row, "itemsdead"),
                ItemsSkipped = RowReader.GetInt(row, "itemsskipped"),
                CostMicros = RowReader.GetLong(row, "costmicros"),
                Parameters = RowReader.GetNullableString(row, "parameters"),
                Summary = RowReader.GetNullableString(row, "summary")
            };
        }

        private static JobItem MapItem(DataRow row)
        {
            return new JobItem
            {
                Id = RowReader.GetString(row, "id"),
                RunId = RowReader.GetString(row, "runid"),
                Key = RowReader.GetString(row, "key"),
                TargetKey = RowReader.GetString(row, "targetkey"),
                Payload = RowReader.GetNullableString(row, "payload"),
                Status = ParseEnum(RowReader.GetString(row, "status"), JobItemStatusEnum.Pending),
                Attempts = RowReader.GetInt(row, "attempts"),
                MaxAttempts = RowReader.GetInt(row, "maxattempts"),
                NextAttemptUtc = RowReader.GetDateTime(row, "nextattemptutc"),
                LockedUntilUtc = RowReader.GetNullableDateTime(row, "lockeduntilutc"),
                WorkerId = RowReader.GetNullableString(row, "workerid"),
                RequiresHarness = RowReader.GetBool(row, "requiresharness"),
                Result = RowReader.GetNullableString(row, "result"),
                Error = RowReader.GetNullableString(row, "error"),
                CostMicros = RowReader.GetLong(row, "costmicros"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                StartedUtc = RowReader.GetNullableDateTime(row, "startedutc"),
                CompletedUtc = RowReader.GetNullableDateTime(row, "completedutc")
            };
        }
    }
}
