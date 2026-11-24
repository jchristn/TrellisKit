namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// Viewer capabilities of a series (docs/PHASE4_CONTRACT.md section 1.3).
    /// </summary>
    public class SeriesViewer
    {
        #region Public-Members

        /// <summary>
        /// May follow the series.
        /// </summary>
        public bool CanFollow { get; set; } = false;

        /// <summary>
        /// May edit it (library staff).
        /// </summary>
        public bool CanEdit { get; set; } = false;

        /// <summary>
        /// May open it (false on locked stubs).
        /// </summary>
        public bool CanOpen { get; set; } = true;

        /// <summary>
        /// SignIn, Membership, or null.
        /// </summary>
        public string? Requires { get; set; } = null;

        #endregion
    }
}
