namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// 403 Muted body: the error plus when the mute ends.
    /// </summary>
    public class MutedErrorResponse : ErrorResponse
    {
        #region Public-Members

        /// <summary>
        /// When the mute ends (UTC), or null for an indefinite mute.
        /// </summary>
        public DateTime? MutedUntilUtc { get; set; } = null;

        #endregion
    }
}
