namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.PropertyConfig;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// One property's access configuration (docs/ACCESS_MODELS_CONTRACT.md section 1): seeded from property.json access
    /// into propertyaccess on the first load, re-seeded in SingleProperty when the folder's access section changed (the
    /// folder wins on change, as for profile fields), edited by property administrators (audited as
    /// property.access.update), and served to every request from an in-memory snapshot. An edit touches the property
    /// registry so other instances reload within deployment.propertyCacheSeconds.
    /// Thread safety: <see cref="Current"/> is replaced atomically; edits are serialized per instance.
    /// </summary>
    public class AccessPolicyService
    {
        #region Public-Members

        /// <summary>
        /// The configuration every request of this property uses now. Never null.
        /// </summary>
        public AccessPolicy Current
        {
            get { return Volatile.Read(ref _Current); }
        }

        /// <summary>
        /// Called after the configuration changed on this instance (edit, seed, or reload with a new revision), or null.
        /// </summary>
        public Action<AccessPolicy>? Changed { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly PropertyConfiguration _Property;
        private readonly AuditService _Audit;
        private readonly AppLogger _Logger;
        private readonly SemaphoreSlim _Lock = new SemaphoreSlim(1, 1);
        private AccessPolicy _Current;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service. Until <see cref="EnsureSeededAsync"/> runs, <see cref="Current"/> is the property
        /// document's access section.
        /// </summary>
        /// <param name="database">Database driver bound to the property.</param>
        /// <param name="property">Property configuration.</param>
        /// <param name="audit">Audit service.</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public AccessPolicyService(DatabaseDriverBase database, PropertyConfiguration property, AuditService audit, AppLogger logger)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Property = property ?? throw new ArgumentNullException(nameof(property));
            _Audit = audit ?? throw new ArgumentNullException(nameof(audit));
            _Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _Current = AccessPolicy.FromDocument(property.Access);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Load the stored configuration, seeding it from the property document when none is stored, or replacing it
        /// from the document when <paramref name="mergeOnChange"/> is set and the document's access section changed
        /// since it was last seeded.
        /// </summary>
        /// <param name="mergeOnChange">Whether the property folder wins on change (SingleProperty folder property).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The current configuration.</returns>
        public async Task<AccessPolicy> EnsureSeededAsync(bool mergeOnChange, CancellationToken token = default)
        {
            await _Lock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                AccessPolicy document = AccessPolicy.FromDocument(_Property.Access);
                AccessPolicy? stored = await _Database.PropertyAccess.ReadAsync(token).ConfigureAwait(false);
                DateTime now = DateTime.UtcNow;
                if (stored == null)
                {
                    document.CreatedUtc = now;
                    document.UpdatedUtc = now;
                    await _Database.PropertyAccess.WriteAsync(document, token).ConfigureAwait(false);
                    _Logger.Info("[AccessPolicyService] [p:" + _Property.Id + "] seeded access model " + document.Model + " from the property document");
                    Publish(document);
                    return document;
                }
                if (mergeOnChange && !String.Equals(stored.SourceHash, document.SourceHash, StringComparison.Ordinal))
                {
                    document.CreatedUtc = stored.CreatedUtc;
                    document.UpdatedUtc = now;
                    document.Revision = stored.Revision + 1;
                    await _Database.PropertyAccess.WriteAsync(document, token).ConfigureAwait(false);
                    _Logger.Info("[AccessPolicyService] [p:" + _Property.Id + "] access section changed on disk; access model is now " + document.Model);
                    await _Audit.WriteAsync((TrellisKit.Core.Security.RequestContext?)null, null, "property.access.sync", "Property", _Property.Id, Summary(stored, document), token).ConfigureAwait(false);
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
        /// GET /api/v1/admin/property/access.
        /// </summary>
        /// <returns>Admin view.</returns>
        public AccessPolicyView View()
        {
            return AccessPolicyView.Admin(Current, _Property.Features.SignupCodes);
        }

        /// <summary>
        /// PUT /api/v1/admin/property/access: validate, store, audit (property.access.update), apply on this instance
        /// immediately, and touch the registry so other instances reload. Existing subscriptions and entitlements are
        /// never changed by a switch.
        /// </summary>
        /// <param name="info">Request information (actor and source IP).</param>
        /// <param name="request">Body.</param>
        /// <param name="present">Body fields that were present.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Admin view.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed.</exception>
        public async Task<AccessPolicyView> UpdateAsync(RequestInfo info, AccessPolicyRequest request, HashSet<string> present, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(present);
            await _Lock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                AccessPolicy before = await _Database.PropertyAccess.ReadAsync(token).ConfigureAwait(false) ?? Current;
                AccessPolicy next = before.Clone();
                if (present.Contains("model") && request.Model != null) next.Model = request.Model.Value;
                if (present.Contains("anonymousBrowsing") && request.AnonymousBrowsing != null) next.AnonymousBrowsing = request.AnonymousBrowsing.Value;
                if (present.Contains("defaultAudience")) next.DefaultAudience = request.DefaultAudience;
                NormalizeDefaultAudience(next, present.Contains("defaultAudience"));
                Dictionary<string, string> errors = new Dictionary<string, string>(StringComparer.Ordinal);
                if (present.Contains("model") && request.Model == null) errors["model"] = "Must be Open, PartiallyGated, or FullyGated.";
                if (present.Contains("anonymousBrowsing") && request.AnonymousBrowsing == null) errors["anonymousBrowsing"] = "Must be true or false.";
                List<string>? keys = present.Contains("gatedFeatures") ? (request.GatedFeatures ?? new List<string>()) : null;
                if (keys != null && keys.Count > 20) errors["gatedFeatures"] = "At most 20 entries.";
                foreach (KeyValuePair<string, string> problem in AccessPolicy.Validate(next.Model, keys ?? next.GatedFeatures.Select(AccessPolicy.FeatureKey), next.DefaultAudience))
                    errors.TryAdd(problem.Key, problem.Value);
                if (next.Model != AccessModelEnum.Open && !HasMembershipProduct(_Property.Membership))
                    errors.TryAdd("model", "A paid access model needs a membership section (productName, priceUsdCents, storeProductId) in the property document.");
                if (errors.Count > 0) throw ApiErrorException.Validation(errors);
                if (keys != null) next.GatedFeatures = AccessPolicy.Normalize(keys);

                DateTime now = DateTime.UtcNow;
                next.Revision = before.Revision + 1;
                next.UpdatedUtc = now;
                next.UpdatedByUserId = info.Context?.UserId ?? info.Context?.PlatformAdminId;
                await _Database.PropertyAccess.WriteAsync(next, token).ConfigureAwait(false);
                await _Audit.WriteAsync(info, "property.access.update", "Property", _Property.Id, Summary(before, next), token).ConfigureAwait(false);
                await TouchRegistryAsync(now, token).ConfigureAwait(false);
                TrellisKitTelemetry.Increment(TrellisKitTelemetry.PlatformActions, "action", "property.access.update");
                Publish(next);
                return AccessPolicyView.Admin(next, _Property.Features.SignupCodes);
            }
            finally
            {
                _Lock.Release();
            }
        }

        /// <summary>
        /// Whether a property document carries a sellable membership product.
        /// </summary>
        /// <param name="membership">Membership section, or null.</param>
        /// <returns>True when productName, a positive price, and storeProductId are set.</returns>
        public static bool HasMembershipProduct(PropertyMembership? membership)
        {
            return membership != null && !String.IsNullOrWhiteSpace(membership.ProductName) && membership.PriceUsdCents > 0 && !String.IsNullOrWhiteSpace(membership.StoreProductId);
        }

        #endregion

        #region Private-Methods

        private static void NormalizeDefaultAudience(AccessPolicy next, bool sent)
        {
            if (next.Model == AccessModelEnum.FullyGated)
            {
                next.DefaultAudience = null;
                return;
            }
            if (!sent && next.Model == AccessModelEnum.Open && next.DefaultAudience == ContentAudienceEnum.Members) next.DefaultAudience = null;
        }

        private void Publish(AccessPolicy policy)
        {
            AccessPolicy previous = Interlocked.Exchange(ref _Current, policy);
            if (previous.Revision != policy.Revision || previous.Model != policy.Model) Changed?.Invoke(policy);
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
                _Logger.Warn("[AccessPolicyService] [p:" + _Property.Id + "] registry touch failed (other instances apply the change on their next reload): " + e.Message);
            }
        }

        private static string Summary(AccessPolicy before, AccessPolicy after)
        {
            List<string> parts = new List<string>();
            if (before.Model != after.Model) parts.Add("model " + before.Model + " -> " + after.Model);
            if (before.AnonymousBrowsing != after.AnonymousBrowsing) parts.Add("anonymousBrowsing " + before.AnonymousBrowsing + " -> " + after.AnonymousBrowsing);
            string beforeFeatures = String.Join(",", before.GatedFeatures.Select(AccessPolicy.FeatureKey));
            string afterFeatures = String.Join(",", after.GatedFeatures.Select(AccessPolicy.FeatureKey));
            if (beforeFeatures != afterFeatures) parts.Add("gatedFeatures [" + beforeFeatures + "] -> [" + afterFeatures + "]");
            if (before.DefaultAudience != after.DefaultAudience) parts.Add("defaultAudience " + (before.DefaultAudience?.ToString() ?? "default") + " -> " + (after.DefaultAudience?.ToString() ?? "default"));
            return parts.Count == 0 ? "no change" : String.Join("; ", parts);
        }

        #endregion
    }
}
