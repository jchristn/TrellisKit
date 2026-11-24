namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.IO;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using TrellisKit.Server.Serialization;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The task roles of the engine (docs/INTEGRATION.md). Each role has an administrator-editable
    /// executor hierarchy; features call a role, never a specific model. Thread safety: immutable.
    /// </summary>
    public static class TaskRoleCatalog
    {
        #region Public-Members

        /// <summary>
        /// Every role, in the order the admin console lists them.
        /// </summary>
        public static IReadOnlyList<TaskRoleDefinition> All
        {
            get { return _All; }
        }

        #endregion

        #region Private-Members

        private static readonly List<TaskRoleDefinition> _All = new List<TaskRoleDefinition>
        {
            Role("Research", "Research", "Background research through explicitly registered tools.", true, false, "Configure a worker harness or an API endpoint", ModelReasoningEffortEnum.Medium),
            Role("Extraction", "Extraction", "Structured extraction from supplied documents.", false, false, "Configure a completion endpoint", ModelReasoningEffortEnum.Low),
            Role("Moderation", "Moderation", "Screen member contributions.", false, false, "Configure a moderation-capable completion endpoint", ModelReasoningEffortEnum.Low),
            Role("Classification", "Classification", "Classify content and member requests.", false, false, "Configure a completion endpoint", ModelReasoningEffortEnum.Low),
            Role("SearchParse", "Search parsing", "Translate natural-language searches into filters.", false, true, "Configure a completion endpoint", ModelReasoningEffortEnum.Minimal),
            Role("CommunityAssistant", "Community assistant", "Answer community questions.", false, true, "Configure a completion endpoint", ModelReasoningEffortEnum.Medium),
            Role("ContentAssistant", "Content assistant", "Help members draft community content.", false, true, "Configure a completion endpoint", ModelReasoningEffortEnum.Low),
            new TaskRoleDefinition { Role = "Embedding", Title = "Embedding", Description = "Vectors for content and queries.", Kind = ModelEndpointKindEnum.Embedding, Suggested = "Configure an embedding endpoint" }
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Load the built-in roles plus optional property-owned definitions from ai/roles.json.
        /// At most 64 roles are allowed. Built-in keys cannot be replaced; interactive roles cannot run harnesses.
        /// Returns an independent collection for each property.
        /// </summary>
        /// <param name="directory">Property directory, or empty for built-in roles only.</param>
        /// <returns>Validated role definitions.</returns>
        /// <exception cref="InvalidOperationException">The file is invalid or contains duplicate or unsafe definitions.</exception>
        public static IReadOnlyList<TaskRoleDefinition> Load(string? directory)
        {
            List<TaskRoleDefinition> roles = _All.Select(r => new TaskRoleDefinition
            {
                Role = r.Role, Title = r.Title, Description = r.Description, Kind = r.Kind,
                AllowsHarness = r.AllowsHarness, Interactive = r.Interactive, Suggested = r.Suggested, DefaultEffort = r.DefaultEffort
            }).ToList();
            if (String.IsNullOrWhiteSpace(directory)) return roles.AsReadOnly();
            string path = Path.Combine(directory, "ai", "roles.json");
            if (!File.Exists(path)) return roles.AsReadOnly();
            if (new FileInfo(path).Length > 65536) throw new InvalidOperationException("ai/roles.json exceeds 64 KiB.");
            List<TaskRoleDefinition> extra;
            try
            {
                extra = JsonSerializer.Deserialize<List<TaskRoleDefinition>>(File.ReadAllText(path), ApiJson.Api)
                    ?? throw new InvalidOperationException("ai/roles.json must contain an array.");
            }
            catch (JsonException e)
            {
                throw new InvalidOperationException("ai/roles.json contains invalid role definitions.", e);
            }
            HashSet<string> keys = roles.Select(r => r.Role).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (roles.Count + extra.Count > 64) throw new InvalidOperationException("At most 64 AI task roles are allowed per property.");
            foreach (TaskRoleDefinition role in extra)
            {
                if (role == null || String.IsNullOrEmpty(role.Role) || !Regex.IsMatch(role.Role, "^[A-Z][A-Za-z0-9]{1,63}$") || !keys.Add(role.Role))
                    throw new InvalidOperationException("AI task roles need unique PascalCase keys; built-in roles cannot be replaced.");
                if (String.IsNullOrWhiteSpace(role.Title) || role.Title.Length > 120 || (role.Description?.Length ?? 0) > 2000 ||
                    (role.Suggested?.Length ?? 0) > 1000 || !Enum.IsDefined(role.Kind) || (role.DefaultEffort.HasValue && !Enum.IsDefined(role.DefaultEffort.Value)) ||
                    (role.AllowsHarness && (role.Interactive || role.Kind != ModelEndpointKindEnum.Completion)))
                    throw new InvalidOperationException("Invalid AI role " + role.Role + ": check text bounds, endpoint kind, and background-only harness access.");
                roles.Add(role);
            }
            return roles.AsReadOnly();
        }

        /// <summary>
        /// The definition of a role, or null.
        /// </summary>
        /// <param name="role">Role key.</param>
        /// <returns>Definition or null.</returns>
        public static TaskRoleDefinition? Find(string? role)
        {
            if (String.IsNullOrWhiteSpace(role)) return null;
            return _All.FirstOrDefault(r => String.Equals(r.Role, role.Trim(), StringComparison.Ordinal));
        }

        #endregion

        #region Private-Methods

        private static TaskRoleDefinition Role(string role, string title, string description, bool harness, bool interactive, string suggested, ModelReasoningEffortEnum effort)
        {
            return new TaskRoleDefinition { Role = role, Title = title, Description = description, AllowsHarness = harness, Interactive = interactive, Suggested = suggested, DefaultEffort = effort };
        }

        #endregion
    }
}
