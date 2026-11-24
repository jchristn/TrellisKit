namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Whether members can see who voted for what (resolved decision 4: the poll author chooses).
    /// </summary>
    public enum PollVoterVisibilityEnum
    {
        /// <summary>
        /// Counts only.
        /// </summary>
        Anonymous,

        /// <summary>
        /// Voters are listed through the voters enumeration.
        /// </summary>
        Visible
    }
}
