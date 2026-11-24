namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// The answer of a directory import (docs/PHASE4_CONTRACT.md section 3.9).
    /// </summary>
    public class DirectoryImportResponse
    {
        #region Public-Members

        /// <summary>
        /// Whether nothing was written.
        /// </summary>
        public bool DryRun { get; set; } = true;

        /// <summary>
        /// Venues created (or that would be).
        /// </summary>
        public int Created { get; set; } = 0;

        /// <summary>
        /// Venues updated (or that would be).
        /// </summary>
        public int Updated { get; set; } = 0;

        /// <summary>
        /// Rows skipped.
        /// </summary>
        public int Skipped { get; set; } = 0;

        /// <summary>
        /// Problems (at most 100 listed).
        /// </summary>
        public BoundedPreview<DirectoryImportError> Errors { get; set; } = new BoundedPreview<DirectoryImportError>();

        #endregion
    }
}
