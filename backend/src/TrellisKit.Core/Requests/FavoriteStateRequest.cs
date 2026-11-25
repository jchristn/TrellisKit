namespace TrellisKit.Core.Requests
{
    using System.Collections.Generic;

    /// <summary>
    /// Body of POST /api/v1/me/saves/state: 1 to <see cref="MaxTargets"/> targets whose saved state the caller wants.
    /// </summary>
    public class FavoriteStateRequest
    {
        #region Public-Members

        /// <summary>
        /// Targets.
        /// </summary>
        public List<FavoriteTargetRef>? Targets { get; set; } = null;

        /// <summary>
        /// Most targets in one request.
        /// </summary>
        public const int MaxTargets = 100;

        #endregion
    }
}
