namespace TrellisKit.Server.Services.Training
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// The Library section (segments Courses when features.library is on, Techniques when features.catalog is on and at
    /// least one entry is published, Saved when features.saves is on), the Continue training and New for you cards, and
    /// the lesson-progress part of the Progress section (available when the member has any lesson progress).
    /// </summary>
    public sealed class LibraryTrainingProvider : ITrainingSectionProvider
    {
        #region Public-Members

        /// <summary>
        /// Courses segment.
        /// </summary>
        public const string Courses = "Courses";

        /// <summary>
        /// Techniques segment.
        /// </summary>
        public const string Techniques = "Techniques";

        /// <summary>
        /// Saved segment.
        /// </summary>
        public const string Saved = "Saved";

        /// <inheritdoc />
        public string Name
        {
            get { return "library"; }
        }

        /// <inheritdoc />
        public IReadOnlyList<TrainingSectionEnum> Owns
        {
            get { return _Owns; }
        }

        #endregion

        #region Private-Members

        private static readonly IReadOnlyList<TrainingSectionEnum> _Owns = new List<TrainingSectionEnum> { TrainingSectionEnum.Library, TrainingSectionEnum.Progress };
        private readonly ApplicationServices _Services;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate for one property.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public LibraryTrainingProvider(ApplicationServices services)
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
            bool library = _Services.Property.Property.Features.Library;
            TrainingSectionView section = new TrainingSectionView { Key = TrainingSectionEnum.Library };
            if (library) section.Segments.Add(Courses);
            if (_Services.Property.Property.Features.Catalog && await AnyPublishedEntryAsync(token).ConfigureAwait(false)) section.Segments.Add(Techniques);
            if (_Services.Property.Property.Features.Saves) section.Segments.Add(Saved);
            section.Available = section.Segments.Count > 0;
            section.Reason = section.Available ? null : TrainingSectionReasonEnum.FeatureOff;
            contribution.Sections.Add(section);
            if (!library)
            {
                contribution.Sections.Add(new TrainingSectionView { Key = TrainingSectionEnum.Progress, Reason = TrainingSectionReasonEnum.FeatureOff });
                return contribution;
            }
            contribution.Continue = await _Services.Library.Progress.ContinueAsync(context.Actor, new EnumerationQuery { MaxResults = _Services.Settings.Training.ContinueLimit }, token).ConfigureAwait(false);
            contribution.NewForYou = await NewForYouAsync(context, token).ConfigureAwait(false);
            bool progress = contribution.Continue.Objects.Count > 0;
            if (!progress)
            {
                EnumerationResult<LessonProgressView> mine = await _Services.Library.Progress.MineAsync(context.Actor, new EnumerationQuery { MaxResults = 1 }, token).ConfigureAwait(false);
                progress = mine.Objects.Count > 0;
            }
            contribution.HasActivity = progress;
            contribution.Sections.Add(new TrainingSectionView { Key = TrainingSectionEnum.Progress, Available = progress, Reason = progress ? null : TrainingSectionReasonEnum.NoData });
            return contribution;
        }

        #endregion

        #region Private-Methods

        private async Task<bool> AnyPublishedEntryAsync(CancellationToken token)
        {
            EnumerationRequest request = _Services.Enumeration.Prepare(new EnumerationQuery { MaxResults = 1 }, ExerciseCatalogCollections.Entries, false, "training-techniques", 1);
            request.CountTotals = false;
            EnumerationResult<CatalogEntry> page = await _Services.Database.ExerciseCatalog.EnumerateAsync(request, true, token).ConfigureAwait(false);
            return page.Objects.Count > 0;
        }

        private async Task<EnumerationResult<LessonSummaryView>> NewForYouAsync(TrainingContext context, CancellationToken token)
        {
            int limit = _Services.Settings.Training.NewForYouLimit;
            LessonFilter filter = new LessonFilter
            {
                Audiences = LibraryAccess.Readable(context.Actor),
                StubAudiences = new List<ContentAudienceEnum>(),
                FollowedByUserId = context.UserId,
                PublishedAfterUtc = context.NowUtc.AddDays(-_Services.Settings.Training.NewForYouDays),
                Progress = LessonProgressStateEnum.NotStarted,
                ProgressUserId = context.UserId
            };
            EnumerationRequest request = _Services.Enumeration.Prepare(new EnumerationQuery { MaxResults = limit }, LibraryCollections.Lessons, false, "training-new|" + context.UserId, limit);
            EnumerationResult<Lesson> page = await _Services.Database.Library.Catalog.EnumerateLessonsAsync(request, filter, token).ConfigureAwait(false);
            return page.With(await _Services.Library.Views.LessonSummariesAsync(page.Objects, context.Actor, false, token).ConfigureAwait(false));
        }

        #endregion
    }
}
