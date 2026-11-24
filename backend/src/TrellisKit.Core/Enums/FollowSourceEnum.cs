namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Why a member follows a thread (docs/PHASE3_CONTRACT.md section 9.2).
    /// </summary>
    public enum FollowSourceEnum
    {
        /// <summary>
        /// The member followed it.
        /// </summary>
        Explicit,

        /// <summary>
        /// The member created it.
        /// </summary>
        Author,

        /// <summary>
        /// The member commented on it (forums.autoFollowOnComment).
        /// </summary>
        Comment
    }
}
