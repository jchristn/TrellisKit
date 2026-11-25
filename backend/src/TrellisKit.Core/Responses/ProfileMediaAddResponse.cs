namespace TrellisKit.Core.Responses
{
    using System.Collections.Generic;

    /// <summary>
    /// Response of POST /api/v1/users/{id}/media: the new items in request order (at most 10, fixed bound).
    /// </summary>
    public class ProfileMediaAddResponse
    {
        #region Public-Members

        /// <summary>
        /// New items.
        /// </summary>
        public List<ProfileMediaItemView> Objects { get; set; } = new List<ProfileMediaItemView>();

        #endregion
    }
}
