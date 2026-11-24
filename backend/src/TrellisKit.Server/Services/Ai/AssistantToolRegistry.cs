namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using PolyPrompt.Models;
    using TrellisKit.Core;
    using TrellisKit.Core.Exceptions;

    /// <summary>
    /// The assistant tool registry of one property (docs/AI_CONTRACT.md section 8.1). Feature modules register their
    /// tools during construction (searchLibrary, findEvents, findVenues); names saved in an assistant's tools.allowed
    /// resolve through <see cref="Canonical"/>, which also accepts aliases (findGyms is a built-in alias of findVenues,
    /// docs/PHASE4_CONTRACT.md section 5.11). Registration is not thread safe; reads after construction are.
    /// </summary>
    public class AssistantToolRegistry
    {
        #region Public-Members

        /// <summary>
        /// Every registered tool, in registration order.
        /// </summary>
        public IReadOnlyList<AssistantTool> Tools
        {
            get { return _Tools; }
        }

        /// <summary>
        /// Most characters of tool output handed to the model. Default 4000, minimum 200, maximum 100000.
        /// </summary>
        public int MaxOutputCharacters
        {
            get { return _MaxOutputCharacters; }
            set
            {
                if (value < 200 || value > 100000) throw new ArgumentOutOfRangeException(nameof(MaxOutputCharacters), "MaxOutputCharacters must be 200 to 100000.");
                _MaxOutputCharacters = value;
            }
        }

        /// <summary>
        /// Time limit of one tool call in milliseconds. Default 5000, minimum 100, maximum 60000.
        /// </summary>
        public int TimeoutMs
        {
            get { return _TimeoutMs; }
            set
            {
                if (value < 100 || value > 60000) throw new ArgumentOutOfRangeException(nameof(TimeoutMs), "TimeoutMs must be 100 to 60000.");
                _TimeoutMs = value;
            }
        }

        #endregion

        #region Private-Members

        private readonly List<AssistantTool> _Tools = new List<AssistantTool>();
        private readonly Dictionary<string, AssistantTool> _ByName = new Dictionary<string, AssistantTool>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { "findGyms", "findVenues" } };
        private int _MaxOutputCharacters = 4000;
        private int _TimeoutMs = 5000;
        private readonly Dictionary<string, JsonElement> _Schemas = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register a tool and its aliases.
        /// </summary>
        /// <param name="tool">Tool.</param>
        /// <exception cref="ArgumentNullException">The tool is null.</exception>
        /// <exception cref="ArgumentException">The name is empty or the handler is missing.</exception>
        /// <exception cref="InvalidOperationException">The name or an alias is already taken.</exception>
        public void Register(AssistantTool tool)
        {
            ArgumentNullException.ThrowIfNull(tool);
            if (String.IsNullOrWhiteSpace(tool.Name)) throw new ArgumentException("An assistant tool needs a name.", nameof(tool));
            if (tool.Handler == null) throw new ArgumentException("Assistant tool " + tool.Name + " needs a handler.", nameof(tool));
            if (_ByName.ContainsKey(tool.Name)) throw new InvalidOperationException("Assistant tool " + tool.Name + " is already registered.");
            if (_Aliases.TryGetValue(tool.Name, out string? aliased) && !String.Equals(aliased, tool.Name, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(tool.Name + " is an alias of " + aliased + ".");
            foreach (string alias in tool.Aliases)
            {
                if (_ByName.ContainsKey(alias)) throw new InvalidOperationException("Alias " + alias + " is a registered tool name.");
                if (_Aliases.TryGetValue(alias, out string? owner) && !String.Equals(owner, tool.Name, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Alias " + alias + " already points to " + owner + ".");
            }
            _ByName[tool.Name] = tool;
            _Tools.Add(tool);
            foreach (string alias in tool.Aliases) _Aliases[alias] = tool.Name;
        }

        /// <summary>
        /// Remove a tool (tests). Its aliases stay reserved for the name.
        /// </summary>
        /// <param name="name">Tool name.</param>
        /// <returns>True when removed.</returns>
        public bool Unregister(string name)
        {
            if (String.IsNullOrEmpty(name) || !_ByName.TryGetValue(name, out AssistantTool? tool)) return false;
            _ByName.Remove(name);
            _Tools.Remove(tool);
            lock (_Schemas) _Schemas.Remove(tool.Name);
            return true;
        }

        /// <summary>
        /// The canonical registered name of a tool name or alias, or null when nothing is registered under it.
        /// </summary>
        /// <param name="nameOrAlias">Name or alias (case-insensitive).</param>
        /// <returns>Canonical name or null.</returns>
        public string? Canonical(string? nameOrAlias)
        {
            if (String.IsNullOrWhiteSpace(nameOrAlias)) return null;
            string name = nameOrAlias.Trim();
            if (_ByName.TryGetValue(name, out AssistantTool? direct)) return direct.Name;
            if (_Aliases.TryGetValue(name, out string? target) && _ByName.TryGetValue(target, out AssistantTool? aliased)) return aliased.Name;
            return null;
        }

        /// <summary>
        /// The tool registered under a name or alias, or null.
        /// </summary>
        /// <param name="nameOrAlias">Name or alias.</param>
        /// <returns>Tool or null.</returns>
        public AssistantTool? Find(string? nameOrAlias)
        {
            string? name = Canonical(nameOrAlias);
            return name == null ? null : _ByName[name];
        }

        /// <summary>
        /// Normalize a tools.allowed list: canonical names, aliases mapped (findGyms to findVenues), duplicates removed,
        /// order kept. Names that are not registered are reported in unknown.
        /// </summary>
        /// <param name="names">Saved names.</param>
        /// <param name="unknown">Names that resolve to no tool.</param>
        /// <returns>Canonical names.</returns>
        /// <exception cref="ArgumentNullException">The names are null.</exception>
        public List<string> NormalizeAllowed(IEnumerable<string> names, out List<string> unknown)
        {
            ArgumentNullException.ThrowIfNull(names);
            List<string> result = new List<string>();
            unknown = new List<string>();
            foreach (string raw in names)
            {
                string? name = Canonical(raw);
                if (name == null)
                {
                    if (!String.IsNullOrWhiteSpace(raw)) unknown.Add(raw.Trim());
                    continue;
                }
                if (!result.Contains(name, StringComparer.Ordinal)) result.Add(name);
            }
            return result;
        }

        /// <summary>
        /// The tools a caller may use from an allowed list (visitors only get tools that work signed out).
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="allowed">Allowed names (aliases accepted).</param>
        /// <returns>Tools.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public List<AssistantTool> ForCaller(AssistantCaller caller, IEnumerable<string> allowed)
        {
            ArgumentNullException.ThrowIfNull(caller);
            ArgumentNullException.ThrowIfNull(allowed);
            bool signedOut = caller.UserId == null && !caller.IsSystemAdmin;
            return NormalizeAllowed(allowed, out List<string> _).Select(n => _ByName[n]).Where(t => !signedOut || t.WorksSignedOut).ToList();
        }

        /// <summary>
        /// Run a tool as the caller with the registry's time limit and output bound. Failures come back as a result
        /// with Status Failed, TimedOut, or Rejected (never thrown), so the chat loop can tell the model.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="nameOrAlias">Tool name or alias.</param>
        /// <param name="arguments">Arguments (a JSON object).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        /// <exception cref="ArgumentNullException">The caller is null.</exception>
        /// <exception cref="OperationCanceledException">The caller's token was canceled.</exception>
        public Task<AssistantToolResult> InvokeAsync(AssistantCaller caller, string nameOrAlias, JsonElement arguments, CancellationToken token = default)
        {
            return InvokeAsync(caller, nameOrAlias, arguments, _TimeoutMs, _MaxOutputCharacters, token);
        }

        /// <summary>
        /// Run a tool as the caller with a time limit and output bound of the caller's choosing (an assistant's
        /// toolCallTimeoutMs and maxToolOutputCharacters). Arguments are validated against the tool's schema first.
        /// Failures come back as a result with Status Failed, TimedOut, or Rejected (never thrown).
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="nameOrAlias">Tool name or alias.</param>
        /// <param name="arguments">Arguments (a JSON object).</param>
        /// <param name="timeoutMs">Time limit in milliseconds (clamped to 100 to 60000).</param>
        /// <param name="maxOutputCharacters">Output bound (clamped to 200 to 100000).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        /// <exception cref="ArgumentNullException">The caller is null.</exception>
        /// <exception cref="OperationCanceledException">The caller's token was canceled.</exception>
        public async Task<AssistantToolResult> InvokeAsync(AssistantCaller caller, string nameOrAlias, JsonElement arguments, int timeoutMs, int maxOutputCharacters, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(caller);
            timeoutMs = Math.Clamp(timeoutMs, 100, 60000);
            maxOutputCharacters = Math.Clamp(maxOutputCharacters, 200, 100000);
            AssistantTool? tool = Find(nameOrAlias);
            if (tool == null) return new AssistantToolResult { Status = "Rejected", Text = "Unknown tool " + nameOrAlias + "." };
            if (caller.UserId == null && !caller.IsSystemAdmin && !tool.WorksSignedOut) return new AssistantToolResult { Status = "Rejected", Text = tool.Name + " needs a signed-in member." };
            if (arguments.ValueKind != JsonValueKind.Object && arguments.ValueKind != JsonValueKind.Undefined) return new AssistantToolResult { Status = "Rejected", Text = "Arguments must be a JSON object.", Error = "NotAnObject" };
            if (arguments.ValueKind == JsonValueKind.Object)
            {
                string? problem = ToolArgumentValidator.Validate(Schema(tool), arguments);
                if (problem != null) return new AssistantToolResult { Status = "Rejected", Text = "Invalid arguments for " + tool.Name + ": " + problem, Error = "InvalidArguments: " + problem };
            }
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(timeoutMs);
                try
                {
                    AssistantToolResult result = await tool.Handler!(caller, arguments, timeout.Token).ConfigureAwait(false);
                    result.Text ??= String.Empty;
                    if (result.Text.Length > maxOutputCharacters) result.Text = result.Text.Substring(0, maxOutputCharacters);
                    if (result.Results.Count > AiConstants.MaxToolResults) result.Results = result.Results.Take(AiConstants.MaxToolResults).ToList();
                    return result;
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    return new AssistantToolResult { Status = "TimedOut", Text = tool.Name + " took too long.", Error = "Timeout after " + timeoutMs + " ms" };
                }
                catch (ApiErrorException e)
                {
                    return new AssistantToolResult { Status = "Failed", Text = tool.Name + " failed: " + e.Error + ".", Error = e.Error };
                }
                catch (Exception e) when (e is JsonException || e is FormatException || e is InvalidOperationException || e is ArgumentException)
                {
                    return new AssistantToolResult { Status = "Failed", Text = tool.Name + " failed.", Error = e.GetType().Name };
                }
            }
        }

        /// <summary>
        /// The parsed JSON schema of a tool's arguments (an empty object schema when the text is not valid JSON).
        /// </summary>
        /// <param name="tool">Tool.</param>
        /// <returns>Schema.</returns>
        /// <exception cref="ArgumentNullException">The tool is null.</exception>
        public JsonElement Schema(AssistantTool tool)
        {
            ArgumentNullException.ThrowIfNull(tool);
            lock (_Schemas)
            {
                if (_Schemas.TryGetValue(tool.Name, out JsonElement cached)) return cached;
                JsonElement parsed;
                try
                {
                    using (JsonDocument doc = JsonDocument.Parse(String.IsNullOrWhiteSpace(tool.ParametersSchema) ? "{}" : tool.ParametersSchema)) parsed = doc.RootElement.Clone();
                }
                catch (JsonException)
                {
                    using (JsonDocument doc = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{}}")) parsed = doc.RootElement.Clone();
                }
                _Schemas[tool.Name] = parsed;
                return parsed;
            }
        }

        /// <summary>
        /// The PolyPrompt function definition of a tool (name, description, parameters schema).
        /// </summary>
        /// <param name="tool">Tool.</param>
        /// <returns>Definition.</returns>
        /// <exception cref="ArgumentNullException">The tool is null.</exception>
        public ToolDefinition Definition(AssistantTool tool)
        {
            ArgumentNullException.ThrowIfNull(tool);
            return Function(tool.Name, tool.Description, Schema(tool));
        }

        /// <summary>
        /// A PolyPrompt function definition from a name, a description, and a parsed JSON schema (also used for job
        /// tools offered to API endpoints).
        /// </summary>
        /// <param name="name">Function name.</param>
        /// <param name="description">Description.</param>
        /// <param name="schema">Parameters schema (an object; anything else becomes an empty object schema).</param>
        /// <returns>Definition.</returns>
        /// <exception cref="ArgumentNullException">name or description is null.</exception>
        public static ToolDefinition Function(string name, string description, JsonElement schema)
        {
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(description);
            Dictionary<string, object> parameters = ToObject(schema) as Dictionary<string, object> ?? new Dictionary<string, object>();
            if (!parameters.ContainsKey("type")) parameters["type"] = "object";
            return ToolDefinition.Function(name, description, parameters);
        }

        /// <summary>
        /// The activity label of a tool while it runs.
        /// </summary>
        /// <param name="tool">Tool, or null for an unknown name.</param>
        /// <param name="name">Name the model used.</param>
        /// <returns>Label.</returns>
        public static string RunningLabel(AssistantTool? tool, string name)
        {
            return tool != null && !String.IsNullOrWhiteSpace(tool.Label) ? tool.Label : "Using " + (tool?.Name ?? name);
        }

        /// <summary>
        /// The activity label of a tool when it finished.
        /// </summary>
        /// <param name="tool">Tool, or null for an unknown name.</param>
        /// <param name="name">Name the model used.</param>
        /// <returns>Label.</returns>
        public static string DoneLabel(AssistantTool? tool, string name)
        {
            return tool != null && !String.IsNullOrWhiteSpace(tool.CompletedLabel) ? tool.CompletedLabel : "Used " + (tool?.Name ?? name);
        }

        /// <summary>
        /// Aliases that point to a tool (built-in and registered).
        /// </summary>
        /// <param name="name">Canonical name.</param>
        /// <returns>Aliases.</returns>
        public List<string> AliasesOf(string name)
        {
            return _Aliases.Where(a => String.Equals(a.Value, name, StringComparison.OrdinalIgnoreCase)).Select(a => a.Key).OrderBy(a => a, StringComparer.Ordinal).ToList();
        }

        #endregion

        #region Private-Methods

        private static object? ToObject(JsonElement e)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Object:
                    Dictionary<string, object> map = new Dictionary<string, object>(StringComparer.Ordinal);
                    foreach (JsonProperty p in e.EnumerateObject())
                    {
                        object? v = ToObject(p.Value);
                        if (v != null) map[p.Name] = v;
                    }
                    return map;
                case JsonValueKind.Array:
                    List<object> list = new List<object>();
                    foreach (JsonElement item in e.EnumerateArray())
                    {
                        object? v = ToObject(item);
                        if (v != null) list.Add(v);
                    }
                    return list;
                case JsonValueKind.String: return e.GetString() ?? String.Empty;
                case JsonValueKind.Number: return e.TryGetInt64(out long l) ? l : e.GetDouble();
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                default: return null;
            }
        }

        #endregion
    }
}
