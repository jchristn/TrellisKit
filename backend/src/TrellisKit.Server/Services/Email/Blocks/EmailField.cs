namespace TrellisKit.Server.Services.Email.Blocks
{
    using System;

    /// <summary>
    /// One row of an <see cref="EmailFieldsBlock"/>.
    /// </summary>
    public class EmailField
    {
        #region Public-Members

        /// <summary>
        /// Label (left column).
        /// </summary>
        public string Label { get; set; } = String.Empty;

        /// <summary>
        /// Value (right column; line breaks kept).
        /// </summary>
        public string Value { get; set; } = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public EmailField()
        {
        }

        /// <summary>
        /// Instantiate with values.
        /// </summary>
        /// <param name="label">Label.</param>
        /// <param name="value">Value.</param>
        public EmailField(string? label, string? value)
        {
            Label = label ?? String.Empty;
            Value = value ?? String.Empty;
        }

        #endregion
    }
}
