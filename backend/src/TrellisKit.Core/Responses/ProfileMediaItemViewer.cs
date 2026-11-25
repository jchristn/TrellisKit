namespace TrellisKit.Core.Responses
{
    /// <summary>
    /// What the caller may do with a profile media item (docs/PHASE4_CONTRACT.md section 3.13).
    /// </summary>
    public class ProfileMediaItemViewer
    {
        #region Public-Members

        /// <summary>
        /// The caller uploaded it.
        /// </summary>
        public bool IsUploader { get; set; } = false;

        /// <summary>
        /// The caller may delete (the uploader) or remove (profile moderators, a linked instructor of the venue) it.
        /// </summary>
        public bool CanRemove { get; set; } = false;

        /// <summary>
        /// The caller may report it (signed in, not the uploader).
        /// </summary>
        public bool CanReport { get; set; } = false;

        #endregion
    }
}
