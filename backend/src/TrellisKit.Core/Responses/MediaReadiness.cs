namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// Processing state of a target's attachments.
    /// </summary>
    public class MediaReadiness
    {
        #region Public-Members

        /// <summary>
        /// Attachments.
        /// </summary>
        public int Count { get; set; } = 0;

        /// <summary>
        /// Whether every attachment is Ready (true when there are none).
        /// </summary>
        public bool AllReady { get; set; } = true;

        /// <summary>
        /// Whether any attachment Failed.
        /// </summary>
        public bool AnyFailed { get; set; } = false;

        #endregion
    }
}
