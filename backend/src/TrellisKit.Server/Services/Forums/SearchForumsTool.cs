namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Services.Ai;

    /// <summary>
    /// The searchForums assistant tool (docs/AI_CONTRACT.md section 8.1): searches forum threads as the chatting caller
    /// through ThreadService.ListAsync, so forum readability, content audience, blocks, and moderation apply exactly as
    /// in the app. Answers titles, forums, answered state, excerpts, and app paths (/threads/{id}). Thread safe.
    /// </summary>
    public static class SearchForumsTool
    {
        #region Public-Members

        /// <summary>
        /// Tool name.
        /// </summary>
        public const string Name = "searchForums";

        /// <summary>
        /// Most threads per answer.
        /// </summary>
        public const int MaxResults = 10;

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
                Description = "Search forum threads the member may read. Arguments: query (text), forumIds (forum ids), answered (true for threads with an accepted answer), maxResults (1 to 10). Returns titles, forums, excerpts, and app paths (/threads/{id}).",
                Label = "Searching the forums",
                CompletedLabel = "Searched the forums",
                ParametersSchema = "{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\",\"maxLength\":200},\"forumIds\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"maxItems\":20}," +
                    "\"answered\":{\"type\":\"boolean\"},\"maxResults\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":10}}}",
                WorksSignedOut = true,
                Handler = (caller, arguments, token) => RunAsync(services, caller, arguments, token)
            };
        }

        #endregion

        #region Private-Methods

        private static async Task<AssistantToolResult> RunAsync(ApplicationServices services, AssistantCaller caller, JsonElement arguments, CancellationToken token)
        {
            if (!services.Property.Property.Features.Forums) return new AssistantToolResult { Status = "Rejected", Text = "The forums are not available.", Error = "NotAvailable" };
            EnumerationQuery query = new EnumerationQuery { MaxResults = 5 };
            if (arguments.ValueKind == JsonValueKind.Object)
            {
                if (arguments.TryGetProperty("query", out JsonElement q) && q.ValueKind == JsonValueKind.String && !String.IsNullOrWhiteSpace(q.GetString()))
                {
                    string search = q.GetString()!.Trim();
                    query.Search = search.Length > 200 ? search.Substring(0, 200) : search;
                }
                if (arguments.TryGetProperty("maxResults", out JsonElement m) && m.ValueKind == JsonValueKind.Number && m.TryGetInt32(out int max)) query.MaxResults = Math.Clamp(max, 1, MaxResults);
                if (arguments.TryGetProperty("forumIds", out JsonElement forums) && forums.ValueKind == JsonValueKind.Array) query.Filters["forumIds"] = forums.Clone();
                if (arguments.TryGetProperty("answered", out JsonElement answered) && (answered.ValueKind == JsonValueKind.True || answered.ValueKind == JsonValueKind.False)) query.Filters["answered"] = answered.Clone();
            }
            EnumerationResult<ThreadSummaryView> page = await services.Forums.Threads.ListAsync(caller.Actor, query, token).ConfigureAwait(false);
            List<ThreadSummaryView> open = page.Objects.Where(t => t.State == ThreadStateEnum.Published && (t.Moderation == null || t.Moderation.State == ModerationStateEnum.None)).Take(MaxResults).ToList();
            if (open.Count == 0) return new AssistantToolResult { Text = "No threads found.", ResultCount = 0 };
            StringBuilder sb = new StringBuilder();
            List<AssistantToolCallResult> results = new List<AssistantToolCallResult>();
            foreach (ThreadSummaryView t in open)
            {
                string excerpt = (t.Excerpt ?? String.Empty).Replace('\n', ' ');
                if (excerpt.Length > 300) excerpt = excerpt.Substring(0, 300).TrimEnd() + "...";
                sb.Append("- ").Append(t.Title).Append(" (").Append(t.Forum.Name).Append(t.HasAcceptedAnswer ? ", answered" : String.Empty).Append(", ").Append(t.CommentCount).Append(" comments) /threads/").Append(t.Id);
                if (excerpt.Length > 0) sb.Append(": ").Append(excerpt);
                sb.Append('\n');
                results.Add(new AssistantToolCallResult { TargetType = "Thread", TargetId = t.Id, Title = t.Title, Path = "/threads/" + t.Id });
            }
            return new AssistantToolResult { Text = sb.ToString().TrimEnd(), ResultCount = open.Count, Results = results };
        }

        #endregion
    }
}
