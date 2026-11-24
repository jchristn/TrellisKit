namespace TrellisKit.Core.Requests
{
    using System;
    using System.Text.Json;

    /// <summary>
    /// One broadcast segment filter as sent and returned (docs/PHASE5_CONTRACT.md section 8.1): a field, an operator,
    /// and a value (a string, number, boolean, or array of strings).
    /// </summary>
    public class BroadcastFilterSpec
    {
        #region Public-Members

        /// <summary>
        /// Field: membership, membershipSource, store, profile.&lt;fieldKey&gt;, labels, tags.&lt;key&gt;, signedUpAfterUtc,
        /// signedUpBeforeUtc, activeWithinDays, inactiveForDays, hasPushToken, hasVerifiedEmail, role, eventRsvp, or
        /// seriesFollowers.
        /// </summary>
        public string? Field { get; set; } = null;

        /// <summary>
        /// Operator (per field; for example eq, in, any, all, exists, or a member directory operator for profile fields).
        /// </summary>
        public string? Operator { get; set; } = null;

        /// <summary>
        /// Value.
        /// </summary>
        public JsonElement? Value { get; set; } = null;

        #endregion
    }
}
