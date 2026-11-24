namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Response of POST /api/v1/admin/profile-fields/reorder.
    /// </summary>
    public class ProfileReorderResponse
    {
        #region Public-Members

        /// <summary>
        /// Field ids in profile order.
        /// </summary>
        public List<string> Order
        {
            get { return _Order; }
            set { _Order = value ?? new List<string>(); }
        }

        #endregion

        #region Private-Members

        private List<string> _Order = new List<string>();

        #endregion
    }
}
