namespace TrellisKit.Core.Settings
{
    using System;

    /// <summary>
    /// Local object storage for uploads in Phase 1 (S3 arrives in Phase 2).
    /// </summary>
    public class StorageSettings
    {
        #region Public-Members

        /// <summary>
        /// Directory for stored objects.
        /// </summary>
        public string Directory { get; set; } = "data/objects";

        /// <summary>
        /// Maximum avatar upload size in bytes. Default 5242880. Minimum 10240, maximum 20971520.
        /// </summary>
        public int MaxAvatarBytes
        {
            get { return _MaxAvatarBytes; }
            set { _MaxAvatarBytes = Math.Clamp(value, 10240, 20971520); }
        }

        #endregion

        #region Private-Members

        private int _MaxAvatarBytes = 5242880;

        #endregion
    }
}
