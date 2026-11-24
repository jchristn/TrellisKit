namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using TrellisKit.Core.Database.Postgresql.Queries;
    using TrellisKit.Core.Database.Shared;

    /// <summary>
    /// PostgreSQL brand palette methods (shared SQL with the PostgreSQL dialect).
    /// </summary>
    internal sealed class BrandPaletteMethods : BrandPaletteMethodsBase
    {
        internal BrandPaletteMethods(DatabaseDriverBase driver, string? propertyId) : base(driver, PostgresqlDialect.Instance, propertyId)
        {
        }
    }
}
