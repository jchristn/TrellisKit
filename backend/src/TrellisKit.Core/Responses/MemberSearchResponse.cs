namespace TrellisKit.Core.Responses
{
    using System.Collections.Generic;

    /// <summary>
    /// Result of GET /api/v1/members/search.
    /// </summary>
    public class MemberSearchResponse
    {
        #region Public-Members

        /// <summary>
        /// Matching members in ranked order. Never null.
        /// </summary>
        public List<UserSummary> Items
        {
            get { return _Items; }
            set { _Items = value ?? new List<UserSummary>(); }
        }

        #endregion

        #region Private-Members

        private List<UserSummary> _Items = new List<UserSummary>();

        #endregion
    }
}
