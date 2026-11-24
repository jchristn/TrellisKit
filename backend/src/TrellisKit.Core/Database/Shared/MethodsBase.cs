namespace TrellisKit.Core.Database.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Threading;
    using System.Globalization;
    using System.Linq;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Base for the shared, dialect-parameterized method implementations. Property-owned method sets are bound to one
    /// property id at construction (docs/MULTIPROPERTY_CONTRACT.md section 4.3): every statement they build carries
    /// the propertyid predicate through <see cref="Scope()"/> and every INSERT the <see cref="Pid"/> literal, so no
    /// caller can read or write another property's rows. Deployment-level method sets (registry, platform admins,
    /// unprefixed job leases) are constructed without a binding.
    /// </summary>
    internal abstract class MethodsBase
    {
        protected DatabaseDriverBase Driver { get; }

        protected SqlDialect D { get; }

        /// <summary>
        /// Bound property id, or null for deployment-level method sets.
        /// </summary>
        protected string? BoundPropertyId { get; }

        /// <summary>
        /// Bound property id. Throws when the method set is not bound (a deployment-level set touching property data).
        /// </summary>
        protected string PropertyId
        {
            get
            {
                if (BoundPropertyId == null) throw Violation("missing_scope", GetType().Name + " is not bound to a property.");
                return BoundPropertyId;
            }
        }

        /// <summary>
        /// SQL literal of the bound property id, for INSERT value lists.
        /// </summary>
        protected string Pid
        {
            get { return D.Str(PropertyId); }
        }

        /// <summary>
        /// Whether enumerations add the property predicate on the collection's own table automatically. False for
        /// deployment-level tables (they have no propertyid column); the labels and tags subqueries are still scoped.
        /// </summary>
        protected virtual bool ScopesCollectionTable
        {
            get { return true; }
        }

        protected MethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId)
        {
            Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            D = dialect ?? throw new ArgumentNullException(nameof(dialect));
            if (propertyId != null && !PropertyIds.IsValid(propertyId) && propertyId != PropertyIds.Platform && propertyId != PropertyIds.Unresolved)
                throw Violation(PropertyIds.IsReserved(propertyId) ? "reserved_id" : "invalid_id", "Invalid property id for a data binding: " + propertyId + ".");
            BoundPropertyId = propertyId;
        }

        /// <summary>
        /// The property predicate on an unaliased table: propertyid = '&lt;id&gt;'.
        /// </summary>
        protected string Scope()
        {
            return "propertyid = " + Pid;
        }

        /// <summary>
        /// The property predicate on an aliased table: alias.propertyid = '&lt;id&gt;'.
        /// </summary>
        protected string Scope(string alias)
        {
            RequireText(alias, nameof(alias));
            return alias + ".propertyid = " + Pid;
        }

        private static PropertyScopeException Violation(string kind, string message)
        {
            TrellisKitTelemetry.Increment(TrellisKitTelemetry.IsolationViolations, "kind", kind);
            return new PropertyScopeException(kind, message);
        }

        protected Task<DataTable> QueryAsync(string sql, CancellationToken token)
        {
            return Driver.ExecuteQueryAsync(sql, false, token);
        }

        /// <summary>
        /// Rows of <paramref name="table"/> (alias-free) whose excerpt still holds Markdown syntax, after an identifier.
        /// </summary>
        protected async Task<List<TrellisKit.Core.Models.ExcerptSource>> ExcerptCandidatesCoreAsync(string table, string? titleColumn, string? afterId, int max, CancellationToken token)
        {
            max = Math.Clamp(max, 1, 1000);
            string after = String.IsNullOrEmpty(afterId) ? String.Empty : " AND id > " + D.Str(afterId);
            string stale = "(excerpt LIKE " + D.Str("%`%") + " OR excerpt LIKE " + D.Str("%|%") + " OR excerpt LIKE " + D.Str("%---%") +
                " OR excerpt LIKE " + D.Str("%~~~%") + " OR excerpt LIKE " + D.Str("#%") + ")";
            DataTable rows = await QueryAsync("SELECT id, body, excerpt" + (titleColumn == null ? String.Empty : ", " + titleColumn + " AS title") + " FROM " + table +
                " WHERE " + Scope() + after + " AND " + stale + " ORDER BY id LIMIT " + D.Num(max) + ";", token).ConfigureAwait(false);
            List<TrellisKit.Core.Models.ExcerptSource> result = new List<TrellisKit.Core.Models.ExcerptSource>();
            foreach (DataRow row in rows.Rows)
            {
                result.Add(new TrellisKit.Core.Models.ExcerptSource
                {
                    Id = RowReader.GetString(row, "id"),
                    Body = RowReader.GetNullableString(row, "body") ?? String.Empty,
                    Title = titleColumn == null ? null : RowReader.GetNullableString(row, "title"),
                    Excerpt = RowReader.GetNullableString(row, "excerpt") ?? String.Empty
                });
            }
            return result;
        }

        /// <summary>
        /// Replace one row's excerpt in <paramref name="table"/>.
        /// </summary>
        protected Task UpdateExcerptCoreAsync(string table, string id, string excerpt, CancellationToken token)
        {
            return QueryAsync("UPDATE " + table + " SET excerpt = " + D.Str(excerpt) + " WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token);
        }

        protected Task<DataTable> TransactionAsync(List<string> statements, CancellationToken token)
        {
            return Driver.ExecuteQueriesAsync(statements, true, token);
        }

        protected async Task<long> CountAsync(string sql, CancellationToken token)
        {
            DataTable table = await QueryAsync(sql, token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? 0 : RowReader.GetLong(table.Rows[0], "cnt");
        }

        protected async Task<DataTable> UniqueAsync(string sql, string what, CancellationToken token)
        {
            try
            {
                return await QueryAsync(sql, token).ConfigureAwait(false);
            }
            catch (Exception e) when (D.IsUniqueViolation(e))
            {
                throw new DuplicateRecordException(what + " already exists.", e);
            }
        }

        protected async Task<DataTable> UniqueTransactionAsync(List<string> statements, string what, CancellationToken token)
        {
            try
            {
                return await TransactionAsync(statements, token).ConfigureAwait(false);
            }
            catch (Exception e) when (D.IsUniqueViolation(e))
            {
                throw new DuplicateRecordException(what + " already exists.", e);
            }
        }

        protected static string Where(List<string> conditions)
        {
            return conditions.Count == 0 ? String.Empty : " WHERE " + String.Join(" AND ", conditions);
        }

        /// <summary>
        /// EXISTS predicate: the member in userColumn has an active entitlement, optionally restricted by source.
        /// </summary>
        protected string ActiveEntitlementExists(string userColumn, DateTime nowUtc, string? sourceCondition)
        {
            return "EXISTS (SELECT 1 FROM entitlements ae WHERE " + Scope("ae") + " AND ae.userid = " + userColumn +
                " AND ae.revokedutc IS NULL AND ae.startsutc <= " + D.Ts(nowUtc) +
                " AND (ae.endsutc IS NULL OR ae.endsutc > " + D.Ts(nowUtc) + ")" +
                (sourceCondition == null ? String.Empty : " AND ae." + sourceCondition) + ")";
        }

        /// <summary>
        /// The shared enumeration conditions of a request (docs/ENUMERATION_AND_METADATA.md section 2): created window,
        /// search over the collection's expressions, and label and tag filters as EXISTS subqueries on the labels and
        /// tags tables. Does not include the keyset position.
        /// </summary>
        protected List<string> EnumerationConditions(EnumerationRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            EnumerationQuery q = request.Query;
            EnumerationCollection c = request.Collection;
            List<string> conditions = new List<string>();
            if (ScopesCollectionTable)
            {
                int dot = c.IdColumn.IndexOf('.');
                conditions.Add(dot > 0 ? Scope(c.IdColumn.Substring(0, dot)) : Scope());
            }
            if (q.CreatedAfterUtc != null) conditions.Add(c.CreatedColumn + " >= " + D.Ts(q.CreatedAfterUtc.Value));
            if (q.CreatedBeforeUtc != null) conditions.Add(c.CreatedColumn + " < " + D.Ts(q.CreatedBeforeUtc.Value));
            if (!String.IsNullOrEmpty(q.Search) && c.SearchExpressions.Count > 0)
                conditions.Add("(" + String.Join(" OR ", c.SearchExpressions.Select(e => D.Contains("COALESCE(" + e + ", '')", q.Search))) + ")");
            if (c.TargetType != null)
            {
                string type = D.Enum(c.TargetType.Value);
                List<string> labels = q.Labels.Select(MetadataRules.Normalize).Distinct(StringComparer.Ordinal).ToList();
                if (labels.Count > 0)
                {
                    if (q.LabelMatch == LabelMatchEnum.Any)
                        conditions.Add("EXISTS (SELECT 1 FROM labels ml WHERE " + Scope("ml") + " AND ml.targettype = " + type + " AND ml.targetid = " + c.IdColumn + " AND ml.labelnormalized IN " + D.InList(labels) + ")");
                    else
                        foreach (string label in labels)
                            conditions.Add("EXISTS (SELECT 1 FROM labels ml WHERE " + Scope("ml") + " AND ml.targettype = " + type + " AND ml.targetid = " + c.IdColumn + " AND ml.labelnormalized = " + D.Str(label) + ")");
                }
                foreach (KeyValuePair<string, string> tag in q.Tags)
                {
                    string condition = "EXISTS (SELECT 1 FROM tags mt WHERE " + Scope("mt") + " AND mt.targettype = " + type + " AND mt.targetid = " + c.IdColumn + " AND mt.tagkeynormalized = " + D.Str(MetadataRules.Normalize(tag.Key));
                    if (tag.Value != "*") condition += " AND mt.tagvalue = " + D.Str(tag.Value ?? String.Empty);
                    conditions.Add(condition + ")");
                }
            }
            return conditions;
        }

        /// <summary>
        /// Run one enumeration page with keyset continuation (or skip), clamped to request.MaxResults, plus exact
        /// totals when request.CountTotals. The sort expression and id are selected as enumsortkey and enumid.
        /// </summary>
        /// <typeparam name="T">Mapped type.</typeparam>
        /// <param name="request">Normalized request.</param>
        /// <param name="select">Select list (without SELECT).</param>
        /// <param name="from">FROM clause body (tables, aliases, joins).</param>
        /// <param name="conditions">Collection-specific conditions (route scope, visibility, filters).</param>
        /// <param name="map">Row mapper.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        protected async Task<EnumerationResult<T>> EnumerateAsync<T>(EnumerationRequest request, string select, string from, List<string> conditions, Func<DataRow, T> map, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(conditions);
            ArgumentNullException.ThrowIfNull(map);
            List<string> all = new List<string>(conditions);
            all.AddRange(EnumerationConditions(request));
            EnumerationOrdering ordering = request.Ordering;
            string id = request.Collection.IdColumn;
            List<string> paged = new List<string>(all);
            if (request.HasPosition) paged.Add(Keyset(ordering, id, request.AfterKey, request.AfterId!));
            string direction = ordering.Descending ? " DESC" : " ASC";
            string sql = "SELECT " + select + ", (" + ordering.Expression + ") AS enumsortkey, " + id + " AS enumid FROM " + from + Where(paged) +
                " ORDER BY (" + ordering.Expression + ")" + direction + ", " + id + direction +
                " LIMIT " + D.Num(request.MaxResults + 1) + (!request.HasPosition && request.Skip > 0 ? " OFFSET " + D.Num(request.Skip) : String.Empty) + ";";
            DataTable table = await QueryAsync(sql, token).ConfigureAwait(false);
            List<DataRow> rows = table.Rows.Cast<DataRow>().ToList();
            bool more = rows.Count > request.MaxResults;
            if (more) rows = rows.Take(request.MaxResults).ToList();

            EnumerationResult<T> result = new EnumerationResult<T>
            {
                MaxResults = request.MaxResults,
                Skip = request.HasPosition ? 0 : request.Skip,
                EndOfResults = !more,
                TimestampUtc = DateTime.UtcNow,
                Objects = rows.Select(map).ToList()
            };
            string? lastKey = null;
            string? lastId = null;
            if (rows.Count > 0)
            {
                DataRow last = rows[rows.Count - 1];
                lastKey = KeyText(ordering.KeyKind, last);
                lastId = RowReader.GetString(last, "enumid");
                if (more) result.ContinuationToken = ContinuationTokens.Encode(request.Fingerprint, ordering.Name, lastKey, lastId);
            }
            if (request.CountTotals)
            {
                result.TotalRecords = await CountAsync("SELECT COUNT(*) AS cnt FROM " + from + Where(all) + ";", token).ConfigureAwait(false);
                if (!more) result.RecordsRemaining = 0;
                else
                {
                    List<string> after = new List<string>(all) { Keyset(ordering, id, lastKey, lastId!) };
                    result.RecordsRemaining = await CountAsync("SELECT COUNT(*) AS cnt FROM " + from + Where(after) + ";", token).ConfigureAwait(false);
                }
            }
            return result;
        }

        private string Keyset(EnumerationOrdering ordering, string idColumn, string? key, string id)
        {
            string op = ordering.Descending ? " < " : " > ";
            if (key == null) return idColumn + op + D.Str(id);
            string literal = KeyLiteral(ordering.KeyKind, key);
            return "((" + ordering.Expression + ")" + op + literal + " OR ((" + ordering.Expression + ") = " + literal + " AND " + idColumn + op + D.Str(id) + "))";
        }

        private string KeyLiteral(EnumerationKeyKindEnum kind, string key)
        {
            switch (kind)
            {
                case EnumerationKeyKindEnum.Time:
                    if (!DateTime.TryParse(key, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime time)) throw ContinuationTokens.Invalid();
                    return D.Ts(DateTime.SpecifyKind(time, DateTimeKind.Utc));
                case EnumerationKeyKindEnum.Number:
                    if (!Int64.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out long number)) throw ContinuationTokens.Invalid();
                    return D.Num(number);
                default:
                    return D.Str(key);
            }
        }

        private static string? KeyText(EnumerationKeyKindEnum kind, DataRow row)
        {
            switch (kind)
            {
                case EnumerationKeyKindEnum.Time:
                    DateTime? time = RowReader.GetNullableDateTime(row, "enumsortkey");
                    return time?.ToString("O", CultureInfo.InvariantCulture);
                case EnumerationKeyKindEnum.Number:
                    long? number = RowReader.GetNullableLong(row, "enumsortkey");
                    return number?.ToString(CultureInfo.InvariantCulture);
                default:
                    return RowReader.GetNullableString(row, "enumsortkey");
            }
        }

        protected static void RequireText(string? value, string name)
        {
            if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(name);
        }
    }
}
