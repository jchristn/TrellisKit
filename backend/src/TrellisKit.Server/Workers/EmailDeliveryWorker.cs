namespace TrellisKit.Server.Workers
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Settings;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Retries queued administrative emails with backoff (docs/TRELLISKIT.md section 12.4) and prunes records past
    /// email.messageRetentionDays. One per property; claims are conditional updates, so several instances can run it.
    /// </summary>
    public sealed class EmailDeliveryWorker : IntervalWorker
    {
        #region Public-Members

        /// <summary>
        /// Job name.
        /// </summary>
        public const string Name = "email_delivery";

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
            get { return TimeSpan.FromSeconds(_Settings.DeliveryIntervalSeconds); }
        }

        #endregion

        #region Private-Members

        private readonly EmailService _Email;
        private readonly EmailSettings _Settings;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the worker.
        /// </summary>
        /// <param name="email">The property's email service.</param>
        /// <param name="settings">Email settings (live instance).</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public EmailDeliveryWorker(EmailService email, EmailSettings settings, AppLogger logger) : base(logger)
        {
            _Email = email ?? throw new ArgumentNullException(nameof(email));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override Task<int> ExecuteAsync(CancellationToken token)
        {
            return _Email.DeliverDueAsync(token);
        }

        #endregion
    }
}
