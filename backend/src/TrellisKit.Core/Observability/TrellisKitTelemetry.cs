namespace TrellisKit.Core.Observability
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;
    using System.Threading;

    /// <summary>
    /// Application meter, activity source, and instruments. Emission is a near-zero-cost no-op until a collector
    /// subscribes to <see cref="Constants.TelemetrySourceName"/>. Label values must be low-cardinality (no identifiers
    /// or free-form input). Thread safety: all members are safe to call concurrently.
    /// </summary>
    public static class TrellisKitTelemetry
    {
        #region Public-Members

        /// <summary>
        /// Application meter.
        /// </summary>
        public static readonly Meter Meter = new Meter(Constants.TelemetrySourceName);

        /// <summary>
        /// Application activity source. Spans started here nest under Watson's per-request server span.
        /// </summary>
        public static readonly ActivitySource ActivitySource = new ActivitySource(Constants.TelemetrySourceName);

        /// <summary>
        /// Database query count, labeled by db.system and outcome.
        /// </summary>
        public static readonly Counter<long> DatabaseQueries = Meter.CreateCounter<long>(
            "trelliskit.db.queries", "{query}", "Database queries executed.");

        /// <summary>
        /// Database query duration in seconds, labeled by db.system and outcome.
        /// </summary>
        public static readonly Histogram<double> DatabaseQueryDuration = Meter.CreateHistogram<double>(
            "trelliskit.db.query.duration", "s", "Database query duration.");

        /// <summary>
        /// Request history capture attempts, labeled by outcome.
        /// </summary>
        public static readonly Counter<long> RequestHistoryCaptures = Meter.CreateCounter<long>(
            "trelliskit.request_history.captures", "{entry}", "Request history capture attempts.");

        /// <summary>
        /// Request history rows pruned.
        /// </summary>
        public static readonly Counter<long> RequestHistoryPruned = Meter.CreateCounter<long>(
            "trelliskit.request_history.pruned", "{entry}", "Request history rows removed by retention.");

        /// <summary>
        /// Background job runs, labeled by job_name and outcome. ("job" is reserved by Prometheus.)
        /// </summary>
        public static readonly Counter<long> BackgroundJobRuns = Meter.CreateCounter<long>(
            "trelliskit.job.runs", "{run}", "Background job runs.");

        /// <summary>
        /// Background job duration in seconds, labeled by job_name and outcome.
        /// </summary>
        public static readonly Histogram<double> BackgroundJobDuration = Meter.CreateHistogram<double>(
            "trelliskit.job.duration", "s", "Background job duration.");

        /// <summary>
        /// Settings changes, labeled by apply_mode.
        /// </summary>
        public static readonly Counter<long> SettingsChanges = Meter.CreateCounter<long>(
            "trelliskit.settings.changes", "{change}", "Runtime settings changes saved.");

        /// <summary>
        /// Property resolutions, labeled by source (header, host, platform, single, webhook, special) and outcome
        /// (resolved, not_found, mismatch, app_not_registered, webhook_unmatched).
        /// </summary>
        public static readonly Counter<long> PropertyResolutions = Meter.CreateCounter<long>(
            "trelliskit.property.resolutions", "{request}", "Property resolutions per request.");

        /// <summary>
        /// Property isolation violations, labeled by kind (missing_scope, reserved_id, invalid_id,
        /// cross_property_token). Must stay 0.
        /// </summary>
        public static readonly Counter<long> IsolationViolations = Meter.CreateCounter<long>(
            "trelliskit.isolation.violations", "{violation}", "Data access attempted without a valid property scope.");

        /// <summary>
        /// Platform administration actions, labeled by action.
        /// </summary>
        public static readonly Counter<long> PlatformActions = Meter.CreateCounter<long>(
            "trelliskit.platform.actions", "{action}", "Platform administration actions.");

        /// <summary>
        /// Admin authentication attempts, labeled by outcome.
        /// </summary>
        public static readonly Counter<long> AdminAuthentications = Meter.CreateCounter<long>(
            "trelliskit.auth.admin", "{attempt}", "Admin token authentication attempts.");

        /// <summary>
        /// Account registrations, labeled by method and outcome.
        /// </summary>
        public static readonly Counter<long> SignUps = Meter.CreateCounter<long>(
            "trelliskit.auth.signups", "{account}", "Account registrations.");

        /// <summary>
        /// Sign-in attempts, labeled by method and outcome.
        /// </summary>
        public static readonly Counter<long> SignIns = Meter.CreateCounter<long>(
            "trelliskit.auth.signins", "{attempt}", "Sign-in attempts.");

        /// <summary>
        /// Refresh token rotations, labeled by outcome.
        /// </summary>
        public static readonly Counter<long> TokenRefreshes = Meter.CreateCounter<long>(
            "trelliskit.auth.token_refreshes", "{refresh}", "Refresh token rotations.");

        /// <summary>
        /// Password lockouts started.
        /// </summary>
        public static readonly Counter<long> Lockouts = Meter.CreateCounter<long>(
            "trelliskit.auth.lockouts", "{lockout}", "Password lockouts started.");

        /// <summary>
        /// Access token authentications, labeled by outcome.
        /// </summary>
        public static readonly Counter<long> SessionAuthentications = Meter.CreateCounter<long>(
            "trelliskit.auth.sessions", "{request}", "Access token authentications.");

        /// <summary>
        /// Authorization denials, labeled by reason and route_group.
        /// </summary>
        public static readonly Counter<long> AuthorizationDenials = Meter.CreateCounter<long>(
            "trelliskit.authz.denials", "{request}", "Authorization denials.");

        /// <summary>
        /// Purchases started or activated, labeled by store and outcome.
        /// </summary>
        public static readonly Counter<long> Purchases = Meter.CreateCounter<long>(
            "trelliskit.membership.purchases", "{purchase}", "Purchases started or activated.");

        /// <summary>
        /// Store purchase verifications, labeled by store and outcome.
        /// </summary>
        public static readonly Counter<long> Verifications = Meter.CreateCounter<long>(
            "trelliskit.membership.verifications", "{verification}", "Store purchase verifications.");

        /// <summary>
        /// Store webhooks, labeled by store and outcome.
        /// </summary>
        public static readonly Counter<long> Webhooks = Meter.CreateCounter<long>(
            "trelliskit.membership.webhooks", "{webhook}", "Store webhooks.");

        /// <summary>
        /// Sign-up code validations, labeled by outcome.
        /// </summary>
        public static readonly Counter<long> CodeValidations = Meter.CreateCounter<long>(
            "trelliskit.signup_codes.validations", "{validation}", "Sign-up code validations.");

        /// <summary>
        /// Sign-up code redemptions, labeled by outcome.
        /// </summary>
        public static readonly Counter<long> CodeRedemptions = Meter.CreateCounter<long>(
            "trelliskit.signup_codes.redemptions", "{redemption}", "Sign-up code redemptions.");

        /// <summary>
        /// Emails, labeled by category and outcome.
        /// </summary>
        public static readonly Counter<long> Emails = Meter.CreateCounter<long>(
            "trelliskit.emails", "{email}", "Emails.");

        /// <summary>
        /// Feed post operations, labeled by action and outcome.
        /// </summary>
        public static readonly Counter<long> FeedPosts = Meter.CreateCounter<long>(
            "trelliskit.feed.posts", "{post}", "Feed post operations, labeled by action and outcome.");

        /// <summary>
        /// Comment operations, labeled by action, outcome, and depth_bucket.
        /// </summary>
        public static readonly Counter<long> Comments = Meter.CreateCounter<long>(
            "trelliskit.comments", "{comment}", "Comment operations, labeled by action, outcome, and depth_bucket.");

        /// <summary>
        /// Reaction adds and removes, labeled by action, target_type, and key.
        /// </summary>
        public static readonly Counter<long> Reactions = Meter.CreateCounter<long>(
            "trelliskit.reactions", "{reaction}", "Reaction adds and removes, labeled by action, target_type, and key.");

        /// <summary>
        /// Media upload sessions, labeled by kind, mode, and outcome.
        /// </summary>
        public static readonly Counter<long> MediaUploads = Meter.CreateCounter<long>(
            "trelliskit.media.uploads", "{upload}", "Media upload sessions, labeled by kind, mode, and outcome.");

        /// <summary>
        /// Media bytes uploaded, labeled by kind.
        /// </summary>
        public static readonly Counter<long> MediaUploadBytes = Meter.CreateCounter<long>(
            "trelliskit.media.upload.bytes", "By", "Media bytes uploaded, labeled by kind.");

        /// <summary>
        /// Media processing results, labeled by kind, processor, outcome, and failure_reason.
        /// </summary>
        public static readonly Counter<long> MediaProcessing = Meter.CreateCounter<long>(
            "trelliskit.media.processing", "{item}", "Media processing results, labeled by kind, processor, outcome, and failure_reason.");

        /// <summary>
        /// Media pipeline stage duration, labeled by kind and stage.
        /// </summary>
        public static readonly Histogram<double> MediaStageDuration = Meter.CreateHistogram<double>(
            "trelliskit.media.stage.duration", "s", "Media pipeline stage duration, labeled by kind and stage.");

        /// <summary>
        /// Media access grants, labeled by mode and outcome.
        /// </summary>
        public static readonly Counter<long> MediaGrants = Meter.CreateCounter<long>(
            "trelliskit.media.grants", "{grant}", "Media access grants, labeled by mode and outcome.");

        /// <summary>
        /// Notifications created or updated, labeled by type and aggregated.
        /// </summary>
        public static readonly Counter<long> NotificationsCreated = Meter.CreateCounter<long>(
            "trelliskit.notifications.created", "{notification}", "Notifications created or updated, labeled by type and aggregated.");

        /// <summary>
        /// Notification fan-out batch duration, labeled by type.
        /// </summary>
        public static readonly Histogram<double> NotificationFanoutDuration = Meter.CreateHistogram<double>(
            "trelliskit.notifications.fanout.duration", "s", "Notification fan-out batch duration, labeled by type.");

        /// <summary>
        /// Push send decisions, labeled by outcome.
        /// </summary>
        public static readonly Counter<long> PushSends = Meter.CreateCounter<long>(
            "trelliskit.push.sends", "{push}", "Push send decisions, labeled by outcome.");

        /// <summary>
        /// Push receipts, labeled by status.
        /// </summary>
        public static readonly Counter<long> PushReceipts = Meter.CreateCounter<long>(
            "trelliskit.push.receipts", "{receipt}", "Push receipts, labeled by status.");

        /// <summary>
        /// Push token changes, labeled by action.
        /// </summary>
        public static readonly Counter<long> PushTokens = Meter.CreateCounter<long>(
            "trelliskit.push.tokens", "{token}", "Push token changes, labeled by action.");

        /// <summary>
        /// Reports filed, labeled by target_type and reason.
        /// </summary>
        public static readonly Counter<long> Reports = Meter.CreateCounter<long>(
            "trelliskit.reports", "{report}", "Reports filed, labeled by target_type and reason.");

        /// <summary>
        /// Moderation actions, labeled by action and target_type.
        /// </summary>
        public static readonly Counter<long> ModerationActions = Meter.CreateCounter<long>(
            "trelliskit.moderation.actions", "{action}", "Moderation actions, labeled by action and target_type.");

        /// <summary>
        /// Blocks and unblocks, labeled by action.
        /// </summary>
        public static readonly Counter<long> Blocks = Meter.CreateCounter<long>(
            "trelliskit.blocks", "{block}", "Blocks and unblocks, labeled by action.");

        /// <summary>
        /// Feed post emails, labeled by outcome.
        /// </summary>
        public static readonly Counter<long> FeedPostEmails = Meter.CreateCounter<long>(
            "trelliskit.emails.feed_posts", "{email}", "Feed post emails, labeled by outcome.");

        /// <summary>
        /// One-click email unsubscribes, labeled by category.
        /// </summary>
        public static readonly Counter<long> EmailUnsubscribes = Meter.CreateCounter<long>(
            "trelliskit.emails.unsubscribes", "{unsubscribe}", "One-click email unsubscribes, labeled by category.");

        /// <summary>
        /// Link preview lookups, labeled by outcome.
        /// </summary>
        public static readonly Counter<long> LinkPreviews = Meter.CreateCounter<long>(
            "trelliskit.link_previews", "{preview}", "Link preview lookups, labeled by outcome.");

        /// <summary>
        /// Word filter matches, labeled by action and field.
        /// </summary>
        public static readonly Counter<long> WordFilterMatches = Meter.CreateCounter<long>(
            "trelliskit.word_filter.matches", "{match}", "Word filter matches, labeled by action and field.");

        /// <summary>
        /// Member searches, labeled by outcome.
        /// </summary>
        public static readonly Counter<long> MemberSearches = Meter.CreateCounter<long>(
            "trelliskit.member_search", "{search}", "Member searches, labeled by outcome.");

        /// <summary>
        /// Outbound integration calls, labeled by service, operation, and outcome.
        /// </summary>
        public static readonly Counter<long> IntegrationCalls = Meter.CreateCounter<long>(
            "trelliskit.integration.calls", "{call}", "Outbound integration calls, labeled by service, operation, and outcome.");

        /// <summary>
        /// Outbound integration call duration, labeled by service and operation.
        /// </summary>
        public static readonly Histogram<double> IntegrationDuration = Meter.CreateHistogram<double>(
            "trelliskit.integration.duration", "s", "Outbound integration call duration, labeled by service and operation.");

        /// <summary>
        /// Profile value writes, labeled by source (member, staff) and outcome (ok, invalid).
        /// </summary>
        public static readonly Counter<long> ProfileUpdates = Meter.CreateCounter<long>(
            "trelliskit.profile.updates", "{update}", "Profile value writes, labeled by source (member, staff) and outcome (ok, invalid).");

        /// <summary>
        /// Rejected profile values, labeled by type and rule (required, length, range, pattern, option, date, entries, filtered, format).
        /// </summary>
        public static readonly Counter<long> ProfileValidationFailures = Meter.CreateCounter<long>(
            "trelliskit.profile.validation_failures", "{failure}", "Rejected profile values, labeled by type and rule (required, length, range, pattern, option, date, entries, filtered, format).");

        /// <summary>
        /// Uses of the Phase 1 to 2 profile fields, labeled by direction (read, write), field, and client (web, mobile, other).
        /// </summary>
        public static readonly Counter<long> ProfileLegacyFields = Meter.CreateCounter<long>(
            "trelliskit.profile.legacy_fields", "{use}", "Uses of the Phase 1 to 2 profile fields, labeled by direction (read, write), field, and client (web, mobile, other).");

        /// <summary>
        /// Member search profile filters, labeled by type and operator.
        /// </summary>
        public static readonly Counter<long> ProfileFilters = Meter.CreateCounter<long>(
            "trelliskit.profile.filters", "{filter}", "Member search profile filters, labeled by type and operator.");

        /// <summary>
        /// Profile field definition changes, labeled by action (create, update, delete, reorder, seed).
        /// </summary>
        public static readonly Counter<long> ProfileDefinitionChanges = Meter.CreateCounter<long>(
            "trelliskit.profile.definitions.changes", "{change}", "Profile field definition changes, labeled by action (create, update, delete, reorder, seed).");

        /// <summary>
        /// Administrative emails queued, labeled by template and category.
        /// </summary>
        public static readonly Counter<long> EmailQueued = Meter.CreateCounter<long>(
            "trelliskit.email.queued", "{message}", "Administrative emails queued, labeled by template and category.");

        /// <summary>
        /// Administrative emails accepted by the provider, labeled by template and provider.
        /// </summary>
        public static readonly Counter<long> EmailSent = Meter.CreateCounter<long>(
            "trelliskit.email.sent", "{message}", "Administrative emails accepted by the provider, labeled by template and provider.");

        /// <summary>
        /// Administrative email delivery failures, labeled by template, provider, and final (true, false).
        /// </summary>
        public static readonly Counter<long> EmailFailed = Meter.CreateCounter<long>(
            "trelliskit.email.failed", "{message}", "Administrative email delivery failures, labeled by template, provider, and final (true, false).");

        /// <summary>
        /// Administrative emails not sent to a suppressed address, labeled by template.
        /// </summary>
        public static readonly Counter<long> EmailSuppressed = Meter.CreateCounter<long>(
            "trelliskit.email.suppressed", "{message}", "Administrative emails not sent to a suppressed address, labeled by template.");

        /// <summary>
        /// Mailgun webhook events, labeled by event and outcome (ok, invalid_signature, unknown_message, ignored).
        /// </summary>
        public static readonly Counter<long> EmailWebhooks = Meter.CreateCounter<long>(
            "trelliskit.email.webhooks", "{event}", "Mailgun webhook events, labeled by event and outcome (ok, invalid_signature, unknown_message, ignored).");

        /// <summary>
        /// Stored secrets that failed to decrypt, labeled by key (bounded by the settings registry).
        /// </summary>
        public static readonly Counter<long> SecretDecryptFailures = Meter.CreateCounter<long>(
            "trelliskit.secrets.decrypt_failures", "{failure}", "Stored secrets that failed to decrypt, labeled by key.");

        /// <summary>
        /// Domain ownership checks, labeled by outcome (verified, not_found, error).
        /// </summary>
        public static readonly Counter<long> DomainVerifications = Meter.CreateCounter<long>(
            "trelliskit.domain.verifications", "{check}", "Domain ownership checks, labeled by outcome (verified, not_found, error).");

        /// <summary>
        /// Open reports past moderation.reviewTargetHours (gauge, set by the moderation due worker).
        /// </summary>
        public static readonly ObservableGauge<long> ModerationOverdue = Meter.CreateObservableGauge<long>(
            "trelliskit.moderation.overdue", () => Interlocked.Read(ref _ModerationOverdue), "{report}", "Open reports past the review target.");

        /// <summary>
        /// Stored values still encrypted with a previous secrets key, per property (gauge, set by the secrets
        /// re-encryption worker after each pass; 0 when rotation is complete).
        /// </summary>
        public static readonly ObservableGauge<long> SecretsReencryptRemaining = Meter.CreateObservableGauge<long>(
            "trelliskit.secrets.reencrypt_remaining", SecretsRemainingMeasurements, "{value}", "Stored values still encrypted with a previous secrets key.");

        /// <summary>
        /// Label name of the property dimension on every engine metric (Prometheus trelliskit_property).
        /// </summary>
        public const string PropertyLabel = "trelliskit.property";

        /// <summary>
        /// Most distinct property label values (deployment.maxProperties is at most 25, plus platform and unresolved);
        /// further values are reported as other so the label stays low-cardinality.
        /// </summary>
        public const int MaxPropertyLabelValues = 30;

        /// <summary>
        /// The property of the current request or worker pass (flows with the execution context), or null. Set it from a
        /// synchronous method (for example when a request's property services are looked up) so it flows to the caller.
        /// </summary>
        public static string? CurrentProperty
        {
            get { return _Property.Value; }
            set { _Property.Value = value; }
        }

        #endregion

        #region Private-Members

        private static long _ModerationOverdue = 0;
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> _SecretsRemaining = new System.Collections.Concurrent.ConcurrentDictionary<string, long>(StringComparer.Ordinal);
        private static readonly System.Threading.AsyncLocal<string?> _Property = new System.Threading.AsyncLocal<string?>();
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _SeenProperties = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

        #endregion

        #region Public-Methods

        /// <summary>
        /// The bounded property label value of the current context: the property id, platform, or unresolved.
        /// </summary>
        /// <returns>Label value.</returns>
        public static string PropertyValue()
        {
            string value = String.IsNullOrEmpty(_Property.Value) ? "unresolved" : _Property.Value!;
            if (_SeenProperties.ContainsKey(value)) return value;
            if (_SeenProperties.Count >= MaxPropertyLabelValues) return "other";
            _SeenProperties.TryAdd(value, 0);
            return value;
        }

        /// <summary>
        /// Record a completed database query: counter, histogram, and span status.
        /// Never throws; telemetry is best-effort.
        /// </summary>
        /// <param name="activity">Span started for the query, or null.</param>
        /// <param name="dbSystem">Provider name, for example "postgresql".</param>
        /// <param name="elapsed">Elapsed time.</param>
        /// <param name="exception">Exception, or null on success.</param>
        public static void RecordDatabaseQuery(Activity? activity, string dbSystem, TimeSpan elapsed, Exception? exception)
        {
            try
            {
                string outcome = exception == null ? "success" : "failure";
                KeyValuePair<string, object?>[] tags = new KeyValuePair<string, object?>[]
                {
                    new KeyValuePair<string, object?>("db.system", dbSystem),
                    new KeyValuePair<string, object?>("outcome", outcome),
                    new KeyValuePair<string, object?>(PropertyLabel, PropertyValue())
                };
                DatabaseQueries.Add(1, tags);
                DatabaseQueryDuration.Record(elapsed.TotalSeconds, tags);

                if (activity != null)
                {
                    if (exception == null)
                    {
                        activity.SetStatus(ActivityStatusCode.Ok);
                    }
                    else
                    {
                        activity.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
                        activity.AddException(exception);
                    }
                }
            }
            catch (InvalidOperationException)
            {
                // Telemetry is best-effort and never affects the caller.
            }
        }

        /// <summary>
        /// Record a completed background job run. Never throws.
        /// </summary>
        /// <param name="job">Job name (low-cardinality), for example "request_history_prune".</param>
        /// <param name="elapsed">Elapsed time.</param>
        /// <param name="success">Whether the run succeeded.</param>
        public static void RecordJob(string job, TimeSpan elapsed, bool success)
        {
            try
            {
                KeyValuePair<string, object?>[] tags = new KeyValuePair<string, object?>[]
                {
                    new KeyValuePair<string, object?>("job_name", job),
                    new KeyValuePair<string, object?>("outcome", success ? "success" : "failure"),
                    new KeyValuePair<string, object?>(PropertyLabel, PropertyValue())
                };
                BackgroundJobRuns.Add(1, tags);
                BackgroundJobDuration.Record(elapsed.TotalSeconds, tags);
            }
            catch (InvalidOperationException)
            {
                // Telemetry is best-effort and never affects the caller.
            }
        }

        /// <summary>
        /// Increment a counter with a single outcome label. Never throws.
        /// </summary>
        /// <param name="counter">Counter.</param>
        /// <param name="labelName">Label name.</param>
        /// <param name="labelValue">Label value (low-cardinality).</param>
        /// <param name="amount">Amount to add. Default 1.</param>
        public static void Increment(Counter<long> counter, string labelName, string labelValue, long amount = 1)
        {
            if (counter == null) return;
            try
            {
                counter.Add(amount, new KeyValuePair<string, object?>(labelName, labelValue), new KeyValuePair<string, object?>(PropertyLabel, PropertyValue()));
            }
            catch (InvalidOperationException)
            {
                // Telemetry is best-effort and never affects the caller.
            }
        }

        /// <summary>
        /// Increment a counter with two labels. Never throws.
        /// </summary>
        /// <param name="counter">Counter.</param>
        /// <param name="firstName">First label name.</param>
        /// <param name="firstValue">First label value (low-cardinality).</param>
        /// <param name="secondName">Second label name.</param>
        /// <param name="secondValue">Second label value (low-cardinality).</param>
        public static void Increment(Counter<long> counter, string firstName, string firstValue, string secondName, string secondValue)
        {
            if (counter == null) return;
            try
            {
                counter.Add(1, new KeyValuePair<string, object?>(firstName, firstValue), new KeyValuePair<string, object?>(secondName, secondValue), new KeyValuePair<string, object?>(PropertyLabel, PropertyValue()));
            }
            catch (InvalidOperationException)
            {
                // Telemetry is best-effort and never affects the caller.
            }
        }

        /// <summary>
        /// Increment a counter with any number of labels given as name and value pairs. Never throws.
        /// </summary>
        /// <param name="counter">Counter.</param>
        /// <param name="amount">Amount to add.</param>
        /// <param name="labels">Alternating label names and low-cardinality values; a trailing name without a value is ignored.</param>
        public static void Add(Counter<long> counter, long amount, params string[] labels)
        {
            if (counter == null) return;
            try
            {
                counter.Add(amount, Tags(labels));
            }
            catch (InvalidOperationException)
            {
                // Telemetry is best-effort and never affects the caller.
            }
        }

        /// <summary>
        /// Record a histogram value with any number of labels given as name and value pairs. Never throws.
        /// </summary>
        /// <param name="histogram">Histogram.</param>
        /// <param name="value">Value.</param>
        /// <param name="labels">Alternating label names and low-cardinality values.</param>
        public static void Record(Histogram<double> histogram, double value, params string[] labels)
        {
            if (histogram == null) return;
            try
            {
                histogram.Record(value, Tags(labels));
            }
            catch (InvalidOperationException)
            {
                // Telemetry is best-effort and never affects the caller.
            }
        }

        /// <summary>
        /// Record a completed outbound integration call (trelliskit.integration.calls and .duration) and close its span.
        /// Never throws.
        /// </summary>
        /// <param name="activity">Span from <see cref="StartIntegrationSpan"/>, or null.</param>
        /// <param name="service">Service, for example s3, mediaconvert, sns, expo, http_preview.</param>
        /// <param name="operation">Operation, for example PutObject.</param>
        /// <param name="elapsed">Elapsed time.</param>
        /// <param name="outcome">Outcome, for example success or failure.</param>
        public static void RecordIntegration(Activity? activity, string service, string operation, TimeSpan elapsed, string outcome)
        {
            Add(IntegrationCalls, 1, "service", service, "operation", operation, "outcome", outcome);
            Record(IntegrationDuration, elapsed.TotalSeconds, "service", service, "operation", operation);
            if (activity == null) return;
            try
            {
                activity.SetTag("outcome", outcome);
                activity.SetStatus(outcome == "success" ? ActivityStatusCode.Ok : ActivityStatusCode.Error, outcome == "success" ? null : outcome);
            }
            catch (InvalidOperationException)
            {
                // Telemetry is best-effort and never affects the caller.
            }
        }

        /// <summary>
        /// Start a client span named "&lt;service&gt; &lt;operation&gt;" for an outbound integration call. Returns null when
        /// nobody is listening.
        /// </summary>
        /// <param name="service">Service, for example s3.</param>
        /// <param name="operation">Operation, for example PutObject.</param>
        /// <returns>Activity, or null.</returns>
        public static Activity? StartIntegrationSpan(string service, string operation)
        {
            Activity? activity = ActivitySource.StartActivity(service + " " + operation, ActivityKind.Client);
            activity?.SetTag("integration.service", service);
            activity?.SetTag("integration.operation", operation);
            return activity;
        }

        /// <summary>
        /// Set the trelliskit.secrets.reencrypt_remaining gauge for the current property (the worker pass's property).
        /// </summary>
        /// <param name="remaining">Values still encrypted with a previous key.</param>
        public static void SetSecretsReencryptRemaining(long remaining)
        {
            _SecretsRemaining[PropertyValue()] = Math.Max(0, remaining);
        }

        /// <summary>
        /// Set the trelliskit.moderation.overdue gauge.
        /// </summary>
        /// <param name="count">Open reports past the review target.</param>
        public static void SetModerationOverdue(long count)
        {
            Interlocked.Exchange(ref _ModerationOverdue, Math.Max(0, count));
        }

        /// <summary>
        /// Start an application span. Returns null when nobody is listening.
        /// </summary>
        /// <param name="name">Span name, for example "auth.signin".</param>
        /// <returns>Activity, or null.</returns>
        public static Activity? StartSpan(string name)
        {
            return ActivitySource.StartActivity(name, ActivityKind.Internal);
        }

        #endregion

        #region Private-Methods

        private static IEnumerable<Measurement<long>> SecretsRemainingMeasurements()
        {
            foreach (KeyValuePair<string, long> entry in _SecretsRemaining)
                yield return new Measurement<long>(entry.Value, new KeyValuePair<string, object?>(PropertyLabel, entry.Key));
        }

        private static KeyValuePair<string, object?>[] Tags(string[] labels)
        {
            int pairs = labels == null ? 0 : labels.Length / 2;
            KeyValuePair<string, object?>[] tags = new KeyValuePair<string, object?>[pairs + 1];
            for (int i = 0; i < pairs; i++) tags[i] = new KeyValuePair<string, object?>(labels![i * 2], labels[i * 2 + 1]);
            tags[pairs] = new KeyValuePair<string, object?>(PropertyLabel, PropertyValue());
            return tags;
        }

        #endregion
    }
}
