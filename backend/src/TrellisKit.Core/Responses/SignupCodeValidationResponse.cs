namespace TrellisKit.Core.Responses
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// POST /api/v1/signup-codes/validate. Invalid codes return only valid and message.
    /// </summary>
    public class SignupCodeValidationResponse
    {
        #region Public-Members

        /// <summary>
        /// Whether the code can be redeemed by this member.
        /// </summary>
        public bool Valid { get; set; } = false;

        /// <summary>
        /// Campaign name, when valid.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Name { get; set; } = null;

        /// <summary>
        /// Description, when valid and set.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Description { get; set; } = null;

        /// <summary>
        /// Grant in words, when valid.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? GrantSummary { get; set; } = null;

        /// <summary>
        /// When the free membership would end; null for lifetime (and when invalid).
        /// </summary>
        public DateTime? MembershipEndsUtc { get; set; } = null;

        /// <summary>
        /// Message, when invalid.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Message { get; set; } = null;

        /// <summary>
        /// Note shown when a paid membership is active and the free one starts after it.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Note { get; set; } = null;

        /// <summary>
        /// What a valid code gives: FreeMembership (redeem it) or Discount (send it with web checkout). Null when invalid.
        /// </summary>
        public string? Kind { get; set; } = null;

        /// <summary>
        /// For a Discount code, the discount in words, for example "40% off the first year". Null otherwise.
        /// </summary>
        public string? DiscountSummary { get; set; } = null;

        #endregion
    }
}
