namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// 409 AccountLinkRequired body: the error plus the methods the member can sign in with.
    /// </summary>
    public class AccountLinkErrorResponse : ErrorResponse
    {
        #region Public-Members

        /// <summary>
        /// Existing sign-in methods. Never null.
        /// </summary>
        public List<SignInMethodEnum> ExistingMethods
        {
            get { return _ExistingMethods; }
            set { _ExistingMethods = value ?? new List<SignInMethodEnum>(); }
        }

        #endregion

        #region Private-Members

        private List<SignInMethodEnum> _ExistingMethods = new List<SignInMethodEnum>();

        #endregion
    }
}
