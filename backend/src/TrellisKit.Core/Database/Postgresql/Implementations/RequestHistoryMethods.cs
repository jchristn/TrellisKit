namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using System;
    using TrellisKit.Core.Database.Shared;
    using TrellisKit.Core.Database.Postgresql.Queries;

    /// <summary>
    /// PostgreSQL request history methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class RequestHistoryMethods : RequestHistoryMethodsBase
    {
        internal RequestHistoryMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }

        protected override string BucketGroupBy
        {
            get { return "1"; }
        }

        protected override string BucketExpression(DateTime fromUtc, int bucketMinutes)
        {
            return "FLOOR(EXTRACT(EPOCH FROM (createdutc - " + D.Ts(fromUtc) + ")) / " + D.Num(bucketMinutes * 60) + ")::BIGINT";
        }

        protected override string ContainsCaseSensitive(string column, string text)
        {
            return "strpos(" + column + ", " + D.Str(text) + ") > 0";
        }
    }
}
