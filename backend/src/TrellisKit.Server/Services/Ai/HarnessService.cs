namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Threading;
    using System.Threading.Tasks;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;

    /// <summary>
    /// Runs CLI agent harness calls (docs/HARNESSES.md; docs/INTEGRATION.md): only on instances with
    /// jobs.runHarnesses, only inside a job item scope, in Restricted or ReadOnly mode, in a throwaway directory with a
    /// scrubbed environment and no MCP server but the call's job-scoped one, with the scope guard leading the system
    /// instructions and a bounded timeout. Each call writes a ledger row with the harness runtime, the reported model,
    /// tokens, and cost (from the Completed event), and its cost is added to the job item. The administrator's harness
    /// profile of the runtime (docs/HARNESSES.md, "Harness profiles") sets the default model, the permission mode, the
    /// timeout, the tool call limit, whether web research is allowed, and the credential and base URL the CLI process
    /// receives.
    /// Thread safety: safe for concurrent calls.
    /// </summary>
    public class HarnessService
    {
        #region Public-Members

        /// <summary>
        /// Executable per runtime (a name on PATH or a full path). Defaults to each CLI's own name (claude, codex,
        /// gemini, cursor-agent, mux, opencode, pi). Tests point a runtime at a fake CLI.
        /// </summary>
        public Dictionary<AiExecutorRuntimeEnum, string> Executables { get; } = new Dictionary<AiExecutorRuntimeEnum, string>
        {
            [AiExecutorRuntimeEnum.ClaudeCode] = ClaudeCodeClient.DefaultExecutable,
            [AiExecutorRuntimeEnum.Codex] = CodexClient.DefaultExecutable,
            [AiExecutorRuntimeEnum.GeminiCli] = GeminiCliClient.DefaultExecutable,
            [AiExecutorRuntimeEnum.CursorCli] = CursorClient.DefaultExecutable,
            [AiExecutorRuntimeEnum.Mux] = MuxClient.DefaultExecutable,
            [AiExecutorRuntimeEnum.OpenCode] = OpenCodeClient.DefaultExecutable,
            [AiExecutorRuntimeEnum.Pi] = PiClient.DefaultExecutable
        };

        /// <summary>
        /// Parent of the per-call directories. Default: trelliskit-harness under the system temporary directory. Never
        /// null.
        /// </summary>
        public string WorkRoot
        {
            get { return _WorkRoot; }
            set { _WorkRoot = String.IsNullOrWhiteSpace(value) ? DefaultWorkRoot : value; }
        }

        /// <summary>
        /// Largest standard output accepted from one harness call, in characters. Default 4000000, minimum 10000, maximum
        /// 50000000.
        /// </summary>
        public int MaxOutputCharacters
        {
            get { return _MaxOutputCharacters; }
            set { _MaxOutputCharacters = Math.Clamp(value, 10000, 50000000); }
        }

        #endregion

        #region Private-Members

        private static readonly string DefaultWorkRoot = Path.Combine(Path.GetTempPath(), "trelliskit-harness");
        private readonly AiServices _Ai;
        private string _WorkRoot = DefaultWorkRoot;
        private int _MaxOutputCharacters = 4000000;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="ai">AI services of the property.</param>
        /// <exception cref="ArgumentNullException">ai is null.</exception>
        public HarnessService(AiServices ai)
        {
            _Ai = ai ?? throw new ArgumentNullException(nameof(ai));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Why a harness call cannot run here and now, or null when it can: harnesses are off on this instance
        /// (jobs.runHarnesses), or the call is not inside a job item (request paths never run harnesses).
        /// </summary>
        /// <returns>Reason or null.</returns>
        public string? RefusalReason()
        {
            if (!_Ai.App.Settings.Jobs.RunHarnesses) return "harnesses run only on worker instances (jobs.runHarnesses)";
            if (AiCallScope.Current?.JobRunId == null || AiCallScope.Current?.JobItemId == null) return "harnesses run only inside a job item, never in request paths";
            return null;
        }

        /// <summary>
        /// Run one harness call. Never throws for harness failures; only caller cancellation throws.
        /// </summary>
        /// <param name="call">Call.</param>
        /// <param name="token">Cancellation token (kills the process tree).</param>
        /// <returns>Result.</returns>
        /// <exception cref="ArgumentNullException">call is null.</exception>
        /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
        public async Task<HarnessCallResult> RunAsync(HarnessCall call, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(call);
            Stopwatch watch = Stopwatch.StartNew();
            HarnessCallResult result = new HarnessCallResult { Model = call.Model };
            string? refusal = RefusalReason();
            if (refusal != null) return Refuse(result, _Ai.App.Settings.Jobs.RunHarnesses ? "NoJobScope" : "HarnessesDisabled", refusal);
            if (call.Runtime == AiExecutorRuntimeEnum.ApiEndpoint) return Refuse(result, "NotSupported", "ApiEndpoint is not a harness runtime.");
            if (call.PermissionMode != CliPermissionMode.Restricted && call.PermissionMode != CliPermissionMode.ReadOnly)
                return Refuse(result, "InvalidPermissionMode", "Harnesses run in Restricted or ReadOnly mode only.");
            AiHarnessProfile? profile;
            try
            {
                profile = await _Ai.HarnessProfiles.ReadAsync(call.Runtime, token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException && e is not OutOfMemoryException)
            {
                // Never fall back to the worker's environment when the administrator's profile cannot be read.
                _Ai.App.Logger.Warn("[HarnessService] " + call.Runtime + " profile could not be read: " + e.Message);
                return Refuse(result, "HarnessProfileUnavailable", "The harness profile could not be read.");
            }
            CliPermissionMode permission = HarnessProfileService.PermissionOf(profile) ?? call.PermissionMode;
            string? model = String.IsNullOrWhiteSpace(call.Model) ? (String.IsNullOrWhiteSpace(profile?.Model) ? null : profile!.Model!.Trim()) : call.Model;
            int timeoutMs = profile?.TimeoutSeconds != null ? Math.Clamp(profile.TimeoutSeconds.Value, 30, 3600) * 1000 : Math.Max(1000, call.TimeoutMs);
            bool allowWeb = call.AllowWebResearch && (profile == null || profile.AllowWebResearch);
            result.Model = model;
            if (!call.SystemPrompt.StartsWith(ScopeGuard.ForPolicy(_Ai.App.Property.Property.AiPolicy.AllowCode), StringComparison.Ordinal)) return Refuse(result, "MissingScopeGuard", "The scope guard must lead the harness instructions.");
            if (call.Tools != null)
            {
                string? unsupported = HarnessLaunchPlanner.ToolsUnsupportedReason(call.Runtime);
                if (unsupported != null) return Refuse(result, "NotSupported", unsupported);
            }

            JobToolServer? server = null;
            HarnessSandbox? sandbox = null;
            CliAgentResult? run = null;
            string? errorCode = null;
            string? credential = null;
            DateTime startedUtc = DateTime.UtcNow;
            using (Activity? span = AiTelemetry.Span("ai.harness " + call.Runtime))
            {
                span?.SetTag("ai.harness.runtime", call.Runtime.ToString());
                try
                {
                    sandbox = new HarnessSandbox(WorkRoot, call.Runtime.ToString());
                    result.WorkingDirectory = sandbox.Work;
                    if (call.Tools != null)
                    {
                        call.Tools.BeginAttempt(AiCallScope.Current?.ExecutorPosition ?? 0, call.Runtime, profile?.MaxToolCalls);
                        server = await JobToolServer.StartAsync(call.Tools, "Tools of one background job item. Report every result through these tools; nothing else is recorded.", _Ai.App.Logger, token).ConfigureAwait(false);
                    }
                    HarnessLaunchPlan plan = HarnessLaunchPlanner.Plan(call.Runtime, sandbox, server, allowWeb);
                    foreach (KeyValuePair<string, string> file in plan.Files) sandbox.WriteFile(file.Key, file.Value);
                    Dictionary<string, string?> environment = HarnessLaunchPlanner.Environment(call.Runtime, sandbox, _Ai.App.Settings.Jobs.HarnessPassEnvironment);
                    if (profile != null)
                    {
                        credential = _Ai.HarnessProfiles.Credential(profile);
                        // A profile that asks for a stored credential never falls back to the worker's own key.
                        if (profile.CredentialType != HarnessCredentialTypeEnum.Environment && String.IsNullOrEmpty(credential)) throw new CryptographicException("The harness profile has no stored credential.");
                        HarnessLaunchPlanner.ApplyProfile(environment, call.Runtime, profile.CredentialType, credential, profile.BaseUrl);
                    }
                    startedUtc = DateTime.UtcNow;
                    using (CliAgentClientBase client = Client(call))
                    {
                        run = await client.RunAsync(new CliAgentRequest(call.Prompt)
                        {
                            SystemPrompt = call.SystemPrompt,
                            Model = model,
                            WorkingDirectory = sandbox.Work,
                            TimeoutMs = timeoutMs,
                            PermissionMode = permission,
                            Environment = environment,
                            ExtraArguments = plan.ExtraArguments,
                            MaxOutputCharacters = MaxOutputCharacters
                        }, token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    await RecordAsync(call, result, null, startedUtc, "Cancelled", AiUsageOutcomeEnum.Cancelled, watch).ConfigureAwait(false);
                    throw;
                }
                catch (CryptographicException)
                {
                    errorCode = "HarnessCredentialUnreadable";
                    result.Error = "The stored harness credential is missing or cannot be decrypted with the configured keys.";
                    _Ai.App.Logger.Warn("[HarnessService] " + call.Runtime + " credential is missing or cannot be decrypted");
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException || e is ArgumentException)
                {
                    errorCode = "HarnessSetupFailed";
                    result.Error = e.Message;
                    _Ai.App.Logger.Warn("[HarnessService] " + call.Runtime + " setup failed: " + e.Message);
                }
                finally
                {
                    server?.Dispose();
                    if (sandbox != null)
                    {
                        sandbox.Dispose();
                        result.DirectoryRemoved = sandbox.Removed;
                        if (!sandbox.Removed) _Ai.App.Logger.Warn("[HarnessService] could not remove " + sandbox.Root);
                    }
                }

                AiUsageOutcomeEnum outcome = AiUsageOutcomeEnum.Error;
                if (run != null)
                {
                    result.Success = run.Success;
                    result.Text = run.Success ? run.Text : String.Empty;
                    result.Model = String.IsNullOrWhiteSpace(run.Model) ? model : run.Model;
                    result.PromptTokens = Math.Max(0, run.Usage?.PromptTokens ?? 0);
                    result.CompletionTokens = Math.Max(0, run.Usage?.CompletionTokens ?? 0);
                    result.ReasoningTokens = Math.Max(0, run.Usage?.ReasoningTokens ?? 0);
                    result.CostMicros = run.CostUsd.HasValue && run.CostUsd.Value > 0 ? (long)Math.Round(run.CostUsd.Value * 1000000m, MidpointRounding.AwayFromZero) : 0;
                    if (run.Success)
                    {
                        outcome = AiUsageOutcomeEnum.Ok;
                    }
                    else
                    {
                        errorCode = "Harness" + run.FailureKind;
                        result.Error = String.IsNullOrEmpty(credential) || run.Error == null ? run.Error : run.Error.Replace(credential, "[redacted]", StringComparison.Ordinal);
                        result.Busy = run.FailureKind == CliFailureKind.RateLimited;
                        outcome = run.TimedOut ? AiUsageOutcomeEnum.Timeout : result.Busy ? AiUsageOutcomeEnum.Busy : AiUsageOutcomeEnum.Error;
                    }
                }
                result.ErrorCode = errorCode;
                span?.SetTag("ai.harness.outcome", outcome.ToString());
                await RecordAsync(call, result, run, startedUtc, errorCode, outcome, watch).ConfigureAwait(false);
            }
            return result;
        }

        #endregion

        #region Private-Methods

        private HarnessCallResult Refuse(HarnessCallResult result, string code, string message)
        {
            result.ErrorCode = code;
            result.Error = message;
            return result;
        }

        private CliAgentClientBase Client(HarnessCall call)
        {
            ClientOptions options = new ClientOptions { Logging = _Ai.App.Logger.Module, CallDetailCapture = CallDetailCapture.None, RedactCredentials = true };
            string executable = Executables.TryGetValue(call.Runtime, out string? configured) && !String.IsNullOrWhiteSpace(configured) ? configured : call.Runtime.ToString().ToLowerInvariant();
            switch (call.Runtime)
            {
                case AiExecutorRuntimeEnum.ClaudeCode: return new ClaudeCodeClient(executable, options);
                case AiExecutorRuntimeEnum.Codex: return new CodexClient(executable, options);
                case AiExecutorRuntimeEnum.GeminiCli: return new GeminiCliClient(executable, options);
                case AiExecutorRuntimeEnum.CursorCli: return new CursorClient(executable, options);
                case AiExecutorRuntimeEnum.Mux: return new MuxClient(executable, options) { Endpoint = String.IsNullOrWhiteSpace(call.MuxEndpoint) ? null : call.MuxEndpoint };
                case AiExecutorRuntimeEnum.OpenCode: return new OpenCodeClient(executable, options);
                case AiExecutorRuntimeEnum.Pi: return new PiClient(executable, options);
                default: throw new InvalidOperationException("Not a harness runtime: " + call.Runtime + ".");
            }
        }

        private static AiCallDetail Detail(HarnessCall call, CliAgentResult? run, DateTime startedUtc)
        {
            // docs/AI_TELEMETRY.md, "Harness calls": whatever the CLI reports. Its events are stamped as they arrive, so
            // the first and last text or reasoning events give the time to first and last token.
            AiCallDetail detail = new AiCallDetail();
            if (call.Tools != null)
            {
                detail.ToolCalls = call.Tools.AttemptCalls();
                detail.ToolExecMs = call.Tools.AttemptExecMs();
            }
            if (run == null) return detail;
            detail.ExitCode = run.ExitCode;
            detail.ResponseId = run.SessionId;
            detail.ProviderModel = run.Model;
            detail.Streamed = true;
            AiCallDetails.ApplyUsage(detail, run.Usage);
            List<CliAgentEvent> output = run.Events.Where(e => e.Kind == CliAgentEventKind.TextDelta || e.Kind == CliAgentEventKind.ReasoningDelta).ToList();
            if (output.Count > 0)
            {
                detail.TimeToFirstTokenMs = Math.Max(0, (long)(output[0].TimestampUtc - startedUtc).TotalMilliseconds);
                detail.TimeToLastTokenMs = Math.Max(detail.TimeToFirstTokenMs.Value, (long)(output[output.Count - 1].TimestampUtc - startedUtc).TotalMilliseconds);
                detail.StreamingMs = detail.TimeToLastTokenMs - detail.TimeToFirstTokenMs;
                detail.Chunks = output.Count;
            }
            int reported = run.Events.Count(e => e.Kind == CliAgentEventKind.ToolUse);
            if (reported > 0 || detail.ToolCalls == null) detail.ToolCalls = Math.Max(reported, detail.ToolCalls ?? 0);
            return detail;
        }

        private async Task RecordAsync(HarnessCall call, HarnessCallResult result, CliAgentResult? run, DateTime startedUtc, string? errorCode, AiUsageOutcomeEnum outcome, Stopwatch watch)
        {
            result.LatencyMs = watch.ElapsedMilliseconds;
            AiCallDetail detail = Detail(call, run, startedUtc);
            detail.TokensPerSecond = AiCallDetails.TokensPerSecond(result.CompletionTokens + result.ReasoningTokens, null, result.Success ? result.LatencyMs : null);
            AiUsageRecord record = new AiUsageRecord
            {
                Purpose = AssistantPurposeEnum.Task,
                Outcome = outcome,
                IsTest = call.IsTest,
                PromptTokens = result.PromptTokens,
                CompletionTokens = result.CompletionTokens,
                ReasoningTokens = result.ReasoningTokens,
                LatencyMs = result.LatencyMs,
                CostMicros = result.CostMicros,
                OccurredUtc = DateTime.UtcNow,
                TimeToFirstTokenMs = detail.TimeToFirstTokenMs,
                Detail = detail
            };
            string? model = result.Model ?? call.Model;
            if (model != null && model.Length > 200) model = model.Substring(0, 200);
            await _Ai.Usage.RecordHarnessAsync(record, call.Runtime, model, call.Runtime == AiExecutorRuntimeEnum.Mux ? call.MuxEndpoint : null, errorCode).ConfigureAwait(false);
        }

        #endregion
    }
}
