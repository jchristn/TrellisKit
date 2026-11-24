namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Monthly budget of an assistant.
    /// </summary>
    public class AssistantBudgetView
    {
        #region Public-Members

        /// <summary>
        /// Budget in US dollars, or null.
        /// </summary>
        public double? MonthlyUsd { get; set; } = null;

        /// <summary>
        /// Stop chat when spent.
        /// </summary>
        public bool HardStop { get; set; } = false;

        /// <summary>
        /// Spend this UTC month.
        /// </summary>
        public double MonthToDateUsd { get; set; } = 0;

        #endregion
    }
}
