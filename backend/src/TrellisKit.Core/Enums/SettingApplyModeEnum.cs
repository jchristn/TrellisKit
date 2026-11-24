namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// When a changed setting takes effect.
    /// </summary>
    public enum SettingApplyModeEnum
    {
        /// <summary>
        /// Applied immediately when saved.
        /// </summary>
        Live,

        /// <summary>
        /// Saved to the settings file and applied after the server restarts.
        /// </summary>
        Restart
    }
}
