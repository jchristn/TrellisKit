namespace TrellisKit.Server.Services.Training
{
    using System;

    /// <summary>
    /// The caller of one Training overview request, shared read-only by every section provider.
    /// </summary>
    public sealed class TrainingContext
    {
        #region Public-Members

        /// <summary>
        /// Caller (always a signed-in member: the overview is session-only).
        /// </summary>
        public ContentActor Actor { get; }

        /// <summary>
        /// The caller's member identifier.
        /// </summary>
        public string UserId { get; }

        /// <summary>
        /// UTC time of the request.
        /// </summary>
        public DateTime NowUtc { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="nowUtc">UTC time of the request.</param>
        /// <exception cref="ArgumentNullException">The actor is null or has no member identifier.</exception>
        public TrainingContext(ContentActor actor, DateTime nowUtc)
        {
            Actor = actor ?? throw new ArgumentNullException(nameof(actor));
            if (String.IsNullOrEmpty(actor.UserId)) throw new ArgumentNullException(nameof(actor), "The overview needs a signed-in member.");
            UserId = actor.UserId;
            NowUtc = nowUtc;
        }

        #endregion
    }
}
