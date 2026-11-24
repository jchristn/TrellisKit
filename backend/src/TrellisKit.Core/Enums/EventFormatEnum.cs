namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// How an event takes place (docs/PHASE4_CONTRACT.md section 2.5).
    /// </summary>
    public enum EventFormatEnum
    {
        /// <summary>
        /// At a place (a directory venue, or a name and address).
        /// </summary>
        InPerson,

        /// <summary>
        /// Online only (a join link).
        /// </summary>
        Online,

        /// <summary>
        /// At a place and online.
        /// </summary>
        Hybrid
    }
}
