namespace TrellisKit.Server.Services.Jobs
{
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Optional for a job handler: summarize a run when its last item finishes. The summary (JSON, at most 65,536
    /// characters) is stored on the run and returned as JobRunView.summary. A summarizer that throws leaves the run
    /// without a summary and records the error on the run; the run's status is not changed.
    /// </summary>
    public interface IJobRunSummarizer
    {
        /// <summary>
        /// Summarize a finished run (every item is final).
        /// </summary>
        /// <param name="context">Run context.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Summary JSON, or null for none.</returns>
        Task<string?> SummarizeAsync(JobRunContext context, CancellationToken token);
    }
}
