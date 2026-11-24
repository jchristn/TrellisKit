namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// One read-only assistant tool (docs/AI_CONTRACT.md section 8.1): a name, a JSON schema for its arguments, and a
    /// handler that runs as the chatting member through the same services and authorization as the app. Feature
    /// modules register tools in <see cref="AiServices.Tools"/> (for example searchLibrary, findEvents, findVenues).
    /// </summary>
    public class AssistantTool
    {
        #region Public-Members

        /// <summary>
        /// Tool name (camelCase, unique per property), for example findEvents.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Description shown to the model.
        /// </summary>
        public string Description { get; set; } = String.Empty;

        /// <summary>
        /// Activity label while the tool runs, for example "Searching the library". Empty uses "Using {name}".
        /// </summary>
        public string Label { get; set; } = String.Empty;

        /// <summary>
        /// Activity label when the tool finished, for example "Searched the library". Empty uses "Used {name}".
        /// </summary>
        public string CompletedLabel { get; set; } = String.Empty;

        /// <summary>
        /// JSON schema of the arguments (an object schema as text).
        /// </summary>
        public string ParametersSchema { get; set; } = "{\"type\":\"object\",\"properties\":{}}";

        /// <summary>
        /// Other names accepted in tools.allowed for this tool (for example findGyms for findVenues).
        /// </summary>
        public List<string> Aliases
        {
            get { return _Aliases; }
            set { _Aliases = value ?? new List<string>(); }
        }

        /// <summary>
        /// Whether visitors (signed out) may use the tool (it then sees Public content only).
        /// </summary>
        public bool WorksSignedOut { get; set; } = false;

        /// <summary>
        /// The tool body: caller, arguments (a JSON object), cancellation token. Must return bounded results and run as
        /// the caller.
        /// </summary>
        public Func<AssistantCaller, JsonElement, CancellationToken, Task<AssistantToolResult>>? Handler { get; set; } = null;

        #endregion

        #region Private-Members

        private List<string> _Aliases = new List<string>();

        #endregion
    }
}
