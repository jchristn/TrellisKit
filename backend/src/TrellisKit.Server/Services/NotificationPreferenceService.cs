namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Notification preferences and notification hours (Phase 2 contract 8.5 and 8.6). Every cell defaults to on except
    /// the email cells of Replies, Mentions, Reactions, and Moderation; Moderation and Account in-app cannot be turned off;
    /// email is editable only for FeedPosts. Turning in-app off for a category also turns its push off. Members without a
    /// notificationhours row get notifications.defaultHoursStart and notifications.defaultHoursEnd (read live), enabled,
    /// no time zone (evaluated as UTC), and not paused.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class NotificationPreferenceService
    {
        #region Public-Members

        /// <summary>
        /// Clock used for update timestamps. Default DateTime.UtcNow. Never null.
        /// </summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public Func<DateTime> Clock
        {
            get { return _Clock; }
            set { _Clock = value ?? throw new ArgumentNullException(nameof(Clock)); }
        }

        /// <summary>
        /// Reads a member's effective follow-on-comment preference (set by the forum module, Phase 3 client assumption 5);
        /// null leaves autoFollowOnComment null.
        /// </summary>
        public Func<string, CancellationToken, Task<bool?>>? AutoFollowReader { get; set; } = null;

        /// <summary>
        /// Stores a member's follow-on-comment preference (set by the forum module).
        /// </summary>
        public Func<string, bool, CancellationToken, Task>? AutoFollowWriter { get; set; } = null;

        /// <summary>
        /// Whether a member is support staff (the Support category is shown and editable only for SupportAgent and
        /// SystemAdmin, docs/PHASE5_CONTRACT.md section 9.2). Null hides staff-only categories from everyone.
        /// </summary>
        public Func<string, CancellationToken, Task<bool>>? StaffCategoryCheck { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly NotificationSettings _Settings;
        private Func<DateTime> _Clock = () => DateTime.UtcNow;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Notification settings (live).</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public NotificationPreferenceService(DatabaseDriverBase database, NotificationSettings settings)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The member's settings document.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Document.</returns>
        /// <exception cref="ArgumentNullException">The member is null or empty.</exception>
        public async Task<NotificationPreferencesDocument> GetAsync(string userId, CancellationToken token = default)
        {
            NotificationPreferencesDocument document = await BuildDocumentAsync(_Database, _Settings, userId, token).ConfigureAwait(false);
            if (AutoFollowReader != null) document.AutoFollowOnComment = await AutoFollowReader(userId, token).ConfigureAwait(false);
            if (!await IsStaffAsync(userId, token).ConfigureAwait(false)) document.Categories = document.Categories.Where(c => !NotificationCatalog.IsStaffOnly(c.Category)).ToList();
            return document;
        }

        /// <summary>
        /// The notification slice's account export: the settings document and the member's push tokens (platform,
        /// device name, dates; never token values).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Export data.</returns>
        /// <exception cref="ArgumentNullException">The member is null or empty.</exception>
        public async Task<NotificationExportData> ExportAsync(string userId, CancellationToken token = default)
        {
            NotificationPreferencesDocument document = await GetAsync(userId, token).ConfigureAwait(false);
            List<PushToken> tokens = await _Database.PushTokens.EnumerateByUserAsync(userId, token).ConfigureAwait(false);
            return new NotificationExportData { NotificationPreferences = document, PushTokens = tokens.Select(PushTokenService.ToView).ToList() };
        }

        /// <summary>
        /// Build a member's settings document (also used by the account export).
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Notification settings.</param>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Document.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        public static async Task<NotificationPreferencesDocument> BuildDocumentAsync(DatabaseDriverBase database, NotificationSettings settings, string userId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(database);
            ArgumentNullException.ThrowIfNull(settings);
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            List<NotificationPreference> rows = await database.NotificationPreferences.EnumerateAsync(new[] { userId }, token).ConfigureAwait(false);
            List<NotificationHours> hoursRows = await database.NotificationPreferences.EnumerateHoursAsync(new[] { userId }, token).ConfigureAwait(false);
            NotificationHours hours = hoursRows.FirstOrDefault() ?? DefaultHours(settings, userId);
            return Document(userId, rows, hours);
        }

        /// <summary>
        /// The effective preference of one category: the stored row, or the defaults. Non-editable cells always hold their
        /// fixed values, and push is off whenever in-app is off.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="rows">The member's stored rows.</param>
        /// <param name="category">Category.</param>
        /// <returns>Preference.</returns>
        /// <exception cref="ArgumentNullException">The rows are null.</exception>
        public static NotificationPreference Effective(string userId, IEnumerable<NotificationPreference> rows, NotificationCategoryEnum category)
        {
            ArgumentNullException.ThrowIfNull(rows);
            NotificationPreference? row = rows.FirstOrDefault(r => r.Category == category && String.Equals(r.UserId, userId, StringComparison.Ordinal));
            bool inApp = NotificationCatalog.InAppEditable(category) ? (row?.InApp ?? NotificationCatalog.DefaultInApp(category)) : NotificationCatalog.DefaultInApp(category);
            bool push = (row?.Push ?? NotificationCatalog.DefaultPush(category)) && inApp;
            bool email = NotificationCatalog.EmailEditable(category) ? (row?.Email ?? NotificationCatalog.DefaultEmail(category)) : NotificationCatalog.DefaultEmail(category);
            return new NotificationPreference { UserId = userId, Category = category, InApp = inApp, Push = push, Email = email, UpdatedUtc = row?.UpdatedUtc ?? DateTime.MinValue };
        }

        /// <summary>
        /// Default notification hours from notifications.defaultHoursStart and defaultHoursEnd (07:00 and 22:00 when a
        /// setting is malformed).
        /// </summary>
        /// <param name="settings">Notification settings.</param>
        /// <param name="userId">Member.</param>
        /// <returns>Hours.</returns>
        /// <exception cref="ArgumentNullException">The settings are null.</exception>
        public static NotificationHours DefaultHours(NotificationSettings settings, string userId)
        {
            ArgumentNullException.ThrowIfNull(settings);
            return new NotificationHours
            {
                UserId = userId ?? String.Empty,
                Enabled = true,
                StartMinute = NotificationHoursRule.ParseMinute(settings.DefaultHoursStart) ?? 420,
                EndMinute = NotificationHoursRule.ParseMinute(settings.DefaultHoursEnd) ?? 1320,
                TimeZone = null,
                PushPaused = false,
                UpdatedUtc = DateTime.MinValue
            };
        }

        /// <summary>
        /// Apply a partial update and return the full document. Validation errors use field paths such as
        /// categories[0].inApp, categories[0].category, notificationHours.start, and notificationHours.timeZone.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Document.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed.</exception>
        public async Task<NotificationPreferencesDocument> UpdateAsync(string userId, NotificationPreferencesRequest request, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            ArgumentNullException.ThrowIfNull(request);
            using (Activity? activity = TrellisKitTelemetry.StartSpan("notifications.preferences.update"))
            {
                Dictionary<string, string> errors = new Dictionary<string, string>();
                List<NotificationPreference> rows = await _Database.NotificationPreferences.EnumerateAsync(new[] { userId }, token).ConfigureAwait(false);
                List<NotificationHours> hoursRows = await _Database.NotificationPreferences.EnumerateHoursAsync(new[] { userId }, token).ConfigureAwait(false);
                NotificationHours hours = hoursRows.FirstOrDefault() ?? DefaultHours(_Settings, userId);
                DateTime now = _Clock();

                Dictionary<NotificationCategoryEnum, NotificationPreference> changed = new Dictionary<NotificationCategoryEnum, NotificationPreference>();
                List<CategoryPreferenceRequest?> categories = request.Categories?.Cast<CategoryPreferenceRequest?>().ToList() ?? new List<CategoryPreferenceRequest?>();
                bool staff = await IsStaffAsync(userId, token).ConfigureAwait(false);
                for (int i = 0; i < categories.Count; i++)
                {
                    string path = "categories[" + i + "]";
                    CategoryPreferenceRequest? item = categories[i];
                    NotificationCategoryEnum? parsed = item == null ? null : NotificationCatalog.ParseCategory(item.Category);
                    if (item == null || parsed == null)
                    {
                        errors[path + ".category"] = "Use one of " + String.Join(", ", NotificationCatalog.Categories) + ".";
                        continue;
                    }
                    NotificationCategoryEnum category = parsed.Value;
                    if (NotificationCatalog.IsStaffOnly(category) && !staff)
                    {
                        errors[path + ".category"] = category + " notifications are for support staff only.";
                        continue;
                    }
                    NotificationPreference current = changed.TryGetValue(category, out NotificationPreference? pending) ? pending : Effective(userId, rows, category);
                    if (item.InApp != null && !NotificationCatalog.InAppEditable(category) && item.InApp.Value != current.InApp) errors[path + ".inApp"] = category + " in-app notifications cannot be turned off.";
                    if (item.Push != null && !NotificationCatalog.PushEditable(category) && item.Push.Value != current.Push) errors[path + ".push"] = category + " push cannot be changed.";
                    if (item.Email != null && !NotificationCatalog.EmailEditable(category) && item.Email.Value != current.Email) errors[path + ".email"] = category + " email cannot be changed.";
                    NotificationPreference next = new NotificationPreference
                    {
                        UserId = userId,
                        Category = category,
                        InApp = NotificationCatalog.InAppEditable(category) ? (item.InApp ?? current.InApp) : current.InApp,
                        Push = item.Push ?? current.Push,
                        Email = NotificationCatalog.EmailEditable(category) ? (item.Email ?? current.Email) : current.Email,
                        UpdatedUtc = now
                    };
                    if (!next.InApp) next.Push = false;
                    changed[category] = next;
                }

                bool hoursChanged = false;
                if (request.NotificationHours != null)
                {
                    NotificationHoursRequest h = request.NotificationHours;
                    if (h.Enabled != null) { hours.Enabled = h.Enabled.Value; hoursChanged = true; }
                    if (h.Start != null)
                    {
                        int? start = NotificationHoursRule.ParseMinute(h.Start);
                        if (start == null) errors["notificationHours.start"] = "Use HH:mm, for example 07:00.";
                        else { hours.StartMinute = start.Value; hoursChanged = true; }
                    }
                    if (h.End != null)
                    {
                        int? end = NotificationHoursRule.ParseMinute(h.End);
                        if (end == null) errors["notificationHours.end"] = "Use HH:mm, for example 22:00.";
                        else { hours.EndMinute = end.Value; hoursChanged = true; }
                    }
                    if (h.TimeZone != null)
                    {
                        if (!NotificationHoursRule.IsIanaTimeZone(h.TimeZone)) errors["notificationHours.timeZone"] = "Use an IANA time zone, for example America/Los_Angeles.";
                        else { hours.TimeZone = h.TimeZone.Trim(); hoursChanged = true; }
                    }
                }
                if (request.PushPaused != null) { hours.PushPaused = request.PushPaused.Value; hoursChanged = true; }
                if (errors.Count > 0)
                {
                    activity?.SetTag("outcome", "invalid");
                    throw ApiErrorException.Validation(errors);
                }

                if (changed.Count > 0) await _Database.NotificationPreferences.UpsertAsync(changed.Values.ToList(), token).ConfigureAwait(false);
                if (hoursChanged)
                {
                    hours.UserId = userId;
                    hours.UpdatedUtc = now;
                    await _Database.NotificationPreferences.UpsertHoursAsync(hours, token).ConfigureAwait(false);
                }
                activity?.SetTag("outcome", "success");
                if (request.AutoFollowOnComment != null && AutoFollowWriter != null) await AutoFollowWriter(userId, request.AutoFollowOnComment.Value, token).ConfigureAwait(false);
                return await GetAsync(userId, token).ConfigureAwait(false);
            }
        }

        private async Task<bool> IsStaffAsync(string userId, CancellationToken token)
        {
            if (StaffCategoryCheck == null) return false;
            return await StaffCategoryCheck(userId, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Turn one email cell off (unsubscribe links). Keeps the category's other cells.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="category">Category (must have an editable email cell).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The member is null or empty.</exception>
        /// <exception cref="ArgumentException">The category's email cell is not editable.</exception>
        public async Task TurnEmailOffAsync(string userId, NotificationCategoryEnum category, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            if (!NotificationCatalog.EmailEditable(category)) throw new ArgumentException(category + " email cannot be changed.", nameof(category));
            List<NotificationPreference> rows = await _Database.NotificationPreferences.EnumerateAsync(new[] { userId }, token).ConfigureAwait(false);
            NotificationPreference current = Effective(userId, rows, category);
            NotificationPreference? stored = rows.FirstOrDefault(r => r.Category == category);
            current.Push = stored?.Push ?? current.Push;
            current.Email = false;
            current.UpdatedUtc = _Clock();
            await _Database.NotificationPreferences.UpsertAsync(new List<NotificationPreference> { current }, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static NotificationPreferencesDocument Document(string userId, List<NotificationPreference> rows, NotificationHours hours)
        {
            NotificationPreferencesDocument document = new NotificationPreferencesDocument
            {
                PushPaused = hours.PushPaused,
                NotificationHours = new NotificationHoursView
                {
                    Enabled = hours.Enabled,
                    Start = NotificationHoursRule.FormatMinute(hours.StartMinute),
                    End = NotificationHoursRule.FormatMinute(hours.EndMinute),
                    TimeZone = hours.TimeZone
                }
            };
            foreach (NotificationCategoryEnum category in NotificationCatalog.Categories)
            {
                NotificationPreference effective = Effective(userId, rows, category);
                document.Categories.Add(new CategoryPreferenceView
                {
                    Category = category,
                    InApp = effective.InApp,
                    Push = effective.Push,
                    Email = effective.Email,
                    Editable = new ChannelFlags
                    {
                        InApp = NotificationCatalog.InAppEditable(category),
                        Push = NotificationCatalog.PushEditable(category),
                        Email = NotificationCatalog.EmailEditable(category)
                    }
                });
            }
            return document;
        }

        #endregion
    }
}
