namespace TrellisKit.Core.Integrations.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The object an upload session writes: media identifier, location, key, declared content type, and size.
    /// </summary>
    public class UploadTarget
    {
        #region Public-Members

        /// <summary>
        /// Media identifier (Local mode upload URLs are addressed by it).
        /// </summary>
        public string MediaId { get; set; } = String.Empty;

        /// <summary>
        /// Location of the original.
        /// </summary>
        public MediaLocationEnum Location { get; set; } = MediaLocationEnum.Source;

        /// <summary>
        /// Object key, for example in/med_x/original.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Declared content type.
        /// </summary>
        public string ContentType { get; set; } = String.Empty;

        /// <summary>
        /// Declared size in bytes.
        /// </summary>
        public long SizeBytes { get; set; } = 0;

        /// <summary>
        /// Origin used to build absolute Local mode URLs, for example http://127.0.0.1:8000. Ignored in S3 mode.
        /// </summary>
        public string Origin { get; set; } = String.Empty;

        #endregion
    }
}
