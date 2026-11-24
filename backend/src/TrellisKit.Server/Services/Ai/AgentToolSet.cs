namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The job tools of one agent call and the single place their calls are checked and run, whichever executor makes
    /// them: the job-scoped MCP server (CLI agent harnesses) and the API tool loop (model endpoints) both call
    /// <see cref="InvokeAsync"/>. A call is refused before its handler runs when the tool is unknown, the arguments are
    /// not a JSON object, are larger than <see cref="MaxArgumentsCharacters"/>, or do not match the tool's schema, or the
    /// attempt has used its tool call limit; every call, accepted or not, is recorded in order.
    /// Thread safety: safe for concurrent calls (a harness may call tools in parallel).
    /// </summary>
    public class AgentToolSet
    {
        #region Public-Members

        /// <summary>
        /// Largest arguments document accepted, in characters.
        /// </summary>
        public const int MaxArgumentsCharacters = 32768;

        /// <summary>
        /// Most tools in one agent call.
        /// </summary>
        public const int MaxTools = 32;

        /// <summary>
        /// The tools, in declaration order.
        /// </summary>
        public IReadOnlyList<AgentTool> Tools
        {
            get { return _Tools; }
        }

        /// <summary>
        /// Most tool calls one executor attempt may make. Minimum 1, maximum 500.
        /// </summary>
        public int MaxCallsPerAttempt { get; }

        /// <summary>
        /// Job key of the job item the calls belong to, or null.
        /// </summary>
        public string? JobKey { get; }

        /// <summary>
        /// Job run of the job item, or null.
        /// </summary>
        public string? JobRunId { get; }

        /// <summary>
        /// Job item the calls belong to, or null.
        /// </summary>
        public string? JobItemId { get; }

        #endregion

        #region Private-Members

        private readonly List<AgentTool> _Tools;
        private readonly Dictionary<string, JsonElement> _Schemas = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        private readonly List<AgentToolRecord> _Records = new List<AgentToolRecord>();
        private readonly object _Lock = new object();
        private readonly AppLogger? _Logger;
        private int _AttemptCalls = 0;
        private int _AttemptLimit = 0;
        private long _AttemptExecMs = 0;
        private int _ExecutorPosition = 0;
        private AiExecutorRuntimeEnum _Runtime = AiExecutorRuntimeEnum.ApiEndpoint;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate. The job context is taken from the ambient <see cref="AiCallScope"/>.
        /// </summary>
        /// <param name="tools">Tools (1 to <see cref="MaxTools"/>, unique names, each with a handler and an object schema).</param>
        /// <param name="maxCallsPerAttempt">Most calls per executor attempt (clamped to 1 to 500).</param>
        /// <param name="logger">Logger for handler failures, or null.</param>
        /// <exception cref="ArgumentNullException">tools is null.</exception>
        /// <exception cref="ArgumentException">No tools, too many, a duplicate name, a missing handler, or a schema that is not a JSON object.</exception>
        public AgentToolSet(IEnumerable<AgentTool> tools, int maxCallsPerAttempt, AppLogger? logger = null)
        {
            ArgumentNullException.ThrowIfNull(tools);
            _Tools = tools.Where(t => t != null).ToList();
            if (_Tools.Count == 0) throw new ArgumentException("At least one tool is required.", nameof(tools));
            if (_Tools.Count > MaxTools) throw new ArgumentException("At most " + MaxTools + " tools.", nameof(tools));
            foreach (AgentTool tool in _Tools)
            {
                if (tool.Handler == null) throw new ArgumentException("Tool " + tool.Name + " has no handler.", nameof(tools));
                if (_Schemas.ContainsKey(tool.Name)) throw new ArgumentException("Duplicate tool name " + tool.Name + ".", nameof(tools));
                JsonElement schema;
                try
                {
                    using (JsonDocument document = JsonDocument.Parse(tool.ParametersSchema)) schema = document.RootElement.Clone();
                }
                catch (JsonException e)
                {
                    throw new ArgumentException("Tool " + tool.Name + " has a schema that is not valid JSON: " + e.Message, nameof(tools));
                }
                if (schema.ValueKind != JsonValueKind.Object) throw new ArgumentException("Tool " + tool.Name + " needs an object schema.", nameof(tools));
                _Schemas[tool.Name] = schema;
            }
            MaxCallsPerAttempt = Math.Clamp(maxCallsPerAttempt, 1, 500);
            _AttemptLimit = MaxCallsPerAttempt;
            _Logger = logger;
            AiCallScope? scope = AiCallScope.Current;
            JobKey = scope?.JobKey;
            JobRunId = scope?.JobRunId;
            JobItemId = scope?.JobItemId;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The parsed schema of a tool.
        /// </summary>
        /// <param name="name">Tool name.</param>
        /// <returns>Schema.</returns>
        /// <exception cref="KeyNotFoundException">No such tool.</exception>
        public JsonElement Schema(string name)
        {
            return _Schemas[name];
        }

        /// <summary>
        /// Start counting calls for the next executor attempt.
        /// </summary>
        /// <param name="position">Executor position.</param>
        /// <param name="runtime">Executor runtime.</param>
        public void BeginAttempt(int position, AiExecutorRuntimeEnum runtime)
        {
            BeginAttempt(position, runtime, null);
        }

        /// <summary>
        /// Start counting calls for the next executor attempt, with a call limit of its own (a harness profile's
        /// maxToolCalls).
        /// </summary>
        /// <param name="position">Executor position.</param>
        /// <param name="runtime">Executor runtime.</param>
        /// <param name="maxCalls">Most calls in this attempt (1 to 500), or null for <see cref="MaxCallsPerAttempt"/>.</param>
        public void BeginAttempt(int position, AiExecutorRuntimeEnum runtime, int? maxCalls)
        {
            lock (_Lock)
            {
                _AttemptCalls = 0;
                _AttemptExecMs = 0;
                _AttemptLimit = maxCalls == null ? MaxCallsPerAttempt : Math.Clamp(maxCalls.Value, 1, 500);
                _ExecutorPosition = position;
                _Runtime = runtime;
            }
        }

        /// <summary>
        /// Calls made in the current executor attempt, accepted or not.
        /// </summary>
        /// <returns>Count.</returns>
        public int AttemptCalls()
        {
            lock (_Lock)
            {
                return _AttemptCalls;
            }
        }

        /// <summary>
        /// Milliseconds the tool handlers ran in the current executor attempt.
        /// </summary>
        /// <returns>Total.</returns>
        public long AttemptExecMs()
        {
            lock (_Lock)
            {
                return _AttemptExecMs;
            }
        }

        /// <summary>
        /// Every call so far, in order.
        /// </summary>
        /// <returns>A copy of the records.</returns>
        public List<AgentToolRecord> Records()
        {
            lock (_Lock)
            {
                return new List<AgentToolRecord>(_Records);
            }
        }

        /// <summary>
        /// Check and run one call. Never throws for a bad call: refusals come back as a rejected result the agent can
        /// read.
        /// </summary>
        /// <param name="name">Tool name the agent asked for, or null.</param>
        /// <param name="argumentsJson">Arguments as JSON text, or null for none.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
        public async Task<AgentToolResult> InvokeAsync(string? name, string? argumentsJson, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string asked = (name ?? String.Empty).Trim();
            string arguments = String.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson!;
            AgentToolRecord record = new AgentToolRecord { ToolName = asked.Length > 100 ? asked.Substring(0, 100) : asked, ArgumentsJson = arguments.Length > MaxArgumentsCharacters ? arguments.Substring(0, MaxArgumentsCharacters) : arguments };
            AiExecutorRuntimeEnum runtime;
            bool overLimit;
            int limit;
            lock (_Lock)
            {
                record.Sequence = _Records.Count + 1;
                record.ExecutorPosition = _ExecutorPosition;
                _Records.Add(record);
                _AttemptCalls++;
                limit = _AttemptLimit;
                overLimit = _AttemptCalls > limit;
                runtime = _Runtime;
            }

            AgentTool? tool = _Tools.FirstOrDefault(t => String.Equals(t.Name, asked, StringComparison.Ordinal));
            if (tool == null) return Refuse(record, "UnknownTool", "There is no tool named " + (asked.Length == 0 ? "(empty)" : asked) + ". Available: " + String.Join(", ", _Tools.Select(t => t.Name)) + ".");
            if (overLimit) return Refuse(record, "ToolLimit", "Not run: the limit of " + limit + " tool calls was reached. Stop and summarize what you reported.");
            if (arguments.Length > MaxArgumentsCharacters) return Refuse(record, "ArgumentsTooLarge", "The arguments are larger than " + MaxArgumentsCharacters + " characters.");
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(arguments, new JsonDocumentOptions { MaxDepth = 16 });
            }
            catch (JsonException)
            {
                return Refuse(record, "InvalidJson", "The arguments are not valid JSON.");
            }
            using (document)
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object) return Refuse(record, "InvalidArguments", "The arguments must be a JSON object.");
                string? problem = ToolArgumentValidator.Validate(_Schemas[tool.Name], document.RootElement);
                if (problem != null) return Refuse(record, "InvalidArguments", "Invalid arguments: " + problem + " Correct them and call " + tool.Name + " again.");
                AgentToolInvocation invocation = new AgentToolInvocation
                {
                    ToolName = tool.Name,
                    Arguments = document.RootElement,
                    ArgumentsJson = document.RootElement.GetRawText(),
                    Sequence = record.Sequence,
                    Runtime = runtime,
                    JobKey = JobKey,
                    JobRunId = JobRunId,
                    JobItemId = JobItemId
                };
                AgentToolResult result;
                Stopwatch watch = Stopwatch.StartNew();
                try
                {
                    result = await tool.Handler!(invocation, token).ConfigureAwait(false) ?? AgentToolResult.Reject("The tool returned no result.");
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    _Logger?.Warn("[AgentToolSet] tool " + tool.Name + " failed for job item " + (JobItemId ?? "(none)") + ": " + e.Message);
                    Timed(record, watch.ElapsedMilliseconds);
                    return Refuse(record, "HandlerFailed", "The tool failed on the server. Try again later or continue without it.");
                }
                Timed(record, watch.ElapsedMilliseconds);
                lock (_Lock)
                {
                    record.Accepted = result.Accepted;
                    record.Message = result.Message;
                    record.DataJson = result.Accepted ? result.DataJson : null;
                }
                return result;
            }
        }

        #endregion

        #region Private-Methods

        private void Timed(AgentToolRecord record, long milliseconds)
        {
            lock (_Lock)
            {
                record.DurationMs = milliseconds;
                _AttemptExecMs += milliseconds;
            }
        }

        private AgentToolResult Refuse(AgentToolRecord record, string error, string message)
        {
            lock (_Lock)
            {
                record.Accepted = false;
                record.Error = error;
                record.Message = message;
            }
            return AgentToolResult.Reject(message);
        }

        #endregion
    }
}
