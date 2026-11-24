namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using System;
    using TrellisKit.Core.Database.Interfaces;

    /// <summary>
    /// PostgreSQL data access of the library (series, lessons, progress, downloads, document text).
    /// </summary>
    internal sealed class LibraryMethods : ILibraryMethods
    {
        public ILibraryCatalogMethods Catalog { get; }

        public ILessonProgressMethods Progress { get; }

        public ILessonDownloadMethods Downloads { get; }

        public IDocumentTextMethods Documents { get; }

        internal LibraryMethods(DatabaseDriverBase driver, string? propertyId)
        {
            ArgumentNullException.ThrowIfNull(driver);
            Catalog = new LibraryCatalogMethods(driver, propertyId);
            Progress = new LessonProgressMethods(driver, propertyId);
            Downloads = new LessonDownloadMethods(driver, propertyId);
            Documents = new DocumentTextMethods(driver, propertyId);
        }
    }
}
