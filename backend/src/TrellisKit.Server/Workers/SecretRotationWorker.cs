namespace TrellisKit.Server.Workers
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Settings;
    using TrellisKit.Server.Services;

    /// <summary>
    /// One property's secrets re-encryption worker (<see cref="SecretRotationService"/>): every
    /// secrets.reencryptIntervalSeconds it rewrites at most secrets.reencryptBatchSize values encrypted with a previous
    /// secrets key using the current key, and updates trelliskit.secrets.reencrypt_remaining. Does nothing when no
    /// previous key is configured. Safe on several instances (rewrites are conditional).
    /// Thread safety: one pass at a time per instance.
    /// </summary>
    public sealed class SecretRotationWorker : IntervalWorker
    {
        #region Public-Members

        /// <summary>
        /// Job name.
        /// </summary>
        public const string Name = "secrets_reencrypt";

        /// <inheritdoc />
        public override string JobName
        {
            get { return Name; }
        }

        #endregion

        #region Protected-Members

        /// <inheritdoc />
        protected override TimeSpan Interval
        {
            get { return TimeSpan.FromSeconds(_Settings.Secrets.ReencryptIntervalSeconds); }
        }

        /// <inheritdoc />
        protected override TimeSpan InitialDelay
        {
            get { return TimeSpan.FromSeconds(30); }
        }

        #endregion

        #region Private-Members

        private readonly SecretRotationService _Rotation;
        private readonly AppSettings _Settings;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the worker.
        /// </summary>
        /// <param name="rotation">Rotation service of the property.</param>
        /// <param name="settings">Running settings (live).</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public SecretRotationWorker(SecretRotationService rotation, AppSettings settings, AppLogger logger) : base(logger)
        {
            _Rotation = rotation ?? throw new ArgumentNullException(nameof(rotation));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override Task<int> ExecuteAsync(CancellationToken token)
        {
            return _Rotation.RunPassAsync(token);
        }

        #endregion
    }
}
