namespace TrellisKit.Server.Services
{
    using System;
    using System.IO;

    /// <summary>
    /// An authorized export download: a local copy of the stored export file for the route to stream. Disposing deletes
    /// the local copy. Thread safety: not thread safe; owned by one request.
    /// </summary>
    public sealed class AccountExportDownload : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Export identifier.
        /// </summary>
        public string ExportId { get; }

        /// <summary>
        /// Absolute path of the local copy.
        /// </summary>
        public string FilePath { get; }

        #endregion

        #region Private-Members

        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the download.
        /// </summary>
        /// <param name="exportId">Export identifier.</param>
        /// <param name="filePath">Local copy (deleted on dispose).</param>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        public AccountExportDownload(string exportId, string filePath)
        {
            if (String.IsNullOrEmpty(exportId)) throw new ArgumentNullException(nameof(exportId));
            if (String.IsNullOrEmpty(filePath)) throw new ArgumentNullException(nameof(filePath));
            ExportId = exportId;
            FilePath = filePath;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Delete the local copy. Never throws.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            try
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
            }
            catch (IOException)
            {
                // A leftover copy stays in the exports work folder; it holds no state the engine relies on.
            }
            catch (UnauthorizedAccessException)
            {
                // Same as above.
            }
        }

        #endregion
    }
}
