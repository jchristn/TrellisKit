namespace TrellisKit.Server.Services.Training
{
    using System;

    /// <summary>
    /// The Training hub services of one property (docs/TRELLISKIT.md section 15): lesson and catalog links and unified library
    /// search. Construct after the library and catalog services.
    /// </summary>
    public sealed class TrainingModule
    {
        #region Public-Members

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
        /// Instantiate the module.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public TrainingModule(ApplicationServices services)
        {
            ArgumentNullException.ThrowIfNull(services);
            Links = new LessonCatalogLinkService(services);
            Search = new LibrarySearchService(services);
        }

        #endregion
    }
}
