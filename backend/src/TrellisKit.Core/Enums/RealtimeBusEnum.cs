namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Cross-task realtime bus implementation (docs/PHASE5_CONTRACT.md section 4.11).
    /// </summary>
    public enum RealtimeBusEnum
    {
        /// <summary>
        /// PostgreSQL LISTEN and NOTIFY (default).
        /// </summary>
        Postgres,

        /// <summary>
        /// In-process only (single task, tests, local development).
        /// </summary>
        InProcess
    }
}
