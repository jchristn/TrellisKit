namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.IO;
    using System.Security.Cryptography;
    using System.Threading;

    /// <summary>
    /// The throwaway directory of one harness call: an empty working directory (work), the harness's home (home), its
    /// temporary files (tmp), and the generated configuration (config), all under one root created with owner-only
    /// permissions and deleted on dispose. Nothing from the repository, the property, or the server's settings is
    /// placed in it. Thread safety: not thread safe; one per call.
    /// </summary>
    public sealed class HarnessSandbox : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Root directory of the call.
        /// </summary>
        public string Root { get; }

        /// <summary>
        /// Working directory of the harness (empty at start).
        /// </summary>
        public string Work { get; }

        /// <summary>
        /// HOME of the harness.
        /// </summary>
        public string Home { get; }

        /// <summary>
        /// TMPDIR of the harness.
        /// </summary>
        public string Temp { get; }

        /// <summary>
        /// Generated configuration (MCP configuration files).
        /// </summary>
        public string Config { get; }

        /// <summary>
        /// Whether dispose removed the root.
        /// </summary>
        public bool Removed { get; private set; } = false;

        #endregion

        #region Private-Members

        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Create the directories.
        /// </summary>
        /// <param name="parent">Parent directory (created when missing).</param>
        /// <param name="label">Short label in the directory name (for example the runtime).</param>
        /// <exception cref="ArgumentException">parent is empty.</exception>
        /// <exception cref="IOException">The directories cannot be created.</exception>
        public HarnessSandbox(string parent, string label)
        {
            if (String.IsNullOrWhiteSpace(parent)) throw new ArgumentException("A parent directory is required.", nameof(parent));
            CreatePrivate(parent);
            string name = (String.IsNullOrWhiteSpace(label) ? "harness" : label.ToLowerInvariant()) + "-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
            Root = Path.Combine(Path.GetFullPath(parent), name);
            Work = Path.Combine(Root, "work");
            Home = Path.Combine(Root, "home");
            Temp = Path.Combine(Root, "tmp");
            Config = Path.Combine(Root, "config");
            CreatePrivate(Root);
            CreatePrivate(Work);
            CreatePrivate(Home);
            CreatePrivate(Temp);
            CreatePrivate(Config);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Write a file readable by the owner only (parent directories are created).
        /// </summary>
        /// <param name="path">Absolute path under <see cref="Root"/>.</param>
        /// <param name="content">Content.</param>
        /// <exception cref="ArgumentException">The path is outside the root.</exception>
        public void WriteFile(string path, string content)
        {
            string full = Path.GetFullPath(path);
            if (!full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new ArgumentException("Harness files stay under the call's directory.", nameof(path));
            string? directory = Path.GetDirectoryName(full);
            if (directory != null) CreatePrivate(directory);
            File.WriteAllText(full, content ?? String.Empty);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(full, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        /// <summary>
        /// Delete the root and everything in it (retried briefly while a killed process releases its files).
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (Directory.Exists(Root)) Directory.Delete(Root, true);
                    Removed = !Directory.Exists(Root);
                    if (Removed) return;
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    // A process that was just killed may still hold a file.
                }
                Thread.Sleep(100 * (attempt + 1));
            }
        }

        #endregion

        #region Private-Methods

        private static void CreatePrivate(string path)
        {
            if (OperatingSystem.IsWindows()) Directory.CreateDirectory(path);
            else Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        #endregion
    }
}
