namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Why a comment was deleted.
    /// </summary>
    public enum CommentDeletedReasonEnum
    {
        /// <summary>
        /// The author deleted it.
        /// </summary>
        Author,

        /// <summary>
        /// An administrator deleted it.
        /// </summary>
        Admin,

        /// <summary>
        /// The author deleted their account.
        /// </summary>
        AccountDeleted
    }
}
