namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;

    /// <summary>
    /// The ticket workflow passes run by TicketDueWorker and TicketAutoCloseWorker (docs/PHASE5_CONTRACT.md section 6.2):
    /// tickets past their first response target get TicketOverdue (and staff_ticket_overdue) once, to the assignee or every
    /// support staff member when unassigned; Resolved tickets with no member reply for support.autoCloseDays become Closed.
    /// Each pass takes the evaluation time so tests run them with a fixed clock.
    /// </summary>
    public class TicketWorkflowService
    {
        #region Public-Members

        /// <summary>
        /// Tickets handled per pass. Default 100. Minimum 1, maximum 1000.
        /// </summary>
        public int BatchSize
        {
            get { return _BatchSize; }
            set { _BatchSize = Math.Clamp(value, 1, 1000); }
        }

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;
        private readonly SupportModule _Module;
        private int _BatchSize = 100;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <param name="module">Support module.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public TicketWorkflowService(ApplicationServices services, SupportModule module)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
            _Module = module ?? throw new ArgumentNullException(nameof(module));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// One due pass: mark overdue tickets (each exactly once), notify, and refresh the open and overdue gauges.
        /// </summary>
        /// <param name="nowUtc">Evaluation time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Tickets marked overdue.</returns>
        public async Task<int> RunDueAsync(DateTime nowUtc, CancellationToken token = default)
        {
            using (Activity? activity = TrellisKitTelemetry.StartSpan("job TicketDueWorker"))
            {
                List<Ticket> claimed = await _Services.Database.Support.ClaimOverdueAsync(nowUtc, _BatchSize, token).ConfigureAwait(false);
                foreach (Ticket ticket in claimed)
                {
                    token.ThrowIfCancellationRequested();
                    await _Module.Notifier.OverdueAsync(ticket, token).ConfigureAwait(false);
                }
                SupportTelemetry.SetGauges(await _Services.Database.Support.CountOpenAsync(token).ConfigureAwait(false), await _Services.Database.Support.CountOverdueAsync(nowUtc, token).ConfigureAwait(false));
                activity?.SetTag("job.batch_size", claimed.Count);
                return claimed.Count;
            }
        }

        /// <summary>
        /// One auto close pass: Resolved tickets resolved at least support.autoCloseDays ago with no member reply since
        /// become Closed (a staff timeline line, TicketUpdate, ticket_resolved, and ticket.updated).
        /// </summary>
        /// <param name="nowUtc">Evaluation time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Tickets closed.</returns>
        public async Task<int> RunAutoCloseAsync(DateTime nowUtc, CancellationToken token = default)
        {
            using (Activity? activity = TrellisKitTelemetry.StartSpan("job TicketAutoCloseWorker"))
            {
                DateTime cutoff = nowUtc.AddDays(-_Services.Settings.Support.AutoCloseDays);
                List<Ticket> due = await _Services.Database.Support.ListAutoCloseAsync(cutoff, _BatchSize, token).ConfigureAwait(false);
                int closed = 0;
                foreach (Ticket ticket in due)
                {
                    token.ThrowIfCancellationRequested();
                    ticket.Status = TicketStatusEnum.Closed;
                    ticket.ClosedUtc = nowUtc;
                    ticket.LastActivityUtc = nowUtc;
                    ticket.UpdatedUtc = nowUtc;
                    TicketMessage line = TicketService.SystemLine(ticket, "Closed automatically after " + _Services.Settings.Support.AutoCloseDays + " days without a reply from the requester.", TicketStatusEnum.Closed, nowUtc);
                    if (!await _Services.Database.Support.UpdateTicketAsync(ticket, new List<TicketMessage> { line }, TicketStatusEnum.Resolved, token).ConfigureAwait(false)) continue;
                    closed++;
                    SupportTelemetry.Count("status", ticket.TypeKey, "system");
                    await _Module.Notifier.StatusChangedAsync(ticket, null, true, token).ConfigureAwait(false);
                }
                activity?.SetTag("job.batch_size", closed);
                return closed;
            }
        }

        #endregion
    }
}
