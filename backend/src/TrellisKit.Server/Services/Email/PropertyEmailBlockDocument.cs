namespace TrellisKit.Server.Services.Email
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One block of a property email template file. Types: body (the editable body paragraphs), button (the template's
    /// button, or label plus urlToken), paragraph, subheading, callout (tone Info, Success, Warning, Danger), notice (small text with an optional label plus urlToken link), list (items,
    /// ordered), fields (label and value rows), code (text), markdown (token), divider. Text, labels, values, and items
    /// take {token} placeholders.
    /// </summary>
    public class PropertyEmailBlockDocument
    {
        #region Public-Members

        /// <summary>
        /// Block type.
        /// </summary>
        public string Type { get; set; } = String.Empty;

        /// <summary>
        /// Text (paragraph, subheading, callout, code).
        /// </summary>
        public string? Text { get; set; } = null;

        /// <summary>
        /// Callout tone.
        /// </summary>
        public string? Tone { get; set; } = null;

        /// <summary>
        /// List items.
        /// </summary>
        public List<string>? Items { get; set; } = null;

        /// <summary>
        /// Numbered list.
        /// </summary>
        public bool Ordered { get; set; } = false;

        /// <summary>
        /// Key/value rows.
        /// </summary>
        public List<EmailFieldDocument>? Fields { get; set; } = null;

        /// <summary>
        /// Button or notice link label.
        /// </summary>
        public string? Label { get; set; } = null;

        /// <summary>
        /// Token whose value is the button or notice link URL.
        /// </summary>
        public string? UrlToken { get; set; } = null;

        /// <summary>
        /// Token whose value is Markdown (markdown block).
        /// </summary>
        public string? Token { get; set; } = null;

        #endregion
    }
}
