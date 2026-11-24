namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Staff broadcasts (docs/PHASE5_CONTRACT.md sections 8.2 and 8.3, as-built note D): drafts, edits, test sends,
    /// scheduling, sending with the confirmCount check, cancel, and deliveries. SystemAdmin and Communicator may use every
    /// route; only SystemAdmin may create, edit, schedule, or send an Essential broadcast. Every create, schedule, send,
    /// cancel, and test is audited broadcast.*; the daily limit (broadcasts.maxPerDay per UTC day) applies to SystemAdmin
    /// too.
    /// Thread safety: safe to call concurrently (status changes are conditional updates).
    /// </summary>
    public class BroadcastService
    {
        #region Public-Members

        /// <summary>
        /// Shortest lead time of a schedule. Default 5 minutes.
        /// </summary>
        public TimeSpan MinimumLead
        {
            get { return _MinimumLead; }
            set { _MinimumLead = value < TimeSpan.Zero ? TimeSpan.Zero : value; }
        }

        /// <summary>
        /// Longest lead time of a schedule. Default 90 days.
        /// </summary>
        public TimeSpan MaximumLead
        {
            get { return _MaximumLead; }
            set { _MaximumLead = value < TimeSpan.FromMinutes(5) ? TimeSpan.FromMinutes(5) : value; }
        }

        /// <summary>
        /// Allowed difference between confirmCount and the current preview count, as a fraction. Default 0.05 (5 percent).
        /// Minimum 0, maximum 0.5.
        /// </summary>
        public double ConfirmTolerance
        {
            get { return _ConfirmTolerance; }
            set { _ConfirmTolerance = Math.Clamp(value, 0, 0.5); }
        }

        /// <summary>
        /// Clock. Default DateTime.UtcNow. Never null.
        /// </summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public Func<DateTime> Clock
        {
            get { return _Clock; }
            set { _Clock = value ?? throw new ArgumentNullException(nameof(Clock)); }
        }

        /// <summary>
        /// Called after a broadcast moved to Sending, so the send worker starts at once. Default null.
        /// </summary>
        public Action? SendSignal { get; set; } = null;

        #endregion

        #region Private-Members

        private static readonly BroadcastStatusEnum[] _Editable = { BroadcastStatusEnum.Draft, BroadcastStatusEnum.Scheduled };
        private static readonly BroadcastStatusEnum[] _Cancelable = { BroadcastStatusEnum.Draft, BroadcastStatusEnum.Scheduled, BroadcastStatusEnum.Sending };

        private readonly ApplicationServices _Services;
        private readonly BroadcastSegmentService _Segments;
        private readonly BroadcastSendService _Sender;
        private Func<DateTime> _Clock = () => DateTime.UtcNow;
        private TimeSpan _MinimumLead = TimeSpan.FromMinutes(5);
        private TimeSpan _MaximumLead = TimeSpan.FromDays(90);
        private double _ConfirmTolerance = 0.05;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <param name="segments">Segments.</param>
        /// <param name="sender">Delivery.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public BroadcastService(ApplicationServices services, BroadcastSegmentService segments, BroadcastSendService sender)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
            _Segments = segments ?? throw new ArgumentNullException(nameof(segments));
            _Sender = sender ?? throw new ArgumentNullException(nameof(sender));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Require the broadcasts feature and a caller who may use broadcasts (SystemAdmin, the break-glass token, or
        /// Communicator).
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <param name="actor">Caller.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">404 NotAvailable when the feature is off, 403 Forbidden otherwise.</exception>
        public static void RequireBroadcaster(ApplicationServices services, ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(actor);
            if (!services.Property.Property.Features.Broadcasts) throw new ApiErrorException(404, "NotAvailable", "Broadcasts are not available in this community.");
            if (!actor.IsSystemAdmin && !actor.IsCommunicator) throw ApiErrorException.Forbidden("Broadcasts need the SystemAdmin or Communicator role.");
        }

        /// <summary>
        /// POST /api/v1/admin/broadcasts: a Draft.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The broadcast.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or SegmentInvalid, 403 Forbidden, 404 NotFound (segment).</exception>
        public async Task<BroadcastView> CreateAsync(ContentActor actor, RequestInfo info, BroadcastRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            RequireBroadcaster(_Services, actor);
            DateTime now = _Clock();
            Broadcast broadcast = new Broadcast
            {
                Id = IdGenerator.Generate(BroadcastIdPrefixes.Broadcast),
                Status = BroadcastStatusEnum.Draft,
                CreatedByUserId = actor.UserId,
                CreatedUtc = now,
                UpdatedUtc = now
            };
            HashSet<string> all = new HashSet<string>(StringComparer.Ordinal) { "title", "channels", "inApp", "push", "email", "segmentId", "segment", "category", "essentialReason" };
            await ApplyAsync(actor, broadcast, request, all, true, token).ConfigureAwait(false);
            await _Services.Metadata.ValidateAsync(actor, request.Labels, request.Tags, actor.IsStaffFor(TargetTypeEnum.Broadcast), token, TargetTypeEnum.Broadcast).ConfigureAwait(false);
            await _Services.Database.Broadcasts.CreateAsync(broadcast, token).ConfigureAwait(false);
            await _Services.Metadata.ApplyAsync(actor, TargetTypeEnum.Broadcast, broadcast.Id, request.Labels, request.Tags, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "broadcast.create", TargetTypeEnum.Broadcast.ToString(), broadcast.Id, Summary(broadcast), token).ConfigureAwait(false);
            if (broadcast.Category == BroadcastCategoryEnum.Essential)
                await _Services.Audit.WriteAsync(info, "broadcast.essential", TargetTypeEnum.Broadcast.ToString(), broadcast.Id, "Essential: " + broadcast.EssentialReason, token).ConfigureAwait(false);
            return await ViewAsync(actor, broadcast, token).ConfigureAwait(false);
        }

        /// <summary>
        /// GET /api/v1/admin/broadcasts/{id}.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="id">Broadcast.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The broadcast.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound.</exception>
        public async Task<BroadcastView> GetAsync(ContentActor actor, string id, CancellationToken token = default)
        {
            RequireBroadcaster(_Services, actor);
            Broadcast broadcast = await RequireAsync(id, token).ConfigureAwait(false);
            return await ViewAsync(actor, broadcast, token).ConfigureAwait(false);
        }

        /// <summary>
        /// PUT /api/v1/admin/broadcasts/{id}: Drafts and Scheduled broadcasts only; omitted fields keep their values.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information.</param>
        /// <param name="id">Broadcast.</param>
        /// <param name="request">Body.</param>
        /// <param name="present">Fields present in the body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The broadcast.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or SegmentInvalid, 403 Forbidden, 404 NotFound, 409 BroadcastNotEditable.</exception>
        public async Task<BroadcastView> UpdateAsync(ContentActor actor, RequestInfo info, string id, BroadcastRequest request, HashSet<string> present, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(present);
            RequireBroadcaster(_Services, actor);
            Broadcast broadcast = await RequireAsync(id, token).ConfigureAwait(false);
            RequireEditable(broadcast);
            RequireEssentialAllowed(actor, broadcast);
            BroadcastCategoryEnum before = broadcast.Category;
            await ApplyAsync(actor, broadcast, request, present, false, token).ConfigureAwait(false);
            await _Services.Metadata.ValidateAsync(actor, request.Labels, request.Tags, actor.IsStaffFor(TargetTypeEnum.Broadcast), token, TargetTypeEnum.Broadcast).ConfigureAwait(false);
            broadcast.UpdatedUtc = _Clock();
            if (!await _Services.Database.Broadcasts.UpdateAsync(broadcast, token).ConfigureAwait(false)) throw NotEditable();
            await _Services.Metadata.ApplyAsync(actor, TargetTypeEnum.Broadcast, broadcast.Id, request.Labels, request.Tags, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "broadcast.update", TargetTypeEnum.Broadcast.ToString(), broadcast.Id, Summary(broadcast), token).ConfigureAwait(false);
            if (before != BroadcastCategoryEnum.Essential && broadcast.Category == BroadcastCategoryEnum.Essential)
                await _Services.Audit.WriteAsync(info, "broadcast.essential", TargetTypeEnum.Broadcast.ToString(), broadcast.Id, "Essential: " + broadcast.EssentialReason, token).ConfigureAwait(false);
            return await GetAsync(actor, broadcast.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// DELETE /api/v1/admin/broadcasts/{id}: Drafts only.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information.</param>
        /// <param name="id">Broadcast.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound, 409 BroadcastNotEditable.</exception>
        public async Task DeleteAsync(ContentActor actor, RequestInfo info, string id, CancellationToken token = default)
        {
            RequireBroadcaster(_Services, actor);
            Broadcast broadcast = await RequireAsync(id, token).ConfigureAwait(false);
            RequireEssentialAllowed(actor, broadcast);
            if (broadcast.Status != BroadcastStatusEnum.Draft || !await _Services.Database.Broadcasts.DeleteDraftAsync(broadcast.Id, token).ConfigureAwait(false))
                throw new ApiErrorException(409, "BroadcastNotEditable", "Only drafts can be deleted; cancel a scheduled broadcast instead.");
            await _Services.Database.Metadata.WriteAsync(TargetTypeEnum.Broadcast, broadcast.Id, new ObjectMetadata(), token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "broadcast.delete", TargetTypeEnum.Broadcast.ToString(), broadcast.Id, broadcast.Title, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/broadcasts/enumerate: filters status (one or a list) and channel (inApp, push, email).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken, 403 Forbidden.</exception>
        public async Task<EnumerationResult<BroadcastView>> EnumerateAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            RequireBroadcaster(_Services, actor);
            EnumerationRequest request = _Services.Enumeration.Prepare(query, BroadcastCollections.Broadcasts, true);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            List<BroadcastStatusEnum> statuses = new List<BroadcastStatusEnum>();
            foreach (string value in FilterValues(request.Query, "status"))
            {
                if (Int32.TryParse(value, out int _) || !Enum.TryParse(value, true, out BroadcastStatusEnum status) || !Enum.IsDefined(status)) errors["filters.status"] = "Use Draft, Scheduled, Sending, Sent, or Canceled.";
                else statuses.Add(status);
            }
            BroadcastChannelEnum? channel = Channel(request.Query.FilterString("channel"), "filters.channel", errors);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors, "One or more filters are invalid.");
            EnumerationResult<Broadcast> page = await _Services.Database.Broadcasts.EnumerateAsync(request, statuses, channel, token).ConfigureAwait(false);
            List<BroadcastView> views = await ViewsAsync(actor, page.Objects, token).ConfigureAwait(false);
            return page.With(views);
        }

        /// <summary>
        /// POST /api/v1/admin/broadcasts/{id}/test: deliver a hidden copy to the caller only, marked Test, on the chosen
        /// channels (Announcements preferences do not apply to a test; suppressions do). Never counts toward the daily
        /// limit. Audited broadcast.test.
        /// </summary>
        /// <param name="actor">Caller (a member session).</param>
        /// <param name="info">Request information.</param>
        /// <param name="id">Broadcast.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The test result.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotFound.</exception>
        public async Task<BroadcastTestResponse> TestAsync(ContentActor actor, RequestInfo info, string id, BroadcastTestRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            RequireBroadcaster(_Services, actor);
            string userId = actor.RequireUserId();
            Broadcast broadcast = await RequireAsync(id, token).ConfigureAwait(false);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            HashSet<BroadcastChannelEnum> channels = new HashSet<BroadcastChannelEnum>();
            if (request.Channels == null || request.Channels.Count == 0)
            {
                if (broadcast.InApp) channels.Add(BroadcastChannelEnum.InApp);
                if (broadcast.Push) channels.Add(BroadcastChannelEnum.Push);
                if (broadcast.Email) channels.Add(BroadcastChannelEnum.Email);
            }
            else
            {
                for (int i = 0; i < request.Channels.Count; i++)
                {
                    BroadcastChannelEnum? channel = Channel(request.Channels[i], "channels[" + i + "]", errors);
                    if (channel != null) channels.Add(channel.Value);
                }
            }
            if (channels.Contains(BroadcastChannelEnum.Push) && !channels.Contains(BroadcastChannelEnum.InApp)) errors["channels"] = "Push rides on the in-app notification; include inApp.";
            if (channels.Count == 0 && errors.Count == 0) errors["channels"] = "Choose at least one channel.";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            DateTime now = _Clock();
            Broadcast copy = new Broadcast
            {
                Id = IdGenerator.Generate(BroadcastIdPrefixes.Broadcast),
                Title = "[Test] " + broadcast.Title,
                InApp = channels.Contains(BroadcastChannelEnum.InApp),
                Push = channels.Contains(BroadcastChannelEnum.Push),
                Email = channels.Contains(BroadcastChannelEnum.Email),
                InAppTitle = "[Test] " + (broadcast.InAppTitle ?? broadcast.Title),
                InAppBody = broadcast.InAppBody,
                PushTitle = "[Test] " + (broadcast.PushTitle ?? broadcast.InAppTitle ?? broadcast.Title),
                PushBody = broadcast.PushBody,
                EmailSubject = "[Test] " + (broadcast.EmailSubject ?? broadcast.InAppTitle ?? broadcast.Title),
                EmailPreheader = broadcast.EmailPreheader,
                EmailBody = broadcast.EmailBody,
                CtaLabel = broadcast.CtaLabel,
                CtaPath = broadcast.CtaPath,
                Category = broadcast.Category,
                EssentialReason = broadcast.EssentialReason,
                Status = BroadcastStatusEnum.Sent,
                IsTest = true,
                StartedUtc = now,
                SentUtc = now,
                CreatedByUserId = userId,
                CreatedUtc = now,
                UpdatedUtc = now
            };
            await _Services.Database.Broadcasts.CreateAsync(copy, token).ConfigureAwait(false);
            BroadcastAudience audience = new BroadcastAudience { UserIds = new List<string> { userId } };
            List<BroadcastRecipient> recipients = await _Services.Database.Broadcasts.EnumerateRecipientsAsync(audience, null, 1, now, token).ConfigureAwait(false);
            await _Sender.DeliverBatchAsync(copy, recipients, Int32.MaxValue, token).ConfigureAwait(false);
            await _Services.Database.Broadcasts.SyncStatsAsync(copy.Id, _Clock(), token).ConfigureAwait(false);
            List<string> names = channels.OrderBy(c => c).Select(ChannelName).ToList();
            await _Services.Audit.WriteAsync(info, "broadcast.test", TargetTypeEnum.Broadcast.ToString(), broadcast.Id, "Test to the caller on " + String.Join(", ", names), token).ConfigureAwait(false);
            return new BroadcastTestResponse { Accepted = true, Channels = names, AnnouncementId = copy.Id };
        }

        /// <summary>
        /// POST /api/v1/admin/broadcasts/{id}/schedule: at least 5 minutes and at most 90 days ahead. Audited
        /// broadcast.schedule.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information.</param>
        /// <param name="id">Broadcast.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The broadcast (Scheduled).</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or SegmentInvalid, 403 Forbidden, 404 NotFound, 409 BroadcastNotEditable or BroadcastLimitReached.</exception>
        public async Task<BroadcastView> ScheduleAsync(ContentActor actor, RequestInfo info, string id, BroadcastScheduleRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            RequireBroadcaster(_Services, actor);
            Broadcast broadcast = await RequireAsync(id, token).ConfigureAwait(false);
            RequireEditable(broadcast);
            RequireEssentialAllowed(actor, broadcast);
            DateTime now = _Clock();
            if (request.SendAtUtc == null) throw ApiErrorException.Field("sendAtUtc", "Required.");
            DateTime sendAt = request.SendAtUtc.Value.Kind == DateTimeKind.Local ? request.SendAtUtc.Value.ToUniversalTime() : DateTime.SpecifyKind(request.SendAtUtc.Value, DateTimeKind.Utc);
            if (sendAt < now + _MinimumLead || sendAt > now + _MaximumLead)
                throw ApiErrorException.Field("sendAtUtc", "Schedule at least " + (int)_MinimumLead.TotalMinutes + " minutes and at most " + (int)_MaximumLead.TotalDays + " days ahead.");
            BroadcastAudience audience = await _Segments.CompileAsync(broadcast.Filters, token).ConfigureAwait(false);
            BroadcastAudienceCounts counts = await _Segments.CountAsync(audience, broadcast.Category == BroadcastCategoryEnum.Essential, token).ConfigureAwait(false);
            RequireWithinRecipients(counts.Count);
            await RequireDailyLimitAsync(sendAt, broadcast.Id, token).ConfigureAwait(false);
            if (!await _Services.Database.Broadcasts.TransitionAsync(broadcast.Id, _Editable, BroadcastStatusEnum.Scheduled, sendAt, now, token).ConfigureAwait(false)) throw NotEditable();
            await _Services.Audit.WriteAsync(info, "broadcast.schedule", TargetTypeEnum.Broadcast.ToString(), broadcast.Id,
                "Scheduled for " + sendAt.ToString("O", CultureInfo.InvariantCulture) + " to " + counts.Count + " recipients", token).ConfigureAwait(false);
            return await GetAsync(actor, broadcast.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/broadcasts/{id}/send: confirmCount must equal the current preview count within 5 percent.
        /// Moves the broadcast to Sending; the send worker delivers it in batches. Audited broadcast.send (and
        /// broadcast.essential for an Essential broadcast).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information.</param>
        /// <param name="id">Broadcast.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The broadcast (Sending).</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or SegmentInvalid, 403 Forbidden, 404 NotFound, 409 BroadcastNotEditable or BroadcastLimitReached.</exception>
        public async Task<BroadcastView> SendAsync(ContentActor actor, RequestInfo info, string id, BroadcastSendRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            RequireBroadcaster(_Services, actor);
            Broadcast broadcast = await RequireAsync(id, token).ConfigureAwait(false);
            RequireEditable(broadcast);
            RequireEssentialAllowed(actor, broadcast);
            if (request.ConfirmCount == null || request.ConfirmCount < 0) throw ApiErrorException.Field("confirmCount", "Send the audience count you confirmed.");
            bool essential = broadcast.Category == BroadcastCategoryEnum.Essential;
            BroadcastAudience audience = await _Segments.CompileAsync(broadcast.Filters, token).ConfigureAwait(false);
            BroadcastAudienceCounts counts = await _Segments.CountAsync(audience, essential, token).ConfigureAwait(false);
            if (Math.Abs(request.ConfirmCount.Value - counts.Count) > Math.Floor(counts.Count * _ConfirmTolerance))
                throw ApiErrorException.Validation(new Dictionary<string, string> { { "confirmCount", "The audience is now " + counts.Count + "; preview the segment again." } }, "The audience changed since the preview.");
            RequireWithinRecipients(counts.Count);
            DateTime now = _Clock();
            await RequireDailyLimitAsync(now, broadcast.Id, token).ConfigureAwait(false);
            if (!await _Services.Database.Broadcasts.TransitionAsync(broadcast.Id, _Editable, BroadcastStatusEnum.Sending, null, now, token).ConfigureAwait(false)) throw NotEditable();
            string category = essential ? "essential" : "optional";
            if (broadcast.InApp) TrellisKitTelemetry.Add(BroadcastTelemetry.Sent, 1, "channel", "in_app", "category", category);
            if (broadcast.Push) TrellisKitTelemetry.Add(BroadcastTelemetry.Sent, 1, "channel", "push", "category", category);
            if (broadcast.Email) TrellisKitTelemetry.Add(BroadcastTelemetry.Sent, 1, "channel", "email", "category", category);
            await _Services.Audit.WriteAsync(info, "broadcast.send", TargetTypeEnum.Broadcast.ToString(), broadcast.Id, "Sending to " + counts.Count + " recipients (" + Channels(broadcast) + ")", token).ConfigureAwait(false);
            if (essential) await _Services.Audit.WriteAsync(info, "broadcast.essential", TargetTypeEnum.Broadcast.ToString(), broadcast.Id, "Essential send: " + broadcast.EssentialReason, token).ConfigureAwait(false);
            try
            {
                SendSignal?.Invoke();
            }
            catch (InvalidOperationException e)
            {
                _Services.Logger.Warn("[BroadcastService] send signal failed: " + e.Message);
            }
            return await GetAsync(actor, broadcast.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/broadcasts/{id}/cancel: a Draft, Scheduled, or Sending broadcast becomes Canceled; remaining
        /// batches stop. Audited broadcast.cancel.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information.</param>
        /// <param name="id">Broadcast.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The broadcast (Canceled).</returns>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound, 409 BroadcastNotEditable.</exception>
        public async Task<BroadcastView> CancelAsync(ContentActor actor, RequestInfo info, string id, CancellationToken token = default)
        {
            RequireBroadcaster(_Services, actor);
            Broadcast broadcast = await RequireAsync(id, token).ConfigureAwait(false);
            if (!await _Services.Database.Broadcasts.TransitionAsync(broadcast.Id, _Cancelable, BroadcastStatusEnum.Canceled, null, _Clock(), token).ConfigureAwait(false))
                throw new ApiErrorException(409, "BroadcastNotEditable", "This broadcast was already sent or canceled.");
            await _Services.Database.Broadcasts.SyncStatsAsync(broadcast.Id, _Clock(), token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "broadcast.cancel", TargetTypeEnum.Broadcast.ToString(), broadcast.Id, "Canceled from " + broadcast.Status, token).ConfigureAwait(false);
            return await GetAsync(actor, broadcast.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/broadcasts/{id}/deliveries/enumerate: filters channel and status.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="id">Broadcast.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken, 403 Forbidden, 404 NotFound.</exception>
        public async Task<EnumerationResult<BroadcastDeliveryView>> DeliveriesAsync(ContentActor actor, string id, EnumerationQuery? query, CancellationToken token = default)
        {
            RequireBroadcaster(_Services, actor);
            Broadcast broadcast = await RequireAsync(id, token).ConfigureAwait(false);
            EnumerationRequest request = _Services.Enumeration.Prepare(query, BroadcastCollections.Deliveries, true, broadcast.Id);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            BroadcastChannelEnum? channel = Channel(request.Query.FilterString("channel"), "filters.channel", errors);
            BroadcastDeliveryStatusEnum? status = null;
            string? statusText = request.Query.FilterString("status");
            if (!String.IsNullOrWhiteSpace(statusText))
            {
                if (Int32.TryParse(statusText, out int _) || !Enum.TryParse(statusText.Trim(), true, out BroadcastDeliveryStatusEnum parsed) || !Enum.IsDefined(parsed))
                    errors["filters.status"] = "Use a delivery status such as Pending, Created, Queued, Sent, Delivered, Failed, Suppressed, SkippedPreference, NoToken, or NoAddress.";
                else status = parsed;
            }
            if (errors.Count > 0) throw ApiErrorException.Validation(errors, "One or more filters are invalid.");
            EnumerationResult<BroadcastDelivery> page = await _Services.Database.Broadcasts.EnumerateDeliveriesAsync(request, broadcast.Id, channel, status, token).ConfigureAwait(false);
            Dictionary<string, UserSummary> users = await _Services.Summaries.BuildAsync(page.Objects.Select(d => (string?)d.UserId), ProfileViewerEnum.Staff, token).ConfigureAwait(false);
            List<BroadcastDeliveryView> views = page.Objects.Select(d => new BroadcastDeliveryView
            {
                Id = d.Id,
                BroadcastId = d.BroadcastId,
                User = users.TryGetValue(d.UserId, out UserSummary? user) ? user : UserSummary.Deleted(),
                InAppStatus = d.InAppStatus,
                PushStatus = d.PushStatus,
                EmailStatus = d.EmailStatus,
                CreatedUtc = d.CreatedUtc,
                UpdatedUtc = d.UpdatedUtc
            }).ToList();
            await _Services.Metadata.AttachAsync(TargetTypeEnum.BroadcastDelivery, views, v => v.Id, actor.IsStaffFor(TargetTypeEnum.BroadcastDelivery), token).ConfigureAwait(false);
            return page.With(views);
        }

        /// <summary>
        /// The JSON name of a channel (inApp, push, email).
        /// </summary>
        /// <param name="channel">Channel.</param>
        /// <returns>Name.</returns>
        public static string ChannelName(BroadcastChannelEnum channel)
        {
            switch (channel)
            {
                case BroadcastChannelEnum.Push:
                    return "push";
                case BroadcastChannelEnum.Email:
                    return "email";
                default:
                    return "inApp";
            }
        }

        #endregion

        #region Private-Methods

        private async Task<Broadcast> RequireAsync(string? id, CancellationToken token)
        {
            Broadcast? broadcast = String.IsNullOrWhiteSpace(id) ? null : await _Services.Database.Broadcasts.ReadAsync(id.Trim(), token).ConfigureAwait(false);
            if (broadcast == null || broadcast.IsTest) throw ApiErrorException.NotFound("Broadcast");
            return broadcast;
        }

        private static void RequireEditable(Broadcast broadcast)
        {
            if (!_Editable.Contains(broadcast.Status)) throw NotEditable();
        }

        private static ApiErrorException NotEditable()
        {
            return new ApiErrorException(409, "BroadcastNotEditable", "This broadcast is sending, sent, or canceled and can no longer be changed.");
        }

        private static void RequireEssentialAllowed(ContentActor actor, Broadcast broadcast)
        {
            if (broadcast.Category == BroadcastCategoryEnum.Essential && !actor.IsSystemAdmin)
                throw ApiErrorException.Forbidden("Only a SystemAdmin may change, schedule, or send an Essential broadcast.");
        }

        private void RequireWithinRecipients(long count)
        {
            int max = _Services.Settings.Broadcasts.MaxRecipients;
            if (count > max)
                throw ApiErrorException.Validation(new Dictionary<string, string> { { "segment", "The audience has " + count + " recipients; a broadcast reaches at most " + max + " (broadcasts.maxRecipients)." } }, "The audience is too large.");
        }

        private async Task RequireDailyLimitAsync(DateTime when, string exceptId, CancellationToken token)
        {
            DateTime day = new DateTime(when.Year, when.Month, when.Day, 0, 0, 0, DateTimeKind.Utc);
            long used = await _Services.Database.Broadcasts.CountUsedInWindowAsync(day, day.AddDays(1), exceptId, token).ConfigureAwait(false);
            int max = _Services.Settings.Broadcasts.MaxPerDay;
            if (used >= max)
                throw new ApiErrorException(409, "BroadcastLimitReached", "At most " + max + " broadcasts are sent per day (UTC); " + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " is full.");
        }

        private async Task ApplyAsync(ContentActor actor, Broadcast b, BroadcastRequest r, HashSet<string> present, bool creating, CancellationToken token)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>();
            if (present.Contains("title")) b.Title = Required(r.Title, "title", 200, errors);
            if (present.Contains("channels"))
            {
                ChannelFlags channels = r.Channels ?? new ChannelFlags { InApp = true };
                b.InApp = channels.InApp;
                b.Push = channels.Push;
                b.Email = channels.Email;
            }
            if (present.Contains("inApp") && r.InApp != null)
            {
                b.InAppTitle = Optional(r.InApp.Title, "inApp.title", 200, errors);
                b.InAppBody = Optional(MarkdownNormalizer.Normalize(r.InApp.Body), "inApp.body", 20000, errors);
            }
            if (present.Contains("push") && r.Push != null)
            {
                b.PushTitle = Optional(r.Push.Title, "push.title", 100, errors);
                b.PushBody = Optional(r.Push.Body, "push.body", 240, errors);
            }
            if (present.Contains("email") && r.Email != null)
            {
                b.EmailSubject = Optional(r.Email.Subject, "email.subject", 200, errors);
                b.EmailPreheader = Optional(r.Email.Preheader, "email.preheader", 200, errors);
                b.EmailBody = Optional(MarkdownNormalizer.Normalize(r.Email.Body), "email.body", 20000, errors);
                b.CtaLabel = Optional(r.Email.CtaLabel, "email.ctaLabel", 60, errors);
                b.CtaPath = Optional(r.Email.CtaPath, "email.ctaPath", 500, errors);
                if (b.CtaPath != null && (!b.CtaPath.StartsWith("/", StringComparison.Ordinal) || b.CtaPath.StartsWith("//", StringComparison.Ordinal) || b.CtaPath.Any(Char.IsWhiteSpace)))
                    errors["email.ctaPath"] = "Use a path on the website that starts with / (no spaces).";
            }
            if (present.Contains("category") && r.Category != null) b.Category = r.Category.Value;
            if (present.Contains("essentialReason") || present.Contains("category"))
                b.EssentialReason = b.Category == BroadcastCategoryEnum.Essential ? Optional(r.EssentialReason ?? b.EssentialReason, "essentialReason", 500, errors) : null;
            if (b.Category == BroadcastCategoryEnum.Essential)
            {
                if (!actor.IsSystemAdmin) throw ApiErrorException.Forbidden("Only a SystemAdmin may create an Essential broadcast.");
                if (b.EssentialReason == null || b.EssentialReason.Length < 10) errors["essentialReason"] = "An Essential broadcast needs a reason of 10 to 500 characters (marketing content must not be essential).";
            }
            if (!b.InApp && !b.Push && !b.Email) errors["channels"] = "Choose at least one channel.";
            if (b.Push && !b.InApp) errors["channels.push"] = "Push rides on the in-app notification; turn inApp on too.";
            if (b.InApp && String.IsNullOrWhiteSpace(b.InAppBody) && errors.Count == 0) errors["inApp.body"] = "The announcement needs a body.";
            if (b.Email && String.IsNullOrWhiteSpace(b.EmailBody ?? b.InAppBody) && errors.Count == 0) errors["email.body"] = "The email needs a body (or an announcement body).";
            if (present.Contains("segmentId") && present.Contains("segment") && !String.IsNullOrWhiteSpace(r.SegmentId) && r.Segment != null) errors["segmentId"] = "Send segmentId or segment, not both.";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            if (present.Contains("segmentId") && !String.IsNullOrWhiteSpace(r.SegmentId))
            {
                BroadcastSegment segment = await _Segments.RequireAsync(r.SegmentId, token).ConfigureAwait(false);
                b.SegmentId = segment.Id;
                b.Filters = BroadcastSegmentService.Copy(segment.Filters);
            }
            else if (present.Contains("segment") && r.Segment != null)
            {
                b.SegmentId = null;
                b.Filters = await _Segments.ValidateAsync(r.Segment.Filters, "segment.filters", token).ConfigureAwait(false);
            }
            else if (creating)
            {
                b.SegmentId = null;
                b.Filters = new List<BroadcastSegmentFilter>();
            }
        }

        private async Task<BroadcastView> ViewAsync(ContentActor actor, Broadcast broadcast, CancellationToken token)
        {
            List<BroadcastView> views = await ViewsAsync(actor, new List<Broadcast> { broadcast }, token).ConfigureAwait(false);
            return views[0];
        }

        private async Task<List<BroadcastView>> ViewsAsync(ContentActor actor, List<Broadcast> broadcasts, CancellationToken token)
        {
            Dictionary<string, UserSummary> creators = await _Services.Summaries.BuildAsync(broadcasts.Select(b => b.CreatedByUserId), ProfileViewerEnum.Staff, token).ConfigureAwait(false);
            List<string> segmentIds = broadcasts.Where(b => b.SegmentId != null).Select(b => b.SegmentId!).Distinct(StringComparer.Ordinal).ToList();
            Dictionary<string, BroadcastSegment> segments = new Dictionary<string, BroadcastSegment>(StringComparer.Ordinal);
            foreach (string segmentId in segmentIds)
            {
                BroadcastSegment? segment = await _Services.Database.Broadcasts.ReadSegmentAsync(segmentId, token).ConfigureAwait(false);
                if (segment != null) segments[segmentId] = segment;
            }
            List<BroadcastView> views = broadcasts.Select(b => new BroadcastView
            {
                Id = b.Id,
                Title = b.Title,
                Channels = new ChannelFlags { InApp = b.InApp, Push = b.Push, Email = b.Email },
                InApp = new BroadcastInAppContent { Title = b.InAppTitle ?? b.Title, Body = b.InAppBody, Path = "/announcements/" + b.Id },
                Push = new BroadcastPushContent { Title = b.PushTitle, Body = b.PushBody },
                Email = new BroadcastEmailContent { Subject = b.EmailSubject, Preheader = b.EmailPreheader, Body = b.EmailBody, CtaLabel = b.CtaLabel, CtaPath = b.CtaPath ?? "/announcements/" + b.Id },
                Segment = new BroadcastSegmentView
                {
                    Id = b.SegmentId,
                    Name = b.SegmentId != null && segments.TryGetValue(b.SegmentId, out BroadcastSegment? s) ? s.Name : null,
                    Filters = BroadcastSegmentService.Specs(b.Filters)
                },
                Category = b.Category,
                EssentialReason = b.EssentialReason,
                Status = b.Status,
                SendAtUtc = b.SendAtUtc,
                Stats = b.Stats,
                CreatedBy = b.CreatedByUserId != null && creators.TryGetValue(b.CreatedByUserId, out UserSummary? creator) ? creator : null,
                CreatedUtc = b.CreatedUtc,
                UpdatedUtc = b.UpdatedUtc,
                StartedUtc = b.StartedUtc,
                SentUtc = b.SentUtc,
                CanceledUtc = b.CanceledUtc
            }).ToList();
            await _Services.Metadata.AttachAsync(TargetTypeEnum.Broadcast, views, v => v.Id, actor.IsStaffFor(TargetTypeEnum.Broadcast), token).ConfigureAwait(false);
            return views;
        }

        private static List<string> FilterValues(EnumerationQuery query, string name)
        {
            List<string> values = new List<string>();
            if (!query.Filters.TryGetValue(name, out JsonElement element)) return values;
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray().Take(10)) values.Add(item.ValueKind == JsonValueKind.String ? item.GetString() ?? String.Empty : item.GetRawText());
            }
            else if (element.ValueKind == JsonValueKind.String)
            {
                foreach (string part in (element.GetString() ?? String.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) values.Add(part);
            }
            else if (element.ValueKind != JsonValueKind.Null) values.Add(element.GetRawText());
            return values;
        }

        private static BroadcastChannelEnum? Channel(string? value, string field, Dictionary<string, string> errors)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            switch (value.Trim().ToLowerInvariant())
            {
                case "inapp":
                    return BroadcastChannelEnum.InApp;
                case "push":
                    return BroadcastChannelEnum.Push;
                case "email":
                    return BroadcastChannelEnum.Email;
                default:
                    errors[field] = "Use inApp, push, or email.";
                    return null;
            }
        }

        private static string Channels(Broadcast b)
        {
            List<string> names = new List<string>();
            if (b.InApp) names.Add("inApp");
            if (b.Push) names.Add("push");
            if (b.Email) names.Add("email");
            return String.Join(", ", names);
        }

        private static string Summary(Broadcast b)
        {
            return b.Title + " (" + Channels(b) + "; " + b.Category + ")";
        }

        private static string Required(string? value, string field, int max, Dictionary<string, string> errors)
        {
            string text = (value ?? String.Empty).Trim();
            if (text.Length < 1 || text.Length > max) errors[field] = "Required, 1 to " + max + " characters.";
            return text;
        }

        private static string? Optional(string? value, string field, int max, Dictionary<string, string> errors)
        {
            string text = (value ?? String.Empty).Trim();
            if (text.Length > max) errors[field] = "At most " + max + " characters.";
            return text.Length == 0 ? null : text;
        }

        #endregion
    }
}
