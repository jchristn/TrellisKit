namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// How a check-in was recorded.
    /// </summary>
    public enum AttendanceMethodEnum
    {
        /// <summary>
        /// The member scanned the rotating QR code.
        /// </summary>
        QR,

        /// <summary>
        /// A rank awarder or SystemAdmin checked the member in.
        /// </summary>
        Staff,

        /// <summary>
        /// The member entered the rotating code.
        /// </summary>
        SelfWithCode
    }
}
