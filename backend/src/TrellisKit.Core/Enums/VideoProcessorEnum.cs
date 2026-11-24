namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Video transcoding backend.
    /// </summary>
    public enum VideoProcessorEnum
    {
        /// <summary>
        /// AWS Elemental MediaConvert (production).
        /// </summary>
        MediaConvert,

        /// <summary>
        /// ffmpeg in the server process host (local Docker stack).
        /// </summary>
        Ffmpeg,

        /// <summary>
        /// Copy the original without transcoding (tests and dotnet run without ffmpeg).
        /// </summary>
        Passthrough
    }
}
