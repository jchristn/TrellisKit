namespace TrellisKit.Core.Integrations
{
    using System;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Integrations.Interfaces;

    /// <summary>
    /// The external integrations the server uses, passed in as one object so the composition root supplies the real
    /// implementations and tests supply fakes. Every property is required; the settable media properties are
    /// assigned by the composition root after construction (so parallel feature slices do not change the constructor).
    /// </summary>
    public class IntegrationSet
    {
        #region Public-Members

        /// <summary>
        /// Sign in with Apple token verifier.
        /// </summary>
        public IIdentityTokenVerifier Apple { get; }

        /// <summary>
        /// Google ID token verifier.
        /// </summary>
        public IIdentityTokenVerifier Google { get; }

        /// <summary>
        /// Facebook token verifier.
        /// </summary>
        public IIdentityTokenVerifier Facebook { get; }

        /// <summary>
        /// App Store client.
        /// </summary>
        public IAppStoreClient AppStore { get; }

        /// <summary>
        /// Google Play client.
        /// </summary>
        public IGooglePlayClient GooglePlay { get; }

        /// <summary>
        /// Stripe client.
        /// </summary>
        public IStripeClient Stripe { get; }

        /// <summary>
        /// Email sender.
        /// </summary>
        public IEmailSender Email { get; }

        /// <summary>
        /// Password hasher.
        /// </summary>
        public IPasswordHasher PasswordHasher { get; }

        /// <summary>
        /// Access token protector.
        /// </summary>
        public ITokenProtector TokenProtector { get; }

        /// <summary>
        /// Media object store (one store addressed by <see cref="MediaLocationEnum"/>). The composition root must set it.
        /// </summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        /// <exception cref="InvalidOperationException">Read before it was set.</exception>
        public IObjectStore ObjectStore
        {
            get { return _ObjectStore ?? throw new InvalidOperationException("IntegrationSet.ObjectStore was not set by the composition root."); }
            set { _ObjectStore = value ?? throw new ArgumentNullException(nameof(ObjectStore)); }
        }

        /// <summary>
        /// Image processor. The composition root must set it.
        /// </summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        /// <exception cref="InvalidOperationException">Read before it was set.</exception>
        public IImageProcessor ImageProcessor
        {
            get { return _ImageProcessor ?? throw new InvalidOperationException("IntegrationSet.ImageProcessor was not set by the composition root."); }
            set { _ImageProcessor = value ?? throw new ArgumentNullException(nameof(ImageProcessor)); }
        }

        /// <summary>
        /// Video processor selected by media.videoProcessor. The composition root must set it.
        /// </summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        /// <exception cref="InvalidOperationException">Read before it was set.</exception>
        public IVideoTranscoder VideoTranscoder
        {
            get { return _VideoTranscoder ?? throw new InvalidOperationException("IntegrationSet.VideoTranscoder was not set by the composition root."); }
            set { _VideoTranscoder = value ?? throw new ArgumentNullException(nameof(VideoTranscoder)); }
        }

        /// <summary>
        /// Media grant cookie signer selected by media.storageMode. The composition root must set it.
        /// </summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        /// <exception cref="InvalidOperationException">Read before it was set.</exception>
        public IMediaDeliverySigner MediaDelivery
        {
            get { return _MediaDelivery ?? throw new InvalidOperationException("IntegrationSet.MediaDelivery was not set by the composition root."); }
            set { _MediaDelivery = value ?? throw new ArgumentNullException(nameof(MediaDelivery)); }
        }

        /// <summary>
        /// SNS message verifier for the MediaConvert webhook. The composition root must set it.
        /// </summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        /// <exception cref="InvalidOperationException">Read before it was set.</exception>
        public ISnsMessageVerifier SnsVerifier
        {
            get { return _SnsVerifier ?? throw new InvalidOperationException("IntegrationSet.SnsVerifier was not set by the composition root."); }
            set { _SnsVerifier = value ?? throw new ArgumentNullException(nameof(SnsVerifier)); }
        }

        #endregion

        #region Private-Members

        private IObjectStore? _ObjectStore = null;
        private IImageProcessor? _ImageProcessor = null;
        private IVideoTranscoder? _VideoTranscoder = null;
        private IMediaDeliverySigner? _MediaDelivery = null;
        private ISnsMessageVerifier? _SnsVerifier = null;

        /// <summary>
        /// Link preview fetcher (Phase 2 contract section 5.9). The composition root must set it (Bootstrapper uses
        /// HttpLinkPreviewFetcher; tests use a scripted fake). Reading it before it is set throws
        /// IntegrationNotConfiguredException.
        /// </summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        /// <exception cref="TrellisKit.Core.Exceptions.IntegrationNotConfiguredException">Read before it was set.</exception>
        public ILinkPreviewFetcher LinkPreviewFetcher
        {
            get { return _LinkPreviewFetcher ?? throw new TrellisKit.Core.Exceptions.IntegrationNotConfiguredException("The link preview fetcher is not configured."); }
            set { _LinkPreviewFetcher = value ?? throw new ArgumentNullException(nameof(LinkPreviewFetcher)); }
        }

        /// <summary>
        /// TXT record resolver for domain ownership verification. Set by the composition root.
        /// </summary>
        /// <exception cref="InvalidOperationException">Read before it was set.</exception>
        public IDnsTxtResolver TxtResolver
        {
            get { return _TxtResolver ?? throw new InvalidOperationException("IntegrationSet.TxtResolver was not set by the composition root."); }
            set { _TxtResolver = value ?? throw new ArgumentNullException(nameof(TxtResolver)); }
        }

        /// <summary>
        /// Push sender (Expo). The composition root must set it (Bootstrapper creates an ExpoPushSender; tests set the
        /// in-memory fake). Reading it before it is set throws.
        /// </summary>
        /// <exception cref="InvalidOperationException">Read before it was set.</exception>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public IPushSender Push
        {
            get { return _Push ?? throw new InvalidOperationException("IntegrationSet.Push was not set by the composition root."); }
            set { _Push = value ?? throw new ArgumentNullException(nameof(Push)); }
        }

        #endregion

        #region Private-Members

        private IPushSender? _Push = null;
        private IDnsTxtResolver? _TxtResolver = null;

        #endregion

        #region Private-Members

        private ILinkPreviewFetcher? _LinkPreviewFetcher = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the set.
        /// </summary>
        /// <param name="apple">Apple verifier.</param>
        /// <param name="google">Google verifier.</param>
        /// <param name="facebook">Facebook verifier.</param>
        /// <param name="appStore">App Store client.</param>
        /// <param name="googlePlay">Google Play client.</param>
        /// <param name="stripe">Stripe client.</param>
        /// <param name="email">Email sender.</param>
        /// <param name="passwordHasher">Password hasher.</param>
        /// <param name="tokenProtector">Token protector.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public IntegrationSet(
            IIdentityTokenVerifier apple,
            IIdentityTokenVerifier google,
            IIdentityTokenVerifier facebook,
            IAppStoreClient appStore,
            IGooglePlayClient googlePlay,
            IStripeClient stripe,
            IEmailSender email,
            IPasswordHasher passwordHasher,
            ITokenProtector tokenProtector)
        {
            Apple = apple ?? throw new ArgumentNullException(nameof(apple));
            Google = google ?? throw new ArgumentNullException(nameof(google));
            Facebook = facebook ?? throw new ArgumentNullException(nameof(facebook));
            AppStore = appStore ?? throw new ArgumentNullException(nameof(appStore));
            GooglePlay = googlePlay ?? throw new ArgumentNullException(nameof(googlePlay));
            Stripe = stripe ?? throw new ArgumentNullException(nameof(stripe));
            Email = email ?? throw new ArgumentNullException(nameof(email));
            PasswordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
            TokenProtector = tokenProtector ?? throw new ArgumentNullException(nameof(tokenProtector));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The identity verifier for a provider.
        /// </summary>
        /// <param name="provider">Provider.</param>
        /// <returns>Verifier.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The provider is not supported.</exception>
        public IIdentityTokenVerifier VerifierFor(ExternalProviderEnum provider)
        {
            switch (provider)
            {
                case ExternalProviderEnum.Apple:
                    return Apple;
                case ExternalProviderEnum.Google:
                    return Google;
                case ExternalProviderEnum.Facebook:
                    return Facebook;
                default:
                    throw new ArgumentOutOfRangeException(nameof(provider), "Unsupported provider " + provider + ".");
            }
        }

        #endregion
    }
}
