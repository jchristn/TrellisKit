namespace TrellisKit.Server.Services.Email
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One email template file in a property's email/templates folder (JSON, file name = key + ".json"). A file whose key
    /// is an engine or registered template replaces that template's default text, button, and (optionally) blocks while
    /// keeping its category, recipient, and tokens; any other key adds a new template and must give category, recipient,
    /// subject, and heading. Null fields keep the replaced template's value.
    /// </summary>
    public class PropertyEmailTemplateDocument
    {
        #region Public-Members

        /// <summary>
        /// Key (optional; defaults to the file name and must match it).
        /// </summary>
        public string? Key { get; set; } = null;

        /// <summary>
        /// Category of a new template: Essential, Optional, or Staff.
        /// </summary>
        public string? Category { get; set; } = null;

        /// <summary>
        /// Recipient of a new template: Member, Staff, or Moderators.
        /// </summary>
        public string? Recipient { get; set; } = null;

        /// <summary>
        /// What the message is for (admin portal).
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Default subject ({token} placeholders).
        /// </summary>
        public string? Subject { get; set; } = null;

        /// <summary>
        /// Default preheader.
        /// </summary>
        public string? Preheader { get; set; } = null;

        /// <summary>
        /// Default heading.
        /// </summary>
        public string? Heading { get; set; } = null;

        /// <summary>
        /// Default body: paragraphs separated by blank lines. Administrators can still override it in the admin portal.
        /// </summary>
        public string? Body { get; set; } = null;

        /// <summary>
        /// Button label (empty string removes the replaced template's button).
        /// </summary>
        public string? ButtonLabel { get; set; } = null;

        /// <summary>
        /// Token whose value is the button URL (default link).
        /// </summary>
        public string? ButtonToken { get; set; } = null;

        /// <summary>
        /// Tokens whose values are Markdown.
        /// </summary>
        public List<string>? MarkdownTokens { get; set; } = null;

        /// <summary>
        /// Extra tokens with sample values (previews); a new template's tokens are the common tokens plus these.
        /// </summary>
        public Dictionary<string, string>? Samples { get; set; } = null;

        /// <summary>
        /// Body layout in order. Null keeps the replaced template's composition (or body then button for a new one). Use a
        /// block of type body where the editable body paragraphs go.
        /// </summary>
        public List<PropertyEmailBlockDocument>? Blocks { get; set; } = null;

        #endregion
    }
}
