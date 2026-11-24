namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A row of pushtokens: an Expo push token bound to the session that registered it.
    /// </summary>
    public class PushToken
    {
        /// <summary>
        /// Identifier (ptk_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Owner.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Registering session; revoking it deletes the token.
        /// </summary>
        public string SessionId { get; set; } = String.Empty;

        /// <summary>
        /// Expo push token value. Never returned by the API.
        /// </summary>
        public string Token { get; set; } = String.Empty;

        /// <summary>
        /// Platform.
        /// </summary>
        public PushPlatformEnum Platform { get; set; } = PushPlatformEnum.Ios;

        /// <summary>
        /// Client device identifier, or null.
        /// </summary>
        public string? DeviceId { get; set; } = null;

        /// <summary>
        /// Device name, or null.
        /// </summary>
        public string? DeviceName { get; set; } = null;

        /// <summary>
        /// App version, or null.
        /// </summary>
        public string? AppVersion { get; set; } = null;

        /// <summary>
        /// Consecutive delivery failures.
        /// </summary>
        public int FailureCount { get; set; } = 0;

        /// <summary>
        /// When the token was disabled after repeated failures, or null.
        /// </summary>
        public DateTime? DisabledUtc { get; set; } = null;

        /// <summary>
        /// Last successful receipt, or null.
        /// </summary>
        public DateTime? LastSuccessUtc { get; set; } = null;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last registration (UTC).
        /// </summary>
        public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
    }
}
