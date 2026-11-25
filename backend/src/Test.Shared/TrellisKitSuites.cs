namespace Test.Shared
{
    using System.Collections.Generic;
    using System.Linq;
    using Test.Shared.IntegrationTests;
    using Test.Shared.Suites;
    using Touchstone.Core;

    /// <summary>
    /// Every TrellisKit backend suite, consumed by all test hosts.
    /// </summary>
    public sealed class TrellisKitSuites : ITestSuiteProvider
    {
        #region Public-Members

        /// <summary>
        /// All suites, with fresh descriptors on each access.
        /// </summary>
        public static IReadOnlyList<TestSuiteDescriptor> All
        {
            get
            {
                TestSuiteDescriptor[] core = new[]
                {
                    ModelSuite.Build(),
                    SecuritySuite.Build(),
                    MembershipRulesSuite.Build(),
                    SettingsSuite.Build(),
                    PropertySuite.Build(),
                    DatabaseSuite.Build(),
                    IdentityDatabaseSuite.Build(),
                    PropertyResolutionSuite.Build(),
                    PropertyIsolationSuite.Build(),
                    PlatformSuite.Build(),
                    PlatformPartTwoSuite.Build(),
                    HttpSuite.Build(),
                    PropertyRoutesSuite.Build(),
                    AuthSuite.Build(),
                    ProfileSuite.Build(),
                    ProfileFieldSuite.Build(),
                    AdministrativeEmailSuite.Build(),
                    EmailCompositionSuite.Build(),
                    MembershipSuite.Build(),
                    SignupCodeSuite.Build(),
                    AdminSuite.Build(),
                    BlockSuite.Build(),
                    WordFilterSuite.Build(),
                    MemberSearchSuite.Build(),
                    LinkPreviewSuite.Build(),
                    MarkdownTextSuite.Build(),
                    NotificationSuite.Build(),
                    PushSuite.Build(),
                    FeedPostEmailSuite.Build(),
                    MediaUnitSuite.Build(),
                    MediaSuite.Build(),
                    AccountExportSuite.Build(),
                    EnumerationSuite.Build(),
                    CatalogRoutesSuite.Build(),
                    MetadataSuite.Build(),
                    EnumerationEnforcementSuite.Build(),
                    FeedSuite.Build(),
                    CommentSuite.Build(),
                    ReactionSuite.Build(),
                    RevisionSuite.Build(),
                    PermissionSuite.Build(),
                    PublicFeedSuite.Build(),
                    PublicMediaSuite.Build(),
                    AdminFeedSuite.Build(),
                    ContentLifecycleSuite.Build(),
                    ModerationSuite.Build(),
                    ModerationAdminSuite.Build(),
                    LiveUpdatesSuite.Build(),
                    AppConfigSuite.Build(),
                    LiveContentSuite.Build(),
                    AccessModelSuite.Build(),
                    SecretKeySuite.Build(),
                    AdminFollowUpSuite.Build(),
                    ForumSuite.Build(),
                    ForumModerationSuite.Build(),
                    ForumAccessSuite.Build(),
                    IdeaSuite.Build(),
                    TicketSuite.Build(),
                    PrivateMediaSuite.Build(),
                    BroadcastSuite.Build(),
                    AiEndpointSuite.Build(),
                    AiChatSuite.Build(),
                    AiToolSuite.Build(),
                    AiModerationSuite.Build(),
                    OpenApiDocsSuite.Build(),
                    UsageSuite.Build(),
                    MetricsSuite.Build(),
                    AdminSummarySuite.Build(),
                    ProductMetricsSuite.Build(),
                    BrandingSuite.Build(),
                    PalettePresetSuite.Build(),
                    DocumentSuite.Build(),
                    DirectorySuite.Build(),
                    GeoQuerySuite.Build(),
                    GeocoderSuite.Build(),
                    SubmissionSuite.Build(),
                    UpstreamIntegrationSuite.Build(),
                    PlatformMetricsSuite.Build(),
                    EmailBrandingSuite.Build(),
                    PromptGuardSuite.Build(),
                    ScopeBenchmarkSuite.Build(),
                    JobEngineSuite.Build(),
                    AiRolesSuite.Build(),
                    DocsCoverageSuite.Build(),
                    HarnessSuite.Build(),
                    AiTelemetrySuite.Build(),
                    ContentReviewSuite.Build(),
                    DirectoryAccessSuite.Build(),
                    PublicPageSuite.Build(),
                    VenueContributionSuite.Build(),
                    ProfileMediaSuite.Build(),
                    EngagementSuite.Build(),
                    Phase4StaffSuite.Build(),
                    EventSuite.Build(),
                    EventMapSuite.Build(),
                    RsvpSuite.Build(),
                    RecurrenceSuite.Build(),
                    EventReminderSuite.Build(),
                    CalendarSuite.Build(),
                    ProposalSuite.Build(),
                    EventAccessSuite.Build(),
                    LibrarySuite.Build(),
                    LessonProgressSuite.Build(),
                    LessonDownloadSuite.Build(),
                    DocumentMediaSuite.Build(),
                    LibraryAccessSuite.Build(),
                    RealtimeProtocolSuite.Build(),
                    RealtimeScaleSuite.Build(),
                    ConversationSuite.Build(),
                    MessageSuite.Build(),
                    MessageHistorySuite.Build(),
                    MessageSearchSuite.Build(),
                    MessagePrivacySuite.Build(),
                    MessagePushSuite.Build(),
                    SyncSuite.Build(),
                    MessagingLapseSuite.Build(),
                    MessagingLifecycleSuite.Build(),
                    ExerciseCatalogSuite.Build(),
                    CatalogImportSuite.Build(),
                    CatalogModalitySuite.Build(),
                    CatalogApparatusSuite.Build(),
                    CatalogGenerationSuite.Build(),
                    CatalogPublicPageSuite.Build(),
                };
                return core.Concat(IntegrationSuites.All).ToList();
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<TestSuiteDescriptor> Suites
        {
            get { return All; }
        }

        #endregion
    }
}
