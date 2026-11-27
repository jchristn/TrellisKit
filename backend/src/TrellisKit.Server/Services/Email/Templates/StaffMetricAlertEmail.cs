namespace TrellisKit.Server.Services.Email.Templates
{
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>Administrative metric threshold notification. Property branding comes from the shared email layout.</summary>
    public sealed class StaffMetricAlertEmail : EmailTemplate
    {
        /// <inheritdoc />
        public override string Key => "staff_metric_alert";
        /// <inheritdoc />
        public override EmailCategoryEnum Category => EmailCategoryEnum.Staff;
        /// <inheritdoc />
        public override EmailRecipientEnum Recipient => EmailRecipientEnum.Staff;
        /// <inheritdoc />
        public override string Description => "A configured metric threshold was crossed.";
        /// <inheritdoc />
        public override string DefaultSubject => "Metric alert on {name}: {metric}";
        /// <inheritdoc />
        public override string DefaultPreheader => "Review the metric in the administrator console.";
        /// <inheritdoc />
        public override string DefaultHeading => "{metric}";
        /// <inheritdoc />
        public override string DefaultBody => "{detail}\n\nThis alert is sent once when the threshold is crossed. It can fire again after the metric recovers.";
        /// <inheritdoc />
        public override string? ButtonLabel => "Open metrics";
        /// <inheritdoc />
        public override string? StaffOrigin => "Metric alert check (scheduled)";
        /// <inheritdoc />
        public override IReadOnlyDictionary<string,string> Samples => new Dictionary<string,string>{{StaffEmailDetails.Token, StaffEmailSamples.System("Metric alert check (scheduled)")},{"metric","Server error rate"},{"detail","Server errors exceeded the configured threshold."},{"link","https://example.test/admin/metrics/health"}};
    }
}
