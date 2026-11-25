namespace TrellisKit.Server.Services.Training
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Requests;

    /// <summary>
    /// The Recently saved card (features.saves). It owns no section: the Saved segment of the Library is decided by
    /// <see cref="LibraryTrainingProvider"/>.
    /// </summary>
    public sealed class SavesTrainingProvider : ITrainingSectionProvider
    {
        #region Public-Members

        /// <inheritdoc />
        public string Name
        {
            get { return "saves"; }
        }

        /// <inheritdoc />
        public IReadOnlyList<TrainingSectionEnum> Owns
        {
            get { return Array.Empty<TrainingSectionEnum>(); }
        }

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate for one property.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public SavesTrainingProvider(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<TrainingContribution> ContributeAsync(TrainingContext context, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(context);
            TrainingContribution contribution = new TrainingContribution();
            if (!_Services.Property.Property.Features.Saves) return contribution;
            contribution.RecentSaves = await _Services.Favorites.ListAsync(context.Actor, new EnumerationQuery { MaxResults = _Services.Settings.Training.RecentSavesLimit }, token).ConfigureAwait(false);
            contribution.HasActivity = contribution.RecentSaves.Objects.Count > 0;
            return contribution;
        }

        #endregion
    }
}
