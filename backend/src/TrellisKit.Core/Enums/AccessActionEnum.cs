namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// What a content route does, for the access model decision (docs/ACCESS_MODELS_CONTRACT.md section 2).
    /// </summary>
    public enum AccessActionEnum
    {
        /// <summary>
        /// Read one object.
        /// </summary>
        Read,

        /// <summary>
        /// List or enumerate a collection.
        /// </summary>
        List,

        /// <summary>
        /// Create or edit an object of the feature (create a post, edit your own, upload media to attach).
        /// </summary>
        Write,

        /// <summary>
        /// Take part on an existing object (comment, reply, react).
        /// </summary>
        Participate
    }
}
