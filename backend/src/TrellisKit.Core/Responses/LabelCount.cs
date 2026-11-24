namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// One row of a label or tag discovery enumeration: a distinct label and how many visible objects carry it.
    /// </summary>
    public class LabelCount
    {
        #region Public-Members

        /// <summary>
        /// The label (with the casing first stored).
        /// </summary>
        public string Label { get; set; } = String.Empty;

        /// <summary>
        /// Number of objects carrying it that the caller can see.
        /// </summary>
        public long Count { get; set; } = 0;

        #endregion
    }
}
