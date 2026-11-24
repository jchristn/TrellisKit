namespace TrellisKit.Server.Services
{
    using System;
    using TrellisKit.Core.Models;

    /// <summary>
    /// An authenticated caller of the platform routes: a platform administrator session, or the break-glass admin
    /// token.
    /// </summary>
    public sealed class PlatformCaller
    {
        #region Public-Members

        /// <summary>
        /// The platform administrator, or null for the break-glass token.
        /// </summary>
        public PlatformAdmin? Admin { get; set; } = null;

        /// <summary>
        /// Platform session id, or null for the break-glass token.
        /// </summary>
        public string? SessionId { get; set; } = null;

        /// <summary>
        /// Whether the caller used the break-glass admin token.
        /// </summary>
        public bool IsBreakGlass
        {
            get { return Admin == null; }
        }

        /// <summary>
        /// Actor id recorded in audits (pad_ id), or null for the break-glass token.
        /// </summary>
        public string? ActorId
        {
            get { return Admin?.Id; }
        }

        /// <summary>
        /// Actor name recorded in audits.
        /// </summary>
        public string ActorName
        {
            get { return Admin == null ? "break-glass" : "platform admin " + Admin.DisplayName; }
        }

        /// <summary>
        /// Source IP of the request.
        /// </summary>
        public string? SourceIp { get; set; } = null;

        #endregion
    }
}
