namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Status of a member's verified rank row (memberranks).
    /// </summary>
    public enum RankStatusEnum
    {
        /// <summary>
        /// The rank was awarded by a rank awarder or SystemAdmin and is the member's effective rank.
        /// </summary>
        Verified,

        /// <summary>
        /// The award was revoked; the member's profile value is shown as self-reported again.
        /// </summary>
        Revoked,

        /// <summary>
        /// The award is disputed; the member's profile value is shown as self-reported until it is resolved.
        /// </summary>
        Disputed
    }
}
