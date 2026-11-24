namespace TrellisKit.Server.Services.Jobs
{
    using System;

    /// <summary>
    /// One item a job handler plans for a run.
    /// </summary>
    public class JobItemPlan
    {
        #region Public-Members

        /// <summary>
        /// Target, unique within the run (for example a map cell or a URL); at most 500 characters.
        /// </summary>
        public string TargetKey { get; set; } = String.Empty;

        /// <summary>
        /// Handler input (JSON), or null; at most 64 KB.
        /// </summary>
        public string? Payload { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public JobItemPlan()
        {
        }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="targetKey">Target key.</param>
        /// <param name="payload">Payload JSON, or null.</param>
        public JobItemPlan(string targetKey, string? payload = null)
        {
            TargetKey = targetKey ?? String.Empty;
            Payload = payload;
        }

        #endregion
    }
}
