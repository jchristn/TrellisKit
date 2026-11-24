namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Services.Ai;

    /// <summary>
    /// The findVenues assistant tool (docs/AI_CONTRACT.md section 8; findGyms is a registry alias): searches the venue
    /// directory as the chatting caller (their audience and locked stubs apply; stubs are left out) by text, place,
    /// and programs, and answers a short list with links. Contact details are included only for Public venues with an
    /// exact address. Thread safe.
    /// </summary>
    public static class FindVenuesTool
    {
        #region Public-Members

        /// <summary>
        /// Tool name.
        /// </summary>
        public const string Name = "findVenues";

        /// <summary>
        /// Most venues per answer.
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
                Label = "Searching the directory",
                CompletedLabel = "Searched the directory",
                Description = "Find venues in the community directory by name or text, near a place (lat, lng, radiusKm), and by program keys. Answers names, places, and links.",
                ParametersSchema = "{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\",\"description\":\"Name or text to search\"},\"lat\":{\"type\":\"number\"},\"lng\":{\"type\":\"number\"},\"radiusKm\":{\"type\":\"number\"}," +
                    "\"programs\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}},\"maxResults\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":10}}}",
                WorksSignedOut = true,
                Handler = (caller, arguments, token) => RunAsync(services, caller, arguments, token)
            };
        }

        #endregion

        #region Private-Methods

        private static async Task<AssistantToolResult> RunAsync(ApplicationServices services, AssistantCaller caller, JsonElement arguments, CancellationToken token)
        {
            if (!services.Property.Property.Features.Directory) return new AssistantToolResult { Status = "Rejected", Text = "The directory is not available." };
            EnumerationQuery query = new EnumerationQuery { MaxResults = MaxResults };
            if (arguments.ValueKind == JsonValueKind.Object)
            {
                if (arguments.TryGetProperty("query", out JsonElement q) && q.ValueKind == JsonValueKind.String && !String.IsNullOrWhiteSpace(q.GetString())) query.Search = q.GetString()!.Trim();
                if (arguments.TryGetProperty("maxResults", out JsonElement m) && m.ValueKind == JsonValueKind.Number && m.TryGetInt32(out int max)) query.MaxResults = Math.Clamp(max, 1, MaxResults);
                if (arguments.TryGetProperty("lat", out JsonElement lat) && arguments.TryGetProperty("lng", out JsonElement lng) && lat.ValueKind == JsonValueKind.Number && lng.ValueKind == JsonValueKind.Number)
                {
                    double radius = arguments.TryGetProperty("radiusKm", out JsonElement r) && r.ValueKind == JsonValueKind.Number ? r.GetDouble() : services.Settings.Directory.DefaultRadiusKm;
                    query.Filters["near"] = JsonSerializer.SerializeToElement(new Dictionary<string, double> { { "lat", lat.GetDouble() }, { "lng", lng.GetDouble() }, { "radiusKm", radius } });
                }
                if (arguments.TryGetProperty("programs", out JsonElement p) && p.ValueKind == JsonValueKind.Array) query.Filters["programs"] = p.Clone();
            }
            EnumerationResult<VenueSummaryView> page = await services.Directory.Venues.ListAsync(caller.Actor, query, token).ConfigureAwait(false);
            List<VenueSummaryView> open = page.Objects.Where(v => !v.Locked).ToList();
            if (open.Count == 0) return new AssistantToolResult { Text = "No matching venues were found.", ResultCount = 0 };
            StringBuilder sb = new StringBuilder();
            foreach (VenueSummaryView v in open)
            {
                sb.Append("- ").Append(v.Name);
                if (!String.IsNullOrWhiteSpace(v.Address.Formatted)) sb.Append(" (").Append(v.Address.Formatted).Append(')');
                if (v.DistanceKm != null) sb.Append(", ").Append(v.DistanceKm.Value.ToString("0.#", CultureInfo.InvariantCulture)).Append(" km");
                if (v.Programs.Count > 0) sb.Append(", programs: ").Append(String.Join(", ", v.Programs.Select(o => o.Label)));
                if (v.Audience == TrellisKit.Core.Enums.ContentAudienceEnum.Public && v.AddressVisibility == TrellisKit.Core.Enums.AddressVisibilityEnum.Exact && !String.IsNullOrWhiteSpace(v.WebsiteUrl)) sb.Append(", ").Append(v.WebsiteUrl);
                sb.Append(": /venues/").Append(v.Id).Append('\n');
            }
            return new AssistantToolResult
            {
                Text = sb.ToString().TrimEnd(),
                ResultCount = open.Count,
                Results = open.Select(v => new TrellisKit.Core.Models.AssistantToolCallResult { TargetType = "Venue", TargetId = v.Id, Title = v.Name, Path = "/venues/" + v.Id }).ToList()
            };
        }

        #endregion
    }
}
