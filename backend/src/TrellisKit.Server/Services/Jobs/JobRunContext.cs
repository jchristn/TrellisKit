namespace TrellisKit.Server.Services.Jobs
{
    using System;
    using TrellisKit.Core.Models;

    /// <summary>
    /// What a job handler sees while planning a run.
    /// </summary>
    public class JobRunContext
    {
        #region Public-Members

        /// <summary>
        /// Services of the property the job runs for.
        /// </summary>
        public ApplicationServices Services { get; }

        /// <summary>
        /// The job's definition.
        /// </summary>
        public JobDefinition Definition { get; }

        /// <summary>
        /// The run.
        /// </summary>
        public JobRun Run { get; }

        /// <summary>
        /// True for a dry run.
        /// </summary>
        public bool IsDryRun
        {
            get { return Run.IsDryRun; }
        }

        /// <summary>
        /// UTC time of the pass.
        /// </summary>
        public DateTime NowUtc { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <param name="definition">Definition.</param>
        /// <param name="run">Run.</param>
        /// <param name="nowUtc">Now.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public JobRunContext(ApplicationServices services, JobDefinition definition, JobRun run, DateTime nowUtc)
        {
            Services = services ?? throw new ArgumentNullException(nameof(services));
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            Run = run ?? throw new ArgumentNullException(nameof(run));
            NowUtc = nowUtc;
        }

        #endregion
    }
}
