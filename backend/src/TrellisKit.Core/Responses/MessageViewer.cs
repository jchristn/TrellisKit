namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// What the caller may do with a message (docs/PHASE5_CONTRACT.md section 1.4).
    /// </summary>
    public class MessageViewer
    {
        #region Public-Members

        /// <summary>
        /// May edit (author, once, within the edit window).
        /// </summary>
        public bool CanEdit { get; set; } = false;

        /// <summary>
        /// May delete for everyone (author).
        /// </summary>
        public bool CanDelete { get; set; } = false;

        /// <summary>
        /// May react.
        /// </summary>
        public bool CanReact { get; set; } = false;

        /// <summary>
        /// May report (not the author).
        /// </summary>
        public bool CanReport { get; set; } = false;

        #endregion
    }
}
