namespace TrellisKit.Server.Services.Email
{
    using System;

    /// <summary>
    /// Turns a composed <see cref="EmailDocument"/> into the final HTML and plain-text parts in the property's brand
    /// (docs/TRELLISKIT.md section 12.2). The engine ships <see cref="StandardEmailLayout"/>; a template can return its own
    /// layout from <see cref="EmailTemplate.Layout"/>. A layout must encode every value it writes into HTML and should
    /// keep the brand footer (property name, support email, postal address when configured, privacy and email
    /// preference links).
    /// </summary>
    public abstract class EmailLayout
    {
        #region Public-Methods

        /// <summary>
        /// Render a document.
        /// </summary>
        /// <param name="document">Document (tokens already filled).</param>
        /// <param name="brand">Brand.</param>
        /// <returns>Subject, preheader, HTML, and text.</returns>
        public abstract EmailRendered Render(EmailDocument document, EmailBrand brand);

        #endregion
    }
}
