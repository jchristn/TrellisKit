namespace TrellisKit.Core.Requests
{
    /// <summary>
    /// Start a run now.
    /// </summary>
    public class JobRunRequest
    {
        #region Public-Members

        /// <summary>
        /// True for a dry run (the handler writes no lasting changes). Default false.
        /// </summary>
        public bool DryRun { get; set; } = false;

        /// <summary>
        /// Optional handler parameters encoded as a JSON object string, at most 16384 characters, validated by handlers
        /// that implement parameter validation (for example catalog.importSources and catalog.generateDrafts). Default null.
        /// </summary>
        public string? Parameters { get; set; } = null;

        #endregion
    }
}
