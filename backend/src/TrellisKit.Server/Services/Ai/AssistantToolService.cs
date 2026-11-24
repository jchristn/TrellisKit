namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Security;

    /// <summary>
    /// The administrator view of the tool registry and the property-wide kill switch per tool (docs/AI_CONTRACT.md
    /// section 8.3). A disabled tool is never offered to any assistant of the property, and a call the model makes to
    /// it anyway is rejected. Switch changes are audited (ai.tool.disable, ai.tool.enable).
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class AssistantToolService
    {
        #region Private-Members

        private readonly AiServices _Ai;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="ai">AI services of the property.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public AssistantToolService(AiServices ai)
        {
            _Ai = ai ?? throw new ArgumentNullException(nameof(ai));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Canonical names of the tools whose kill switch is on.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Names (case-insensitive set).</returns>
        public async Task<HashSet<string>> DisabledAsync(CancellationToken token)
        {
            List<AssistantToolSwitch> switches = await _Ai.App.Database.Ai.Assistants.ListToolSwitchesAsync(token).ConfigureAwait(false);
            return new HashSet<string>(switches.Where(s => s.Disabled).Select(s => s.ToolName), StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The tools an assistant may offer a caller this turn: tools enabled on the assistant, in its allowed list,
        /// registered, permitted for the caller (visitors get signed-out tools only), and not switched off.
        /// </summary>
        /// <param name="assistant">Assistant.</param>
        /// <param name="caller">Caller.</param>
        /// <param name="disabled">Disabled tool names.</param>
        /// <returns>Tools.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public List<AssistantTool> Usable(Assistant assistant, AssistantCaller caller, HashSet<string> disabled)
        {
            ArgumentNullException.ThrowIfNull(assistant);
            ArgumentNullException.ThrowIfNull(caller);
            ArgumentNullException.ThrowIfNull(disabled);
            if (!assistant.ToolsEnabled || assistant.AllowedTools.Count == 0) return new List<AssistantTool>();
            return _Ai.Tools.ForCaller(caller, assistant.AllowedTools).Where(t => !disabled.Contains(t.Name)).ToList();
        }

        /// <summary>
        /// Enumerate the registered tools with their kill switches (the registry is a fixed engine bound; search
        /// matches name, description, and label; filter disabled true or false).
        /// </summary>
        /// <param name="query">Enumeration query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">404 NotAvailable, 400 ValidationFailed.</exception>
        public async Task<EnumerationResult<AssistantToolView>> EnumerateAsync(EnumerationQuery? query, CancellationToken token)
        {
            _Ai.EnsureAvailable();
            EnumerationRequest request = _Ai.App.Enumeration.Prepare(query, AiCollections.Tools, true);
            Dictionary<string, AssistantToolSwitch> switches = (await _Ai.App.Database.Ai.Assistants.ListToolSwitchesAsync(token).ConfigureAwait(false)).ToDictionary(s => s.ToolName, StringComparer.OrdinalIgnoreCase);
            IEnumerable<AssistantToolView> all = _Ai.Tools.Tools.Select(t => View(t, switches.TryGetValue(t.Name, out AssistantToolSwitch? entry) ? entry : null));
            Dictionary<string, string> errors = new Dictionary<string, string>();
            bool? wanted = request.Query.FilterBool("disabled", errors);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors, "One or more filters are invalid.");
            if (wanted.HasValue) all = all.Where(v => v.Disabled == wanted.Value);
            return CatalogEnumeration.Page(request, all.ToList(), item => item.Name,
                search: item => item.Name + " " + item.Description + " " + item.Label + " " + String.Join(" ", item.Aliases),
                labels: item => item.Labels, tags: item => item.Tags, orderKey: item => item.Name);

        }

        /// <summary>
        /// Turn the kill switch of one tool on or off (aliases accepted). Audited.
        /// </summary>
        /// <param name="actor">Administrator.</param>
        /// <param name="name">Tool name or alias.</param>
        /// <param name="request">Body.</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The tool view.</returns>
        /// <exception cref="ApiErrorException">404 NotFound, 400 ValidationFailed.</exception>
        public async Task<AssistantToolView> SwitchAsync(ContentActor actor, string name, AssistantToolSwitchRequest? request, RequestInfo info, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Ai.EnsureAvailable();
            AssistantTool tool = _Ai.Tools.Find(name) ?? throw ApiErrorException.NotFound("Tool");
            if (request?.Disabled == null) throw ApiErrorException.Field("disabled", "Required.");
            string? reason = String.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
            if (reason != null && reason.Length > 500) throw ApiErrorException.Field("reason", "At most 500 characters.");
            AssistantToolSwitch state = new AssistantToolSwitch { ToolName = tool.Name, Disabled = request.Disabled.Value, Reason = reason, UpdatedByUserId = actor.UserId, UpdatedUtc = DateTime.UtcNow };
            await _Ai.App.Database.Ai.Assistants.UpsertToolSwitchAsync(state, token).ConfigureAwait(false);
            await _Ai.App.Audit.WriteAsync(info, state.Disabled ? "ai.tool.disable" : "ai.tool.enable", "AssistantTool", tool.Name, reason, token).ConfigureAwait(false);
            _Ai.App.Logger.Info("[AssistantToolService] tool " + tool.Name + (state.Disabled ? " disabled" : " enabled") + " by " + (actor.UserId ?? "admin token"));
            return View(tool, state);
        }

        #endregion

        #region Private-Methods

        private AssistantToolView View(AssistantTool tool, AssistantToolSwitch? state)
        {
            return new AssistantToolView
            {
                Name = tool.Name,
                Description = tool.Description,
                Label = AssistantToolRegistry.RunningLabel(tool, tool.Name),
                CompletedLabel = AssistantToolRegistry.DoneLabel(tool, tool.Name),
                Aliases = _Ai.Tools.AliasesOf(tool.Name),
                WorksSignedOut = tool.WorksSignedOut,
                ParametersSchema = _Ai.Tools.Schema(tool),
                Disabled = state?.Disabled ?? false,
                DisabledReason = state?.Reason,
                DisabledByUserId = state?.UpdatedByUserId,
                DisabledUtc = state?.UpdatedUtc
            };
        }

        #endregion
    }
}
