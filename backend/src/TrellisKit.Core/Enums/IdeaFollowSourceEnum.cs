namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Why a member follows an idea (docs/PHASE5_CONTRACT.md section 7.1).
    /// </summary>
    public enum IdeaFollowSourceEnum
    {
        /// <summary>
        /// The member submitted the idea.
        /// </summary>
        Author,

        /// <summary>
        /// The member voted (voting follows automatically).
        /// </summary>
        Vote,

        /// <summary>
        /// The member followed explicitly.
        /// </summary>
        Manual,

        /// <summary>
        /// The member followed an idea that was merged into this one.
        /// </summary>
        Merge
    }
}
