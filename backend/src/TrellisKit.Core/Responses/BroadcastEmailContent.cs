namespace TrellisKit.Core.Responses
{
    /// <summary>
    /// The email part of a broadcast (docs/PHASE5_CONTRACT.md section 2.5), in requests and responses.
    /// </summary>
    public class BroadcastEmailContent
    {
        #region Public-Members

        /// <summary>
        /// Subject (at most 200 characters), or null for the announcement title.
        /// </summary>
        public string? Subject { get; set; } = null;

        /// <summary>
        /// Preheader (at most 200 characters), or null.
        /// </summary>
        public string? Preheader { get; set; } = null;

        /// <summary>
        /// Body (Markdown subset, at most 20000 characters), or null for the announcement body.
        /// </summary>
        public string? Body { get; set; } = null;

        /// <summary>
        /// Button label (at most 60 characters), or null for "Read more".
        /// </summary>
        public string? CtaLabel { get; set; } = null;

        /// <summary>
        /// Button path on the property's website (starts with /), or null for the announcement path.
        /// </summary>
        public string? CtaPath { get; set; } = null;

        #endregion
    }
}
