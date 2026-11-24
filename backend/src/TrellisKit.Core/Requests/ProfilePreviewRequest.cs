namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Body of POST /api/v1/admin/profile-fields/preview.
    /// </summary>
    public class ProfilePreviewRequest
    {
        #region Public-Members

        /// <summary>
        /// Surface.
        /// </summary>
        public ProfilePreviewTargetEnum Target { get; set; } = ProfilePreviewTargetEnum.ProfilePage;

        /// <summary>
        /// Viewer.
        /// </summary>
        public ProfileViewerEnum Viewer { get; set; } = ProfileViewerEnum.Member;

        /// <summary>
        /// Unsaved drafts to preview instead of the saved definitions (non-built-in fields; built-ins come from the saved set).
        /// </summary>
        public List<ProfileField>? Fields { get; set; } = null;

        /// <summary>
        /// Sample values by key.
        /// </summary>
        public Dictionary<string, JsonElement>? Values { get; set; } = null;

        #endregion
    }
}
