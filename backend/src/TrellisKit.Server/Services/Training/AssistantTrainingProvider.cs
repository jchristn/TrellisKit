namespace TrellisKit.Server.Services.Training
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Services.Ai;

    /// <summary>
    /// The Ask your coach card: available when AI is available for the property (the same rule as features.ai: the
    /// property AI switch and ai.enabled) and at least one assistant is usable by the member. Placement is
    /// features.aiPlacement (ai.mobilePlacement). It owns no section.
    /// </summary>
    public sealed class AssistantTrainingProvider : ITrainingSectionProvider
    {
        #region Public-Members

        /// <inheritdoc />
        public string Name
        {
            get { return "coach"; }
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
        public AssistantTrainingProvider(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<TrainingContribution> ContributeAsync(TrainingContext context, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(context);
            TrainingCoachCard card = new TrainingCoachCard { Placement = _Services.Settings.Ai.MobilePlacement.ToString() };
            TrainingContribution contribution = new TrainingContribution { Coach = card };
            if (!_Services.Ai.IsAvailable) return contribution;
            AssistantCaller caller = new AssistantCaller
            {
                UserId = context.UserId,
                Actor = context.Actor,
                DisplayName = context.Actor.User?.DisplayName ?? String.Empty,
                IsSystemAdmin = context.Actor.IsSystemAdmin,
                HasMembership = context.Actor.HasMembership,
                SourceIp = context.Actor.SourceIp
            };
            EnumerationResult<AssistantSummaryView> page = await _Services.Ai.Assistants.EnumerateForCallerAsync(caller, new EnumerationQuery { MaxResults = 1 }, token).ConfigureAwait(false);
            card.Available = page.Objects.Count > 0;
            return contribution;
        }

        #endregion
    }
}
