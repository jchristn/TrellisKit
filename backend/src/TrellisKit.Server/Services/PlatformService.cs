namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Integrations.Implementations;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.PropertyConfig;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Security;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Platform administration (MultiProperty; docs/MULTIPROPERTY_CONTRACT.md section 6 with the resolved decisions):
    /// platform administrators live in their own table, separate from every property's member accounts, and sign in
    /// with short sessions on a platform host; they register, enable, disable, delete (30-day restore window, then
    /// purge), and restore properties (at most deployment.maxProperties, 25); they annotate a property's members as
    /// its SystemAdmin; and they act inside one property only through a short-lived, audited delegated session.
    /// Every platform action is audited under the platform realm (and delegations also in the property's audit).
    /// Thread safety: stateless apart from the shared settings, database, and directory; safe to call concurrently.
    /// </summary>
    public class PlatformService
    {
        #region Public-Members

        /// <summary>
        /// Failed sign-ins before a platform administrator is locked. Default 5. Minimum 1, maximum 100.
        /// </summary>
        public int MaxFailedLogins
        {
            get { return _MaxFailedLogins; }
            set { _MaxFailedLogins = Math.Clamp(value, 1, 100); }
        }

        /// <summary>
        /// Lockout length in minutes. Default 15. Minimum 1, maximum 1440.
        /// </summary>
        public int LockoutMinutes
        {
            get { return _LockoutMinutes; }
            set { _LockoutMinutes = Math.Clamp(value, 1, 1440); }
        }

        /// <summary>
        /// Platform audit records (stored under the platform realm).
        /// </summary>
        public DatabaseDriverBase Realm
        {
            get { return _Realm; }
        }

        /// <summary>
        /// Property directory.
        /// </summary>
        public PropertyDirectory Directory
        {
            get { return _Directory; }
        }

        /// <summary>
        /// Sender used for platform mail instead of a new SMTP sender (tests). Default null.
        /// </summary>
        public IEmailSender? PlatformSmtp { get; set; } = null;

        /// <summary>
        /// Display name of the platform in its own email.
        /// </summary>
        public const string PlatformName = "TrellisKit Platform";

        /// <summary>
        /// Prefix of the DNS TXT record name that proves domain ownership.
        /// </summary>
        public const string VerificationRecordPrefix = "_trelliskit-verify.";

        /// <summary>
        /// Prefix of the DNS TXT record value that proves domain ownership.
        /// </summary>
        public const string VerificationValuePrefix = "trelliskit-verify=";

        #endregion

        #region Private-Members

        private const int _MaxDomains = 20;
        private const int _MaxApps = 10;
        private readonly AppSettings _Settings;
        private readonly DatabaseDriverBase _Database;
        private readonly DatabaseDriverBase _Realm;
        private readonly PropertyDirectory _Directory;
        private readonly ServerLifetime _Lifetime;
        private readonly AppLogger _Logger;
        private readonly IPasswordHasher _Hasher = new Pbkdf2PasswordHasher();
        private readonly EnumerationService _Enumeration;
        private readonly MetadataService _Metadata;
        private int _MaxFailedLogins = 5;
        private int _LockoutMinutes = 15;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="settings">Running settings.</param>
        /// <param name="database">Deployment driver.</param>
        /// <param name="directory">Property directory.</param>
        /// <param name="lifetime">Server lifetime.</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public PlatformService(AppSettings settings, DatabaseDriverBase database, PropertyDirectory directory, ServerLifetime lifetime, AppLogger logger)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Database = (database ?? throw new ArgumentNullException(nameof(database))).Deployment;
            _Directory = directory ?? throw new ArgumentNullException(nameof(directory));
            _Lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
            _Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _Realm = _Database.ForProperty(PropertyIds.Platform);
            _Enumeration = new EnumerationService(settings) { PropertyId = PropertyIds.Platform };
            _Metadata = new MetadataService(_Realm, _Enumeration);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The base URL of the platform console on a platform host, used in platform email links. Public hosts use
        /// https. A local host (localhost, a *.localhost name, or a 127.x address) uses http, and takes the port of
        /// web.publicBaseUrl when that is also a local http address, so links work on a local stack.
        /// </summary>
        /// <param name="host">Platform host name (no scheme or port).</param>
        /// <param name="webPublicBaseUrl">web.publicBaseUrl, or null.</param>
        /// <returns>Base URL without a trailing slash.</returns>
        /// <exception cref="ArgumentNullException">host is null.</exception>
        public static string PlatformBaseUrl(string host, string? webPublicBaseUrl)
        {
            ArgumentNullException.ThrowIfNull(host);
            string h = host.Trim().TrimEnd('.').ToLowerInvariant();
            if (!IsLocalHost(h)) return "https://" + h;
            if (!String.IsNullOrWhiteSpace(webPublicBaseUrl)
                && Uri.TryCreate(webPublicBaseUrl.Trim(), UriKind.Absolute, out Uri? web)
                && (web.Scheme == Uri.UriSchemeHttp || web.Scheme == Uri.UriSchemeHttps)
                && IsLocalHost(web.Host.ToLowerInvariant()))
            {
                return web.Scheme + "://" + h + (web.IsDefaultPort ? String.Empty : ":" + web.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            return "http://" + h;
        }

        /// <summary>
        /// Create the bootstrap platform administrator when platform.bootstrapAdminEmail is set and no platform
        /// administrator exists. With platform.bootstrapAdminPassword the account can sign in at once; otherwise an
        /// invitation token is logged once at warning level.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task BootstrapAsync(CancellationToken token = default)
        {
            string email = (_Settings.Platform.BootstrapAdminEmail ?? String.Empty).Trim();
            if (email.Length == 0) return;
            if (await _Database.PlatformAdmins.CountActiveAsync(token).ConfigureAwait(false) > 0) return;
            DateTime now = DateTime.UtcNow;
            PlatformAdmin admin = new PlatformAdmin
            {
                Id = IdGenerator.Generate(Constants.PlatformAdminIdPrefix),
                Email = email,
                DisplayName = email.Split('@')[0],
                CreatedUtc = now,
                UpdatedUtc = now
            };
            string password = _Settings.Platform.BootstrapAdminPassword ?? String.Empty;
            string? invite = null;
            if (password.Length > 0) admin.PasswordHash = _Hasher.Hash(password);
            else
            {
                invite = TokenService.NewOpaqueToken();
                admin.InviteTokenHash = TokenService.Hash(invite);
                admin.InviteExpiresUtc = now.AddDays(_Settings.Platform.InviteDays);
            }
            await _Database.PlatformAdmins.CreateAsync(admin, token).ConfigureAwait(false);
            await AuditAsync(null, "platform.admin.bootstrap", "PlatformAdmin", admin.Id, "Bootstrap platform administrator " + email, token).ConfigureAwait(false);
            if (invite != null && await SendInviteAsync(admin, invite, "The deployment", token).ConfigureAwait(false))
                _Logger.Info("[PlatformService] bootstrap platform administrator " + email + " created; the invitation was emailed");
            else if (invite != null)
                _Logger.Warn("[PlatformService] bootstrap platform administrator " + email + " created; accept the invitation (shown once) with POST " + Constants.ApiPrefix + "/platform/auth/accept-invite and token " + invite);
            else
                _Logger.Info("[PlatformService] bootstrap platform administrator " + email + " created");
        }

        /// <summary>
        /// Authenticate a platform request from its Authorization header: a platform session token (tkp_...) or the
        /// break-glass admin token.
        /// </summary>
        /// <param name="authorization">Authorization header.</param>
        /// <param name="sourceIp">Source IP.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Caller, or null when the credentials are missing or invalid.</returns>
        public async Task<PlatformCaller?> AuthenticateAsync(string? authorization, string? sourceIp, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(authorization) || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
            string presented = authorization.Substring(7).Trim();
            if (presented.Length == 0) return null;
            string expected = _Settings.Auth.AdminToken;
            if (!String.IsNullOrEmpty(expected) && FixedTimeEquals(presented, expected)) return new PlatformCaller { SourceIp = sourceIp };
            if (!presented.StartsWith(Constants.PlatformTokenPrefix, StringComparison.Ordinal)) return null;
            DateTime now = DateTime.UtcNow;
            PlatformSession? session = await _Database.PlatformAdmins.ReadActiveSessionAsync(TokenService.Hash(presented), now, token).ConfigureAwait(false);
            if (session == null) return null;
            PlatformAdmin? admin = await _Database.PlatformAdmins.ReadAsync(session.AdminId, token).ConfigureAwait(false);
            if (admin == null || admin.RemovedUtc != null) return null;
            if (session.LastUsedUtc < now.AddMinutes(-1)) await _Database.PlatformAdmins.TouchSessionAsync(session.Id, now, token).ConfigureAwait(false);
            return new PlatformCaller { Admin = admin, SessionId = session.Id, SourceIp = sourceIp };
        }

        /// <summary>
        /// Sign a platform administrator in.
        /// </summary>
        /// <param name="request">Email and password.</param>
        /// <param name="sourceIp">Source IP.</param>
        /// <param name="userAgent">User agent.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Session.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 401 InvalidCredentials, or 423 AccountLocked.</exception>
        public async Task<PlatformSessionResponse> LoginAsync(PlatformLoginRequest request, string? sourceIp, string? userAgent, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (String.IsNullOrWhiteSpace(request.Email) || String.IsNullOrEmpty(request.Password))
                throw ApiErrorException.Validation(new Dictionary<string, string> { { "email", "Enter your email and password." } });
            DateTime now = DateTime.UtcNow;
            PlatformAdmin? admin = await _Database.PlatformAdmins.ReadByEmailAsync(request.Email.Trim(), token).ConfigureAwait(false);
            if (admin != null && admin.LockedUntilUtc != null && admin.LockedUntilUtc > now)
                throw new ApiErrorException(423, "AccountLocked", "Too many failed sign-ins. Try again later.");
            if (admin == null || admin.PasswordHash == null || !_Hasher.Verify(request.Password, admin.PasswordHash, out bool _))
            {
                if (admin != null)
                {
                    admin.FailedLoginCount++;
                    if (admin.FailedLoginCount >= _MaxFailedLogins)
                    {
                        admin.LockedUntilUtc = now.AddMinutes(_LockoutMinutes);
                        admin.FailedLoginCount = 0;
                    }
                    admin.UpdatedUtc = now;
                    await _Database.PlatformAdmins.UpdateAsync(admin, token).ConfigureAwait(false);
                }
                TrellisKitTelemetry.Increment(TrellisKitTelemetry.PlatformActions, "action", "login_failed");
                throw new ApiErrorException(401, "InvalidCredentials", "The email or password is incorrect.");
            }
            admin.FailedLoginCount = 0;
            admin.LockedUntilUtc = null;
            admin.LastLoginUtc = now;
            admin.UpdatedUtc = now;
            await _Database.PlatformAdmins.UpdateAsync(admin, token).ConfigureAwait(false);
            string bearer = Constants.PlatformTokenPrefix + TokenService.NewOpaqueToken();
            PlatformSession session = new PlatformSession
            {
                Id = IdGenerator.Generate(Constants.PlatformSessionIdPrefix),
                AdminId = admin.Id,
                TokenHash = TokenService.Hash(bearer),
                SourceIp = sourceIp,
                UserAgent = userAgent,
                CreatedUtc = now,
                LastUsedUtc = now,
                ExpiresUtc = now.AddMinutes(_Settings.Platform.SessionMinutes)
            };
            await _Database.PlatformAdmins.CreateSessionAsync(session, token).ConfigureAwait(false);
            PlatformCaller caller = new PlatformCaller { Admin = admin, SessionId = session.Id, SourceIp = sourceIp };
            await AuditAsync(caller, "platform.login", "PlatformAdmin", admin.Id, null, token).ConfigureAwait(false);
            return new PlatformSessionResponse { AccessToken = bearer, ExpiresUtc = session.ExpiresUtc, Admin = await AdminViewAsync(admin, token).ConfigureAwait(false) };
        }

        /// <summary>
        /// Sign out (revoke the caller's platform session).
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task LogoutAsync(PlatformCaller caller, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(caller);
            if (caller.SessionId != null) await _Database.PlatformAdmins.RevokeSessionAsync(caller.SessionId, DateTime.UtcNow, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Accept an invitation: set the password of the invited platform administrator.
        /// </summary>
        /// <param name="request">Token and password.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ApiErrorException">400 InvalidToken or 400 ValidationFailed.</exception>
        public async Task AcceptInviteAsync(PlatformAcceptInviteRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (String.IsNullOrWhiteSpace(request.Token)) throw new ApiErrorException(400, "InvalidToken", "The invitation is invalid or has expired.");
            PlatformAdmin? admin = await _Database.PlatformAdmins.ReadByInviteTokenHashAsync(TokenService.Hash(request.Token.Trim()), token).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            if (admin == null || admin.InviteExpiresUtc == null || admin.InviteExpiresUtc <= now) throw new ApiErrorException(400, "InvalidToken", "The invitation is invalid or has expired.");
            string? problem = Validators.Password(request.Password, admin.Email);
            if (problem != null) throw ApiErrorException.Field("password", problem);
            admin.PasswordHash = _Hasher.Hash(request.Password);
            admin.InviteTokenHash = null;
            admin.InviteExpiresUtc = null;
            admin.UpdatedUtc = now;
            await _Database.PlatformAdmins.UpdateAsync(admin, token).ConfigureAwait(false);
            await AuditAsync(new PlatformCaller { Admin = admin }, "platform.admin.accept", "PlatformAdmin", admin.Id, null, token).ConfigureAwait(false);
        }

        /// <summary>
        /// The caller's platform administrator view.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden for the break-glass token.</exception>
        public Task<PlatformAdminResponse> MeAsync(PlatformCaller caller, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(caller);
            if (caller.Admin == null) throw ApiErrorException.Forbidden("The break-glass token has no platform administrator account.");
            return AdminViewAsync(caller.Admin, token);
        }

        /// <summary>
        /// Enumerate platform administrators.
        /// </summary>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        public async Task<EnumerationResult<PlatformAdminResponse>> EnumerateAdminsAsync(EnumerationQuery? query, CancellationToken token = default)
        {
            EnumerationRequest request = _Enumeration.Prepare(query, PlatformCollections.PlatformAdmins, true);
            EnumerationResult<PlatformAdmin> page = await _Database.PlatformAdmins.EnumerateAsync(request, token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> metadata = await _Metadata.LoadAsync(TargetTypeEnum.PlatformAdmin, page.Objects.Select(a => a.Id), true, token).ConfigureAwait(false);
            return page.With(page.Objects.Select(a => AdminView(a, metadata)).ToList());
        }

        /// <summary>
        /// Invite a platform administrator. The invitation is emailed through the deployment's email settings
        /// (email.provider, email.mailgun*, email.smtp*); when none is configured the token is returned once and logged.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="request">Email and display name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The administrator and invitation.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or 409 EmailInUse.</exception>
        public async Task<PlatformInviteResponse> InviteAdminAsync(PlatformCaller caller, PlatformAdminInviteRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(caller);
            ArgumentNullException.ThrowIfNull(request);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            string? emailProblem = Validators.Email(request.Email);
            if (emailProblem != null) errors["email"] = emailProblem;
            string? nameProblem = Validators.DisplayName(request.DisplayName);
            if (nameProblem != null) errors["displayName"] = nameProblem;
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            DateTime now = DateTime.UtcNow;
            string invite = TokenService.NewOpaqueToken();
            PlatformAdmin admin = new PlatformAdmin
            {
                Id = IdGenerator.Generate(Constants.PlatformAdminIdPrefix),
                Email = request.Email.Trim(),
                DisplayName = request.DisplayName.Trim(),
                InviteTokenHash = TokenService.Hash(invite),
                InviteExpiresUtc = now.AddDays(_Settings.Platform.InviteDays),
                CreatedUtc = now,
                UpdatedUtc = now
            };
            try
            {
                await _Database.PlatformAdmins.CreateAsync(admin, token).ConfigureAwait(false);
            }
            catch (DuplicateRecordException)
            {
                throw new ApiErrorException(409, "EmailInUse", "A platform administrator with that email already exists.");
            }
            await AuditAsync(caller, "platform.admin.invite", "PlatformAdmin", admin.Id, "Invited " + admin.Email, token).ConfigureAwait(false);
            bool emailed = await SendInviteAsync(admin, invite, caller.Admin?.DisplayName ?? "A platform administrator", token).ConfigureAwait(false);
            return new PlatformInviteResponse
            {
                Admin = await AdminViewAsync(admin, token).ConfigureAwait(false),
                InviteToken = emailed ? null : invite,
                InviteDelivery = emailed ? "Email" : "Log",
                InviteExpiresUtc = admin.InviteExpiresUtc.Value
            };
        }

        /// <summary>
        /// Remove a platform administrator and revoke their sessions.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="adminId">Administrator id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ApiErrorException">404 NotFound, 409 CannotRemoveOwnAdmin, or 409 LastPlatformAdmin.</exception>
        public async Task RemoveAdminAsync(PlatformCaller caller, string adminId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(caller);
            PlatformAdmin? admin = await _Database.PlatformAdmins.ReadAsync(adminId, token).ConfigureAwait(false);
            if (admin == null || admin.RemovedUtc != null) throw ApiErrorException.NotFound("Platform administrator");
            if (caller.Admin != null && caller.Admin.Id == admin.Id) throw new ApiErrorException(409, "CannotRemoveOwnAdmin", "You cannot remove yourself.");
            if (await _Database.PlatformAdmins.CountActiveAsync(token).ConfigureAwait(false) <= 1) throw new ApiErrorException(409, "LastPlatformAdmin", "The last platform administrator cannot be removed.");
            DateTime now = DateTime.UtcNow;
            admin.RemovedUtc = now;
            admin.InviteTokenHash = null;
            admin.UpdatedUtc = now;
            await _Database.PlatformAdmins.UpdateAsync(admin, token).ConfigureAwait(false);
            await _Database.PlatformAdmins.RevokeSessionsForAdminAsync(admin.Id, now, token).ConfigureAwait(false);
            await AuditAsync(caller, "platform.admin.remove", "PlatformAdmin", admin.Id, "Removed " + admin.Email, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Enumerate properties (never purged tombstones unless filtered by status Deleted).
        /// </summary>
        /// <param name="query">Query; filters status (PropertyStatusEnum) and host (exact domain).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed.</exception>
        public async Task<EnumerationResult<PropertyRecordResponse>> EnumeratePropertiesAsync(EnumerationQuery? query, CancellationToken token = default)
        {
            EnumerationRequest request = _Enumeration.Prepare(query, PlatformCollections.Properties, true);
            PropertyStatusEnum? status = null;
            string? rawStatus = request.Query.FilterString("status");
            if (rawStatus != null)
            {
                if (!Enum.TryParse(rawStatus, true, out PropertyStatusEnum parsed)) throw ApiErrorException.Field("filters.status", "Use Disabled, Enabled, Deleting, or Deleted.");
                status = parsed;
            }
            EnumerationResult<PropertyRecord> page = await _Database.Registry.EnumerateAsync(request, status, request.Query.FilterString("host"), token).ConfigureAwait(false);
            List<PropertyRecordResponse> views = new List<PropertyRecordResponse>();
            Dictionary<string, ObjectMetadata> metadata = await _Metadata.LoadAsync(TargetTypeEnum.Property, page.Objects.Select(p => p.Id), true, token).ConfigureAwait(false);
            foreach (PropertyRecord record in page.Objects) views.Add(await ViewAsync(record, metadata, token).ConfigureAwait(false));
            return page.With(views);
        }

        /// <summary>
        /// Read a property.
        /// </summary>
        /// <param name="propertyId">Property id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<PropertyRecordResponse> GetPropertyAsync(string propertyId, CancellationToken token = default)
        {
            PropertyRecord record = await RequireAsync(propertyId, token).ConfigureAwait(false);
            return await ViewAsync(record, null, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Register a property (status Disabled). The id is always generated; the document comes from the body or is
        /// cloned from another property (configuration only, never members or content).
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 404 NotFound, 409 PropertyLimitReached, DomainInUse, or AppIdInUse.</exception>
        public async Task<PropertyRecordResponse> CreatePropertyAsync(PlatformCaller caller, PropertyCreateRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(caller);
            ArgumentNullException.ThrowIfNull(request);
            if (await _Database.Registry.CountLiveAsync(token).ConfigureAwait(false) >= _Settings.Deployment.MaxProperties)
                throw new ApiErrorException(409, "PropertyLimitReached", "This deployment already has " + _Settings.Deployment.MaxProperties + " properties (deployment.maxProperties).");

            string? sourceDirectory = null;
            JsonObject document;
            if (!String.IsNullOrWhiteSpace(request.CloneFromPropertyId))
            {
                PropertyRecord source = await RequireAsync(request.CloneFromPropertyId.Trim(), token).ConfigureAwait(false);
                document = JsonNode.Parse(source.DocumentJson) as JsonObject ?? throw ApiErrorException.Field("cloneFromPropertyId", "The source property document is not an object.");
                sourceDirectory = source.SourceDirectory;
            }
            else if (request.Document != null && request.Document.Value.ValueKind == JsonValueKind.Object)
            {
                document = JsonNode.Parse(request.Document.Value.GetRawText()) as JsonObject ?? new JsonObject();
            }
            else throw ApiErrorException.Field("document", "Send a property.json document, or cloneFromPropertyId.");

            string id = PropertyIds.Generate();
            document["id"] = id;
            if (!String.IsNullOrWhiteSpace(request.Name)) document["name"] = request.Name.Trim();
            if (!String.IsNullOrWhiteSpace(request.Slug)) document["slug"] = request.Slug.Trim();
            else document.Remove("slug");
            string json = document.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

            PropertyConfiguration configuration = ParseAndValidate(json);
            DateTime now = DateTime.UtcNow;
            List<PropertyDomain> domains = request.Domains == null ? PropertyDirectory.DomainsOf(configuration, now) : DomainsFrom(id, request.Domains, now);
            List<PropertyApp> apps = request.Apps == null ? PropertyDirectory.AppsOf(configuration, now) : AppsFrom(id, request.Apps, now);
            await CheckConflictsAsync(id, domains, apps, token).ConfigureAwait(false);
            if (await _Database.Registry.SlugInUseAsync(configuration.EffectiveSlug, token).ConfigureAwait(false))
                throw new ApiErrorException(409, "SlugInUse", "Another property already uses the slug " + configuration.EffectiveSlug + "; send a different slug.",
                    new Dictionary<string, string> { ["slug"] = "The slug " + configuration.EffectiveSlug + " is in use." });
            PropertyRecord record = new PropertyRecord
            {
                Id = id,
                Slug = configuration.EffectiveSlug,
                Name = configuration.Name,
                Status = PropertyStatusEnum.Disabled,
                DocumentJson = json,
                SourceDirectory = sourceDirectory,
                CreatedUtc = now,
                UpdatedUtc = now
            };
            try
            {
                await _Database.Registry.CreateAsync(record, domains, apps, token).ConfigureAwait(false);
            }
            catch (DuplicateRecordException)
            {
                throw new ApiErrorException(409, "PropertyIdInUse", "The slug, a domain, or an app id is already registered in this deployment.");
            }
            await WriteMetadataAsync(TargetTypeEnum.Property, id, request.Labels, request.Tags, token).ConfigureAwait(false);
            await AuditAsync(caller, "platform.property.create", "Property", id, "Created " + record.Name + (request.CloneFromPropertyId == null ? String.Empty : " from " + request.CloneFromPropertyId), token).ConfigureAwait(false);
            await _Directory.ReloadAsync(token).ConfigureAwait(false);
            return await ViewAsync(record, null, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Update a property's name, domains, apps, labels, or tags.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="propertyId">Property id.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 404 NotFound, 409 DomainInUse or AppIdInUse.</exception>
        public async Task<PropertyRecordResponse> UpdatePropertyAsync(PlatformCaller caller, string propertyId, PropertyUpdateRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(caller);
            ArgumentNullException.ThrowIfNull(request);
            PropertyRecord record = await RequireLiveAsync(propertyId, token).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            List<string> changed = new List<string>();
            if (!String.IsNullOrWhiteSpace(request.Name) && request.Name.Trim() != record.Name)
            {
                JsonObject document = JsonNode.Parse(record.DocumentJson) as JsonObject ?? new JsonObject();
                document["name"] = request.Name.Trim();
                string json = document.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                ParseAndValidate(json);
                record.Name = request.Name.Trim();
                record.DocumentJson = json;
                record.UpdatedUtc = now;
                await _Database.Registry.UpdateAsync(record, token).ConfigureAwait(false);
                changed.Add("name");
            }
            List<PropertyDomain>? domains = request.Domains == null ? null : DomainsFrom(record.Id, request.Domains, now);
            List<PropertyApp>? apps = request.Apps == null ? null : AppsFrom(record.Id, request.Apps, now);
            if (domains != null || apps != null)
            {
                await CheckConflictsAsync(record.Id, domains ?? new List<PropertyDomain>(), apps ?? new List<PropertyApp>(), token).ConfigureAwait(false);
                await _Database.Registry.ReplaceDomainsAndAppsAsync(record.Id, domains, apps, now, token).ConfigureAwait(false);
                if (domains != null) changed.Add("domains");
                if (apps != null) changed.Add("apps");
            }
            if (request.Labels != null || request.Tags != null)
            {
                await WriteMetadataAsync(TargetTypeEnum.Property, record.Id, request.Labels, request.Tags, token).ConfigureAwait(false);
                changed.Add("metadata");
            }
            if (changed.Count > 0)
            {
                await AuditAsync(caller, "platform.property.update", "Property", record.Id, "Changed " + String.Join(", ", changed), token).ConfigureAwait(false);
                await _Directory.ReloadAsync(token).ConfigureAwait(false);
            }
            return await GetPropertyAsync(record.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Enable a property (readiness: a valid document and at least one Web domain).
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="propertyId">Property id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        /// <exception cref="ApiErrorException">404 NotFound or 409 PropertyNotReady.</exception>
        public async Task<PropertyRecordResponse> EnableAsync(PlatformCaller caller, string propertyId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(caller);
            PropertyRecord record = await RequireLiveAsync(propertyId, token).ConfigureAwait(false);
            if (record.Status == PropertyStatusEnum.Deleting) throw new ApiErrorException(409, "PropertyNotReady", "Restore the property before enabling it.");
            Dictionary<string, string> problems = new Dictionary<string, string>();
            try
            {
                ParseAndValidate(record.DocumentJson);
            }
            catch (ApiErrorException)
            {
                problems["document"] = "The property document does not validate.";
            }
            List<PropertyDomain> domains = await _Database.Registry.ReadDomainsAsync(record.Id, token).ConfigureAwait(false);
            if (!domains.Any(d => d.Kind == DomainKindEnum.Web)) problems["domains"] = "Add at least one Web domain.";
            if (problems.Count > 0) throw new ApiErrorException(409, "PropertyNotReady", "The property is not ready to be enabled.", problems);
            DateTime now = DateTime.UtcNow;
            record.Status = PropertyStatusEnum.Enabled;
            record.EnabledUtc = now;
            record.DisabledReason = null;
            record.UpdatedUtc = now;
            await _Database.Registry.UpdateAsync(record, token).ConfigureAwait(false);
            await AuditAsync(caller, "platform.property.enable", "Property", record.Id, null, token).ConfigureAwait(false);
            await _Directory.ReloadAsync(token).ConfigureAwait(false);
            return await GetPropertyAsync(record.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Disable a property: its requests resolve to 404 PropertyNotFound within deployment.propertyCacheSeconds;
        /// sessions are kept so enabling again restores them.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="propertyId">Property id.</param>
        /// <param name="reason">Reason, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<PropertyRecordResponse> DisableAsync(PlatformCaller caller, string propertyId, string? reason, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(caller);
            PropertyRecord record = await RequireLiveAsync(propertyId, token).ConfigureAwait(false);
            if (record.Status == PropertyStatusEnum.Deleting) throw new ApiErrorException(409, "PropertyNotReady", "The property is being deleted; restore it first.");
            DateTime now = DateTime.UtcNow;
            record.Status = PropertyStatusEnum.Disabled;
            record.DisabledUtc = now;
            record.DisabledReason = String.IsNullOrWhiteSpace(reason) ? null : Truncate(reason.Trim(), 500);
            record.UpdatedUtc = now;
            await _Database.Registry.UpdateAsync(record, token).ConfigureAwait(false);
            await AuditAsync(caller, "platform.property.disable", "Property", record.Id, record.DisabledReason, token).ConfigureAwait(false);
            await _Directory.ReloadAsync(token).ConfigureAwait(false);
            await RevokeRealtimeAsync(record.Id, token).ConfigureAwait(false);
            return await GetPropertyAsync(record.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Delete a property: it is disabled immediately and restorable until platform.propertyPurgeDelayHours (30
        /// days by default) pass, then <see cref="Workers.PropertyPurgeWorker"/> purges all of its data and objects.
        /// The id is never reused.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="propertyId">Property id.</param>
        /// <param name="confirm">Typed confirmation (the property id).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed without the confirmation, 404 NotFound, 409 PropertyNotReady for the folder property.</exception>
        public async Task<PropertyRecordResponse> DeleteAsync(PlatformCaller caller, string propertyId, string? confirm, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(caller);
            PropertyRecord record = await RequireLiveAsync(propertyId, token).ConfigureAwait(false);
            if (!String.Equals(confirm, record.Id, StringComparison.Ordinal)) throw ApiErrorException.Field("confirm", "Type the property id to confirm.");
            if (record.Status == PropertyStatusEnum.Deleting) return await GetPropertyAsync(record.Id, token).ConfigureAwait(false);
            if (_Directory.Default != null && _Directory.Default.Id == record.Id)
                throw new ApiErrorException(409, "PropertyNotReady", "The property loaded from property.directory cannot be deleted while the server is configured with it.");
            DateTime now = DateTime.UtcNow;
            if (record.DisabledUtc == null || record.Status == PropertyStatusEnum.Enabled) record.DisabledUtc = now;
            record.Status = PropertyStatusEnum.Deleting;
            record.DeleteScheduledUtc = now.AddHours(_Settings.Platform.PropertyPurgeDelayHours);
            record.UpdatedUtc = now;
            await _Database.Registry.UpdateAsync(record, token).ConfigureAwait(false);
            await AuditAsync(caller, "platform.property.delete", "Property", record.Id, "Purge scheduled " + record.DeleteScheduledUtc.Value.ToString("O", System.Globalization.CultureInfo.InvariantCulture), token).ConfigureAwait(false);
            await _Directory.ReloadAsync(token).ConfigureAwait(false);
            await RevokeRealtimeAsync(record.Id, token).ConfigureAwait(false);
            return await GetPropertyAsync(record.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Restore a property being deleted (back to Disabled) while its purge has not started.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="propertyId">Property id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        /// <exception cref="ApiErrorException">404 NotFound or 409 PropertyNotRestorable.</exception>
        public async Task<PropertyRecordResponse> RestoreAsync(PlatformCaller caller, string propertyId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(caller);
            PropertyRecord record = await RequireAsync(propertyId, token).ConfigureAwait(false);
            if (record.Status != PropertyStatusEnum.Deleting || record.PurgeStartedUtc != null)
                throw new ApiErrorException(409, "PropertyNotRestorable", "Only a property being deleted, before its purge starts, can be restored.");
            DateTime now = DateTime.UtcNow;
            record.Status = PropertyStatusEnum.Disabled;
            record.DeleteScheduledUtc = null;
            record.UpdatedUtc = now;
            await _Database.Registry.UpdateAsync(record, token).ConfigureAwait(false);
            await AuditAsync(caller, "platform.property.restore", "Property", record.Id, null, token).ConfigureAwait(false);
            await _Directory.ReloadAsync(token).ConfigureAwait(false);
            return await GetPropertyAsync(record.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Enumerate the members annotated as SystemAdmin of a property.
        /// </summary>
        /// <param name="propertyId">Property id.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of member summaries.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<EnumerationResult<UserSummary>> EnumeratePropertyAdminsAsync(string propertyId, EnumerationQuery? query, CancellationToken token = default)
        {
            PropertyRuntime runtime = RequireRuntime(propertyId);
            EnumerationRequest request = runtime.Services.Enumeration.Prepare(query, PhaseOneCollections.AdminMembers, true, "platform-admins");
            EnumerationResult<User> page = await runtime.Services.Database.Users.EnumerateAsync(request, new UserFilter { Role = RoleEnum.SystemAdmin }, DateTime.UtcNow, token).ConfigureAwait(false);
            return page.With(page.Objects.Select(u => runtime.Services.Summaries.From(u, true)).ToList());
        }

        /// <summary>
        /// Annotate a member of a property as that property's SystemAdmin (or remove the annotation).
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="propertyId">Property id.</param>
        /// <param name="userId">Member id in that property.</param>
        /// <param name="grant">True to grant, false to revoke.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The member summary.</returns>
        /// <exception cref="ApiErrorException">404 NotFound (unknown property, or a member id that is not in that property).</exception>
        public async Task<UserSummary> SetPropertyAdminAsync(PlatformCaller caller, string propertyId, string userId, bool grant, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(caller);
            PropertyRuntime runtime = RequireRuntime(propertyId);
            if (String.IsNullOrWhiteSpace(userId)) throw ApiErrorException.Field("userId", "Enter a member id.");
            User? user = await runtime.Services.Database.Users.ReadAsync(userId.Trim(), token).ConfigureAwait(false);
            if (user == null || user.IsDeleted) throw ApiErrorException.NotFound("Member");
            List<RoleAssignment> roles = await runtime.Services.Database.RoleAssignments.EnumerateByUserAsync(user.Id, token).ConfigureAwait(false);
            bool has = roles.Any(r => r.Role == RoleEnum.SystemAdmin);
            if (grant && !has)
            {
                await runtime.Services.Database.RoleAssignments.AddAsync(new RoleAssignment
                {
                    Id = IdGenerator.GenerateRoleAssignmentId(),
                    UserId = user.Id,
                    Role = RoleEnum.SystemAdmin,
                    ResourceType = ResourceTypeEnum.Global,
                    ResourceId = "*",
                    CreatedUtc = DateTime.UtcNow
                }, token).ConfigureAwait(false);
            }
            else if (!grant && has)
            {
                await runtime.Services.Database.RoleAssignments.ReplaceForUserAsync(user.Id, roles.Where(r => r.Role != RoleEnum.SystemAdmin).ToList(), token).ConfigureAwait(false);
            }
            runtime.Services.Tokens.InvalidateUser(user.Id);
            string action = grant ? "platform.property.admin.grant" : "platform.property.admin.revoke";
            await AuditAsync(caller, action, "User", user.Id, "Property " + runtime.Id, token).ConfigureAwait(false);
            await runtime.Services.Audit.WriteAsync(DelegatedContext(caller, runtime.Id), caller.SourceIp, grant ? "member.role.grant" : "member.role.revoke", "User", user.Id, "SystemAdmin (by " + caller.ActorName + ")", token).ConfigureAwait(false);
            return runtime.Services.Summaries.From(user, true);
        }

        /// <summary>
        /// Start a delegated session: the caller acts as SystemAdmin inside one property for
        /// platform.delegatedSessionMinutes (not refreshable), audited in the platform audit and the property's audit.
        /// </summary>
        /// <param name="caller">Caller (a platform administrator; the break-glass token already acts as SystemAdmin in any property).</param>
        /// <param name="propertyId">Property id.</param>
        /// <param name="userAgent">User agent.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The property-bound access token.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden for the break-glass token, 404 NotFound.</exception>
        public async Task<DelegatedSessionResponse> CreateDelegatedSessionAsync(PlatformCaller caller, string propertyId, string? userAgent, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(caller);
            if (caller.Admin == null) throw ApiErrorException.Forbidden("The break-glass token already acts as SystemAdmin in every property; delegated sessions are for platform administrators.");
            PropertyRuntime runtime = RequireRuntime(propertyId);
            DateTime now = DateTime.UtcNow;
            AuthSession session = new AuthSession
            {
                Id = IdGenerator.GenerateSessionId(),
                UserId = caller.Admin.Id,
                PrincipalKind = PrincipalKindEnum.Delegated,
                ClientType = ClientTypeEnum.Web,
                SourceIp = caller.SourceIp,
                UserAgent = userAgent,
                CreatedUtc = now,
                LastUsedUtc = now,
                ExpiresUtc = now.AddMinutes(_Settings.Platform.DelegatedSessionMinutes)
            };
            await runtime.Services.Database.Sessions.CreateAsync(session, token).ConfigureAwait(false);
            IssuedAccessToken issued = runtime.Services.Tokens.IssueAccessToken(session, now);
            await AuditAsync(caller, "platform.delegate", "Property", runtime.Id, "Delegated session " + session.Id + " until " + session.ExpiresUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture), token).ConfigureAwait(false);
            await runtime.Services.Audit.WriteAsync(DelegatedContext(caller, runtime.Id), caller.SourceIp, "platform.delegate", "Session", session.Id, "Delegated SystemAdmin session for " + caller.ActorName, token).ConfigureAwait(false);
            return new DelegatedSessionResponse { AccessToken = issued.Token, AccessTokenExpiresUtc = issued.ExpiresUtc, PropertyId = runtime.Id };
        }

        /// <summary>
        /// Enumerate platform audit records (default the platform realm, or a property's audit with filters.propertyId).
        /// </summary>
        /// <param name="query">Query; filters propertyId, actorUserId, action, targetType, targetId.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 404 NotFound.</exception>
        public async Task<EnumerationResult<AuditRecord>> EnumerateAuditAsync(EnumerationQuery? query, CancellationToken token = default)
        {
            EnumerationRequest request = _Enumeration.Prepare(query, PlatformCollections.Audit, true);
            string? propertyId = request.Query.FilterString("propertyId");
            DatabaseDriverBase source = _Realm;
            if (!String.IsNullOrEmpty(propertyId) && propertyId != PropertyIds.Platform)
            {
                PropertyRecord? record = await _Database.Registry.ReadAsync(propertyId, token).ConfigureAwait(false);
                if (record == null) throw ApiErrorException.NotFound("Property");
                source = _Database.ForProperty(record.Id);
            }
            AuditFilter filter = new AuditFilter
            {
                ActorUserId = request.Query.FilterString("actorUserId"),
                Action = request.Query.FilterString("action"),
                TargetType = request.Query.FilterString("targetType"),
                TargetId = request.Query.FilterString("targetId")
            };
            return await source.Audit.EnumerateAsync(request, filter, token).ConfigureAwait(false);
        }

        /// <summary>
        /// GET /api/v1/platform/request-history/{id}: one captured request of one property (redacted at capture, as in
        /// the property request history), with its labels and tags.
        /// </summary>
        /// <param name="propertyId">Property id, platform (default when empty), or unresolved.</param>
        /// <param name="id">Entry id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Entry.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<RequestHistoryEntry> ReadRequestHistoryAsync(string? propertyId, string id, CancellationToken token = default)
        {
            string scope = String.IsNullOrWhiteSpace(propertyId) ? PropertyIds.Platform : propertyId.Trim();
            if (scope != PropertyIds.Platform && scope != PropertyIds.Unresolved)
            {
                PropertyRecord? record = PropertyIds.IsValid(scope) ? await _Database.Registry.ReadAsync(scope, token).ConfigureAwait(false) : null;
                if (record == null) throw ApiErrorException.NotFound("Property");
            }
            RequestHistoryEntry? entry = String.IsNullOrWhiteSpace(id) ? null : await _Database.ForProperty(scope).RequestHistory.ReadAsync(id, token).ConfigureAwait(false);
            if (entry == null) throw ApiErrorException.NotFound("Request history entry");
            PropertyRuntime? runtime = _Directory.Find(scope);
            if (runtime != null) await runtime.Services.Metadata.AttachOneAsync(TargetTypeEnum.RequestHistory, entry, entry.Id, true, token).ConfigureAwait(false);
            return entry;
        }

        /// <summary>
        /// POST /api/v1/platform/request-history/enumerate: one property's request history, or the platform realm's
        /// (default) or unresolved requests (filters.propertyId platform or unresolved), with the request history filters.
        /// </summary>
        /// <param name="query">Query; filters propertyId, method, statusCode, pathContains, userId.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 404 NotFound.</exception>
        public async Task<EnumerationResult<RequestHistoryListItem>> EnumerateRequestHistoryAsync(EnumerationQuery? query, CancellationToken token = default)
        {
            EnumerationRequest request = _Enumeration.Prepare(query, PlatformCollections.RequestHistory, true);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            string propertyId = request.Query.FilterString("propertyId") ?? PropertyIds.Platform;
            DatabaseDriverBase source;
            if (propertyId == PropertyIds.Platform || propertyId == PropertyIds.Unresolved) source = _Database.ForProperty(propertyId);
            else
            {
                PropertyRecord? record = PropertyIds.IsValid(propertyId) ? await _Database.Registry.ReadAsync(propertyId, token).ConfigureAwait(false) : null;
                if (record == null) throw ApiErrorException.NotFound("Property");
                source = _Database.ForProperty(record.Id);
            }
            RequestHistoryFilter filter = new RequestHistoryFilter
            {
                Method = request.Query.FilterString("method"),
                PathContains = request.Query.FilterString("pathContains"),
                UserId = request.Query.FilterString("userId")
            };
            long? status = request.Query.FilterLong("statusCode", errors);
            if (status != null) filter.StatusCode = (int)Math.Clamp(status.Value, 0, 999);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors, "One or more filters are invalid.");
            if (!_Settings.RequestHistory.Enabled)
                return new EnumerationResult<RequestHistoryListItem> { MaxResults = request.MaxResults, Skip = request.Skip, EndOfResults = true, TotalRecords = 0, RecordsRemaining = 0 };
            EnumerationResult<RequestHistoryEntry> page = await source.RequestHistory.EnumerateAsync(request, filter, token).ConfigureAwait(false);
            return page.With(page.Objects.Select(RequestHistoryListItem.FromModel).ToList());
        }

        /// <summary>
        /// Most registry rows (live properties plus tombstones) a computed property metrics ordering reads. 500.
        /// </summary>
        public const int MetricsScanLimit = 500;

        /// <summary>
        /// POST /api/v1/platform/metrics/properties/enumerate: members, active members (30 days), requests and server
        /// errors in the last 24 hours, and stored media bytes per property (bounded by deployment.maxProperties).
        /// </summary>
        /// <param name="query">Query; filter status.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken.</exception>
        public async Task<EnumerationResult<PropertyMetricsItem>> EnumeratePropertyMetricsAsync(EnumerationQuery? query, CancellationToken token = default)
        {
            EnumerationRequest request = _Enumeration.Prepare(query, PlatformCollections.PropertyMetrics, true);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            PropertyStatusEnum? status = request.Query.FilterEnum<PropertyStatusEnum>("status", errors);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors, "One or more filters are invalid.");
            string ordering = request.Ordering.Name;
            bool computed = ordering == PlatformCollections.MetricsMembersDescending || ordering == PlatformCollections.MetricsErrorsDescending || ordering == PlatformCollections.MetricsStorageDescending;
            EnumerationRequest source = request;
            if (computed)
            {
                // Computed orderings: metrics of every matching registry row (at most MetricsScanLimit), sorted in
                // memory, paged with standard keyset cursors.
                source = new EnumerationRequest
                {
                    Collection = request.Collection,
                    Query = request.Query,
                    MaxResults = MetricsScanLimit,
                    Skip = 0,
                    Ordering = new EnumerationOrdering { Name = "CreatedDescending", Expression = "p.createdutc", Descending = true, KeyKind = EnumerationKeyKindEnum.Time },
                    Fingerprint = request.Fingerprint,
                    CountTotals = true,
                    IncludeStaffMetadata = request.IncludeStaffMetadata
                };
            }
            EnumerationResult<PropertyRecord> page = await _Database.Registry.EnumerateAsync(source, status, null, token).ConfigureAwait(false);
            if (computed && !page.EndOfResults) throw ApiErrorException.Field("search", "Narrow the property selection to at most " + MetricsScanLimit + " matching properties.");
            DateTime now = DateTime.UtcNow;
            List<PropertyMetricsItem> items = new List<PropertyMetricsItem>();
            Dictionary<string, ObjectMetadata> metadata = await _Metadata.LoadAsync(TargetTypeEnum.Property, page.Objects.Select(p => p.Id), true, token).ConfigureAwait(false);
            foreach (PropertyRecord record in page.Objects)
            {
                PropertyMetricsItem item = new PropertyMetricsItem { PropertyId = record.Id, Slug = record.Slug, Name = record.Name, Status = record.Status };
                if (record.Status != PropertyStatusEnum.Deleted)
                {
                    DatabaseDriverBase bound = _Database.ForProperty(record.Id);
                    item.Members = await _Database.Registry.CountMembersAsync(record.Id, token).ConfigureAwait(false);
                    item.ActiveMembers = await bound.Users.CountSeenSinceAsync(now.AddDays(-30), token).ConfigureAwait(false);
                    item.Requests24h = await bound.RequestHistory.CountSinceAsync(now.AddHours(-24), null, token).ConfigureAwait(false);
                    item.Errors24h = await bound.RequestHistory.CountSinceAsync(now.AddHours(-24), 500, token).ConfigureAwait(false);
                    item.StorageBytes = await bound.Media.TotalBytesAsync(token).ConfigureAwait(false);
                }
                if (metadata.TryGetValue(record.Id, out ObjectMetadata? meta))
                {
                    item.Labels = meta.Labels;
                    item.Tags = meta.Tags;
                }
                items.Add(item);
            }
            if (!computed) return page.With(items);
            Func<PropertyMetricsItem, long> key = ordering == PlatformCollections.MetricsMembersDescending ? i => i.Members : ordering == PlatformCollections.MetricsErrorsDescending ? i => i.Errors24h : i => i.StorageBytes;
            Dictionary<string, DateTime> created = page.Objects.ToDictionary(p => p.Id, p => p.CreatedUtc, StringComparer.Ordinal);
            return CatalogEnumeration.Page(request, items, item => item.PropertyId, item => created[item.PropertyId],
                item => item.PropertyId + " " + item.Slug + " " + item.Name, item => item.Labels, item => item.Tags,
                item => key(item).ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// The platform realm's metadata service (labels and tags of properties and platform administrators).
        /// </summary>
        public MetadataService RealmMetadata
        {
            get { return _Metadata; }
        }

        /// <summary>
        /// Write a platform audit record.
        /// </summary>
        /// <param name="caller">Caller, or null for the system.</param>
        /// <param name="action">Action (platform.*).</param>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetId">Target id.</param>
        /// <param name="summary">Summary (never secrets).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task AuditAsync(PlatformCaller? caller, string action, string? targetType, string? targetId, string? summary, CancellationToken token = default)
        {
            await _Realm.Audit.CreateAsync(new AuditRecord
            {
                Id = IdGenerator.GenerateAuditRecordId(),
                ActorUserId = caller?.ActorId,
                ActorName = caller == null ? "system" : caller.ActorName,
                Action = action,
                TargetType = targetType,
                TargetId = targetId,
                Summary = summary == null ? null : Truncate(summary, 1000),
                SourceIp = caller?.SourceIp,
                CreatedUtc = DateTime.UtcNow
            }, token).ConfigureAwait(false);
            TrellisKitTelemetry.Increment(TrellisKitTelemetry.PlatformActions, "action", action.StartsWith("platform.", StringComparison.Ordinal) ? action.Substring(9) : action);
        }

        /// <summary>
        /// The deployment-level sender of platform mail: Mailgun (email.mailgunDomain and email.mailgunApiKey) or SMTP
        /// (email.smtpHost), per email.provider; null when none is configured.
        /// </summary>
        /// <returns>Sender, or null.</returns>
        public IEmailSender? PlatformSender()
        {
            EmailSettings e = _Settings.Email;
            if (e.Provider == EmailProviderEnum.Log) return null;
            bool mailgun = !String.IsNullOrWhiteSpace(e.MailgunDomain) && !String.IsNullOrWhiteSpace(e.MailgunApiKey) && !String.IsNullOrWhiteSpace(e.FromAddress);
            if (mailgun && e.Provider != EmailProviderEnum.Smtp) return new MailgunEmailSender(e.MailgunDomain, e.MailgunApiKey, e.MailgunBaseUrl, String.IsNullOrWhiteSpace(e.FromName) ? PlatformName : e.FromName, e.FromAddress);
            if (!String.IsNullOrWhiteSpace(e.SmtpHost) && e.Provider != EmailProviderEnum.Mailgun) return PlatformSmtp ?? new SmtpEmailSender(e, PlatformName, e.FromAddress);
            return null;
        }

        /// <summary>
        /// The platform view of a domain, with the DNS challenge for domains a property administrator added.
        /// </summary>
        /// <param name="d">Domain.</param>
        /// <returns>View.</returns>
        public static PlatformDomainResponse DomainView(PropertyDomain d)
        {
            ArgumentNullException.ThrowIfNull(d);
            return new PlatformDomainResponse
            {
                Id = d.Id,
                Host = d.Host,
                Kind = d.Kind,
                VerifiedUtc = d.VerifiedUtc,
                Source = d.Source,
                Verification = d.VerificationToken == null ? null : new DomainVerificationView
                {
                    RecordName = VerificationRecordPrefix + d.Host,
                    RecordValue = VerificationValuePrefix + d.VerificationToken,
                    LastCheckedUtc = d.VerificationCheckedUtc,
                    Error = d.VerificationError
                }
            };
        }

        /// <summary>
        /// GET /api/v1/admin/property/domains: the resolved property's domains and apps.
        /// </summary>
        /// <param name="propertyId">Property id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Domains and apps.</returns>
        public async Task<PropertyDomainsResponse> PropertyDomainsAsync(string propertyId, CancellationToken token = default)
        {
            List<PropertyDomain> domains = await _Database.Registry.ReadDomainsAsync(propertyId, token).ConfigureAwait(false);
            List<PropertyApp> apps = await _Database.Registry.ReadAppsAsync(propertyId, token).ConfigureAwait(false);
            return new PropertyDomainsResponse
            {
                PropertyId = propertyId,
                Domains = domains.Select(DomainView).ToList(),
                Apps = apps.Select(a => new PlatformAppResponse { Id = a.Id, Platform = a.Platform, AppId = a.AppId, StoreAppId = a.StoreAppId, Enabled = a.Enabled }).ToList()
            };
        }

        /// <summary>
        /// PUT /api/v1/admin/property/domains (resolved decision 6): a property administrator replaces the domain and app
        /// lists. Hosts and app ids stay unique across the deployment (409 DomainInUse, AppIdInUse). Unchanged hosts keep
        /// their state; new or changed hosts start unverified with a DNS TXT challenge and are not routed until verified.
        /// Audited property.domains.update in the property's audit.
        /// </summary>
        /// <param name="services">The property's services.</param>
        /// <param name="request">Lists.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Domains and apps.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 409 DomainInUse or AppIdInUse.</exception>
        public async Task<PropertyDomainsResponse> UpdatePropertyDomainsAsync(ApplicationServices services, PropertyDomainsUpdateRequest request, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(info);
            string propertyId = services.PropertyId;
            DateTime now = DateTime.UtcNow;
            List<PropertyDomain> current = await _Database.Registry.ReadDomainsAsync(propertyId, token).ConfigureAwait(false);
            List<PropertyDomain>? domains = null;
            if (request.Domains != null)
            {
                domains = DomainsFrom(propertyId, request.Domains, now);
                foreach (PropertyDomain d in domains)
                {
                    PropertyDomain? same = current.FirstOrDefault(c => c.Host == d.Host);
                    if (same != null)
                    {
                        d.Id = same.Id;
                        d.VerifiedUtc = same.VerifiedUtc;
                        d.Source = same.Source;
                        d.VerificationToken = same.VerificationToken;
                        d.VerificationCheckedUtc = same.VerificationCheckedUtc;
                        d.VerificationError = same.VerificationError;
                        d.CreatedUtc = same.CreatedUtc;
                        continue;
                    }
                    d.VerifiedUtc = null;
                    d.Source = "Property";
                    d.VerificationToken = TokenService.NewOpaqueToken().Substring(0, 32);
                }
            }
            List<PropertyApp>? apps = request.Apps == null ? null : AppsFrom(propertyId, request.Apps, now);
            if (domains == null && apps == null) return await PropertyDomainsAsync(propertyId, token).ConfigureAwait(false);
            await CheckConflictsAsync(propertyId, domains ?? new List<PropertyDomain>(), apps ?? new List<PropertyApp>(), token).ConfigureAwait(false);
            try
            {
                await _Database.Registry.ReplaceDomainsAndAppsAsync(propertyId, domains, apps, now, token).ConfigureAwait(false);
            }
            catch (DuplicateRecordException)
            {
                throw new ApiErrorException(409, domains != null ? "DomainInUse" : "AppIdInUse", "A host or app id is registered to another property.",
                    new Dictionary<string, string> { { domains != null ? "domains" : "apps", "A host or app id is registered to another property." } });
            }
            List<string> changed = new List<string>();
            if (domains != null) changed.Add("domains (" + String.Join(", ", domains.Select(d => d.Host)) + ")");
            if (apps != null) changed.Add("apps");
            await services.Audit.WriteAsync(info, "property.domains.update", "Property", propertyId, "Changed " + String.Join(", ", changed), token).ConfigureAwait(false);
            await AuditAsync(null, "platform.property.update", "Property", propertyId, "Property administrator changed " + String.Join(", ", changed), token).ConfigureAwait(false);
            await _Directory.ReloadAsync(token).ConfigureAwait(false);
            return await PropertyDomainsAsync(propertyId, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/property/domains/{domainId}/verify: look up the TXT record and mark the domain verified when
        /// it carries the challenge value. The result (and any error) is stored either way. Audited property.domain.verify.
        /// </summary>
        /// <param name="services">The property's services.</param>
        /// <param name="domainId">Domain id.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The domain.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<PlatformDomainResponse> VerifyDomainAsync(ApplicationServices services, string domainId, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(info);
            List<PropertyDomain> domains = await _Database.Registry.ReadDomainsAsync(services.PropertyId, token).ConfigureAwait(false);
            PropertyDomain domain = domains.FirstOrDefault(d => d.Id == domainId) ?? throw ApiErrorException.NotFound("Domain");
            if (domain.VerifiedUtc != null) return DomainView(domain);
            DateTime now = DateTime.UtcNow;
            domain.VerificationCheckedUtc = now;
            string outcome;
            try
            {
                List<string> values = await services.Integrations.TxtResolver.ResolveTxtAsync(VerificationRecordPrefix + domain.Host, token).ConfigureAwait(false);
                if (domain.VerificationToken != null && values.Any(v => String.Equals(v.Trim(), VerificationValuePrefix + domain.VerificationToken, StringComparison.Ordinal)))
                {
                    domain.VerifiedUtc = now;
                    domain.VerificationError = null;
                    outcome = "verified";
                }
                else
                {
                    domain.VerificationError = values.Count == 0 ? "No TXT record found at " + VerificationRecordPrefix + domain.Host + "." : "The TXT record does not contain the expected value.";
                    outcome = "not_found";
                }
            }
            catch (InvalidOperationException e)
            {
                domain.VerificationError = "The DNS lookup failed; try again later (" + e.Message + ").";
                outcome = "error";
            }
            await _Database.Registry.UpdateDomainVerificationAsync(domain, token).ConfigureAwait(false);
            TrellisKitTelemetry.Increment(TrellisKitTelemetry.DomainVerifications, "outcome", outcome);
            await services.Audit.WriteAsync(info, "property.domain.verify", "Property", services.PropertyId, domain.Host + ": " + outcome, token).ConfigureAwait(false);
            if (domain.VerifiedUtc != null) await _Directory.ReloadAsync(token).ConfigureAwait(false);
            return DomainView(domain);
        }

        #endregion

        #region Private-Methods

        private async Task RevokeRealtimeAsync(string propertyId, CancellationToken token)
        {
            // Every task closes the property's sockets with 4401 (docs/PHASE5_CONTRACT.md section 4.2 step 4).
            TrellisKit.Server.Services.Realtime.RealtimeEvent revoked = new TrellisKit.Server.Services.Realtime.RealtimeEvent
            {
                PropertyId = propertyId,
                Kind = TrellisKit.Server.Services.Realtime.RealtimeEventKinds.SessionRevoked,
                State = "PropertyDisabled"
            };
            try
            {
                _Directory.RealtimeSink?.DispatchLocal(revoked);
                if (_Directory.RealtimeBus != null) await _Directory.RealtimeBus.PublishAsync(revoked, token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _Logger.Warn("[PlatformService] realtime revocation for " + propertyId + " failed: " + e.Message);
            }
        }

        private static bool IsLocalHost(string host)
        {
            return host == "localhost"
                || host.EndsWith(".localhost", StringComparison.Ordinal)
                || host.StartsWith("127.", StringComparison.Ordinal)
                || host == "[::1]"
                || host == "::1";
        }

        private async Task<bool> SendInviteAsync(PlatformAdmin admin, string invite, string inviterName, CancellationToken token)
        {
            IEmailSender? sender = PlatformSender();
            if (sender == null || !sender.IsConfigured) return false;
            string baseUrl = PlatformBaseUrl(_Settings.Platform.Hosts.FirstOrDefault() ?? "localhost", _Settings.Web.PublicBaseUrl);
            Dictionary<string, string> tokens = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = PlatformName,
                ["memberName"] = admin.DisplayName,
                ["supportEmail"] = _Settings.Email.FromAddress,
                ["webUrl"] = baseUrl,
                ["link"] = baseUrl + "/platform/accept-invite?token=" + Uri.EscapeDataString(invite),
                ["inviterName"] = inviterName,
                ["expiresDays"] = _Settings.Platform.InviteDays.ToString(System.Globalization.CultureInfo.InvariantCulture)
            };
            TrellisKit.Server.Services.Email.EmailRendered rendered = TrellisKit.Server.Services.Email.EmailRenderer.Render(TrellisKit.Server.Services.Email.EmailTemplateCatalog.Get("platform_admin_invite"), null, tokens,
                new TrellisKit.Server.Services.Email.EmailBrand { Name = PlatformName, SupportEmail = _Settings.Email.FromAddress, WebUrl = baseUrl, PostalAddress = String.Empty });
            try
            {
                await sender.SendAsync(new EmailMessage { To = admin.Email, Subject = rendered.Subject, TextBody = rendered.Text, HtmlBody = rendered.Html, Category = "platform_admin_invite" }, token).ConfigureAwait(false);
                TrellisKitTelemetry.Increment(TrellisKitTelemetry.EmailSent, "template", "platform_admin_invite", "provider", sender is MailgunEmailSender ? "Mailgun" : "Smtp");
                return true;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                TrellisKitTelemetry.Add(TrellisKitTelemetry.EmailFailed, 1, "template", "platform_admin_invite", "provider", sender is MailgunEmailSender ? "Mailgun" : "Smtp", "final", "true");
                _Logger.Warn("[PlatformService] the invitation email to " + admin.Email + " failed: " + e.Message);
                return false;
            }
        }

        private async Task<PropertyRecord> RequireAsync(string propertyId, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(propertyId) || !PropertyIds.IsValid(propertyId.Trim())) throw ApiErrorException.NotFound("Property");
            PropertyRecord? record = await _Database.Registry.ReadAsync(propertyId.Trim(), token).ConfigureAwait(false);
            if (record == null) throw ApiErrorException.NotFound("Property");
            return record;
        }

        private async Task<PropertyRecord> RequireLiveAsync(string propertyId, CancellationToken token)
        {
            PropertyRecord record = await RequireAsync(propertyId, token).ConfigureAwait(false);
            if (record.Status == PropertyStatusEnum.Deleted) throw ApiErrorException.NotFound("Property");
            return record;
        }

        private PropertyRuntime RequireRuntime(string propertyId)
        {
            PropertyRuntime? runtime = _Directory.Find(propertyId);
            if (runtime == null || runtime.Record.Status == PropertyStatusEnum.Deleting || runtime.Record.Status == PropertyStatusEnum.Deleted) throw ApiErrorException.NotFound("Property");
            return runtime;
        }

        private static RequestContext DelegatedContext(PlatformCaller caller, string propertyId)
        {
            return new RequestContext
            {
                IsAuthenticated = true,
                IsAdmin = true,
                IsBreakGlass = caller.Admin == null,
                PrincipalKind = caller.Admin == null ? PrincipalKindEnum.BreakGlass : PrincipalKindEnum.Delegated,
                PlatformAdminId = caller.ActorId,
                PrincipalName = caller.ActorName,
                PropertyId = propertyId
            };
        }

        private static PropertyConfiguration ParseAndValidate(string json)
        {
            PropertyConfiguration configuration;
            try
            {
                configuration = PropertyLoader.Parse(json, "property document");
            }
            catch (PropertyConfigurationException e)
            {
                throw ApiErrorException.Field("document", e.Message);
            }
            List<string> problems = PropertyValidator.Validate(configuration);
            if (problems.Count > 0)
            {
                Dictionary<string, string> errors = new Dictionary<string, string>();
                for (int i = 0; i < problems.Count && i < 50; i++) errors["document[" + i + "]"] = problems[i];
                throw ApiErrorException.Validation(errors, "The property document is invalid.");
            }
            return configuration;
        }

        private static List<PropertyDomain> DomainsFrom(string propertyId, List<PropertyDomainRequest> requests, DateTime now)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>();
            if (requests.Count > _MaxDomains) errors["domains"] = "At most " + _MaxDomains + " domains.";
            List<PropertyDomain> domains = new List<PropertyDomain>();
            for (int i = 0; i < requests.Count && i < _MaxDomains; i++)
            {
                string host = PropertyDirectory.NormalizeHost(requests[i]?.Host);
                if (host.Length == 0 || host.Length > 253 || !System.Text.RegularExpressions.Regex.IsMatch(host, "^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)*$"))
                {
                    errors["domains[" + i + "].host"] = "Enter a host name such as www.example.test.";
                    continue;
                }
                if (domains.Any(d => d.Host == host))
                {
                    errors["domains[" + i + "].host"] = "Listed twice.";
                    continue;
                }
                domains.Add(new PropertyDomain { Id = IdGenerator.Generate(Constants.PropertyDomainIdPrefix), PropertyId = propertyId, Host = host, Kind = requests[i].Kind, VerifiedUtc = now, CreatedUtc = now });
            }
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            return domains;
        }

        private static List<PropertyApp> AppsFrom(string propertyId, List<PropertyAppRequest> requests, DateTime now)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>();
            if (requests.Count > _MaxApps) errors["apps"] = "At most " + _MaxApps + " apps.";
            List<PropertyApp> apps = new List<PropertyApp>();
            for (int i = 0; i < requests.Count && i < _MaxApps; i++)
            {
                string appId = (requests[i]?.AppId ?? String.Empty).Trim();
                if (appId.Length == 0 || appId.Length > 255 || !System.Text.RegularExpressions.Regex.IsMatch(appId, "^[A-Za-z][A-Za-z0-9_]*(\\.[A-Za-z0-9_]+)+$"))
                {
                    errors["apps[" + i + "].appId"] = "Enter a bundle id or package such as com.example.app.";
                    continue;
                }
                if (apps.Any(a => a.Platform == requests[i].Platform && String.Equals(a.AppId, appId, StringComparison.OrdinalIgnoreCase)))
                {
                    errors["apps[" + i + "].appId"] = "Listed twice.";
                    continue;
                }
                apps.Add(new PropertyApp { Id = IdGenerator.Generate(Constants.PropertyAppIdPrefix), PropertyId = propertyId, Platform = requests[i].Platform, AppId = appId, StoreAppId = requests[i].StoreAppId, Enabled = requests[i].Enabled, CreatedUtc = now, UpdatedUtc = now });
            }
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            return apps;
        }

        private async Task CheckConflictsAsync(string propertyId, List<PropertyDomain> domains, List<PropertyApp> apps, CancellationToken token)
        {
            await _Directory.ReloadAsync(token).ConfigureAwait(false);
            // fieldErrors name the offending row (domains[i].host, apps[i].appId, in list order) so clients can mark it.
            Dictionary<string, string> domainErrors = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < domains.Count; i++)
            {
                PropertyDomain domain = domains[i];
                string? owner = _Directory.FindClaimedHost(domain.Host);
                if (owner != null && owner != propertyId) domainErrors["domains[" + i + "].host"] = domain.Host + " is registered to another property.";
                else if (_Settings.Platform.IsPlatformHost(domain.Host)) domainErrors["domains[" + i + "].host"] = domain.Host + " is a platform host.";
            }
            if (domainErrors.Count > 0) throw new ApiErrorException(409, "DomainInUse", String.Join(" ", domainErrors.Values), domainErrors);
            Dictionary<string, string> appErrors = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < apps.Count; i++)
            {
                PropertyRuntime? owner = _Directory.FindByApp(apps[i].Platform, apps[i].AppId);
                if (owner != null && owner.Id != propertyId) appErrors["apps[" + i + "].appId"] = apps[i].AppId + " is registered to another property.";
            }
            if (appErrors.Count > 0) throw new ApiErrorException(409, "AppIdInUse", String.Join(" ", appErrors.Values), appErrors);
        }

        private async Task WriteMetadataAsync(TargetTypeEnum type, string id, List<string>? labels, Dictionary<string, string>? tags, CancellationToken token)
        {
            if (labels == null && tags == null) return;
            ContentActor staff = new ContentActor { IsBreakGlass = true, IsSystemAdmin = true, IsStaff = true, HasMembership = true, AgeAttested = true };
            await _Metadata.ApplyAsync(staff, type, id, labels, tags, token).ConfigureAwait(false);
        }

        private async Task<PropertyRecordResponse> ViewAsync(PropertyRecord record, Dictionary<string, ObjectMetadata>? metadata, CancellationToken token)
        {
            List<PropertyDomain> domains = await _Database.Registry.ReadDomainsAsync(record.Id, token).ConfigureAwait(false);
            List<PropertyApp> apps = await _Database.Registry.ReadAppsAsync(record.Id, token).ConfigureAwait(false);
            ObjectMetadata meta = metadata != null && metadata.TryGetValue(record.Id, out ObjectMetadata? found)
                ? found
                : await _Metadata.LoadOneAsync(TargetTypeEnum.Property, record.Id, true, token).ConfigureAwait(false);
            return new PropertyRecordResponse
            {
                Id = record.Id,
                Slug = record.Slug,
                Name = record.Name,
                Status = record.Status,
                Domains = domains.Select(DomainView).ToList(),
                Apps = apps.Select(a => new PlatformAppResponse { Id = a.Id, Platform = a.Platform, AppId = a.AppId, StoreAppId = a.StoreAppId, Enabled = a.Enabled }).ToList(),
                MemberCount = record.Status == PropertyStatusEnum.Deleted ? 0 : await _Database.Registry.CountMembersAsync(record.Id, token).ConfigureAwait(false),
                CreatedUtc = record.CreatedUtc,
                UpdatedUtc = record.UpdatedUtc,
                EnabledUtc = record.EnabledUtc,
                DisabledUtc = record.DisabledUtc,
                DisabledReason = record.DisabledReason,
                DeleteScheduledUtc = record.DeleteScheduledUtc,
                PurgeStartedUtc = record.PurgeStartedUtc,
                Labels = meta.Labels,
                Tags = meta.Tags
            };
        }

        private async Task<PlatformAdminResponse> AdminViewAsync(PlatformAdmin admin, CancellationToken token)
        {
            Dictionary<string, ObjectMetadata> metadata = await _Metadata.LoadAsync(TargetTypeEnum.PlatformAdmin, new[] { admin.Id }, true, token).ConfigureAwait(false);
            return AdminView(admin, metadata);
        }

        private static PlatformAdminResponse AdminView(PlatformAdmin admin, Dictionary<string, ObjectMetadata> metadata)
        {
            ObjectMetadata meta = metadata.TryGetValue(admin.Id, out ObjectMetadata? found) ? found : new ObjectMetadata();
            return new PlatformAdminResponse
            {
                Id = admin.Id,
                Email = admin.Email,
                DisplayName = admin.DisplayName,
                InvitationPending = admin.PasswordHash == null,
                LastLoginUtc = admin.LastLoginUtc,
                CreatedUtc = admin.CreatedUtc,
                Labels = meta.Labels,
                Tags = meta.Tags
            };
        }

        private static bool FixedTimeEquals(string presented, string expected)
        {
            byte[] a = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(presented));
            byte[] b = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(expected));
            return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(a, b);
        }

        private static string Truncate(string value, int max)
        {
            return value.Length <= max ? value : value.Substring(0, max);
        }

        #endregion
    }
}
