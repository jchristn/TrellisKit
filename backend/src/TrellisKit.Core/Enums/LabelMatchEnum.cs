namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// How an enumeration's label filter combines several labels.
    /// </summary>
    public enum LabelMatchEnum
    {
        /// <summary>
        /// Objects must carry every listed label.
        /// </summary>
        All,

        /// <summary>
        /// Objects must carry at least one listed label.
        /// </summary>
        Any
    }
}
