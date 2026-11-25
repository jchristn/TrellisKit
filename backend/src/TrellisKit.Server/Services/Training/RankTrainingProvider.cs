namespace TrellisKit.Server.Services.Training
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Verified ranks, curricula, and attendance on the Training overview (active only while ranks.enabled is on): the
    /// Your rank card (from the same rules as GET /api/v1/me/rank, only fields where the member holds a rank), the Programs
    /// section (a published curriculum the member can see), the rank and attendance part of the Progress section (a rank,
    /// a recorded promotion, or a check-in), and the attendance part of the Schedule section (a check-in).
    /// </summary>
    public sealed class RankTrainingProvider : ITrainingSectionProvider
    {
        #region Public-Members

        /// <inheritdoc />
        public string Name
        {
            get { return "rank"; }
        }

        /// <inheritdoc />
        public IReadOnlyList<TrainingSectionEnum> Owns
        {
            get { return _Owns; }
        }

        #endregion

        #region Private-Members

        private static readonly IReadOnlyList<TrainingSectionEnum> _Owns = new List<TrainingSectionEnum> { TrainingSectionEnum.Schedule, TrainingSectionEnum.Programs, TrainingSectionEnum.Progress };
        private readonly ApplicationServices _Services;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate for one property.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public RankTrainingProvider(ApplicationServices services)
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
            if (!_Services.Ranks.Enabled)
            {
                foreach (TrainingSectionEnum key in _Owns) contribution.Sections.Add(new TrainingSectionView { Key = key, Reason = TrainingSectionReasonEnum.FeatureOff });
                return contribution;
            }
            MyRankResponse mine = await _Services.Ranks.Ranks.MineAsync(context.Actor, token).ConfigureAwait(false);
            MyRankFieldView? first = mine.Fields.FirstOrDefault();
            List<MyRankFieldView> held = mine.Fields.Where(f => !String.IsNullOrEmpty(f.Rank.OptionKey)).ToList();
            if (first != null)
            {
                contribution.Rank = new TrainingRankCard
                {
                    Rank = first.Rank,
                    Next = first.Next,
                    Curriculum = await CurriculumAsync(context, first, token).ConfigureAwait(false),
                    ShowVerificationLabels = mine.ShowVerificationLabels
                };
            }
            long attendance = await _Services.Database.Attendance.CountForUserAsync(context.UserId, null, token).ConfigureAwait(false);
            EnumerationResult<CurriculumView> curricula = await _Services.Ranks.Curricula.ListAsync(context.Actor, new EnumerationQuery { MaxResults = 1 }, token).ConfigureAwait(false);
            bool progress = held.Count > 0 || mine.HistoryTotal > 0 || attendance > 0;
            contribution.Sections.Add(Section(TrainingSectionEnum.Schedule, attendance > 0));
            contribution.Sections.Add(Section(TrainingSectionEnum.Programs, curricula.Objects.Count > 0));
            contribution.Sections.Add(Section(TrainingSectionEnum.Progress, progress));
            return contribution;
        }

        #endregion

        #region Private-Methods

        private async Task<CurriculumView?> CurriculumAsync(TrainingContext context, MyRankFieldView field, CancellationToken token)
        {
            string? id = field.Next?.Requirement?.CurriculumId;
            if (!String.IsNullOrEmpty(id))
            {
                try
                {
                    return await _Services.Ranks.Curricula.GetAsync(context.Actor, id, token).ConfigureAwait(false);
                }
                catch (ApiErrorException)
                {
                    // A requirement may name a curriculum that is unpublished or hidden from the member: fall back.
                }
            }
            EnumerationQuery query = new EnumerationQuery { MaxResults = 1 };
            query.SetFilter("fieldKey", field.Rank.FieldKey);
            EnumerationResult<CurriculumView> page = await _Services.Ranks.Curricula.ListAsync(context.Actor, query, token).ConfigureAwait(false);
            return page.Objects.FirstOrDefault();
        }

        private static TrainingSectionView Section(TrainingSectionEnum key, bool available)
        {
            return new TrainingSectionView { Key = key, Available = available, Reason = available ? null : TrainingSectionReasonEnum.NoData };
        }

        #endregion
    }
}
