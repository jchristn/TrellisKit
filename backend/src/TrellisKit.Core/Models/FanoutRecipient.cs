namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// One eligible member of a fan-out batch with the channel preferences of the fan-out category.
    /// </summary>
    public class FanoutRecipient
    {
        /// <summary>
        /// Member identifier.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Email address, or null.
        /// </summary>
        public string? Email { get; set; } = null;

        /// <summary>
        /// Whether the email address is verified.
        /// </summary>
        public bool EmailVerified { get; set; } = false;

        /// <summary>
        /// Display name.
        /// </summary>
        public string DisplayName { get; set; } = String.Empty;

        /// <summary>
        /// In-app preference of the category (the default when the member has no row).
        /// </summary>
        public bool InApp { get; set; } = true;

        /// <summary>
        /// Email preference of the category (the default when the member has no row).
        /// </summary>
        public bool EmailOn { get; set; } = true;
    }
}
