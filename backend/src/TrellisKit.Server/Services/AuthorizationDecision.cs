namespace TrellisKit.Server.Services
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Result of the central authorization check.
    /// </summary>
    public class AuthorizationDecision
    {
        #region Public-Members

        /// <summary>
        /// Outcome.
        /// </summary>
        public AuthorizationOutcomeEnum Outcome { get; set; } = AuthorizationOutcomeEnum.Permitted;

        /// <summary>
        /// Route group of the request path.
        /// </summary>
        public RouteGroupEnum Group { get; set; } = RouteGroupEnum.Member;

        /// <summary>
        /// HTTP status to send when denied.
        /// </summary>
        public int StatusCode { get; set; } = 200;

        /// <summary>
        /// Error code when denied, otherwise empty.
        /// </summary>
        public string Error { get; set; } = String.Empty;

        /// <summary>
        /// Message when denied, otherwise empty.
        /// </summary>
        public string Message { get; set; } = String.Empty;

        /// <summary>
        /// What the caller needs (SignIn or Membership) for access model denials, or null.
        /// </summary>
        public string? Required { get; set; } = null;

        /// <summary>
        /// Feature key of the denied route (for example feed), or null.
        /// </summary>
        public string? Feature { get; set; } = null;

        /// <summary>
        /// Keys of the documents to accept (403 DocumentAcceptanceRequired), or null.
        /// </summary>
        public System.Collections.Generic.List<string>? Documents { get; set; } = null;

        /// <summary>
        /// Whether the request may proceed.
        /// </summary>
        public bool IsPermitted
        {
            get { return Outcome == AuthorizationOutcomeEnum.Permitted; }
        }

        #endregion
    }
}
