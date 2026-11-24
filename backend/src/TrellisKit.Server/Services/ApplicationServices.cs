namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Integrations;
    using TrellisKit.Core.PropertyConfig;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// The application service graph, built once from the database, integrations, and running settings. Route
    /// registrars and workers take their services from here.
    /// Thread safety: every service is safe to share across requests.
    /// </summary>
    public class ApplicationServices
    {
        #region Public-Members

        /// <summary>
        /// Running settings.
        /// </summary>
        public AppSettings Settings { get; }

        /// <summary>
        /// Active property with the settings it supplies defaults for.
        /// </summary>
        public PropertyContext Property { get; }

        /// <summary>
        /// Database driver bound to this property.
        /// </summary>
        public DatabaseDriverBase Database { get; }

        /// <summary>
        /// Property id.
        /// </summary>
        public string PropertyId
        {
            get { return Property.Property.Id; }
        }

        /// <summary>
        /// Integrations.
        /// </summary>
        public IntegrationSet Integrations { get; }

        /// <summary>
        /// Logger.
        /// </summary>
        public AppLogger Logger { get; }

        /// <summary>
        /// Rate limiter shared by sign-up codes and password reset.
        /// </summary>
        public SlidingWindowRateLimiter RateLimiter { get; }

        /// <summary>
        /// Phase 2 rate limits with Retry-After, over <see cref="RateLimiter"/>.
        /// </summary>
        public RateLimitService Limits { get; }

        /// <summary>
        /// Resolves content callers (roles, flags, posting permissions, mute).
        /// </summary>
        public ActorService Actors { get; }

        /// <summary>
        /// Builds UserSummary objects.
        /// </summary>
        public UserSummaryService Summaries { get; }

        /// <summary>
        /// Enumeration preparation (paging, ordering, filters) with the live settings.
        /// </summary>
        public EnumerationService Enumeration { get; }

        /// <summary>
        /// Labels and tags for every object type.
        /// </summary>
        public MetadataService Metadata { get; }

        /// <summary>
        /// Token service.
        /// </summary>
        public TokenService Tokens { get; }

        /// <summary>
        /// Authorization service.
        /// </summary>
        public AuthorizationService Authorization { get; }

        /// <summary>
        /// Authentication hook.
        /// </summary>
        public AuthenticationService Authentication { get; }

        /// <summary>
        /// Audit service.
        /// </summary>
        public AuditService Audit { get; }

        /// <summary>
        /// The property's access configuration (access model, gated features, default audience).
        /// </summary>
        public AccessPolicyService Access { get; }

        /// <summary>
        /// Brand palette (docs/TRELLISKIT.md section 2.1).
        /// </summary>
        public BrandPaletteService Branding { get; }

        /// <summary>
        /// AI switch (docs/TRELLISKIT.md section 9.5).
        /// </summary>
        public PropertyAiSwitchService AiSwitch { get; }

        /// <summary>
        /// Property documents and acceptances (docs/TRELLISKIT.md section 14).
        /// </summary>
        public TrellisKit.Server.Services.Documents.PropertyDocumentService Documents { get; }

        /// <summary>
        /// Secrets key rotation: re-encrypts this property's values that use a previous secrets key.
        /// </summary>
        public SecretRotationService SecretRotation { get; }

        /// <summary>
        /// Email service.
        /// </summary>
        public EmailService Email { get; }

        /// <summary>
        /// Per-property settings (integration settings and encrypted secrets).
        /// </summary>
        public PropertySettingsService PropertySettings { get; }

        /// <summary>
        /// Admin portal Email section (templates, staff events, suppressions, message log, Mailgun webhooks).
        /// </summary>
        public TrellisKit.Server.Services.Email.EmailAdminService EmailAdmin { get; }

        /// <summary>
        /// Membership service.
        /// </summary>
        public MembershipService Membership { get; }

        /// <summary>
        /// View builder.
        /// </summary>
        public MemberViewBuilder Views { get; }

        /// <summary>
        /// Avatar store.
        /// </summary>
        public AvatarStore Avatars { get; }

        /// <summary>
        /// Session service.
        /// </summary>
        public SessionService Sessions { get; }

        /// <summary>
        /// Account service.
        /// </summary>
        public AccountService Accounts { get; }

        /// <summary>
        /// Profile service.
        /// </summary>
        public ProfileService Profiles { get; }

        /// <summary>
        /// Configurable profile field definitions.
        /// </summary>
        public ProfileFieldService ProfileFields { get; }

        /// <summary>
        /// Configurable profile values (visibility, validation, writes, filters).
        /// </summary>
        public ProfileValueService ProfileValues { get; }

        /// <summary>
        /// Member enumeration with profile filters.
        /// </summary>
        public MemberDirectoryService Members { get; }

        /// <summary>
        /// Purchase service.
        /// </summary>
        public PurchaseService Purchases { get; }

        /// <summary>
        /// Webhook service.
        /// </summary>
        public WebhookService Webhooks { get; }

        /// <summary>
        /// Sign-up code service.
        /// </summary>
        public SignupCodeService SignupCodes { get; }

        /// <summary>
        /// Admin member service.
        /// </summary>
        public AdminMemberService AdminMembers { get; }

        /// <summary>
        /// Admin subscription service.
        /// </summary>
        public AdminSubscriptionService AdminSubscriptions { get; }

        /// <summary>
        /// Word filter (Phase 2 contract section 9.4).
        /// </summary>
        public WordFilterService WordFilter { get; }

        /// <summary>
        /// Member blocks (Phase 2 contract section 9.2).
        /// </summary>
        public BlockService Blocks { get; }

        /// <summary>
        /// Forums, threads, polls, trending, and search (Phase 3 contract).
        /// </summary>
        public ForumModule Forums { get; }

        /// <summary>
        /// Ideas board (docs/PHASE5_CONTRACT.md section 7).
        /// </summary>
        public IdeaModule Ideas { get; }

        /// <summary>
        /// Support tickets, ticket types, and private ticket media (Phase 5 contract section 6).
        /// </summary>
        public SupportModule Support { get; }

        /// <summary>
        /// Broadcasts, segments, and announcements (docs/PHASE5_CONTRACT.md section 8).
        /// </summary>
        public BroadcastModule Broadcasts { get; }

        /// <summary>
        /// Direct messaging (Phase 5 contract sections 3 and 4.9).
        /// </summary>
        public TrellisKit.Server.Services.Messaging.MessagingModule Messaging { get; }

        /// <summary>
        /// Member search for mention autocomplete (Phase 2 contract section 3.4).
        /// </summary>
        public MemberSearchService MemberSearch { get; }

        /// <summary>
        /// Link previews (Phase 2 contract section 5.9).
        /// </summary>
        public LinkPreviewService LinkPreviews { get; }

        /// <summary>
        /// Notification creation and the notification center.
        /// </summary>
        public NotificationService Notifications { get; }

        /// <summary>
        /// Notification preferences and notification hours.
        /// </summary>
        public NotificationPreferenceService NotificationPreferences { get; }

        /// <summary>
        /// Push token registration.
        /// </summary>
        public PushTokenService PushTokens { get; }

        /// <summary>
        /// New feed post emails and unsubscribe tokens.
        /// </summary>
        public FeedPostEmailService FeedPostEmails { get; }

        /// <summary>
        /// Media pipeline service (uploads, processing, delivery, and the media API for content and moderation).
        /// </summary>
        public MediaService Media { get; }

        /// <summary>
        /// Asynchronous account data exports (register more sections with AccountExports.Register).
        /// </summary>
        public AccountExportService AccountExports { get; }

        /// <summary>
        /// Who may see feed posts, comments, and their media.
        /// </summary>
        public ContentAccessService ContentAccess { get; }

        /// <summary>
        /// Mentions.
        /// </summary>
        public MentionService Mentions { get; }

        /// <summary>
        /// Reactions.
        /// </summary>
        public ReactionService Reactions { get; }

        /// <summary>
        /// FeedPost, PublicFeedPost, and Comment payload builder.
        /// </summary>
        public ContentViewBuilder ContentViews { get; }

        /// <summary>
        /// Feed posts.
        /// </summary>
        public FeedService Feed { get; }

        /// <summary>
        /// Comments.
        /// </summary>
        public CommentService Comments { get; }

        /// <summary>
        /// Account deletion and export of a member's posts, comments, reactions, and mentions.
        /// </summary>
        public ContentLifecycleService ContentLifecycle { get; }

        /// <summary>
        /// Member reports (Phase 2 contract section 9.1).
        /// </summary>
        public ReportService Reports { get; }

        /// <summary>
        /// Moderation actions, queue, auto-hide, overdue notices, and the removed-content purge.
        /// </summary>
        public ModerationService Moderation { get; }

        /// <summary>
        /// Moderators admin (grant and revoke FeedModerator).
        /// </summary>
        public ModeratorService Moderators { get; }

        /// <summary>
        /// Live updates: code updates, feature flags, kill switches, and the app content manifest.
        /// </summary>
        public LiveUpdateServices LiveUpdates { get; }

        /// <summary>
        /// AI models and assistants (docs/AI_CONTRACT.md).
        /// </summary>
        public Ai.AiServices Ai { get; }

        /// <summary>
        /// Usage metering (docs/USAGE_METERING_CONTRACT.md).
        /// </summary>
        public Usage.UsageService Usage { get; }

        /// <summary>Property-scoped metrics, reporting and alert evaluation.</summary>
        public Metrics.MetricsService Metrics { get; }

        /// <summary>
        /// Phase 4 library module (series, lessons, progress, downloads).
        /// </summary>
        public LibraryModule Library { get; }

        /// <summary>
        /// Phase 4 events module (events, recurrence, RSVPs, reminders, calendar).
        /// </summary>
        public EventModule Events { get; }

        /// <summary>
        /// Phase 4 directory module (venues, instructors, submissions, geocoding, map).
        /// </summary>
        public DirectoryModule Directory { get; }

        /// <summary>
        /// Job engine: scheduled background jobs with runs, items, retries, and budgets (docs/INTEGRATION.md).
        /// </summary>
        public Jobs.JobService Jobs { get; }

        /// <summary>
        /// Content changelog and Terms of Service review: the content.review, content.reviewDigest, and
        /// content.changesPrune jobs and the admin queue (docs/INTEGRATION.md content review contract).
        /// </summary>
        public ContentReview.ContentReviewService ContentReview { get; }


        /// <summary>
        /// Public landing pages: venue, event, and opt-in member profile pages, the sitemap, and robots.txt
        /// (docs/PHASE4_CONTRACT.md section 18).
        /// </summary>
        public TrellisKit.Server.Services.PublicPages.PublicPageService PublicPages { get; }

        /// <summary>
        /// Phase 4 content target providers (comments, reactions, reports, moderation, and search for Venue,
        /// Instructor, Event, Series, and Lesson). Modules register during construction.
        /// </summary>
        public ContentTargetRegistry ContentTargets { get; } = new ContentTargetRegistry();

        /// <summary>
        /// Decorators applied to GET /api/v1/property after the property.json defaults (the Phase 4 modules replace the
        /// directory, events, and library sections with the database copy). Modules add to it during construction.
        /// </summary>
        public List<Func<TrellisKit.Core.Responses.PublicPropertyResponse, CancellationToken, Task>> PropertyDecorators { get; } = new List<Func<TrellisKit.Core.Responses.PublicPropertyResponse, CancellationToken, Task>>();

        /// <summary>
        /// Realtime bus shared by every property of the deployment (docs/PHASE5_CONTRACT.md section 4.11). The server host
        /// replaces the default in-process bus with the configured one before it starts.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public TrellisKit.Server.Services.Realtime.IRealtimeBus RealtimeBus
        {
            get { return _RealtimeBus; }
            set { _RealtimeBus = value ?? throw new ArgumentNullException(nameof(RealtimeBus)); }
        }

        /// <summary>
        /// Realtime seams of this property (docs/PHASE5_CONTRACT.md section 4): inbound frame handlers, event renderers,
        /// the connection registry, and delivery helpers.
        /// </summary>
        public TrellisKit.Server.Services.Realtime.RealtimeModule Realtime { get; }

        #endregion

        #region Private-Members

        private TrellisKit.Server.Services.Realtime.IRealtimeBus _RealtimeBus = new TrellisKit.Server.Services.Realtime.InProcessRealtimeBus();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Build the service graph.
        /// </summary>
        /// <param name="settings">Running settings.</param>
        /// <param name="property">Active property.</param>
        /// <param name="database">Initialized database driver bound to the property (see DatabaseDriverBase.ForProperty);
        /// a deployment driver is bound here.</param>
        /// <param name="integrations">Integrations.</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public ApplicationServices(AppSettings settings, PropertyConfiguration property, DatabaseDriverBase database, IntegrationSet integrations, AppLogger logger)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Property = new PropertyContext(property ?? throw new ArgumentNullException(nameof(property)), settings);
            ArgumentNullException.ThrowIfNull(database);
            if (database.BoundPropertyId != property.Id) database = database.ForProperty(property.Id);
            Database = database;
            Integrations = integrations ?? throw new ArgumentNullException(nameof(integrations));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));

            RateLimiter = new SlidingWindowRateLimiter();
            Limits = new RateLimitService(RateLimiter);
            Actors = new ActorService(database) { Decorator = ContentTargets.Decorate };
            Summaries = new UserSummaryService(database, Property);
            Enumeration = new EnumerationService(settings) { PropertyId = property.Id };
            Metadata = new MetadataService(database, Enumeration);
            Tokens = new TokenService(database, settings.Auth, integrations.TokenProtector, logger)
            {
                PropertyId = property.Id,
                AcceptLegacyTokens = settings.Deployment.Mode == DeploymentModeEnum.SingleProperty
            };
            Audit = new AuditService(database, logger);
            Access = new AccessPolicyService(database, property, Audit, logger);
            Property.AccessSource = () => Access.Current;
            Branding = new BrandPaletteService(database, property, settings, Audit, logger);
            Property.BrandSource = () => Branding.Current;
            AiSwitch = new PropertyAiSwitchService(database, property, settings, Audit, logger);
            SecretRotation = new SecretRotationService(database, settings, property.Id, Audit, logger);
            Actors.Access = () => Access.Current;
            Authorization = new AuthorizationService(Property.MinimumAge) { Access = () => Access.Current, SignupCodesFeature = () => property.Features.SignupCodes, DirectoryFeature = () => property.Features.Directory, SearchFeature = () => property.Features.Search, EventsFeature = () => property.Features.Events, LibraryFeature = () => property.Features.Library };
            Authentication = new AuthenticationService(settings.Auth, Tokens, Authorization, logger) { PropertyId = property.Id };
            Documents = new TrellisKit.Server.Services.Documents.PropertyDocumentService(database, property, settings, Audit, Enumeration, Metadata, Summaries, Tokens, logger);
            Authorization.DocumentRequirements = () => Documents.Requirements();
            ProfileFields = new ProfileFieldService(database, settings, Audit, Enumeration, Metadata, logger);
            ProfileValues = new ProfileValueService(database, settings, ProfileFields);
            Summaries.ProfileValues = ProfileValues;
            Summaries.ProfileFields = ProfileFields;
            PropertySettings = new PropertySettingsService(database, settings, property.Id, Audit, logger);
            Email = new EmailService(database, integrations.Email, Property, settings, PropertySettings, logger);
            EmailAdmin = new TrellisKit.Server.Services.Email.EmailAdminService(database, Email, PropertySettings, Enumeration, Metadata, Audit, logger);
            Membership = new MembershipService(database);
            Views = new MemberViewBuilder(database, Membership, Property, Metadata) { ProfileValues = ProfileValues, ProfileFields = ProfileFields };
            Views.PendingDocuments = Documents.PendingAsync;
            Avatars = new AvatarStore(settings.Storage, property.Id);
            Sessions = new SessionService(database, settings.Auth, Tokens, Views, logger);
            Accounts = new AccountService(database, integrations, settings, Property, Sessions, Tokens, Email, Audit, Views, Membership, Avatars, RateLimiter, logger);
            Accounts.Documents = Documents;
            Members = new MemberDirectoryService(database, Enumeration, Summaries, ProfileValues, Metadata);
            Profiles = new ProfileService(database, Views, Avatars, settings.Storage, property, Metadata) { ProfileFields = ProfileFields, ProfileValues = ProfileValues, Audit = Audit, Settings = settings };
            Purchases = new PurchaseService(database, integrations, settings, Property, Membership, Tokens, logger) { Email = Email };
            Webhooks = new WebhookService(database, integrations, Purchases, logger);
            SignupCodes = new SignupCodeService(database, settings, Property, Membership, Tokens, Email, Audit, RateLimiter, logger, Enumeration, Metadata);
            Purchases.SignupCodes = SignupCodes;
            AdminMembers = new AdminMemberService(database, Membership, Views, Sessions, Tokens, SignupCodes, Audit, Property, Enumeration, Metadata) { ProfileValues = ProfileValues, ProfileFields = ProfileFields, Email = Email };
            AdminSubscriptions = new AdminSubscriptionService(database, property.Membership, Enumeration, Metadata);
            AccountExports = new AccountExportService(database, integrations, property.Id, settings, RateLimiter, logger) { Email = Email };
            AccountExports.Register(new ProfileExportSection(Views));
            AccountExports.Register(new IdentitiesExportSection(database));
            AccountExports.Register(new SessionsExportSection(database));
            AccountExports.Register(new SubscriptionsExportSection(database));
            AccountExports.Register(new EntitlementsExportSection(database));
            AccountExports.Register(new RedemptionsExportSection(database));
            AccountExports.Register(new AuditRecordsExportSection(database));
            AccountExports.Register(new MetadataExportSection(database));
            AccountExports.Register(new DocumentAcceptancesExportSection(database));
            Accounts.AccountClosed = AccountExports.DeleteForUserAsync;
            Notifications = new NotificationService(database, settings, Summaries, Enumeration, Metadata, logger) { Access = () => Access.Current };
            NotificationPreferences = new NotificationPreferenceService(database, settings.Notifications);
            PushTokens = new PushTokenService(database, Enumeration, Metadata);
            FeedPostEmails = new FeedPostEmailService(Email, Property, settings, NotificationPreferences, logger);
            Media = new MediaService(settings, database, integrations, Limits, Summaries, Metadata, Enumeration, Tokens, logger, property.Id);
            Media.Access = () => Access.Current;
            WordFilter = new WordFilterService(database, settings, Audit, Enumeration, Metadata);
            Metadata.TextFilter = WordFilter.MetadataTextFilterAsync;
            Profiles.WordFilter = WordFilter;
            ProfileValues.WordFilter = WordFilter;
            Accounts.WordFilter = WordFilter;
            Blocks = new BlockService(database, settings, Limits, Summaries, Enumeration, Metadata);
            MemberSearch = new MemberSearchService(database, settings, Limits, Summaries, Enumeration);
            LinkPreviews = new LinkPreviewService(database, settings, integrations, Limits, WordFilter, Media, logger);
            ContentAccess = new ContentAccessService(database) { Targets = ContentTargets };
            Mentions = new MentionService(database, settings, Summaries);
            Reactions = new ReactionService(this);
            ContentViews = new ContentViewBuilder(this);
            Feed = new FeedService(this);
            Comments = new CommentService(this);
            ContentLifecycle = new ContentLifecycleService(this);
            Feed.Attach();
            Reports = new ReportService(this);
            Moderation = new ModerationService(this);
            Moderators = new ModeratorService(this);
            Feed.ModerationActionsPreview = Moderation.FeedPostActionsPreviewAsync;
            ContentExportSections.Register(this);
            Ai = new Ai.AiServices(this);
            AccountExports.Register(new AssistantConversationsExportSection(this));
            Func<string, CancellationToken, Task>? exportCleanup = Accounts.AccountClosed;
            Accounts.AccountClosed = async (userId, token) =>
            {
                await ContentLifecycle.EraseForAccountAsync(userId, token).ConfigureAwait(false);
                await Ai.EraseForAccountAsync(userId, token).ConfigureAwait(false);
                await new Metrics.ProductMetricEventService(this).EraseAsync(userId, token).ConfigureAwait(false);
                await Media.DeleteUnattachedForUserAsync(userId, token).ConfigureAwait(false);
                if (exportCleanup != null) await exportCleanup(userId, token).ConfigureAwait(false);
            };
            LiveUpdates = new LiveUpdateServices(this);
            Authentication.DeployKeyAuthenticator = LiveUpdates.AuthenticateDeployKeyAsync;
            Authentication.DeployKeyPermits = LiveUpdateServices.DeployKeyPermits;
            Authentication.RequestGate = LiveUpdates.GateAsync;
            Forums = new ForumModule(this);
            Forums.Attach();
            Usage = new Usage.UsageService(database, settings, integrations, Metadata, Enumeration, logger);
            Metrics = new Metrics.MetricsService(this);
            Metrics.ProductMetricEventService productEvents = new Metrics.ProductMetricEventService(this);
            Accounts.ProductEvents = productEvents;
            Profiles.ProductEvents = productEvents;
            Notifications.ProductEvents = productEvents;
            PropertySettings.Changed += Usage.InvalidateZone;
            Library = new LibraryModule(this);
            Events = new EventModule(this);
            Directory = new DirectoryModule(this);
            PublicPages = new TrellisKit.Server.Services.PublicPages.PublicPageService(this);
            Views.PublicPages = PublicPages;
            Library.Attach();
            Events.Attach();
            Directory.Attach();
            Ideas = new IdeaModule(this);
            Ideas.Attach();
            Support = new SupportModule(this);
            Support.Attach();
            Broadcasts = new BroadcastModule(this);
            Broadcasts.Attach();
            Realtime = new TrellisKit.Server.Services.Realtime.RealtimeModule(this);
            Realtime.Attach();
            Messaging = new TrellisKit.Server.Services.Messaging.MessagingModule(this);
            Messaging.Attach();
            Jobs = new Jobs.JobService(this);
            ContentReview = new ContentReview.ContentReviewService(this);
            ContentReview.Attach();
        }

        #endregion
    }
}
