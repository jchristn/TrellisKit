namespace TrellisKit.Server.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Access rules of one private media purpose (Ticket, Message), registered by the owning module with
    /// <see cref="MediaService.RegisterPrivateScope"/>. The media service asks <see cref="CanAccess"/> on every read of a
    /// private object, so a viewer removed from the scope (a conversation member who left, a ticket that was deleted) loses
    /// access at once, even with an unexpired URL.
    /// </summary>
    public class PrivateMediaScope
    {
        #region Public-Members

        /// <summary>
        /// Whether a viewer may read media of a scope: arguments are the viewer user identifier and the scope identifier
        /// (ticket or conversation). Default: nobody.
        /// </summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public Func<string, string, CancellationToken, Task<bool>> CanAccess
        {
            get { return _CanAccess; }
            set { _CanAccess = value ?? throw new ArgumentNullException(nameof(CanAccess)); }
        }

        /// <summary>
        /// Whether uploads of this purpose may be video. Default: never.
        /// </summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public Func<bool> AllowVideo
        {
            get { return _AllowVideo; }
            set { _AllowVideo = value ?? throw new ArgumentNullException(nameof(AllowVideo)); }
        }

        /// <summary>
        /// Whether uploads need the member's posting and media permissions (canPost, canPostMedia, not muted). False for
        /// support tickets, so a muted or restricted member can still send a screenshot to support. Default true.
        /// </summary>
        public bool RequirePostingPermissions { get; set; } = true;

        #endregion

        #region Private-Members

        private Func<string, string, CancellationToken, Task<bool>> _CanAccess = (u, s, t) => Task.FromResult(false);
        private Func<bool> _AllowVideo = () => false;

        #endregion
    }
}
