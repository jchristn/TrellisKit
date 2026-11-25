namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One Training hub section and whether the caller can use it (docs/TRELLISKIT.md section 15). Clients show only
    /// available sections and ignore keys they do not know.
    /// </summary>
    public class TrainingSectionView
    {
        #region Public-Members

        /// <summary>
        /// Most Library segments.
        /// </summary>
        public const int MaxSegments = 3;

        /// <summary>
        /// Section key.
        /// </summary>
        public TrainingSectionEnum Key { get; set; } = TrainingSectionEnum.Overview;

        /// <summary>
        /// Whether the section is available to the caller.
        /// </summary>
        public bool Available { get; set; } = false;

        /// <summary>
        /// Why the section is unavailable, or null when available.
        /// </summary>
        public TrainingSectionReasonEnum? Reason { get; set; } = null;

        /// <summary>
        /// Library segments the caller can open in order (Courses, Techniques, Saved); at most <see cref="MaxSegments"/>,
        /// empty for other sections.
        /// </summary>
        public List<string> Segments { get; set; } = new List<string>();

        #endregion
    }
}
