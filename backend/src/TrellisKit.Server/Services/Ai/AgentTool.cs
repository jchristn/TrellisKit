namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// One typed job tool that an agent (a CLI agent harness through the job-scoped MCP server, or an API endpoint
    /// through the engine's tool loop) calls to hand a result back to the job (docs/HARNESSES.md). The handler validates
    /// the arguments a second time if it needs rules the schema cannot express, and stores what it accepts; it is the
    /// only way an agent's output reaches the database. Examples: submit_organization_candidate, report_source.
    /// </summary>
    public class AgentTool
    {
        #region Public-Members

        /// <summary>
        /// Tool name: lower snake case, 1 to 64 characters, starting with a letter (for example report_source). Unique
        /// within one agent call.
        /// </summary>
        /// <exception cref="ArgumentException">The name is not lower snake case.</exception>
        public string Name
        {
            get { return _Name; }
            set
            {
                if (value == null || !_NamePattern.IsMatch(value)) throw new ArgumentException("Tool names are lower snake case, 1 to 64 characters, starting with a letter.", nameof(Name));
                _Name = value;
            }
        }

        /// <summary>
        /// What the tool is for, as the agent reads it. Never null.
        /// </summary>
        public string Description
        {
            get { return _Description; }
            set { _Description = value ?? String.Empty; }
        }

        /// <summary>
        /// JSON schema of the arguments (an object schema as text). The engine validates every call against it before
        /// the handler runs (type, properties, required, additionalProperties false, enum, minimum, maximum, minLength,
        /// maxLength, items, maxItems). Default: an object with no properties.
        /// </summary>
        public string ParametersSchema
        {
            get { return _ParametersSchema; }
            set { _ParametersSchema = String.IsNullOrWhiteSpace(value) ? _EmptySchema : value; }
        }

        /// <summary>
        /// The handler: receives a validated call and returns whether it accepted it. It runs on the worker, never in
        /// the harness. Required.
        /// </summary>
        public Func<AgentToolInvocation, CancellationToken, Task<AgentToolResult>>? Handler { get; set; } = null;

        #endregion

        #region Private-Members

        private const string _EmptySchema = "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}";
        private static readonly Regex _NamePattern = new Regex("^[a-z][a-z0-9_]{0,63}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private string _Name = "tool";
        private string _Description = String.Empty;
        private string _ParametersSchema = _EmptySchema;

        #endregion
    }
}
