namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// One import problem.
    /// </summary>
    public class DirectoryImportError
    {
        #region Public-Members

        /// <summary>
        /// Zero-based row index.
        /// </summary>
        public int Row { get; set; } = 0;

        /// <summary>
        /// Field.
        /// </summary>
        public string Field { get; set; } = String.Empty;

        /// <summary>
        /// Problem.
        /// </summary>
        public string Message { get; set; } = String.Empty;

        #endregion
    }
}
