namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Services.Ai;

    /// <summary>
    /// The listForums assistant tool (docs/AI_CONTRACT.md section 8.1): lists the forums the chatting caller may read
    /// through ForumService.ListAsync (the same audience rule as POST /api/v1/forums/enumerate), optionally filtered by
    /// a search text. Answers names, descriptions, thread counts, and app paths (/forums/{id}), at most 20. Thread safe.
    /// </summary>
    public static class ListForumsTool
    {
        #region Public-Members

        /// <summary>
        /// Tool name.
        /// </summary>
        public const string Name = "listForums";

        /// <summary>
        /// Most forums per answer.
        /// </summary>
        public const int MaxResults = 20;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the tool.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <returns>Tool.</returns>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public static AssistantTool Build(ApplicationServices services)
        {
            ArgumentNullException.ThrowIfNull(services);
            return new AssistantTool
            {
                Name = Name,
                Description = "List the forums the member may read. Arguments: search (optional text matched against forum names and descriptions). Returns up to 20 forums with descriptions, thread counts, and app paths (/forums/{id}).",
                Label = "Listing the forums",
                CompletedLabel = "Listed the forums",
                ParametersSchema = "{\"type\":\"object\",\"properties\":{\"search\":{\"type\":\"string\",\"maxLength\":100}},\"additionalProperties\":false}",
                WorksSignedOut = true,
                Handler = (caller, arguments, token) => RunAsync(services, caller, arguments, token)
            };
        }

        #endregion

        #region Private-Methods

        private static async Task<AssistantToolResult> RunAsync(ApplicationServices services, AssistantCaller caller, JsonElement arguments, CancellationToken token)
        {
            if (!services.Property.Property.Features.Forums) return new AssistantToolResult { Status = "Rejected", Text = "The forums are not available.", Error = "NotAvailable" };
            EnumerationQuery query = new EnumerationQuery { MaxResults = MaxResults };
            if (arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty("search", out JsonElement s) && s.ValueKind == JsonValueKind.String && !String.IsNullOrWhiteSpace(s.GetString()))
            {
                string search = s.GetString()!.Trim();
                query.Search = search.Length > 100 ? search.Substring(0, 100) : search;
            }
            EnumerationResult<ForumView> page = await services.Forums.Catalog.ListAsync(caller.Actor, query, token).ConfigureAwait(false);
            List<ForumView> forums = page.Objects.Take(MaxResults).ToList();
            if (forums.Count == 0) return new AssistantToolResult { Text = "No forums found.", ResultCount = 0 };
            StringBuilder sb = new StringBuilder();
            List<AssistantToolCallResult> results = new List<AssistantToolCallResult>();
            foreach (ForumView forum in forums)
            {
                string description = (forum.Description ?? String.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
                if (description.Length > 200) description = description.Substring(0, 200).TrimEnd() + "...";
                sb.Append("- ").Append(forum.Name);
                if (!String.IsNullOrWhiteSpace(forum.Section)) sb.Append(" [").Append(forum.Section).Append(']');
                sb.Append(" (").Append(forum.ThreadCount).Append(" threads").Append(forum.IsArchived ? ", archived" : String.Empty).Append(") /forums/").Append(forum.Id);
                if (description.Length > 0) sb.Append(": ").Append(description);
                sb.Append('\n');
                if (results.Count < AiConstants.MaxToolResults) results.Add(new AssistantToolCallResult { TargetType = "Forum", TargetId = forum.Id, Title = forum.Name, Path = "/forums/" + forum.Id });
            }
            return new AssistantToolResult { Text = sb.ToString().TrimEnd(), ResultCount = forums.Count, Results = results };
        }

        #endregion
    }
}
