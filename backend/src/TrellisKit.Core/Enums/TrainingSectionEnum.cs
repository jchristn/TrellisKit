namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Sections of the member Training hub (docs/TRELLISKIT.md, "Training hub"). The server reports each section with
    /// whether it is available to the caller; clients show only available sections and ignore unknown keys.
    /// </summary>
    public enum TrainingSectionEnum
    {
        /// <summary>
        /// Overview: cards for what to do next (up next, continue, recently saved, new for you).
        /// </summary>
        Overview,

        /// <summary>
        /// Library: courses, techniques, and saved items in one place.
        /// </summary>
        Library,

        /// <summary>
        /// Schedule: the caller's upcoming registrations and check-ins.
        /// </summary>
        Schedule,

        /// <summary>
        /// Programs: structured programs or curricula, reserved for engines and properties that have them.
        /// </summary>
        Programs,

        /// <summary>
        /// Progress: completed lessons and series, attendance, and rank history.
        /// </summary>
        Progress
    }
}
