namespace TrellisKit.Infra.Stacks
{
    using System;
    using System.Collections.Generic;
    using Amazon.CDK;
    using Amazon.CDK.AWS.Budgets;
    using TrellisKit.Infra.Config;
    using Constructs;

    /// <summary>
    /// Monthly AWS Budgets cost alarm with actual and forecast email notifications.
    /// </summary>
    public class BudgetStack : Stack
    {
        /// <summary>
        /// The budget.
        /// </summary>
        public CfnBudget Budget { get; }

        /// <summary>
        /// Instantiate the stack.
        /// </summary>
        /// <param name="scope">Parent construct.</param>
        /// <param name="id">Construct id.</param>
        /// <param name="config">Environment configuration.</param>
        /// <param name="props">Stack properties.</param>
        /// <exception cref="ArgumentNullException">The configuration is null.</exception>
        public BudgetStack(Construct scope, string id, EnvironmentConfig config, IStackProps props) : base(scope, id, props)
        {
            ArgumentNullException.ThrowIfNull(config);

            List<CfnBudget.ISubscriberProperty> subscribers = new List<CfnBudget.ISubscriberProperty>();
            foreach (string email in config.Budget.NotificationEmails)
            {
                subscribers.Add(new CfnBudget.SubscriberProperty { Address = email, SubscriptionType = "EMAIL" });
            }

            CfnBudget.BudgetDataProperty data = new CfnBudget.BudgetDataProperty
            {
                BudgetName = config.ResourceName("monthly"),
                BudgetType = "COST",
                TimeUnit = "MONTHLY",
                BudgetLimit = new CfnBudget.SpendProperty { Amount = config.Budget.MonthlyLimitUsd, Unit = "USD" }
            };

            // With another environment in the same account, an unfiltered budget would count both environments' costs.
            string tagValue = CostTagValue(config);
            if (tagValue.Length > 0)
            {
                data.CostFilters = new Dictionary<string, object>
                {
                    ["TagKeyValue"] = new string[] { "user:" + BudgetSettings.CostTagKey + "$" + tagValue }
                };
            }

            Budget = new CfnBudget(this, "MonthlyBudget", new CfnBudgetProps
            {
                Budget = data,
                NotificationsWithSubscribers = new CfnBudget.INotificationWithSubscribersProperty[]
                {
                    new CfnBudget.NotificationWithSubscribersProperty
                    {
                        Notification = new CfnBudget.NotificationProperty
                        {
                            NotificationType = "ACTUAL",
                            ComparisonOperator = "GREATER_THAN",
                            Threshold = config.Budget.ActualThresholdPercent,
                            ThresholdType = "PERCENTAGE"
                        },
                        Subscribers = subscribers.ToArray()
                    },
                    new CfnBudget.NotificationWithSubscribersProperty
                    {
                        Notification = new CfnBudget.NotificationProperty
                        {
                            NotificationType = "FORECASTED",
                            ComparisonOperator = "GREATER_THAN",
                            Threshold = config.Budget.ForecastThresholdPercent,
                            ThresholdType = "PERCENTAGE"
                        },
                        Subscribers = subscribers.ToArray()
                    }
                }
            });
        }

        /// <summary>
        /// The cost allocation tag value the budget of an environment is limited to, or empty for the whole account.
        /// </summary>
        /// <param name="config">Environment configuration.</param>
        /// <returns>Tag value, or empty.</returns>
        /// <exception cref="ArgumentNullException">The configuration is null.</exception>
        public static string CostTagValue(EnvironmentConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);
            if (config.Budget.CostTagValue.Length > 0) return config.Budget.CostTagValue;
            return config.SharedAccount ? config.StackPrefix : String.Empty;
        }
    }
}
