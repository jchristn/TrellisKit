namespace TrellisKit.Core.Database.Interfaces
{
    /// <summary>
    /// Data access of the library (series, lessons, progress, downloads, document text) (docs/PHASE4_CONTRACT.md section 14): a container of method sets, each bound to
    /// one property through the property-bound driver.
    /// </summary>
    public interface ILibraryMethods
    {
        /// <summary>
        /// Library configuration, levels, series, lessons, and their child lists.
        /// </summary>
        ILibraryCatalogMethods Catalog { get; }

        /// <summary>
        /// Progress, plays, follows, and library metrics.
        /// </summary>
        ILessonProgressMethods Progress { get; }

        /// <summary>
        /// Offline download licenses.
        /// </summary>
        ILessonDownloadMethods Downloads { get; }

        /// <summary>
        /// Extracted document text (captions and PDF resources).
        /// </summary>
        IDocumentTextMethods Documents { get; }
    }
}
