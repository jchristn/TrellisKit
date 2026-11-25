namespace TrellisKit.Server.Services.Training
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// GET /api/v1/me/training (docs/TRELLISKIT.md section 15): composes the overview from the registered
    /// <see cref="ITrainingSectionProvider"/> instances, run concurrently. A section several providers own is available
    /// when any of them reports it available. A provider that throws is logged, counted
    /// (trelliskit.training.provider.errors), and every section it owns is reported unavailable (NoData); the request
    /// still succeeds. Sections no provider decides are NoData. The overview is session-only.
    /// Thread safety: register providers at startup (tests may add or remove them); safe to share afterwards.
    /// </summary>
    public sealed class TrainingHubService
    {
        #region Public-Members

        /// <summary>
        /// Section order of every overview.
        /// </summary>
        public static readonly IReadOnlyList<TrainingSectionEnum> SectionOrder = new List<TrainingSectionEnum>
        {
            TrainingSectionEnum.Overview, TrainingSectionEnum.Library, TrainingSectionEnum.Schedule, TrainingSectionEnum.Programs, TrainingSectionEnum.Progress
        };

        /// <summary>
        /// Registered providers, in merge order.
        /// </summary>
        public List<ITrainingSectionProvider> Providers { get; } = new List<ITrainingSectionProvider>();

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
        public TrainingHubService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The caller's Training overview.
        /// </summary>
        /// <param name="actor">Caller (a signed-in member).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Overview.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">401 Unauthorized (signed out), 404 NotAvailable (features.training off or no section available), 429 RateLimited.</exception>
        public async Task<TrainingOverview> OverviewAsync(ContentActor actor, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            Stopwatch stopwatch = Stopwatch.StartNew();
            string outcome = "error";
            using Activity? span = TrainingTelemetry.StartOverview();
            try
            {
                if (!_Services.Property.Property.Features.Training)
                {
                    outcome = "not_available";
                    throw NotAvailable();
                }
                if (actor.IsAnonymous || String.IsNullOrEmpty(actor.UserId)) throw new ApiErrorException(401, "Unauthorized", "A valid bearer token is required.");
                _Services.Media.EnforceRead(actor);
                TrainingContext context = new TrainingContext(actor, DateTime.UtcNow);
                List<ITrainingSectionProvider> providers;
                lock (Providers) providers = Providers.ToList();
                TrainingContribution?[] contributions = await Task.WhenAll(providers.Select(p => RunAsync(p, context, token))).ConfigureAwait(false);
                TrainingOverview overview = Merge(providers, contributions);
                bool anyFailed = contributions.Any(c => c == null);
                // A transient provider failure must not hide the hub: 404 only when nothing failed and nothing is available.
                if (!anyFailed && !overview.Sections.Any(s => s.Available))
                {
                    outcome = "not_available";
                    throw NotAvailable();
                }
                outcome = anyFailed ? "partial" : "success";
                span?.SetTag("training.sections", overview.Sections.Count(s => s.Available));
                return overview;
            }
            finally
            {
                span?.SetTag("outcome", outcome);
                TrainingTelemetry.Count(TrainingTelemetry.OverviewRequests, "outcome", outcome);
                TrellisKitTelemetry.Record(TrainingTelemetry.OverviewDuration, stopwatch.Elapsed.TotalSeconds, "outcome", outcome);
            }
        }

        #endregion

        #region Private-Methods

        private async Task<TrainingContribution?> RunAsync(ITrainingSectionProvider provider, TrainingContext context, CancellationToken token)
        {
            try
            {
                return await provider.ContributeAsync(context, token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _Services.Logger.Warn("[TrainingHubService] provider " + provider.Name + " failed: " + e.GetType().Name + ": " + e.Message);
                TrainingTelemetry.Count(TrainingTelemetry.ProviderErrors, "provider", provider.Name);
                return null;
            }
        }

        private static TrainingOverview Merge(List<ITrainingSectionProvider> providers, TrainingContribution?[] contributions)
        {
            TrainingOverview overview = new TrainingOverview();
            Dictionary<TrainingSectionEnum, TrainingSectionView> sections = new Dictionary<TrainingSectionEnum, TrainingSectionView>();
            HashSet<TrainingSectionEnum> failed = new HashSet<TrainingSectionEnum>();
            bool activity = false;
            for (int i = 0; i < providers.Count; i++)
            {
                TrainingContribution? contribution = contributions[i];
                if (contribution == null)
                {
                    // A failure wins over another provider's answer for the same section: the section's data is incomplete.
                    failed.UnionWith(providers[i].Owns);
                    continue;
                }
                foreach (TrainingSectionView section in contribution.Sections.Where(s => providers[i].Owns.Contains(s.Key)))
                {
                    if (sections.TryGetValue(section.Key, out TrainingSectionView? existing) && Rank(existing) >= Rank(section))
                    {
                        if (existing.Available && section.Available) existing.Segments = existing.Segments.Union(section.Segments, StringComparer.Ordinal).Take(TrainingSectionView.MaxSegments).ToList();
                        continue;
                    }
                    sections[section.Key] = section;
                }
                if (contribution.UpNext != null) overview.UpNext = contribution.UpNext;
                if (contribution.Continue != null) overview.Continue = contribution.Continue;
                if (contribution.RecentSaves != null) overview.RecentSaves = contribution.RecentSaves;
                if (contribution.NewForYou != null) overview.NewForYou = contribution.NewForYou;
                if (contribution.Rank != null) overview.Rank = contribution.Rank;
                if (contribution.Coach != null) overview.Coach = contribution.Coach;
                activity |= contribution.HasActivity;
            }
            foreach (TrainingSectionEnum key in failed) sections[key] = new TrainingSectionView { Key = key, Available = false, Reason = TrainingSectionReasonEnum.NoData };
            foreach (TrainingSectionEnum key in SectionOrder.Where(k => k != TrainingSectionEnum.Overview))
            {
                TrainingSectionView view = sections.TryGetValue(key, out TrainingSectionView? decided) ? decided
                    : new TrainingSectionView { Key = key, Available = false, Reason = TrainingSectionReasonEnum.NoData };
                if (view.Available) view.Reason = null;
                else
                {
                    view.Reason ??= TrainingSectionReasonEnum.NoData;
                    view.Segments = new List<string>();
                }
                sections[key] = view;
            }
            bool anyContent = sections.Values.Any(s => s.Available);
            sections[TrainingSectionEnum.Overview] = new TrainingSectionView { Key = TrainingSectionEnum.Overview, Available = anyContent, Reason = anyContent ? null : TrainingSectionReasonEnum.NoData };
            overview.Sections = SectionOrder.Select(k => sections[k]).ToList();
            // firstRun only when every provider answered: an unknown activity never shows the first-run card.
            overview.FirstRun = !activity && contributions.All(c => c != null);
            return overview;
        }

        private static int Rank(TrainingSectionView view)
        {
            if (view.Available) return 4;
            switch (view.Reason)
            {
                case TrainingSectionReasonEnum.NoData: return 3;
                case TrainingSectionReasonEnum.NoAccess: return 2;
                case TrainingSectionReasonEnum.SignedOut: return 1;
                default: return 0;
            }
        }

        private static ApiErrorException NotAvailable()
        {
            return new ApiErrorException(404, "NotAvailable", "Training is not available for this account.");
        }

        #endregion
    }
}
