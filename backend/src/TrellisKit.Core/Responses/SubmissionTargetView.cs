namespace TrellisKit.Core.Responses
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The target of a directory submission.
    /// </summary>
    public class SubmissionTargetView
    {
        #region Public-Members

        /// <summary>
        /// Venue or Instructor.
        /// </summary>
        public TargetTypeEnum Type { get; set; } = TargetTypeEnum.Venue;

        /// <summary>
        /// Identifier (null for a new entry before approval).
        /// </summary>
        public string? Id { get; set; } = null;

        /// <summary>
        /// Name.
        /// </summary>
        public string? Name { get; set; } = null;

        #endregion
    }
}
