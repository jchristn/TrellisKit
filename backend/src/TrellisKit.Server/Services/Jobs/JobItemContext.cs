namespace TrellisKit.Server.Services.Jobs
{
    using System;
    using TrellisKit.Core.Models;

    /// <summary>
    /// What a job handler sees while executing one item.
    /// </summary>
    public class JobItemContext : JobRunContext
    {
        #region Public-Members

        /// <summary>
        /// The item (Attempts already counts this attempt).
        /// </summary>
        public JobItem Item { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <param name="definition">Definition.</param>
        /// <param name="run">Run.</param>
        /// <param name="item">Item.</param>
        /// <param name="nowUtc">Now.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public JobItemContext(ApplicationServices services, JobDefinition definition, JobRun run, JobItem item, DateTime nowUtc) : base(services, definition, run, nowUtc)
        {
            Item = item ?? throw new ArgumentNullException(nameof(item));
        }

        #endregion
    }
}
