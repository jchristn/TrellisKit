namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// Body of POST /api/v1/auth/register.
    /// </summary>
    public class RegisterRequest
    {
        #region Public-Members

        /// <summary>
        /// Email address.
        /// </summary>
        public string? Email { get; set; } = null;

        /// <summary>
        /// Password, 10 to 128 characters, not equal to the email.
        /// </summary>
        public string? Password { get; set; } = null;

        /// <summary>
        /// Display name, 2 to 60 characters.
        /// </summary>
        public string? DisplayName { get; set; } = null;

        /// <summary>
        /// Date of birth as YYYY-MM-DD. Used only to check the member meets the property minimum age; never stored.
        /// </summary>
        public string? DateOfBirth { get; set; } = null;

        /// <summary>
        /// Must be true.
        /// </summary>
        public bool AcceptTerms { get; set; } = false;

        /// <summary>
        /// Documents accepted with their own checkbox (RequiredAtSignup documents with separateCheckbox), each with the
        /// version the member saw, or null. Every such document must be listed at its current version.
        /// </summary>
        public System.Collections.Generic.List<DocumentAcceptanceItem>? AcceptedDocuments { get; set; } = null;

        #endregion
    }
}
