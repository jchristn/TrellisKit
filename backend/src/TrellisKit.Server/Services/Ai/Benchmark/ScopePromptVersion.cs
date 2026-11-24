namespace TrellisKit.Server.Services.Ai.Benchmark
{
    using System;

    /// <summary>
    /// The version of one catalog prompt a scope benchmark run measured.
    /// </summary>
    public class ScopePromptVersion
    {
        #region Public-Members

        /// <summary>
        /// Prompt catalog key.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Version number (0 for a property or engine default).
        /// </summary>
        public int Version { get; set; } = 0;

        /// <summary>
        /// Version identifier of a draft, or null.
        /// </summary>
        public string? VersionId { get; set; } = null;

        /// <summary>
        /// Draft (the candidate), Saved (the administrator's text in use), Property, or Engine.
        /// </summary>
        public string Source { get; set; } = "Engine";

        #endregion
    }
}
