namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// A chapter marker.
    /// </summary>
    public class LessonMarkerView
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Offset.
        /// </summary>
        public int Seconds { get; set; } = 0;

        /// <summary>
        /// Label.
        /// </summary>
        public string Label { get; set; } = String.Empty;

        #endregion
    }
}
