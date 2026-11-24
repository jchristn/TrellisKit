namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// A daily library metric value.
    /// </summary>
    public class LessonDailyRow
    {
        #region Public-Members

        /// <summary>
        /// Day (yyyy-MM-dd).
        /// </summary>
        public string Date { get; set; } = String.Empty;

        /// <summary>
        /// Value.
        /// </summary>
        public long Count { get; set; } = 0;

        #endregion
    }
}
