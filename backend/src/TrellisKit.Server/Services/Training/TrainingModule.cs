namespace TrellisKit.Server.Services.Training
{
    using System;

    /// <summary>
    /// The Training hub services of one property (docs/TRELLISKIT.md section 15): the overview with the engine's section
    /// providers, lesson and catalog links, and unified library search. Construct after the library, events, AI, ranks,
    /// and saves services.
    /// </summary>
    public sealed class TrainingModule
    {
        #region Public-Members

        /// <summary>
        /// The overview and its section providers.
        /// </summary>
        public TrainingHubService Hub { get; }

        /// <summary>
        /// Lesson and catalog links.
        /// </summary>
        public LessonCatalogLinkService Links { get; }

        /// <summary>
        /// Unified library search.
        /// </summary>
        public LibrarySearchService Search { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the module and register the engine's section providers.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public TrainingModule(ApplicationServices services)
        {
            ArgumentNullException.ThrowIfNull(services);
            Links = new LessonCatalogLinkService(services);
            Search = new LibrarySearchService(services);
            Hub = new TrainingHubService(services);
            Hub.Providers.Add(new LibraryTrainingProvider(services));
            Hub.Providers.Add(new SavesTrainingProvider(services));
            Hub.Providers.Add(new EventsTrainingProvider(services));
            Hub.Providers.Add(new RankTrainingProvider(services));
            Hub.Providers.Add(new AssistantTrainingProvider(services));
        }

        #endregion
    }
}
