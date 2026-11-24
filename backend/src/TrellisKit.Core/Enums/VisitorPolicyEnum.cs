namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Whether a venue welcomes visitors (docs/PHASE4_CONTRACT.md section 2.2).
    /// </summary>
    public enum VisitorPolicyEnum
    {
        /// <summary>
        /// Not stated.
        /// </summary>
        Unknown,

        /// <summary>
        /// Visitors welcome.
        /// </summary>
        Welcome,

        /// <summary>
        /// Visitors by arrangement.
        /// </summary>
        ByArrangement,

        /// <summary>
        /// Members only.
        /// </summary>
        MembersOnly
    }
}
