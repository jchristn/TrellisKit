namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Body of POST /api/v1/admin/profile-fields/reorder.
    /// </summary>
    public class ProfileReorderRequest
    {
        #region Public-Members

        /// <summary>
        /// Every field id of the property (built-ins included), in the new order; at most 104.
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
