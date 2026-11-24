namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;

    /// <summary>
    /// Body of PUT /api/v1/admin/members/{id}/profile.
    /// </summary>
    public class MemberProfileUpdateRequest
    {
        #region Public-Members

        /// <summary>
        /// Keys and values (null clears).
        /// </summary>
        public Dictionary<string, JsonElement>? Profile { get; set; } = null;

        #endregion
    }
}
