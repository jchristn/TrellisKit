namespace TrellisKit.Server.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Security;

    /// <summary>
    /// Writes and reads audit records for administrative and account actions. Writing never fails the caller.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class AuditService
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly AppLogger _Logger;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public AuditService(DatabaseDriverBase database, AppLogger logger)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Record an action. Failures are logged and swallowed.
        /// </summary>
        /// <param name="actor">Acting context, or null for the system.</param>
        /// <param name="sourceIp">Source IP, or null.</param>
        /// <param name="action">Action, for example member.ban.</param>
        /// <param name="targetType">Target type, or null.</param>
        /// <param name="targetId">Target identifier, or null.</param>
        /// <param name="summary">Summary, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task WriteAsync(RequestContext? actor, string? sourceIp, string action, string? targetType, string? targetId, string? summary, CancellationToken token = default)
        {
            AuditRecord record = new AuditRecord
            {
                Id = IdGenerator.GenerateAuditRecordId(),
                ActorUserId = actor?.UserId ?? actor?.PlatformAdminId,
                ActorName = actor == null ? "system" : actor.PrincipalKind == PrincipalKindEnum.Delegated ? actor.PrincipalName : actor.IsBreakGlass ? BreakGlassActor(action) : actor.PrincipalName,
                Action = action,
                TargetType = targetType,
                TargetId = targetId,
                Summary = summary != null && summary.Length > 1000 ? summary.Substring(0, 1000) : summary,
                SourceIp = sourceIp,
                CreatedUtc = DateTime.UtcNow
            };
            try
            {
                await _Database.Audit.CreateAsync(record, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _Logger.Warn("[AuditService] failed to record " + action + ": " + e.Message);
            }
        }

        /// <summary>
        /// Record an action for a request.
        /// </summary>
        /// <param name="request">Request info (actor and source IP).</param>
        /// <param name="action">Action.</param>
        /// <param name="targetType">Target type, or null.</param>
        /// <param name="targetId">Target identifier, or null.</param>
        /// <param name="summary">Summary, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The request is null.</exception>
        public Task WriteAsync(RequestInfo request, string action, string? targetType, string? targetId, string? summary, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            return WriteAsync(request.Context, request.SourceIp, action, targetType, targetId, summary, token);
        }

        #endregion

        #region Private-Methods

        private static string BreakGlassActor(string action)
        {
            // Moderation by the break-glass token is recorded as "break-glass token", distinct from "system".
            return action.StartsWith("moderation.", StringComparison.Ordinal) ? ModerationService.BreakGlassName : "admin token";
        }

        #endregion
    }
}
