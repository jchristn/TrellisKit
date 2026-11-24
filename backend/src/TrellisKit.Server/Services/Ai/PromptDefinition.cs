namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One entry of the prompt catalog: an administrator-editable prompt with its engine default and validation rules.
    /// </summary>
    public class PromptDefinition
    {
        #region Public-Members

        /// <summary>
        /// Stable key (for example platform.scopeGuardrail). A property may supply its own default in
        /// ai/prompts/&lt;key&gt;.md.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Title shown to administrators.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// What the prompt does and where it is used.
        /// </summary>
        public string Description { get; set; } = String.Empty;

        /// <summary>
        /// The engine default text (property-neutral).
        /// </summary>
        public string DefaultContent { get; set; } = String.Empty;

        /// <summary>
        /// Minimum length in characters. Default 10.
        /// </summary>
        public int MinLength { get; set; } = 10;

        /// <summary>
        /// Maximum length in characters. Default 4000.
        /// </summary>
        public int MaxLength { get; set; } = 4000;

        /// <summary>
        /// Placeholders the prompt may use (replaced when the prompt is rendered).
        /// </summary>
        public List<string> Variables { get; set; } = new List<string>();

        /// <summary>
        /// Words the prompt must contain (case-insensitive), for prompts whose output is parsed.
        /// </summary>
        public List<string> RequiredWords { get; set; } = new List<string>();

        #endregion
    }
}
