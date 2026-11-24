namespace TrellisKit.Server.Workers
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Settings;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Emails members whose free code membership ends soon: a first reminder (default 14 days before) and a final one
    /// (default 3 days before). Each reminder is claimed with an atomic stage update, so it is sent once even with
    /// several server instances. Members whose membership continues past the code (for example a paid subscription)
    /// are skipped.
    /// </summary>
    public sealed class SignupCodeReminderWorker : IntervalWorker
    {
        #region Public-Members

        /// <inheritdoc />
        public override string JobName
        {
            get { return "signup_code_reminders"; }
        }

        /// <summary>
        /// Notification service, or null. When set, each 14 and 3 day reminder also creates a MembershipExpiring
        /// notification (category Account, path /account/membership). The composition root sets it.
        /// </summary>
        public NotificationService? Notifications { get; set; } = null;

        /// <summary>
        /// Supplies the property's current access configuration, or null. In the Open access model (no memberships)
        /// no reminders are sent; switching back resumes them for redemptions still due.
        /// </summary>
        public Func<AccessPolicy>? Access { get; set; } = null;

        #endregion

        #region Protected-Members

        /// <inheritdoc />
        protected override TimeSpan Interval
        {
            get { return TimeSpan.FromMinutes(_Membership.ReminderIntervalMinutes); }
        }

        #endregion

        #region Private-Members

        private const int _BatchSize = 500;
        private readonly DatabaseDriverBase _Database;
        private readonly MembershipService _MembershipService;
        private readonly EmailService _Email;
        private readonly SignupCodeSettings _Codes;
        private readonly MembershipSettings _Membership;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the worker.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="membershipService">Membership service.</param>
        /// <param name="email">Email service.</param>
        /// <param name="codes">Sign-up code settings (live).</param>
        /// <param name="membership">Membership settings (live).</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public SignupCodeReminderWorker(DatabaseDriverBase database, MembershipService membershipService, EmailService email, SignupCodeSettings codes, MembershipSettings membership, AppLogger logger)
            : base(logger)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _MembershipService = membershipService ?? throw new ArgumentNullException(nameof(membershipService));
            _Email = email ?? throw new ArgumentNullException(nameof(email));
            _Codes = codes ?? throw new ArgumentNullException(nameof(codes));
            _Membership = membership ?? throw new ArgumentNullException(nameof(membership));
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<int> ExecuteAsync(CancellationToken token)
        {
            if (Access != null && !Access().PurchasesEnabled) return 0;
            DateTime now = DateTime.UtcNow;
            int sent = 0;
            int firstDays = Math.Max(_Codes.FirstReminderDays, _Codes.FinalReminderDays + 1);
            List<SignupCodeRedemption> due = await _Database.SignupCodeRedemptions.EnumerateDueForReminderAsync(now, now.AddDays(firstDays), 2, _BatchSize, token).ConfigureAwait(false);
            foreach (SignupCodeRedemption redemption in due)
            {
                token.ThrowIfCancellationRequested();
                bool final = redemption.MembershipEndsUtc!.Value <= now.AddDays(_Codes.FinalReminderDays);
                int stage = final ? 2 : 1;
                if (redemption.ReminderStage >= stage) continue;

                User? user = await _Database.Users.ReadAsync(redemption.UserId, token).ConfigureAwait(false);
                MembershipState state = await _MembershipService.GetStateAsync(redemption.UserId, token).ConfigureAwait(false);
                DateTime? coverage = MembershipService.CoverageEnd(state.Entitlements, now);
                bool continues = state.Summary.IsActive && (coverage == null || coverage.Value > redemption.MembershipEndsUtc.Value.AddMinutes(1));

                if (!await _Database.SignupCodeRedemptions.TrySetReminderStageAsync(redemption.Id, stage, token).ConfigureAwait(false)) continue;
                if (user == null || user.IsDeleted || continues) continue;
                if (Notifications != null)
                {
                    await Notifications.CreateAsync(new NotificationDraft
                    {
                        RecipientUserId = user.Id,
                        Type = NotificationTypeEnum.MembershipExpiring,
                        TargetType = TargetTypeEnum.User,
                        TargetId = user.Id,
                        EventUtc = redemption.MembershipEndsUtc.Value,
                        Path = "/account/membership"
                    }, token).ConfigureAwait(false);
                }
                if (user.Email == null) continue;
                await _Email.SendCodeReminderAsync(user.Email, user.DisplayName, redemption.MembershipEndsUtc.Value, token).ConfigureAwait(false);
                sent++;
            }
            if (sent > 0) Logger.Info("[SignupCodeReminderWorker] sent " + sent + " code membership reminders");
            return sent;
        }

        #endregion
    }
}
