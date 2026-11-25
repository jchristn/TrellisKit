namespace TrellisKit.Server.Services.Training
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One contributor to the Training overview (docs/TRELLISKIT.md section 15). Providers run concurrently; each decides
    /// the sections it owns and may fill cards. A section several providers own is available when any of them reports it
    /// available. A provider that throws is logged and counted (trelliskit.training.provider.errors), and every section
    /// it owns is reported unavailable (NoData); it never fails the request.
    /// </summary>
    public interface ITrainingSectionProvider
    {
        /// <summary>
        /// Low-cardinality name for logs and the provider label of trelliskit.training.provider.errors.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Sections this provider decides.
        /// </summary>
        IReadOnlyList<TrainingSectionEnum> Owns { get; }

        /// <summary>
        /// Compute the provider's sections and cards for a caller.
        /// </summary>
        /// <param name="context">Caller.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Contribution.</returns>
        Task<TrainingContribution> ContributeAsync(TrainingContext context, CancellationToken token);
    }
}
