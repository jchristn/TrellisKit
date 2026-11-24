namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Supported database providers. TrellisKit is PostgreSQL-only (docs/TRELLISKIT.md section 13).
    /// </summary>
    public enum DatabaseTypeEnum
    {
        /// <summary>
        /// PostgreSQL (17 or later, with pgvector) in every environment.
        /// </summary>
        Postgresql
    }
}
