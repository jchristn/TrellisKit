namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// What a task role does when every executor has been tried.
    /// </summary>
    public enum AiExhaustionPolicyEnum
    {
        /// <summary>
        /// Return the failure to the caller.
        /// </summary>
        Fail,

        /// <summary>
        /// Return a retryable failure, so a job item is retried later with backoff.
        /// </summary>
        Defer,

        /// <summary>
        /// Walk the hierarchy again after a short delay, up to three rounds.
        /// </summary>
        Wait
    }
}
