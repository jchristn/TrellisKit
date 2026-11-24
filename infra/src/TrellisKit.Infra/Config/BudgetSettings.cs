namespace TrellisKit.Infra.Config
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// AWS Budgets monthly cost alarm.
    /// </summary>
    public class BudgetSettings
    {
        private int _MonthlyLimitUsd = 100;
        private int _ActualThresholdPercent = 80;
        private int _ForecastThresholdPercent = 100;
        private List<string> _NotificationEmails = new List<string> { "alerts@example.test" };
        private string _CostTagValue = String.Empty;

        /// <summary>
        /// Cost allocation tag every stack carries, with the environment's stack prefix as its value (for example
        /// example-dev; the shared prefix for every stack of a shared deployment). A budget scoped with
        /// <see cref="CostTagValue"/> filters on it.
        /// </summary>
        public static readonly string CostTagKey = "Deployment";

        /// <summary>
        /// Monthly cost budget in USD. Default 100. Minimum 1, maximum 1000000.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int MonthlyLimitUsd
        {
            get { return _MonthlyLimitUsd; }
            set { _MonthlyLimitUsd = Guard.Range(value, 1, 1000000, nameof(MonthlyLimitUsd)); }
        }

        /// <summary>
        /// Notify when actual spend passes this percent of the budget. Default 80. Minimum 1, maximum 1000.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int ActualThresholdPercent
        {
            get { return _ActualThresholdPercent; }
            set { _ActualThresholdPercent = Guard.Range(value, 1, 1000, nameof(ActualThresholdPercent)); }
        }

        /// <summary>
        /// Notify when forecast spend passes this percent of the budget. Default 100. Minimum 1, maximum 1000.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int ForecastThresholdPercent
        {
            get { return _ForecastThresholdPercent; }
            set { _ForecastThresholdPercent = Guard.Range(value, 1, 1000, nameof(ForecastThresholdPercent)); }
        }

        /// <summary>
        /// Email addresses notified by the budget (at most 10). Default alerts@example.test.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public List<string> NotificationEmails
        {
            get { return _NotificationEmails; }
            set { _NotificationEmails = Guard.NotNull(value, nameof(NotificationEmails)); }
        }

        /// <summary>
        /// Value of the <see cref="CostTagKey"/> cost allocation tag the budget is limited to. Default empty, which
        /// means: the environment's stack prefix when another environment shares the account
        /// (<see cref="EnvironmentConfig.SharedAccount"/>), so each environment's budget sees only its own resources,
        /// and the whole account otherwise. A filtered budget needs the tag activated as a cost allocation tag in the
        /// Billing console (infra/README.md).
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public string CostTagValue
        {
            get { return _CostTagValue; }
            set { _CostTagValue = value ?? throw new ArgumentNullException(nameof(CostTagValue)); }
        }
    }
}
