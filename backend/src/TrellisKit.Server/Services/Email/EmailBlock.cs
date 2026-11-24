namespace TrellisKit.Server.Services.Email
{
    using System;

    /// <summary>
    /// One body block of an email (docs/TRELLISKIT.md section 12.2): a paragraph, a key/value table, a code block, a
    /// callout, and so on. A block renders itself twice, as inline-styled HTML for the HTML part and as plain text for the
    /// text part, so the two parts always say the same thing. Implementations must encode every value they write into
    /// HTML (use <see cref="EmailStyle.Encode"/>); a block never emits caller-supplied markup.
    /// Subclass this to add a block of your own and add it with <see cref="EmailComposer.Add"/>.
    /// </summary>
    public abstract class EmailBlock
    {
        #region Public-Methods

        /// <summary>
        /// Render the block as inline-styled HTML.
        /// </summary>
        /// <param name="style">Colors and base styles of the layout.</param>
        /// <returns>HTML fragment (empty to render nothing).</returns>
        public abstract string RenderHtml(EmailStyle style);

        /// <summary>
        /// Render the block as plain text.
        /// </summary>
        /// <returns>Text (empty to render nothing).</returns>
        public abstract string RenderText();

        #endregion
    }
}
