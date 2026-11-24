namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// One row of a label or tag discovery enumeration: a distinct value and how many visible objects carry it.
    /// </summary>
    public class TagValueCount
    {
        #region Public-Members

        /// <summary>
        /// The value (with the casing first stored).
        /// </summary>
        public string Value { get; set; } = String.Empty;

        /// <summary>
        /// Number of objects carrying it that the caller can see.
        /// </summary>
        public long Count { get; set; } = 0;

        #endregion
    }
}
