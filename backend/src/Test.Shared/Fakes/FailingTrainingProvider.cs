namespace Test.Shared.Fakes
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Server.Services.Training;

    /// <summary>
    /// A Training overview section provider that always throws, for the provider failure isolation case.
    /// </summary>
    internal sealed class FailingTrainingProvider : ITrainingSectionProvider
    {
        #region Internal-Members

        public string Name
        {
            get { return "failing"; }
        }

        public IReadOnlyList<TrainingSectionEnum> Owns
        {
            get { return _Owns; }
        }

        #endregion

        #region Private-Members

        private static readonly IReadOnlyList<TrainingSectionEnum> _Owns = new List<TrainingSectionEnum> { TrainingSectionEnum.Library };

        #endregion

        #region Internal-Methods

        public Task<TrainingContribution> ContributeAsync(TrainingContext context, CancellationToken token)
        {
            throw new InvalidOperationException("Simulated provider failure.");
        }

        #endregion
    }
}
