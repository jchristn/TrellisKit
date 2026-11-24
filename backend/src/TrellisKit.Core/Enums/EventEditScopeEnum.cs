namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Which occurrences of a recurring event an edit or cancellation applies to (resolved decision 11).
    /// </summary>
    public enum EventEditScopeEnum
    {
        /// <summary>
        /// Only this occurrence (it becomes an exception).
        /// </summary>
        One,

        /// <summary>
        /// This and every later occurrence (the rule is split here).
        /// </summary>
        Following,

        /// <summary>
        /// Every not-yet-started occurrence that is not an exception.
        /// </summary>
        All
    }
}
