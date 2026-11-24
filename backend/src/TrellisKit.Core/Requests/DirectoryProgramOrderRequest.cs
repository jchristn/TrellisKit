namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Reorder program options.
    /// </summary>
    public class DirectoryProgramOrderRequest
    {
        #region Public-Members

        /// <summary>
        /// Every non-retired program exactly once.
        /// </summary>
        public List<string>? ProgramIds { get; set; } = null;

        #endregion
    }
}
