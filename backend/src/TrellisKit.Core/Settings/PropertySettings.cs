namespace TrellisKit.Core.Settings
{
    using System;

    /// <summary>
    /// Selects the active property (the configured personality the engine serves).
    /// </summary>
    public class PropertySettings
    {
        #region Public-Members

        /// <summary>
        /// Property directory containing property.json and brand/. Absolute, or relative to the working directory or
        /// any of its ancestors (for example the engine root). Default empty: use the engine's sample property
        /// (samples/example, found by searching upward), or /app/property in a container. Requires restart.
        /// </summary>
        public string Directory { get; set; } = String.Empty;

        /// <summary>
        /// Brand palette versions kept per property (docs/TRELLISKIT.md section 2.1); older versions are deleted after
        /// each change. Default 20. Minimum 2 (the current and the previous version), maximum 200. Applies live.
        /// </summary>
        public int MaxPaletteVersions
        {
            get { return _MaxPaletteVersions; }
            set { _MaxPaletteVersions = Math.Clamp(value, 2, 200); }
        }

        /// <summary>
        /// Most characters of a property document version's Markdown content published from the admin console
        /// (docs/TRELLISKIT.md section 14). Default 200000. Minimum 1000, maximum 1000000. Applies live. Seeded files
        /// longer than this are stored in full (the folder is trusted) and logged.
        /// </summary>
        public int MaxDocumentCharacters
        {
            get { return _MaxDocumentCharacters; }
            set { _MaxDocumentCharacters = Math.Clamp(value, 1000, 1000000); }
        }

        #endregion

        #region Private-Members

        private int _MaxPaletteVersions = 20;
        private int _MaxDocumentCharacters = 200000;

        #endregion
    }
}
