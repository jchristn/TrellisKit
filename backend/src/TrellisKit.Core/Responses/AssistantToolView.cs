namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;

    /// <summary>
    /// One registered assistant tool for the administrator console (docs/AI_CONTRACT.md section 8.1), with its
    /// property-wide kill switch.
    /// </summary>
    public class AssistantToolView
    {
        #region Public-Members

        /// <summary>
        /// Canonical tool name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Description shown to the model.
        /// </summary>
        public string Description { get; set; } = String.Empty;

        /// <summary>
        /// Activity label while running.
        /// </summary>
        public string Label { get; set; } = String.Empty;

        /// <summary>
        /// Activity label when done.
        /// </summary>
        public string CompletedLabel { get; set; } = String.Empty;

        /// <summary>
        /// Other accepted names (for example findGyms).
        /// </summary>
        public List<string> Aliases { get; set; } = new List<string>();

        /// <summary>
        /// Whether visitors (signed out) may use the tool.
        /// </summary>
        public bool WorksSignedOut { get; set; } = false;

        /// <summary>
        /// JSON schema of the arguments.
        /// </summary>
        public JsonElement ParametersSchema { get; set; }

        /// <summary>
        /// Whether the property-wide kill switch is on.
        /// </summary>
        public bool Disabled { get; set; } = false;

        /// <summary>
        /// Reason entered with the kill switch.
        /// </summary>
        public string? DisabledReason { get; set; } = null;

        /// <summary>
        /// Administrator who changed the switch last.
        /// </summary>
        public string? DisabledByUserId { get; set; } = null;

        /// <summary>
        /// Last switch change (UTC).
        /// </summary>
        public DateTime? DisabledUtc { get; set; } = null;

        /// <summary>
        /// Labels (tools are engine code and carry none).
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags (tools are engine code and carry none).
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        #endregion
    }
}
