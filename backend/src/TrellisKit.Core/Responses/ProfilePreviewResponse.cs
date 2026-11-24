namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Response of POST /api/v1/admin/profile-fields/preview (bounded by the field count).
    /// </summary>
    public class ProfilePreviewResponse
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
        /// Fields in display order.
        /// </summary>
        public List<ProfilePreviewItem> Items
        {
            get { return _Items; }
            set { _Items = value ?? new List<ProfilePreviewItem>(); }
        }

        #endregion

        #region Private-Members

        private List<ProfilePreviewItem> _Items = new List<ProfilePreviewItem>();

        #endregion
    }
}
