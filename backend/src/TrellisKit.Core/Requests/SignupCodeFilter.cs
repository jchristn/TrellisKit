namespace TrellisKit.Core.Requests
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Admin sign-up code list filter.
    /// </summary>
    public class SignupCodeFilter
    {
        #region Public-Members

        /// <summary>
        /// Derived status, or null.
        /// </summary>
        public SignupCodeStatusEnum? Status { get; set; } = null;

        /// <summary>
        /// Only codes of this batch, or null.
        /// </summary>
        public string? BatchId { get; set; } = null;

        #endregion
    }
}
