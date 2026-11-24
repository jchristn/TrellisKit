namespace TrellisKit.Core.Integrations.Models
{
    /// <summary>
    /// Header facts read from an ISO BMFF (MP4 or QuickTime) file.
    /// </summary>
    public class Mp4Info
    {
        #region Public-Members

        /// <summary>
        /// Duration in seconds from the mvhd box.
        /// </summary>
        public double DurationSeconds { get; set; } = 0;

        /// <summary>
        /// Display width of the first video track (after its rotation matrix), or null when no track header was found.
        /// </summary>
        public int? Width { get; set; } = null;

        /// <summary>
        /// Display height of the first video track (after its rotation matrix), or null when no track header was found.
        /// </summary>
        public int? Height { get; set; } = null;

        #endregion
    }
}
