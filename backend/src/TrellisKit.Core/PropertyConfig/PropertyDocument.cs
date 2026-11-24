namespace TrellisKit.Core.PropertyConfig
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One document in property.json documents (docs/TRELLISKIT.md section 14): a Markdown file in the property folder
    /// that the engine versions, serves at a public slug, and, for gating documents, requires members to accept.
    /// </summary>
    public class PropertyDocument
    {
        #region Public-Members

        /// <summary>
        /// Stable key used in the API and in acceptance records (1 to 64 letters, digits, underscores, or hyphens; unique).
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Display title (1 to 120 characters).
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Markdown source file, relative to the property folder (for example legal/terms-of-service.md).
        /// </summary>
        public string File { get; set; } = String.Empty;

        /// <summary>
        /// Display version of the file (1 to 32 characters, for example 1.0). Changing it, or the file, publishes a new
        /// version in SingleProperty on the next start.
        /// </summary>
        public string Version { get; set; } = String.Empty;

        /// <summary>
        /// What the document is. Default Other.
        /// </summary>
        public DocumentKindEnum Kind { get; set; } = DocumentKindEnum.Other;

        /// <summary>
        /// How members accept it. Default InformationOnly.
        /// </summary>
        public DocumentAcceptanceModeEnum Acceptance { get; set; } = DocumentAcceptanceModeEnum.InformationOnly;

        /// <summary>
        /// Whether sign-up shows its own required checkbox (RequiredAtSignup only). Default false.
        /// </summary>
        public bool SeparateCheckbox { get; set; } = false;

        /// <summary>
        /// Whether signed-in members must accept a new version before continuing (RequiredAtSignup and
        /// AcceptedWithTerms only). Default false.
        /// </summary>
        public bool ReacceptOnChange { get; set; } = false;

        /// <summary>
        /// Public website path segment (lowercase letters, digits, and hyphens; unique), for example terms.
        /// </summary>
        public string Slug { get; set; } = String.Empty;

        /// <summary>
        /// Whether the website footer links it. Default true.
        /// </summary>
        public bool Footer { get; set; } = true;

        /// <summary>
        /// Optional short description for the page's meta description (at most 300 characters), or null.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Whether members must accept the document (RequiredAtSignup or AcceptedWithTerms).
        /// </summary>
        public bool IsGating
        {
            get { return Acceptance == DocumentAcceptanceModeEnum.RequiredAtSignup || Acceptance == DocumentAcceptanceModeEnum.AcceptedWithTerms; }
        }

        /// <summary>
        /// Whether the sign-up terms checkbox (acceptTerms) accepts the document: AcceptedWithTerms, and
        /// RequiredAtSignup without its own checkbox.
        /// </summary>
        public bool CoveredByTerms
        {
            get { return Acceptance == DocumentAcceptanceModeEnum.AcceptedWithTerms || (Acceptance == DocumentAcceptanceModeEnum.RequiredAtSignup && !SeparateCheckbox); }
        }

        #endregion
    }
}
