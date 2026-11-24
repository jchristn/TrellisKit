namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// GET /api/v1/admin/signup-codes/{id}.
    /// </summary>
    public class SignupCodeDetail : SignupCodeView
    {
        #region Public-Members

        /// <summary>
        /// Redemptions per day for the last 90 days, oldest first. Never null.
        /// </summary>
        public List<DailyCount> RedemptionsByDay
        {
            get { return _RedemptionsByDay; }
            set { _RedemptionsByDay = value ?? new List<DailyCount>(); }
        }

        /// <summary>
        /// Outcomes.
        /// </summary>
        public RedemptionOutcomes Outcomes { get; set; } = new RedemptionOutcomes();

        #endregion

        #region Private-Members

        private List<DailyCount> _RedemptionsByDay = new List<DailyCount>();

        #endregion
    }
}
