namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.PropertyConfig;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// The engine assistant tools that belong to no feature module (docs/AI_CONTRACT.md section 8.1): searchFeed (feed
    /// posts the caller can open, through the same feed service and rules as the app; visitors read the public feed
    /// only), getPropertyInfo (public property configuration), and getMyProfile (the signed-in caller's own profile and
    /// membership, never offered to visitors). Registered when the AI services are built.
    /// Thread safety: stateless.
    /// </summary>
    public static class CoreAssistantTools
    {
        #region Public-Members

        /// <summary>
        /// Most posts per searchFeed answer.
        /// </summary>
        public const int MaxResults = 10;

        /// <summary>
        /// Name of the getMyProfile tool.
        /// </summary>
        public const string GetMyProfileName = "getMyProfile";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register the tools in the property's registry.
        /// </summary>
        /// <param name="ai">AI services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public static void Register(AiServices ai)
        {
            ArgumentNullException.ThrowIfNull(ai);
            ai.Tools.Register(new AssistantTool
            {
                Name = "searchFeed",
                Description = "Search the community feed for posts the member may open. Arguments: query (text), labels (label names), maxResults (1 to 10). Returns titles, authors, dates, excerpts, and app paths (/feed/{id}).",
                Label = "Searching the feed",
                CompletedLabel = "Searched the feed",
                ParametersSchema = "{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\",\"maxLength\":200},\"labels\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"maxItems\":10},\"maxResults\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":10}}}",
                WorksSignedOut = true,
                Handler = (caller, arguments, token) => SearchFeedAsync(ai, caller, arguments, token)
            });
            ai.Tools.Register(new AssistantTool
            {
                Name = "getPropertyInfo",
                Description = "Public information about this community: name, description, support email, and which features are enabled. No arguments.",
                Label = "Looking up the community",
                CompletedLabel = "Looked up the community",
                ParametersSchema = "{\"type\":\"object\",\"properties\":{}}",
                WorksSignedOut = true,
                Handler = (caller, arguments, token) => Task.FromResult(PropertyInfo(ai))
            });
            ai.Tools.Register(new AssistantTool
            {
                Name = GetMyProfileName,
                Description = "The signed-in member's own profile: display name, handle, bio, the profile fields they filled in, location, goals, membership status, and member since. No arguments. Never another member's profile.",
                Label = "Looking up your profile",
                CompletedLabel = "Looked up your profile",
                ParametersSchema = "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}",
                WorksSignedOut = false,
                Handler = (caller, arguments, token) => MyProfileAsync(ai, caller, token)
            });
        }

        #endregion

        #region Private-Methods

        private static async Task<AssistantToolResult> SearchFeedAsync(AiServices ai, AssistantCaller caller, JsonElement arguments, CancellationToken token)
        {
            EnumerationQuery query = new EnumerationQuery { MaxResults = 5, Ordering = "CreatedDescending" };
            if (arguments.ValueKind == JsonValueKind.Object)
            {
                if (arguments.TryGetProperty("query", out JsonElement q) && q.ValueKind == JsonValueKind.String && !String.IsNullOrWhiteSpace(q.GetString()))
                {
                    string search = q.GetString()!.Trim();
                    query.Search = search.Length > 200 ? search.Substring(0, 200) : search;
                }
                if (arguments.TryGetProperty("maxResults", out JsonElement m) && m.ValueKind == JsonValueKind.Number && m.TryGetInt32(out int max)) query.MaxResults = Math.Clamp(max, 1, MaxResults);
                if (arguments.TryGetProperty("labels", out JsonElement labels) && labels.ValueKind == JsonValueKind.Array)
                    query.Labels = labels.EnumerateArray().Where(l => l.ValueKind == JsonValueKind.String && !String.IsNullOrWhiteSpace(l.GetString())).Select(l => l.GetString()!.Trim()).Take(10).ToList();
            }
            List<AssistantToolCallResult> results = new List<AssistantToolCallResult>();
            StringBuilder sb = new StringBuilder();
            if (caller.UserId == null && !caller.IsSystemAdmin)
            {
                EnumerationResult<PublicFeedPost> page = await ai.App.Feed.ListPublicAsync(query, caller.SourceIp, token).ConfigureAwait(false);
                foreach (PublicFeedPost post in page.Objects.Where(p => p.State == FeedPostStateEnum.Published))
                    Line(sb, results, post.Id, post.Title, post.Body, post.Excerpt, post.Author.DisplayName, post.PublishedUtc ?? post.CreatedUtc);
            }
            else
            {
                EnumerationResult<FeedPostView> page = await ai.App.Feed.ListAsync(caller.Actor, query, token).ConfigureAwait(false);
                // Locked stubs (gated content the caller cannot open) and moderated posts are never handed to the model.
                foreach (FeedPostView post in page.Objects.Where(p => !p.Locked && p.State == FeedPostStateEnum.Published && (p.Moderation == null || p.Moderation.State == ModerationStateEnum.None)))
                    Line(sb, results, post.Id, post.Title, post.Body, post.Excerpt, post.Author.DisplayName, post.PublishedUtc ?? post.CreatedUtc);
            }
            if (results.Count == 0) return new AssistantToolResult { Text = "No posts found.", ResultCount = 0 };
            return new AssistantToolResult { Text = sb.ToString().TrimEnd(), ResultCount = results.Count, Results = results };
        }

        private static void Line(StringBuilder sb, List<AssistantToolCallResult> results, string id, string? title, string body, string excerpt, string author, DateTime when)
        {
            if (results.Count >= MaxResults) return;
            string name = KnowledgeRetrievalService.TitleOf(title, body);
            string text = String.IsNullOrWhiteSpace(excerpt) ? MarkdownNormalizer.ToPlainText(body) : excerpt;
            if (text.Length > 300) text = text.Substring(0, 300).TrimEnd() + "...";
            sb.Append("- ").Append(name).Append(" by ").Append(String.IsNullOrWhiteSpace(author) ? "a member" : author).Append(", ")
                .Append(when.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(" /feed/").Append(id).Append(": ").Append(text.Replace('\n', ' ')).Append('\n');
            results.Add(new AssistantToolCallResult { TargetType = "FeedPost", TargetId = id, Title = name, Path = "/feed/" + id });
        }

        private static async Task<AssistantToolResult> MyProfileAsync(AiServices ai, AssistantCaller caller, CancellationToken token)
        {
            // Signed-in only, and always the caller's own account: the tool has no arguments, so it cannot name anyone else.
            if (String.IsNullOrEmpty(caller.UserId)) return new AssistantToolResult { Status = "Rejected", Text = GetMyProfileName + " needs a signed-in member.", Error = "SignInRequired" };
            MeResponse me = await ai.App.Views.BuildMeAsync(caller.UserId, token).ConfigureAwait(false);
            StringBuilder sb = new StringBuilder();
            sb.Append("Display name: ").Append(me.DisplayName).Append('\n');
            if (!String.IsNullOrWhiteSpace(me.Handle)) sb.Append("Handle: @").Append(me.Handle).Append('\n');
            if (!String.IsNullOrWhiteSpace(me.Bio)) sb.Append("Bio: ").Append(Flat(me.Bio, 500)).Append('\n');
            if (!String.IsNullOrWhiteSpace(me.Rank)) sb.Append("Rank: ").Append(me.Rank).Append('\n');
            if (!String.IsNullOrWhiteSpace(me.Affiliation)) sb.Append("Affiliation: ").Append(me.Affiliation).Append('\n');
            string location = String.Join(", ", new[] { me.City, me.Region, me.Country }.Where(p => !String.IsNullOrWhiteSpace(p)));
            if (location.Length > 0) sb.Append("Location: ").Append(location).Append('\n');
            if (me.Goals.Count > 0) sb.Append("Goals: ").Append(String.Join(", ", me.Goals.Take(20))).Append('\n');
            int fields = 0;
            foreach (KeyValuePair<string, object?> field in me.Profile.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (fields >= 40) break;
                string? value = FieldText(field.Value);
                if (String.IsNullOrWhiteSpace(value)) continue;
                sb.Append("Profile ").Append(field.Key).Append(": ").Append(Flat(value, 300)).Append('\n');
                fields++;
            }
            sb.Append("Membership: ").Append(me.Membership.IsActive ? "active" : "none");
            if (me.Membership.IsActive && me.Membership.ExpiresUtc != null) sb.Append(me.Membership.AutoRenew ? ", renews " : ", ends ").Append(me.Membership.ExpiresUtc.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            sb.Append('\n');
            sb.Append("Access: ").Append(me.AccessTier).Append('\n');
            if (!me.ProfileComplete && me.ProfileMissing.Count > 0) sb.Append("Profile fields still to fill in: ").Append(String.Join(", ", me.ProfileMissing.Take(20))).Append('\n');
            sb.Append("Member since: ").Append(me.CreatedUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            return new AssistantToolResult { Text = sb.ToString(), ResultCount = 1 };
        }

        private static string? FieldText(object? value)
        {
            switch (value)
            {
                case null: return null;
                case string text: return text;
                case bool flag: return flag ? "yes" : "no";
                case JsonElement element:
                    if (element.ValueKind == JsonValueKind.Null || element.ValueKind == JsonValueKind.Undefined) return null;
                    if (element.ValueKind == JsonValueKind.String) return element.GetString();
                    if (element.ValueKind == JsonValueKind.Array) return String.Join(", ", element.EnumerateArray().Take(20).Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.GetRawText()));
                    return element.GetRawText();
                case IFormattable formattable: return formattable.ToString(null, CultureInfo.InvariantCulture);
                case System.Collections.IEnumerable list:
                    List<string> parts = new List<string>();
                    foreach (object? item in list)
                    {
                        if (parts.Count >= 20) break;
                        string? part = FieldText(item);
                        if (!String.IsNullOrWhiteSpace(part)) parts.Add(part);
                    }
                    return String.Join(", ", parts);
                default: return JsonSerializer.Serialize(value);
            }
        }

        private static string Flat(string text, int max)
        {
            string flat = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return flat.Length > max ? flat.Substring(0, max).TrimEnd() + "..." : flat;
        }

        private static AssistantToolResult PropertyInfo(AiServices ai)
        {
            PropertyConfiguration p = ai.App.Property.Property;
            PropertyFeatures f = p.Features;
            List<string> features = new List<string> { "feed" };
            if (f.Forums) features.Add("forums");
            if (f.Events) features.Add("events");
            if (f.Library) features.Add("library");
            if (f.Directory) features.Add("directory");
            if (f.Messaging) features.Add("messaging");
            if (f.Support) features.Add("support");
            if (f.Ideas) features.Add("ideas");
            StringBuilder sb = new StringBuilder();
            sb.Append("Name: ").Append(p.Name).Append('\n');
            if (!String.IsNullOrWhiteSpace(p.Tagline)) sb.Append("Tagline: ").Append(p.Tagline).Append('\n');
            if (!String.IsNullOrWhiteSpace(p.Description)) sb.Append("Description: ").Append(p.Description).Append('\n');
            if (!String.IsNullOrWhiteSpace(p.Contact.SupportEmail)) sb.Append("Support email: ").Append(p.Contact.SupportEmail).Append('\n');
            sb.Append("Features: ").Append(String.Join(", ", features));
            return new AssistantToolResult { Text = sb.ToString(), ResultCount = 1 };
        }

        #endregion
    }
}
