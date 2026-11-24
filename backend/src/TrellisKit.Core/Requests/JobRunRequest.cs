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

        #endregion
    }
}
