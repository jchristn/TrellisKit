namespace TrellisKit.Server.Services.AdminSummary
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Metrics;
    using TrellisKit.Core.PropertyConfig;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Settings;
    using TrellisKit.Server.Services.Email;
    using TrellisKit.Server.Services.Email.Blocks;
    using TrellisKit.Server.Services.Email.Templates;
    using TrellisKit.Server.Services.Usage;

    /// <summary>
    /// Daily administrator summary (docs/ADMIN_SUMMARY.md): builds a report of one local day from the property's registered
    /// metrics and emails it (staff_admin_summary) to the deployment's adminSummary.recipients once per day after
    /// adminSummary.sendHour. Days follow the property's usage calendar; operational metrics use UTC days, as on the
    /// dashboards. Delivery is idempotent per property, date, and recipient, so any number of instances and passes send
    /// each recipient one message per day. Thread safety: stateless apart from the settings it reads live.
    /// </summary>
    public sealed class AdminSummaryService
    {
        #region Public-Members

        /// <summary>
        /// Lease and worker name.
        /// </summary>
        public const string WorkerName = "admin_summary";

        /// <summary>
        /// Idempotency key prefix of summary messages.
        /// </summary>
        public const string KeyPrefix = "admin_summary:";

        /// <summary>
        /// Clock (tests replace it).
        /// </summary>
        public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

        #endregion

        #region Private-Members

        private static readonly Regex _Email = new Regex("^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private readonly ApplicationServices _Services;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">services is null.</exception>
        public AdminSummaryService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The valid configured recipients: trimmed, deduplicated case-insensitively, at most 20.
        /// </summary>
        /// <param name="settings">Settings.</param>
        /// <returns>Addresses.</returns>
        /// <exception cref="ArgumentNullException">settings is null.</exception>
        public static List<string> Recipients(AdminSummarySettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            return settings.Recipients.Where(r => !String.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).Where(r => r.Length <= 254 && _Email.IsMatch(r))
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(AdminSummarySettings.MaxRecipients).ToList();
        }

        /// <summary>
        /// The items reported: the engine defaults (unless adminSummary.includeDefaultItems is false) followed by the
        /// configured items (at most 60).
        /// </summary>
        /// <returns>Items.</returns>
        public List<AdminSummaryItem> Items()
        {
            AdminSummarySettings settings = _Services.Settings.AdminSummary;
            List<AdminSummaryItem> items = settings.IncludeDefaultItems ? AdminSummaryDefaults.Items : new List<AdminSummaryItem>();
            items.AddRange(settings.Items.Where(i => i != null).Take(AdminSummarySettings.MaxItems));
            return items;
        }

        /// <summary>
        /// Parse an optional yyyy-MM-dd date; null means yesterday in the property's calendar.
        /// </summary>
        /// <param name="text">Text, or null.</param>
        /// <param name="field">Field name for the validation error.</param>
        /// <returns>Date, or null.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed.</exception>
        public static DateOnly? ParseDate(string? text, string field)
        {
            if (String.IsNullOrWhiteSpace(text)) return null;
            if (!DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
                throw ApiErrorException.Field(field, "Use yyyy-MM-dd.");
            return date;
        }

        /// <summary>
        /// Build the report of one local day.
        /// </summary>
        /// <param name="date">Local date, or null for yesterday.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Report.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed when the date is in the future or more than 1,800 days ago.</exception>
        public async Task<AdminSummaryReport> BuildAsync(DateOnly? date, CancellationToken token = default)
        {
            DateTime now = DateTime.SpecifyKind(Clock(), DateTimeKind.Utc);
            UsageZone zone = await _Services.Usage.ZoneAsync(token).ConfigureAwait(false);
            DateOnly today = zone.LocalDate(now);
            DateOnly day = date ?? today.AddDays(-1);
            if (day > today) throw ApiErrorException.Field("date", "The date cannot be in the future.");
            if (day < today.AddDays(-1800)) throw ApiErrorException.Field("date", "The date must be within the last 1800 days.");
            AdminSummaryReport report = new AdminSummaryReport
            {
                PropertyId = _Services.Property.Property.Id,
                PropertyName = _Services.Property.Name,
                Date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                TimeZone = zone.Id,
                FromUtc = zone.Midnight(day),
                ToUtc = zone.Midnight(day.AddDays(1)),
                GeneratedUtc = now
            };
            int maxGroups = _Services.Settings.AdminSummary.MaxGroups;
            foreach (AdminSummaryItem item in Items())
            {
                token.ThrowIfCancellationRequested();
                string name = String.IsNullOrWhiteSpace(item.Metric) ? "(no metric)" : item.Metric.Trim();
                string? problem = Problem(item);
                if (problem != null)
                {
                    report.Skipped.Add(name + ": " + problem);
                    continue;
                }
                List<AdminSummaryRow> rows;
                try
                {
                    rows = await RowsAsync(item, day, zone, maxGroups, token).ConfigureAwait(false);
                }
                catch (ApiErrorException e)
                {
                    report.Skipped.Add(name + ": " + e.Message);
                    continue;
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    _Services.Logger.Warn("[AdminSummaryService] [p:" + report.PropertyId + "] " + name + " failed: " + e.Message);
                    report.Skipped.Add(name + ": the metric could not be read.");
                    continue;
                }
                if (rows.Count == 0) continue;
                AdminSummarySection? section = report.Sections.FirstOrDefault(s => s.Title == item.Section);
                if (section == null)
                {
                    section = new AdminSummarySection { Title = item.Section.Length > 60 ? item.Section.Substring(0, 60) : item.Section };
                    report.Sections.Add(section);
                }
                section.Rows.AddRange(rows);
            }
            return report;
        }

        /// <summary>
        /// Send the summary of a day. With <paramref name="to"/> one copy goes to that address (always sent); otherwise
        /// every configured recipient that has not received this date's summary gets one.
        /// </summary>
        /// <param name="date">Local date, or null for yesterday.</param>
        /// <param name="to">One address, or null for the configured recipients.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Queued and skipped addresses.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed (date, to, or no configured recipients).</exception>
        public async Task<AdminSummarySendResult> SendAsync(DateOnly? date, string? to, CancellationToken token = default)
        {
            List<string> recipients;
            bool explicitAddress = !String.IsNullOrWhiteSpace(to);
            if (explicitAddress)
            {
                string address = to!.Trim();
                if (address.Length > 254 || !_Email.IsMatch(address)) throw ApiErrorException.Field("to", "Not an email address.");
                recipients = new List<string> { address };
            }
            else
            {
                recipients = Recipients(_Services.Settings.AdminSummary);
                if (recipients.Count == 0) throw ApiErrorException.Field("to", "No adminSummary.recipients are configured; give an address.");
            }
            AdminSummaryReport report = await BuildAsync(date, token).ConfigureAwait(false);
            return await DeliverAsync(report, recipients, !explicitAddress, token).ConfigureAwait(false);
        }

        /// <summary>
        /// One worker pass: when summaries are enabled, at least one recipient is configured, and the local hour has reached
        /// adminSummary.sendHour, send yesterday's summary to every recipient that has not received it.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Messages queued.</returns>
        public async Task<int> RunDueAsync(CancellationToken token = default)
        {
            AdminSummarySettings settings = _Services.Settings.AdminSummary;
            if (!settings.Enabled) return 0;
            List<string> recipients = Recipients(settings);
            if (recipients.Count == 0) return 0;
            DateTime now = DateTime.SpecifyKind(Clock(), DateTimeKind.Utc);
            UsageZone zone = await _Services.Usage.ZoneAsync(token).ConfigureAwait(false);
            if (zone.Local(now).Hour < settings.SendHour) return 0;
            DateOnly day = zone.LocalDate(now).AddDays(-1);
            List<string> due = await PendingAsync(day, recipients, now, token).ConfigureAwait(false);
            if (due.Count == 0) return 0;
            AdminSummaryReport report = await BuildAsync(day, token).ConfigureAwait(false);
            AdminSummarySendResult result = await DeliverAsync(report, due, true, token).ConfigureAwait(false);
            if (result.Queued.Count > 0) _Services.Logger.Info("[AdminSummaryService] [p:" + report.PropertyId + "] queued the " + report.Date + " summary for " + result.Queued.Count + " recipients");
            return result.Queued.Count;
        }

        /// <summary>
        /// Email tokens of a report (the report is stored in the queued message, so retries render the same summary).
        /// </summary>
        /// <param name="report">Report.</param>
        /// <returns>Tokens.</returns>
        /// <exception cref="ArgumentNullException">report is null.</exception>
        public Dictionary<string, string> Tokens(AdminSummaryReport report)
        {
            ArgumentNullException.ThrowIfNull(report);
            DateOnly day = DateOnly.ParseExact(report.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            List<AdminSummaryEmailSection> sections = report.Sections.Select(s => new AdminSummaryEmailSection
            {
                Title = s.Title,
                Lines = s.Rows.Select(r => new EmailField(r.Label, r.Display)).ToList()
            }).ToList();
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "date", day.ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture) },
                { "timeZone", report.TimeZone },
                { "newMembers", Headline(report, "users.new") },
                { "activeMembers", Headline(report, "users.active") },
                { "totalMembers", Headline(report, "users.total") },
                { StaffAdminSummaryEmail.ReportToken, JsonSerializer.Serialize(sections) },
                { "link", _Services.Email.Link("/admin/metrics/growth") }
            };
        }

        #endregion

        #region Private-Methods

        private string? Problem(AdminSummaryItem item)
        {
            if (String.IsNullOrWhiteSpace(item.Metric)) return "an item needs a metric key.";
            MetricDefinition? definition = _Services.Metrics.Registry.Find(item.Metric.Trim());
            if (definition == null) return "the metric is not registered for this property.";
            if (!FeatureOn(item.Feature) || !FeatureOn(definition.Feature)) return "the feature is turned off.";
            if (!definition.Buckets.Contains("Day")) return "the metric has no daily buckets.";
            if (item.GroupBy != null && !definition.Groupings.Contains(item.GroupBy)) return "unsupported grouping " + item.GroupBy + ".";
            if (item.Filters.Count > 8 || item.Filters.Any(f => !definition.Groupings.Contains(f.Key))) return "unsupported filter.";
            if (item.Label != null && item.Label.Length > 80) return "the label is longer than 80 characters.";
            return null;
        }

        private bool FeatureOn(string? feature)
        {
            if (String.IsNullOrWhiteSpace(feature)) return true;
            PropertyFeatures f = _Services.Property.Property.Features;
            switch (feature.Trim().ToLowerInvariant())
            {
                case "forums": return f.Forums;
                case "events": return f.Events;
                case "library": return f.Library;
                case "directory": return f.Directory;
                case "messaging": return f.Messaging;
                case "support": return f.Support;
                case "ideas": return f.Ideas;
                case "broadcasts": return f.Broadcasts;
                case "ai": return f.Ai;
                case "signupcodes": return f.SignupCodes;
                case "search": return f.Search;
                default: return true;
            }
        }

        private async Task<List<AdminSummaryRow>> RowsAsync(AdminSummaryItem item, DateOnly day, UsageZone zone, int maxGroups, CancellationToken token)
        {
            MetricDefinition definition = _Services.Metrics.Registry.Find(item.Metric.Trim())!;
            string label = String.IsNullOrWhiteSpace(item.Label) ? definition.Title : item.Label.Trim();
            // Business metrics use the property's calendar and operational metrics UTC days; cover both.
            DateTime from = Min(zone.Midnight(day.AddDays(-7)), UsageZone.Utc.Midnight(day.AddDays(-7)));
            DateTime to = Max(zone.Midnight(day.AddDays(1)), UsageZone.Utc.Midnight(day.AddDays(1)));
            MetricQuery query = new MetricQuery
            {
                Metric = definition.Key,
                From = from,
                To = to,
                Bucket = "Day",
                GroupBy = item.GroupBy,
                Filters = new Dictionary<string, string>(item.Filters, StringComparer.Ordinal),
                Compare = false,
                Percentile = item.Percentile
            };
            MetricResult result = await _Services.Metrics.QueryAsync(new ContentActor { IsSystemAdmin = true }, query, token).ConfigureAwait(false);
            UsageZone calendar = String.Equals(result.TimeZone, zone.Id, StringComparison.Ordinal) ? zone : UsageZone.Utc;
            DateTime start = calendar.Midnight(day);
            List<AdminSummaryRow> rows = new List<AdminSummaryRow>();
            foreach (MetricSeries series in result.Series)
            {
                AdminSummaryRow? row = Row(series, start, definition, calendar.Id);
                if (row != null) rows.Add(row);
            }
            bool empty = rows.Count == 0 || rows.All(r => r.Value == null && r.PreviousValue == null && r.SevenDayAverage == null && !r.Suppressed);
            if (empty && item.HideWhenEmpty) return new List<AdminSummaryRow>();
            if (item.GroupBy == null || empty)
            {
                AdminSummaryRow single = rows.FirstOrDefault(r => r.Group == "total") ?? rows.FirstOrDefault() ?? new AdminSummaryRow { Metric = definition.Key, Unit = definition.Unit, TimeZone = calendar.Id, Complete = true };
                single.Label = label;
                single.Group = null;
                single.Display = AdminSummaryFormat.Display(single);
                return new List<AdminSummaryRow> { single };
            }
            List<AdminSummaryRow> groups = rows.Where(r => r.Value != null || r.Suppressed || r.PreviousValue != null || r.SevenDayAverage != null)
                .OrderBy(r => r.Group == "Other" ? 1 : 0).ThenByDescending(r => r.Value ?? -1).ThenBy(r => r.Group, StringComparer.Ordinal).ToList();
            bool additive = Additive(definition);
            List<AdminSummaryRow> shown = groups.Where(r => r.Group != "Other").Take(maxGroups).ToList();
            List<AdminSummaryRow> folded = groups.Where(r => !shown.Contains(r)).ToList();
            if (folded.Count > 0 && additive) shown.Add(Sum(folded, definition, calendar.Id, "Other"));
            List<AdminSummaryRow> output = new List<AdminSummaryRow>();
            if (additive && groups.Count > 1)
            {
                AdminSummaryRow total = Sum(groups, definition, calendar.Id, null);
                total.Label = label;
                total.Display = AdminSummaryFormat.Display(total);
                output.Add(total);
            }
            foreach (AdminSummaryRow row in shown)
            {
                row.Label = label + ": " + row.Group;
                row.Display = AdminSummaryFormat.Display(row);
                output.Add(row);
            }
            return output;
        }

        private static AdminSummaryRow? Row(MetricSeries series, DateTime start, MetricDefinition definition, string calendar)
        {
            int index = series.Points.FindIndex(p => p.StartUtc == start);
            if (index < 0) return null;
            MetricPoint point = series.Points[index];
            List<double> week = series.Points.Take(index).Skip(Math.Max(0, index - 7)).Where(p => p.Value != null && !p.Suppressed).Select(p => p.Value!.Value).ToList();
            MetricPoint? previous = index > 0 ? series.Points[index - 1] : null;
            AdminSummaryRow row = new AdminSummaryRow
            {
                Metric = definition.Key,
                Group = series.Key,
                Unit = definition.Unit,
                Value = point.Suppressed ? null : point.Value,
                PreviousValue = previous == null || previous.Suppressed ? null : previous.Value,
                SevenDayAverage = week.Count == 0 ? null : Math.Round(week.Average(), 2),
                Complete = point.Complete,
                Suppressed = point.Suppressed,
                TimeZone = calendar
            };
            row.Change = Change(row.Value, row.PreviousValue);
            return row;
        }

        private static AdminSummaryRow Sum(List<AdminSummaryRow> rows, MetricDefinition definition, string calendar, string? group)
        {
            double? Total(Func<AdminSummaryRow, double?> pick)
            {
                List<double> values = rows.Select(pick).Where(v => v != null).Select(v => v!.Value).ToList();
                return values.Count == 0 ? null : values.Sum();
            }
            AdminSummaryRow row = new AdminSummaryRow
            {
                Metric = definition.Key,
                Group = group,
                Unit = definition.Unit,
                Value = Total(r => r.Value),
                PreviousValue = Total(r => r.PreviousValue),
                SevenDayAverage = Total(r => r.SevenDayAverage),
                Complete = rows.All(r => r.Complete),
                Suppressed = rows.All(r => r.Suppressed),
                TimeZone = calendar
            };
            if (row.SevenDayAverage != null) row.SevenDayAverage = Math.Round(row.SevenDayAverage.Value, 2);
            row.Change = Change(row.Value, row.PreviousValue);
            return row;
        }

        private static bool Additive(MetricDefinition definition)
        {
            if (definition.Unit == "percent" || definition.Unit == "ms") return false;
            return definition.Kind == "Count" || definition.Kind == "Gauge";
        }

        private static double? Change(double? value, double? previous)
        {
            if (value == null || previous == null || previous.Value == 0) return null;
            return Math.Round((value.Value - previous.Value) / previous.Value, 4);
        }

        private static string Headline(AdminSummaryReport report, string metric)
        {
            AdminSummaryRow? row = report.Sections.SelectMany(s => s.Rows).FirstOrDefault(r => r.Metric == metric && r.Group == null);
            return row?.Value == null ? "no data" : AdminSummaryFormat.Value(row.Value.Value, row.Unit);
        }

        private async Task<List<string>> PendingAsync(DateOnly day, List<string> recipients, DateTime now, CancellationToken token)
        {
            List<string> due = new List<string>();
            foreach (string recipient in recipients)
            {
                if (!await _Services.Database.Email.ExistsSinceAsync(Key(day, recipient), now.AddDays(-3), token).ConfigureAwait(false)) due.Add(recipient);
            }
            return due;
        }

        private async Task<AdminSummarySendResult> DeliverAsync(AdminSummaryReport report, List<string> recipients, bool once, CancellationToken token)
        {
            DateOnly day = DateOnly.ParseExact(report.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            Dictionary<string, string> tokens = Tokens(report);
            AdminSummarySendResult result = new AdminSummarySendResult { Date = report.Date };
            foreach (string recipient in recipients)
            {
                EmailRequest request = new EmailRequest
                {
                    Template = StaffAdminSummaryEmail.TemplateKey,
                    To = recipient,
                    IdempotencyKey = once ? Key(day, recipient) : null
                };
                foreach (KeyValuePair<string, string> pair in tokens) request.Tokens[pair.Key] = pair.Value;
                request.Tokens.TryAdd("memberName", "team");
                if (await _Services.Email.QueueAsync(request, token).ConfigureAwait(false) != null) result.Queued.Add(recipient);
                else result.AlreadySent.Add(recipient);
            }
            return result;
        }

        private static string Key(DateOnly day, string recipient)
        {
            return KeyPrefix + day.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ":" + recipient.Trim().ToLowerInvariant();
        }

        private static DateTime Min(DateTime a, DateTime b) { return a < b ? a : b; }

        private static DateTime Max(DateTime a, DateTime b) { return a > b ? a : b; }

        #endregion
    }
}
