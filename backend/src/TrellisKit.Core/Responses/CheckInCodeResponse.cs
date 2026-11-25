namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// GET /api/v1/attendance/check-in-code: the target's current rotating code.
    /// </summary>
    public class CheckInCodeResponse
    {
        #region Public-Members

        /// <summary>
        /// Event or VenueSession.
        /// </summary>
        public string TargetType { get; set; } = String.Empty;

        /// <summary>
        /// Target identifier.
        /// </summary>
        public string TargetId { get; set; } = String.Empty;

        /// <summary>
        /// Occurrence date (yyyy-MM-dd).
        /// </summary>
        public string OccurrenceDate { get; set; } = String.Empty;

        /// <summary>
        /// Six-digit code.
        /// </summary>
        public string Code { get; set; } = String.Empty;

        /// <summary>
        /// When the code rotates (the previous code is still accepted for one more period).
        /// </summary>
        public DateTime ExpiresUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// attendance.codeRotationSeconds.
        /// </summary>
        public int RotationSeconds { get; set; } = 60;

        /// <summary>
        /// Text to encode in the QR code: checkin?targetType=...&amp;targetId=...&amp;occurrenceDate=...&amp;code=... (values URL-encoded).
        /// </summary>
        public string QrPayload { get; set; } = String.Empty;

        /// <summary>
        /// Check-in window start.
        /// </summary>
        public DateTime WindowOpensUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Check-in window end.
        /// </summary>
        public DateTime WindowClosesUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
