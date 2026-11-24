namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// What happened to a member contribution in one content changelog row (docs/INTEGRATION.md content review contract).
    /// </summary>
    public enum ContentChangeActionEnum
    {
        /// <summary>
        /// The contribution was created.
        /// </summary>
        Create,

        /// <summary>
        /// Its text or media changed.
        /// </summary>
        Update,

        /// <summary>
        /// It was deleted (soft or hard).
        /// </summary>
        Delete
    }
}
