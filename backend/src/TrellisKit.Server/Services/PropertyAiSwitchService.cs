namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.PropertyConfig;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// One property's AI switch (docs/TRELLISKIT.md section 9.5): seeded from property.json features.ai into propertyai
    /// on the first load, replaced by the folder's value in SingleProperty when it changed on disk (the folder wins on
    /// change), switched by property administrators and platform administrators (audited as property.ai.update), and
    /// served from an in-memory snapshot, so a change applies to the next request without a restart. AI is available
    /// when the switch and the deployment setting ai.enabled are both on.
    /// Thread safety: <see cref="Current"/> is replaced atomically; changes are serialized per instance.
    /// </summary>
    public class PropertyAiSwitchService
    {
        #region Public-Members

        /// <summary>
        /// The switch every request of this property uses now. Never null.
        /// </summary>
        public PropertyAiSwitch Current
        {
            get { return Volatile.Read(ref _Current); }
        }

        /// <summary>
        /// Whether AI is switched on for the property (the deployment setting is not included).
        /// </summary>
        public bool Enabled
        {
            get { return Current.Enabled; }
        }

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly PropertyConfiguration _Property;
        private readonly AppSettings _Settings;
        private readonly AuditService _Audit;
        private readonly AppLogger _Logger;
        private readonly SemaphoreSlim _Lock = new SemaphoreSlim(1, 1);
        private PropertyAiSwitch _Current;
        private bool _SyncsFromFolder = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service. Until <see cref="EnsureSeededAsync"/> runs, <see cref="Current"/> is the property
        /// document's features.ai.
        /// </summary>
        /// <param name="database">Database driver bound to the property.</param>
        /// <param name="property">Property configuration.</param>
        /// <param name="settings">Running settings (ai.enabled, read live).</param>
        /// <param name="audit">Audit service.</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public PropertyAiSwitchService(DatabaseDriverBase database, PropertyConfiguration property, AppSettings settings, AuditService audit, AppLogger logger)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Property = property ?? throw new ArgumentNullException(nameof(property));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Audit = audit ?? throw new ArgumentNullException(nameof(audit));
            _Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _Current = PropertyAiSwitch.FromDocument(property.Features?.Ai ?? false);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Load the stored switch, seeding it from the property document when none is stored, or replacing it from the
        /// document (audited property.ai.sync) when <paramref name="mergeOnChange"/> is set and the document's
        /// features.ai changed since it was last seeded.
        /// </summary>
        /// <param name="mergeOnChange">Whether the property folder wins on change (SingleProperty folder property).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The current switch.</returns>
        public async Task<PropertyAiSwitch> EnsureSeededAsync(bool mergeOnChange, CancellationToken token = default)
        {
            await _Lock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                Volatile.Write(ref _SyncsFromFolder, mergeOnChange);
                PropertyAiSwitch document = PropertyAiSwitch.FromDocument(_Property.Features?.Ai ?? false);
                PropertyAiSwitch? stored = await _Database.PropertyAi.ReadAsync(token).ConfigureAwait(false);
                DateTime now = DateTime.UtcNow;
                if (stored == null)
                {
                    document.CreatedUtc = now;
                    document.UpdatedUtc = now;
                    await _Database.PropertyAi.WriteAsync(document, token).ConfigureAwait(false);
                    _Logger.Info("[PropertyAiSwitchService] [p:" + _Property.Id + "] seeded AI " + (document.Enabled ? "on" : "off") + " from the property document");
                    Publish(document);
                    return document;
                }
                if (mergeOnChange && !String.Equals(stored.SourceHash, document.SourceHash, StringComparison.Ordinal))
                {
                    document.CreatedUtc = stored.CreatedUtc;
                    document.UpdatedUtc = now;
                    document.Revision = stored.Revision + 1;
                    await _Database.PropertyAi.WriteAsync(document, token).ConfigureAwait(false);
                    _Logger.Info("[PropertyAiSwitchService] [p:" + _Property.Id + "] features.ai changed on disk; AI is now " + (document.Enabled ? "on" : "off"));
                    await _Audit.WriteAsync((TrellisKit.Core.Security.RequestContext?)null, null, "property.ai.sync", "Property", _Property.Id, "enabled " + stored.Enabled + " -> " + document.Enabled, token).ConfigureAwait(false);
                    Publish(document);
                    return document;
                }
                Publish(stored);
                return stored;
            }
            finally
            {
                _Lock.Release();
            }
        }

        /// <summary>
        /// GET /api/v1/admin/property/ai.
        /// </summary>
        /// <returns>View.</returns>
        public PropertyAiView View()
        {
            PropertyAiSwitch current = Current;
            return new PropertyAiView
            {
                PropertyId = _Property.Id,
                Enabled = current.Enabled,
                DeploymentEnabled = _Settings.Ai.Enabled,
                Available = current.Enabled && _Settings.Ai.Enabled,
                SeedEnabled = _Property.Features?.Ai ?? false,
                SyncsFromFolder = Volatile.Read(ref _SyncsFromFolder),
                Revision = current.Revision,
                UpdatedByUserId = current.UpdatedByUserId,
                UpdatedUtc = current.UpdatedUtc
            };
        }

        /// <summary>
        /// PUT /api/v1/admin/property/ai: switch AI on or off, audit (property.ai.update), apply on this instance
        /// immediately, and touch the registry so other instances reload. Turning AI off keeps every endpoint,
        /// assistant, and conversation; the AI routes answer 404 NotAvailable and the AI workers pause until it is on.
        /// </summary>
        /// <param name="info">Request information (actor and source IP).</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed.</exception>
        public async Task<PropertyAiView> UpdateAsync(RequestInfo info, PropertyAiRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            if (request.Enabled == null) throw ApiErrorException.Validation(new Dictionary<string, string> { ["enabled"] = "Must be true or false." });
            await _Lock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                PropertyAiSwitch before = await _Database.PropertyAi.ReadAsync(token).ConfigureAwait(false) ?? Current;
                if (before.Enabled == request.Enabled.Value)
                {
                    Publish(before);
                    return View();
                }
                DateTime now = DateTime.UtcNow;
                PropertyAiSwitch next = before.Clone();
                next.Enabled = request.Enabled.Value;
                next.Revision = before.Revision + 1;
                next.UpdatedUtc = now;
                next.UpdatedByUserId = info.Context?.UserId ?? info.Context?.PlatformAdminId;
                await _Database.PropertyAi.WriteAsync(next, token).ConfigureAwait(false);
                await _Audit.WriteAsync(info, "property.ai.update", "Property", _Property.Id, "enabled " + before.Enabled + " -> " + next.Enabled + (_Settings.Ai.Enabled ? String.Empty : " (ai.enabled is off for the deployment)"), token).ConfigureAwait(false);
                await TouchRegistryAsync(now, token).ConfigureAwait(false);
                TrellisKitTelemetry.Increment(TrellisKitTelemetry.PlatformActions, "action", "property.ai.update");
                Publish(next);
                return View();
            }
            finally
            {
                _Lock.Release();
            }
        }

        #endregion

        #region Private-Methods

        private void Publish(PropertyAiSwitch value)
        {
            Interlocked.Exchange(ref _Current, value);
        }

        private async Task TouchRegistryAsync(DateTime now, CancellationToken token)
        {
            try
            {
                PropertyRecord? record = await _Database.Registry.ReadAsync(_Property.Id, token).ConfigureAwait(false);
                if (record == null) return;
                record.UpdatedUtc = now;
                await _Database.Registry.UpdateAsync(record, token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _Logger.Warn("[PropertyAiSwitchService] [p:" + _Property.Id + "] registry touch failed (other instances apply the change on their next reload): " + e.Message);
            }
        }

        #endregion
    }
}
