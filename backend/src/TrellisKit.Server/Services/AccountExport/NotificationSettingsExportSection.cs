namespace TrellisKit.Server.Services
{
    using System;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Serialization;

    /// <summary>
    /// Account export sections "notificationPreferences" (the member's settings document: one row per category plus
    /// notification hours, a fixed-size object) and "pushTokens" (platform, device name, app version, and dates; never
    /// token values; bounded by the per-member token limit), both from NotificationPreferenceService.ExportAsync.
    /// Thread safety: stateless; safe to call concurrently.
    /// </summary>
    public sealed class NotificationSettingsExportSection : IAccountExportSection
    {
        #region Public-Members

        /// <summary>
        /// Name of the preferences section.
        /// </summary>
        public const string PreferencesName = "notificationPreferences";

        /// <summary>
        /// Name of the push tokens section.
        /// </summary>
        public const string PushTokensName = "pushTokens";

        /// <summary>
        /// Property name in the export file.
        /// </summary>
        public string Name { get; }

        #endregion

        #region Private-Members

        private readonly NotificationPreferenceService _Preferences;
        private readonly bool _PushTokens;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate one of the two sections.
        /// </summary>
        /// <param name="preferences">Notification preference service.</param>
        /// <param name="pushTokens">True for "pushTokens", false for "notificationPreferences".</param>
        /// <exception cref="ArgumentNullException">The service is null.</exception>
        public NotificationSettingsExportSection(NotificationPreferenceService preferences, bool pushTokens)
        {
            _Preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
            _PushTokens = pushTokens;
            Name = pushTokens ? PushTokensName : PreferencesName;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Write the section's JSON value.
        /// </summary>
        /// <param name="writer">Writer positioned after the property name.</param>
        /// <param name="userId">Member identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The writer is null or the member identifier is empty.</exception>
        public async Task WriteAsync(Utf8JsonWriter writer, string userId, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(writer);
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            NotificationExportData data = await _Preferences.ExportAsync(userId, token).ConfigureAwait(false);
            if (_PushTokens) JsonSerializer.Serialize(writer, data.PushTokens, ApiJson.Api);
            else JsonSerializer.Serialize(writer, data.NotificationPreferences, ApiJson.Api);
            await writer.FlushAsync(token).ConfigureAwait(false);
        }

        #endregion
    }
}
