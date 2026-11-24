namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// An admin forum with a bounded moderators preview.
    /// </summary>
    public class AdminForumDetail : AdminForumView
    {
        #region Public-Members

        /// <summary>
        /// At most 10 moderators covering the forum, with the total and the enumerate path.
        /// </summary>
        public BoundedPreview<ForumModeratorView> Moderators
        {
            get { return _Moderators; }
            set { _Moderators = value ?? new BoundedPreview<ForumModeratorView>(); }
        }

        #endregion

        #region Private-Members

        private BoundedPreview<ForumModeratorView> _Moderators = new BoundedPreview<ForumModeratorView>();

        #endregion
    }
}
