namespace TrellisKit.Core.Database.Postgresql.Queries
{
    using System;
    using Npgsql;
    using TrellisKit.Core.Database.Shared;

    /// <summary>
    /// PostgreSQL SQL fragments: timestamptz literals, native booleans, to_char for day buckets, and SQLSTATE 23505.
    /// </summary>
    internal sealed class PostgresqlDialect : SqlDialect
    {
        internal static readonly PostgresqlDialect Instance = new PostgresqlDialect();

        private const string _UniqueViolation = "23505";

        internal override string Name
        {
            get { return "postgresql"; }
        }

        internal override string Str(string? value)
        {
            return Sanitizer.Str(value);
        }

        internal override string Ts(DateTime value)
        {
            return Sanitizer.Ts(value);
        }

        internal override string Bool(bool value)
        {
            return Sanitizer.Bool(value);
        }

        internal override string Num(long value)
        {
            return Sanitizer.Num(value);
        }

        internal override string Num(double value)
        {
            return Sanitizer.Num(value);
        }

        internal override string SubscriptionUpdateWithMetrics(string updateSql, string propertyId, string subscriptionId, DateTime occurredUtc)
        {
            return SubscriptionMetricsSql.Update(updateSql, propertyId, subscriptionId, occurredUtc);
        }

        internal override string DayOf(string column)
        {
            return "to_char(" + column + " AT TIME ZONE 'UTC', 'YYYY-MM-DD')";
        }

        internal override string StartsWith(string expression, string prefix)
        {
            if (String.IsNullOrEmpty(prefix)) return "TRUE";
            return expression + " LIKE " + Str(LikeEscape(prefix) + "%") + " ESCAPE '\\'";
        }

        internal override bool IsUniqueViolation(Exception exception)
        {
            return exception is PostgresException postgres && postgres.SqlState == _UniqueViolation;
        }
    }
}
