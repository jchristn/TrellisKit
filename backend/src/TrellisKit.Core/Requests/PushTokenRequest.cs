namespace TrellisKit.Core.Requests
{
    /// <summary>
    /// Body of POST and DELETE /api/v1/me/push-tokens (DELETE uses only the token).
    /// </summary>
    public class PushTokenRequest
    {
        /// <summary>
        /// Expo push token, matching ^Expo(nent)?PushToken\[[A-Za-z0-9_-]+\]$.
        /// </summary>
        public string? Token { get; set; } = null;

        /// <summary>
        /// Ios or Android.
        /// </summary>
        public string? Platform { get; set; } = null;

        /// <summary>
        /// Client device identifier (optional, at most 200 characters).
        /// </summary>
        public string? DeviceId { get; set; } = null;

        /// <summary>
        /// Device name (optional, at most 100 characters).
        /// </summary>
        public string? DeviceName { get; set; } = null;

        /// <summary>
        /// App version (optional, at most 50 characters).
        /// </summary>
        public string? AppVersion { get; set; } = null;
    }
}
