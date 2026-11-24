namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// An administrator-editable prompt with its current text and default.
    /// </summary>
    public class AiPromptView
    {
        #region Public-Members

        /// <summary>Immutable catalog creation anchor used by enumeration date filters.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UnixEpoch;


        /// <summary>
        /// Catalog key.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Title.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// What the prompt does.
        /// </summary>
        public string Description { get; set; } = String.Empty;

        /// <summary>
        /// Current text (placeholders not substituted).
        /// </summary>
        public string Content { get; set; } = String.Empty;

        /// <summary>
        /// Default text (placeholders not substituted).
        /// </summary>
        public string DefaultContent { get; set; } = String.Empty;

        /// <summary>
        /// Where the default comes from: Property (the property's ai/prompts file) or Engine.
        /// </summary>
        public string DefaultSource { get; set; } = "Engine";

        /// <summary>
        /// True when no administrator text is saved.
        /// </summary>
        public bool IsDefault { get; set; } = true;

        /// <summary>
        /// Current (published) version (0 while the default is in use).
        /// </summary>
        public int Version { get; set; } = 0;

        /// <summary>
        /// The pending draft (saved, not yet published), or null.
        /// </summary>
        public AiPromptVersionView? Draft { get; set; } = null;

        /// <summary>
        /// The newest ai.scopeBenchmark run of the pending draft (its summary says whether it clears the gate), or null.
        /// </summary>
        public JobRunView? DraftBenchmark { get; set; } = null;

        /// <summary>
        /// Whether publishing needs a passing live scope benchmark of the draft (setting ai.promptPublishRequiresBenchmark).
        /// </summary>
        public bool RequiresBenchmark { get; set; } = true;

        /// <summary>
        /// Minimum length.
        /// </summary>
        public int MinLength { get; set; } = 0;

        /// <summary>
        /// Maximum length.
        /// </summary>
        public int MaxLength { get; set; } = 0;

        /// <summary>
        /// Placeholders the prompt may use.
        /// </summary>
        public List<string> Variables { get; set; } = new List<string>();

        /// <summary>
        /// Words the prompt must contain.
        /// </summary>
        public List<string> RequiredWords { get; set; } = new List<string>();

        /// <summary>
        /// The engine's fixed rules that precede this prompt (the scope guardrail only), or null.
        /// </summary>
        public string? LockedPrefix { get; set; } = null;

        /// <summary>
        /// Administrator who saved the current text, or null.
        /// </summary>
        public string? UpdatedByUserId { get; set; } = null;

        /// <summary>
        /// UTC time the current text was saved, or null for the default.
        /// </summary>
        public DateTime? UpdatedUtc { get; set; } = null;

        /// <summary>Catalog labels. Empty when none are declared.</summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>Catalog tags. Empty when none are declared.</summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        #endregion
    }
}
