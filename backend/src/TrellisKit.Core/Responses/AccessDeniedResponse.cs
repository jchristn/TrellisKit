namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// Body of 401 Unauthorized and 402 MembershipRequired from the access model (docs/ACCESS_MODELS_CONTRACT.md
    /// section 3.4): the error plus what is required and, when known, the feature and the object.
    /// </summary>
    public class AccessDeniedResponse : ErrorResponse
    {
        #region Public-Members

        /// <summary>
        /// What the caller needs: SignIn or Membership.
        /// </summary>
        public string Required { get; set; } = String.Empty;

        /// <summary>
        /// Feature key of the route or object (for example feed), or null.
        /// </summary>
        public string? Feature { get; set; } = null;

        /// <summary>
        /// Object type (for example FeedPost), or null for route-level denials.
        /// </summary>
        public string? TargetType { get; set; } = null;

        /// <summary>
        /// Object id, or null for route-level denials.
        /// </summary>
        public string? TargetId { get; set; } = null;

        #endregion
    }
}
