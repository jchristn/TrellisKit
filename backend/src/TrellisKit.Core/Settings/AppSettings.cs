namespace TrellisKit.Core.Settings
{
    using System;
    using TrellisKit.Core.Database;

    /// <summary>
    /// Root application settings, loaded from the settings JSON file and then overridden by environment variables.
    /// </summary>
    public class AppSettings
    {
        #region Public-Members

        /// <summary>
        /// UTC time the settings file was first created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// HTTP listener settings.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public RestSettings Rest
        {
            get { return _Rest; }
            set { _Rest = value ?? throw new ArgumentNullException(nameof(Rest)); }
        }

        /// <summary>
        /// CORS settings.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public CorsSettings Cors
        {
            get { return _Cors; }
            set { _Cors = value ?? throw new ArgumentNullException(nameof(Cors)); }
        }

        /// <summary>
        /// Database settings.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public DatabaseSettings Database
        {
            get { return _Database; }
            set { _Database = value ?? throw new ArgumentNullException(nameof(Database)); }
        }

        /// <summary>
        /// Logging settings.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public LoggingSettings Logging
        {
            get { return _Logging; }
            set { _Logging = value ?? throw new ArgumentNullException(nameof(Logging)); }
        }

        /// <summary>
        /// Request history settings.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public RequestHistorySettings RequestHistory
        {
            get { return _RequestHistory; }
            set { _RequestHistory = value ?? throw new ArgumentNullException(nameof(RequestHistory)); }
        }

        /// <summary>
        /// Deployment mode and property resolution settings.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public DeploymentSettings Deployment
        {
            get { return _Deployment; }
            set { _Deployment = value ?? throw new ArgumentNullException(nameof(Deployment)); }
        }

        /// <summary>
        /// Platform administration settings (MultiProperty).
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public PlatformSettings Platform
        {
            get { return _Platform; }
            set { _Platform = value ?? throw new ArgumentNullException(nameof(Platform)); }
        }

        /// <summary>
        /// Property selection settings.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public PropertySettings Property
        {
            get { return _Property; }
            set { _Property = value ?? throw new ArgumentNullException(nameof(Property)); }
        }

        /// <summary>
        /// Telemetry settings.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public TelemetrySettings Telemetry
        {
            get { return _Telemetry; }
            set { _Telemetry = value ?? throw new ArgumentNullException(nameof(Telemetry)); }
        }

        /// <summary>
        /// Authentication settings.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public AuthSettings Auth
        {
            get { return _Auth; }
            set { _Auth = value ?? throw new ArgumentNullException(nameof(Auth)); }
        }

        /// <summary>
        /// External sign-in provider settings.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public OAuthSettings OAuth
        {
            get { return _OAuth; }
            set { _OAuth = value ?? throw new ArgumentNullException(nameof(OAuth)); }
        }

        /// <summary>
        /// Stripe settings.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public StripeSettings Stripe
        {
            get { return _Stripe; }
            set { _Stripe = value ?? throw new ArgumentNullException(nameof(Stripe)); }
        }

        /// <summary>
        /// Apple App Store settings.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public AppStoreSettings AppStore
        {
            get { return _AppStore; }
            set { _AppStore = value ?? throw new ArgumentNullException(nameof(AppStore)); }
        }

        /// <summary>
        /// Google Play settings.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public GooglePlaySettings GooglePlay
        {
            get { return _GooglePlay; }
            set { _GooglePlay = value ?? throw new ArgumentNullException(nameof(GooglePlay)); }
        }

        /// <summary>
        /// Email settings.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public EmailSettings Email
        {
            get { return _Email; }
            set { _Email = value ?? throw new ArgumentNullException(nameof(Email)); }
        }

        /// <summary>
        /// Public website settings.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public WebSettings Web
        {
            get { return _Web; }
            set { _Web = value ?? throw new ArgumentNullException(nameof(Web)); }
        }

        /// <summary>
        /// Object storage settings.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public StorageSettings Storage
        {
            get { return _Storage; }
            set { _Storage = value ?? throw new ArgumentNullException(nameof(Storage)); }
        }

        /// <summary>
        /// Member account settings.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public AccountSettings Account
        {
            get { return _Account; }
            set { _Account = value ?? throw new ArgumentNullException(nameof(Account)); }
        }

        /// <summary>
        /// Sign-up code settings.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public SignupCodeSettings SignupCodes
        {
            get { return _SignupCodes; }
            set { _SignupCodes = value ?? throw new ArgumentNullException(nameof(SignupCodes)); }
        }

        /// <summary>
        /// Membership offer and background worker settings.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public MembershipSettings Membership
        {
            get { return _Membership; }
            set { _Membership = value ?? throw new ArgumentNullException(nameof(Membership)); }
        }

        /// <summary>
        /// Enumeration (paging) limits shared by every list route.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public EnumerationSettings Enumeration
        {
            get { return _Enumeration; }
            set { _Enumeration = value ?? throw new ArgumentNullException(nameof(Enumeration)); }
        }

        /// <summary>
        /// Media upload, processing, storage, and delivery settings (contract section 4).
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public MediaSettings Media
        {
            get { return _Media; }
            set { _Media = value ?? throw new ArgumentNullException(nameof(Media)); }
        }

        /// <summary>
        /// Feed post settings (contract section 5).
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public FeedSettings Feed
        {
            get { return _Feed; }
            set { _Feed = value ?? throw new ArgumentNullException(nameof(Feed)); }
        }

        /// <summary>
        /// Settings shared by feed posts and comments.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public ContentSettings Content
        {
            get { return _Content; }
            set { _Content = value ?? throw new ArgumentNullException(nameof(Content)); }
        }

        /// <summary>
        /// Comment settings (contract section 7).
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public CommentSettings Comments
        {
            get { return _Comments; }
            set { _Comments = value ?? throw new ArgumentNullException(nameof(Comments)); }
        }

        /// <summary>
        /// Reaction settings (contract section 6). The reaction set itself comes from the property.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public ReactionSettings Reactions
        {
            get { return _Reactions; }
            set { _Reactions = value ?? throw new ArgumentNullException(nameof(Reactions)); }
        }

        /// <summary>
        /// Mention settings (contract section 3.3).
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public MentionSettings Mentions
        {
            get { return _Mentions; }
            set { _Mentions = value ?? throw new ArgumentNullException(nameof(Mentions)); }
        }

        /// <summary>
        /// Notification center settings (contract section 8).
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public NotificationSettings Notifications
        {
            get { return _Notifications; }
            set { _Notifications = value ?? throw new ArgumentNullException(nameof(Notifications)); }
        }

        /// <summary>
        /// Push delivery settings (contract section 8.8).
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public PushSettings Push
        {
            get { return _Push; }
            set { _Push = value ?? throw new ArgumentNullException(nameof(Push)); }
        }

        /// <summary>
        /// Library settings (Phase 4 section 12).
        /// </summary>
        public LibrarySettings Library
        {
            get { return _Library; }
            set { _Library = value ?? throw new ArgumentNullException(nameof(Library)); }
        }

        /// <summary>
        /// Verified ranks, curricula, and attendance switches.
        /// </summary>
        public RankSettings Ranks
        {
            get { return _Ranks; }
            set { _Ranks = value ?? throw new ArgumentNullException(nameof(Ranks)); }
        }

        /// <summary>
        /// Attendance check-in settings.
        /// </summary>
        public AttendanceSettings Attendance
        {
            get { return _Attendance; }
            set { _Attendance = value ?? throw new ArgumentNullException(nameof(Attendance)); }
        }

        /// <summary>
        /// Events settings (Phase 4 section 12).
        /// </summary>
        public EventSettings Events
        {
            get { return _Events; }
            set { _Events = value ?? throw new ArgumentNullException(nameof(Events)); }
        }

        /// <summary>
        /// Venue and instructor directory settings (Phase 4 section 12).
        /// </summary>
        public DirectorySettings Directory
        {
            get { return _Directory; }
            set { _Directory = value ?? throw new ArgumentNullException(nameof(Directory)); }
        }

        /// <summary>
        /// Forum settings (Phase 3 section 15).
        /// </summary>
        public ForumSettings Forums
        {
            get { return _Forums; }
            set { _Forums = value ?? throw new ArgumentNullException(nameof(Forums)); }
        }

        /// <summary>
        /// Poll settings (Phase 3 section 15).
        /// </summary>
        public PollSettings Polls
        {
            get { return _Polls; }
            set { _Polls = value ?? throw new ArgumentNullException(nameof(Polls)); }
        }

        /// <summary>
        /// Trending settings (Phase 3 sections 5 and 15).
        /// </summary>
        public TrendingSettings Trending
        {
            get { return _Trending; }
            set { _Trending = value ?? throw new ArgumentNullException(nameof(Trending)); }
        }

        /// <summary>
        /// Search settings (Phase 3 resolved decisions 11 and 13).
        /// </summary>
        public SearchSettings Search
        {
            get { return _Search; }
            set { _Search = value ?? throw new ArgumentNullException(nameof(Search)); }
        }

        /// <summary>
        /// Moderation settings (contract section 9).
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public ModerationSettings Moderation
        {
            get { return _Moderation; }
            set { _Moderation = value ?? throw new ArgumentNullException(nameof(Moderation)); }
        }

        /// <summary>
        /// Link preview settings (contract section 5.9).
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public LinkPreviewSettings LinkPreviews
        {
            get { return _LinkPreviews; }
            set { _LinkPreviews = value ?? throw new ArgumentNullException(nameof(LinkPreviews)); }
        }

        /// <summary>
        /// Request rate limits (contract section 12).
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public RateLimitSettings RateLimits
        {
            get { return _RateLimits; }
            set { _RateLimits = value ?? throw new ArgumentNullException(nameof(RateLimits)); }
        }

        /// <summary>
        /// Configurable profile field limits.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public ProfileFieldSettings ProfileFields
        {
            get { return _ProfileFields; }
            set { _ProfileFields = value ?? throw new ArgumentNullException(nameof(ProfileFields)); }
        }

        /// <summary>
        /// Live updates (code updates over the Expo Updates protocol).
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public UpdatesSettings Updates
        {
            get { return _Updates; }
            set { _Updates = value ?? throw new ArgumentNullException(nameof(Updates)); }
        }

        /// <summary>
        /// Feature flags, kill switches, and the app content manifest.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public AppConfigSettings AppConfig
        {
            get { return _AppConfig; }
            set { _AppConfig = value ?? throw new ArgumentNullException(nameof(AppConfig)); }
        }

        /// <summary>
        /// AI models and assistants (docs/AI_CONTRACT.md section 17).
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public AiSettings Ai
        {
            get { return _Ai; }
            set { _Ai = value ?? throw new ArgumentNullException(nameof(Ai)); }
        }

        /// <summary>
        /// Usage metering (docs/USAGE_METERING_CONTRACT.md section 10).
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public UsageSettings Usage
        {
            get { return _Usage; }
            set { _Usage = value ?? throw new ArgumentNullException(nameof(Usage)); }
        }

        /// <summary>
        /// Job engine (docs/INTEGRATION.md).
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public JobEngineSettings Jobs
        {
            get { return _Jobs; }
            set { _Jobs = value ?? throw new ArgumentNullException(nameof(Jobs)); }
        }

        /// <summary>
        /// Content changelog and Terms of Service review (docs/INTEGRATION.md content review contract).
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public ContentReviewSettings ContentReview
        {
            get { return _ContentReview; }
            set { _ContentReview = value ?? throw new ArgumentNullException(nameof(ContentReview)); }
        }


        /// <summary>
        /// Direct message settings (docs/PHASE5_CONTRACT.md section 14).
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public MessagingSettings Messaging
        {
            get { return _Messaging; }
            set { _Messaging = value ?? throw new ArgumentNullException(nameof(Messaging)); }
        }

        /// <summary>
        /// Realtime WebSocket transport settings (docs/PHASE5_CONTRACT.md section 4).
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public RealtimeSettings Realtime
        {
            get { return _Realtime; }
            set { _Realtime = value ?? throw new ArgumentNullException(nameof(Realtime)); }
        }

        /// <summary>
        /// Support ticket settings (docs/PHASE5_CONTRACT.md section 6).
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public SupportSettings Support
        {
            get { return _Support; }
            set { _Support = value ?? throw new ArgumentNullException(nameof(Support)); }
        }

        /// <summary>
        /// Ideas board settings (docs/PHASE5_CONTRACT.md section 7).
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public IdeaSettings Ideas
        {
            get { return _Ideas; }
            set { _Ideas = value ?? throw new ArgumentNullException(nameof(Ideas)); }
        }

        /// <summary>
        /// Broadcast settings (docs/PHASE5_CONTRACT.md section 8).
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public BroadcastSettings Broadcasts
        {
            get { return _Broadcasts; }
            set { _Broadcasts = value ?? throw new ArgumentNullException(nameof(Broadcasts)); }
        }

        /// <summary>
        /// Daily administrator summary email (docs/ADMIN_SUMMARY.md).
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public AdminSummarySettings AdminSummary
        {
            get { return _AdminSummary; }
            set { _AdminSummary = value ?? throw new ArgumentNullException(nameof(AdminSummary)); }
        }

        /// <summary>
        /// Saves and private collections (REST_API.md, Saves).
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public SavesSettings Saves
        {
            get { return _Saves; }
            set { _Saves = value ?? throw new ArgumentNullException(nameof(Saves)); }
        }

        /// <summary>
        /// Engine secrets key (per-property integration secrets, queued email payloads, stored update signing keys, AI endpoint secrets).
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public SecretSettings Secrets
        {
            get { return _Secrets; }
            set { _Secrets = value ?? throw new ArgumentNullException(nameof(Secrets)); }
        }

        #endregion

        #region Private-Members

        private RestSettings _Rest = new RestSettings();
        private CorsSettings _Cors = new CorsSettings();
        private DatabaseSettings _Database = new DatabaseSettings();
        private LoggingSettings _Logging = new LoggingSettings();
        private RequestHistorySettings _RequestHistory = new RequestHistorySettings();
        private DeploymentSettings _Deployment = new DeploymentSettings();
        private PlatformSettings _Platform = new PlatformSettings();
        private PropertySettings _Property = new PropertySettings();
        private TelemetrySettings _Telemetry = new TelemetrySettings();
        private AuthSettings _Auth = new AuthSettings();
        private OAuthSettings _OAuth = new OAuthSettings();
        private StripeSettings _Stripe = new StripeSettings();
        private AppStoreSettings _AppStore = new AppStoreSettings();
        private GooglePlaySettings _GooglePlay = new GooglePlaySettings();
        private EmailSettings _Email = new EmailSettings();
        private WebSettings _Web = new WebSettings();
        private StorageSettings _Storage = new StorageSettings();
        private AccountSettings _Account = new AccountSettings();
        private SignupCodeSettings _SignupCodes = new SignupCodeSettings();
        private MembershipSettings _Membership = new MembershipSettings();
        private EnumerationSettings _Enumeration = new EnumerationSettings();
        private MediaSettings _Media = new MediaSettings();
        private FeedSettings _Feed = new FeedSettings();
        private ContentSettings _Content = new ContentSettings();
        private CommentSettings _Comments = new CommentSettings();
        private ReactionSettings _Reactions = new ReactionSettings();
        private MentionSettings _Mentions = new MentionSettings();
        private NotificationSettings _Notifications = new NotificationSettings();
        private PushSettings _Push = new PushSettings();
        private ForumSettings _Forums = new ForumSettings();
        private LibrarySettings _Library = new LibrarySettings();
        private EventSettings _Events = new EventSettings();
        private DirectorySettings _Directory = new DirectorySettings();
        private PollSettings _Polls = new PollSettings();
        private TrendingSettings _Trending = new TrendingSettings();
        private SearchSettings _Search = new SearchSettings();
        private ModerationSettings _Moderation = new ModerationSettings();
        private LinkPreviewSettings _LinkPreviews = new LinkPreviewSettings();
        private RateLimitSettings _RateLimits = new RateLimitSettings();
        private ProfileFieldSettings _ProfileFields = new ProfileFieldSettings();
        private UpdatesSettings _Updates = new UpdatesSettings();
        private AppConfigSettings _AppConfig = new AppConfigSettings();
        private AiSettings _Ai = new AiSettings();
        private SecretSettings _Secrets = new SecretSettings();
        private UsageSettings _Usage = new UsageSettings();
        private JobEngineSettings _Jobs = new JobEngineSettings();
        private ContentReviewSettings _ContentReview = new ContentReviewSettings();
        private MessagingSettings _Messaging = new MessagingSettings();
        private RealtimeSettings _Realtime = new RealtimeSettings();
        private SupportSettings _Support = new SupportSettings();
        private IdeaSettings _Ideas = new IdeaSettings();
        private BroadcastSettings _Broadcasts = new BroadcastSettings();
        private AdminSummarySettings _AdminSummary = new AdminSummarySettings();
        private RankSettings _Ranks = new RankSettings();
        private AttendanceSettings _Attendance = new AttendanceSettings();
        private SavesSettings _Saves = new SavesSettings();

        #endregion
    }
}
