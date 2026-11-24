namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Who made a thread revision (docs/PHASE3_CONTRACT.md section 4.1).
    /// </summary>
    public enum ThreadEditorRoleEnum
    {
        /// <summary>
        /// The thread author.
        /// </summary>
        Author,

        /// <summary>
        /// A moderator in scope (title, labels, tags, or type).
        /// </summary>
        Moderator
    }
}
