namespace Test.Shared.Fakes
{
    using System;
    using System.Net.Http;
    using Test.Shared.IntegrationTests;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Integrations;
    using TrellisKit.Core.Integrations.Implementations;
    using TrellisKit.Core.Security;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// A full set of fake integrations plus the real password hasher (at its minimum cost) and token protector.
    /// </summary>
    public sealed class FakeIntegrations
    {
        /// <summary>
        /// Scripted DNS TXT resolver (domain verification).
        /// </summary>
        public ScriptedTxtResolver Txt { get; } = new ScriptedTxtResolver();

        /// <summary>
        /// Instantiate the fakes.
        /// </summary>
        /// <param name="auth">Authentication settings (token key).</param>
        /// <param name="storage">Storage settings for the local media store, or null for a fresh temporary folder.</param>
        /// <param name="media">Media settings for the SNS verifier, or null for defaults.</param>
        public FakeIntegrations(AuthSettings auth, StorageSettings? storage = null, MediaSettings? media = null)
        {
            ArgumentNullException.ThrowIfNull(auth);
            Set = new IntegrationSet(Apple, Google, Facebook, AppStore, GooglePlay, Stripe, Email,
                new Pbkdf2PasswordHasher { Iterations = 100000 }, new AesGcmTokenProtector(() => auth.TokenEncryptionKey));
            Set.LinkPreviewFetcher = LinkPreviews;
            Set.Push = Push;
            Set.TxtResolver = Txt;
            StorageSettings objects = storage ?? new StorageSettings { Directory = System.IO.Path.Combine(TestEnvironment.TempRoot, "objects-" + Guid.NewGuid().ToString("N")) };
            ObjectStore = new LocalObjectStore(objects, new LocalUploadSigner(() => auth.TokenEncryptionKey));
            Set.ObjectStore = ObjectStore;
            Set.ImageProcessor = new SkiaImageProcessor();
            Set.VideoTranscoder = new PassthroughTranscoder(ObjectStore);
            Set.MediaDelivery = new LocalMediaCookieSigner(Set.TokenProtector);
            Set.SnsVerifier = new SnsMessageVerifier(media ?? new MediaSettings(), new HttpClient(SnsHttp));
        }

        /// <summary>
        /// Local media object store (Local mode, as the test suites run).
        /// </summary>
        public LocalObjectStore ObjectStore { get; }

        /// <summary>
        /// HTTP fake behind the SNS verifier (certificate downloads and subscription confirmation).
        /// </summary>
        public FakeHttpHandler SnsHttp { get; } = new FakeHttpHandler();

        /// <summary>
        /// Apple verifier.
        /// </summary>
        public FakeIdentityTokenVerifier Apple { get; } = new FakeIdentityTokenVerifier(ExternalProviderEnum.Apple);

        /// <summary>
        /// Google verifier.
        /// </summary>
        public FakeIdentityTokenVerifier Google { get; } = new FakeIdentityTokenVerifier(ExternalProviderEnum.Google);

        /// <summary>
        /// Facebook verifier.
        /// </summary>
        public FakeIdentityTokenVerifier Facebook { get; } = new FakeIdentityTokenVerifier(ExternalProviderEnum.Facebook);

        /// <summary>
        /// App Store.
        /// </summary>
        public FakeAppStoreClient AppStore { get; } = new FakeAppStoreClient();

        /// <summary>
        /// Google Play.
        /// </summary>
        public FakeGooglePlayClient GooglePlay { get; } = new FakeGooglePlayClient();

        /// <summary>
        /// Stripe.
        /// </summary>
        public FakeStripeClient Stripe { get; } = new FakeStripeClient();

        /// <summary>
        /// Email.
        /// </summary>
        public InMemoryEmailSender Email { get; } = new InMemoryEmailSender();

        /// <summary>
        /// Push sender.
        /// </summary>
        public InMemoryPushSender Push { get; } = new InMemoryPushSender();

        /// <summary>
        /// The integration set to pass to the server.
        /// </summary>
        public IntegrationSet Set { get; }

        /// <summary>
        /// Scripted link preview fetcher (fixture pages and images by URL).
        /// </summary>
        public ScriptedLinkPreviewFetcher LinkPreviews { get; } = new ScriptedLinkPreviewFetcher();
    }
}
