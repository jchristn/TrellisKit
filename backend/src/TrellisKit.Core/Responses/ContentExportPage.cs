namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One keyset batch of a member's content for the account export (contract section 11). Pass
    /// <see cref="NextAfterId"/> back to read the next batch; null means the end.
    /// </summary>
    /// <typeparam name="T">Item type.</typeparam>
    public class ContentExportPage<T>
    {
        #region Public-Members

        /// <summary>
        /// Items in identifier order (never null).
        /// </summary>
        public List<T> Objects
        {
            get { return _Objects; }
            set { _Objects = value ?? new List<T>(); }
        }

        /// <summary>
        /// Identifier to continue after, or null when this was the last batch.
        /// </summary>
        public string? NextAfterId { get; set; } = null;

        #endregion

        #region Private-Members

        private List<T> _Objects = new List<T>();

        #endregion
    }
}
