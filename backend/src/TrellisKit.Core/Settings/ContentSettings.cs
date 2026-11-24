namespace TrellisKit.Core.Settings
{
    using System;

    /// <summary>
    /// Settings shared by feed posts and comments.
    /// </summary>
    public class ContentSettings
    {
        #region Public-Members

        /// <summary>
        /// Edits within this time after publishing store no revision and set no editedUtc. Default 120. Minimum 0, maximum 3600.
        /// </summary>
        public int EditGraceSeconds
        {
            get { return _EditGraceSeconds; }
            set { _EditGraceSeconds = Math.Clamp(value, 0, 3600); }
        }

        #endregion

        #region Private-Members

        private int _EditGraceSeconds = 120;

        #endregion
    }
}
