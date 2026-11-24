namespace TrellisKit.Core.Responses
{
    using System.Collections.Generic;

    /// <summary>
    /// The notification slice's part of the account export (Phase 2 contract 11): the preferences document and the
    /// push tokens (platform, device name, dates; never token values; at most 10).
    /// </summary>
    public class NotificationExportData
    {
        /// <summary>
        /// Notification preferences document.
        /// </summary>
        public NotificationPreferencesDocument NotificationPreferences { get; set; } = new NotificationPreferencesDocument();

        /// <summary>
        /// Push tokens without values. Never null.
        /// </summary>
        public List<PushTokenView> PushTokens
        {
            get { return _PushTokens; }
            set { _PushTokens = value ?? new List<PushTokenView>(); }
        }

        private List<PushTokenView> _PushTokens = new List<PushTokenView>();
    }
}
