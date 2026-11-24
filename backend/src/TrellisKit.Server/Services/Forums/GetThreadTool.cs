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
    /// The getThread assistant tool (docs/AI_CONTRACT.md section 8.1): reads one forum thread as the chatting caller
    /// through the same access check and view builder as GET /api/v1/threads/{id} (without recording a read position),
    /// so forum readability, content audience, blocks, and moderation apply exactly as in the app. Answers the title,
    /// forum, a body excerpt, the accepted answer, and the top comments (at most 5) the caller may see. Thread safe.
    /// </summary>
    public static class GetThreadTool
    {
        #region Public-Members

        /// <summary>
        /// Tool name.
        /// </summary>
        public const string Name = "getThread";

        /// <summary>
        /// Most comments per answer.
        /// </summary>
        public const int MaxComments = 5;

        /// <summary>
        /// Most characters of the thread body excerpt.
        /// </summary>
        public const int MaxBodyCharacters = 1500;

        /// <summary>
        /// Most characters of each comment and of the accepted answer.
        /// </summary>
        public const int MaxCommentCharacters = 400;

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
                Description = "Read one forum thread the member may open. Arguments: threadId (the thr_ id from searchForums or a /threads/{id} path). Returns the title, forum, a body excerpt, the accepted answer, and up to 5 top comments.",
                Label = "Reading the thread",
                CompletedLabel = "Read the thread",
                ParametersSchema = "{\"type\":\"object\",\"properties\":{\"threadId\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":80}},\"required\":[\"threadId\"],\"additionalProperties\":false}",
                WorksSignedOut = true,
                Handler = (caller, arguments, token) => RunAsync(services, caller, arguments, token)
            };
        }

        #endregion

        #region Private-Methods

        private static async Task<AssistantToolResult> RunAsync(ApplicationServices services, AssistantCaller caller, JsonElement arguments, CancellationToken token)
        {
            if (!services.Property.Property.Features.Forums) return new AssistantToolResult { Status = "Rejected", Text = "The forums are not available.", Error = "NotAvailable" };
            string id = arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty("threadId", out JsonElement t) && t.ValueKind == JsonValueKind.String ? (t.GetString() ?? String.Empty).Trim() : String.Empty;
            // Accept an app path as well as a bare id.
            int slash = id.LastIndexOf('/');
            if (slash >= 0) id = id.Substring(slash + 1);
            int query = id.IndexOfAny(new[] { '?', '#' });
            if (query >= 0) id = id.Substring(0, query);
            if (id.Length == 0) return new AssistantToolResult { Status = "Rejected", Text = "threadId is required.", Error = "InvalidArguments: threadId" };

            ThreadContext context;
            try
            {
                context = await services.Forums.Access.RequireThreadAsync(caller.Actor, id, token).ConfigureAwait(false);
            }
            catch (TrellisKit.Core.Exceptions.ApiErrorException e) when (e.StatusCode == 404 || e.StatusCode == 402 || e.StatusCode == 403)
            {
                return new AssistantToolResult { Text = "Thread not found.", ResultCount = 0 };
            }
            ThreadView thread = await services.Forums.Threads.ViewAsync(context, caller.Actor, token).ConfigureAwait(false);
            // Locked or moderated threads are never handed to the model.
            if (thread.State != ThreadStateEnum.Published || (thread.Moderation != null && thread.Moderation.State != ModerationStateEnum.None) || thread.Body == null)
                return new AssistantToolResult { Text = "Thread not found.", ResultCount = 0 };

            StringBuilder sb = new StringBuilder();
            sb.Append("Title: ").Append(thread.Title).Append('\n');
            sb.Append("Forum: ").Append(thread.Forum.Name).Append('\n');
            sb.Append("Type: ").Append(thread.Type).Append(thread.AcceptedAnswer != null ? " (answered)" : String.Empty).Append('\n');
            sb.Append("By: ").Append(String.IsNullOrWhiteSpace(thread.Author.DisplayName) ? "a member" : thread.Author.DisplayName).Append('\n');
            sb.Append("Comments: ").Append(thread.CommentCount).Append('\n');
            sb.Append("Path: /threads/").Append(thread.Id).Append('\n');
            sb.Append("Body: ").Append(Cut(MarkdownNormalizer.ToPlainText(thread.Body), MaxBodyCharacters)).Append('\n');
            if (thread.AcceptedAnswer != null && Visible(thread.AcceptedAnswer))
                sb.Append("Accepted answer (").Append(AuthorOf(thread.AcceptedAnswer)).Append("): ").Append(Cut(MarkdownNormalizer.ToPlainText(thread.AcceptedAnswer.Body ?? String.Empty), MaxCommentCharacters)).Append('\n');

            List<CommentView> comments = new List<CommentView>();
            try
            {
                EnumerationQuery tree = new EnumerationQuery { MaxResults = MaxComments + 2, Ordering = "Top" };
                using (JsonDocument depth = JsonDocument.Parse("1")) tree.Filters["depth"] = depth.RootElement.Clone();
                using (JsonDocument children = JsonDocument.Parse("1")) tree.Filters["childLimit"] = children.RootElement.Clone();
                CommentTreePage page = await services.Comments.ThreadTreeAsync(caller.Actor, thread.Id, tree, token).ConfigureAwait(false);
                string? accepted = thread.AcceptedAnswer?.Id;
                comments = page.Objects.Where(c => Visible(c) && !String.Equals(c.Id, accepted, StringComparison.Ordinal)).Take(MaxComments).ToList();
            }
            catch (TrellisKit.Core.Exceptions.ApiErrorException)
            {
                // Comments the caller may not read are left out; the thread itself still answers.
            }
            if (comments.Count > 0)
            {
                sb.Append("Top comments:\n");
                foreach (CommentView c in comments)
                    sb.Append("- ").Append(AuthorOf(c)).Append(": ").Append(Cut(MarkdownNormalizer.ToPlainText(c.Body ?? String.Empty), MaxCommentCharacters)).Append('\n');
            }
            List<AssistantToolCallResult> results = new List<AssistantToolCallResult>
            {
                new AssistantToolCallResult { TargetType = "Thread", TargetId = thread.Id, Title = thread.Title, Path = "/threads/" + thread.Id }
            };
            return new AssistantToolResult { Text = sb.ToString().TrimEnd(), ResultCount = 1, Results = results };
        }

        private static bool Visible(CommentView c)
        {
            return c.State == CommentStateEnum.Visible && (c.Moderation == null || c.Moderation.State == ModerationStateEnum.None) && !String.IsNullOrWhiteSpace(c.Body);
        }

        private static string AuthorOf(CommentView c)
        {
            return c.Author == null || String.IsNullOrWhiteSpace(c.Author.DisplayName) ? "a member" : c.Author.DisplayName;
        }

        private static string Cut(string text, int max)
        {
            string flat = (text ?? String.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
            return flat.Length > max ? flat.Substring(0, max).TrimEnd() + "..." : flat;
        }

        #endregion
    }
}
