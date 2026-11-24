namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;

    /// <summary>
    /// docs/ENUMERATION_AND_METADATA.md section 2.5: scans /openapi.json and fails when a GET route declares no 200
    /// response schema, when a 200 schema contains an array anywhere (properties, items, additional properties,
    /// compositions) unless it is an EnumerationResult or the route is on the reviewed fixed-bound allow-list, and when a
    /// route declared with .Bounded is not on the allow-list. Every offending route is named in the failure.
    /// </summary>
    public static class EnumerationEnforcementSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "EnumerationEnforcement";

        /// <summary>
        /// Reviewed fixed-bound GET routes (path as it appears in the OpenAPI document) and why each list is bounded.
        /// Every entry must be declared with .Bounded (a 200 schema description starting "Fixed-bound:").
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> AllowList = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "/api/v1/auth/me", "Me: goals (property goal values), rankHistory (profile.rankHistory.maxEntries), signInMethods (provider count), roles (at most 20 assignments), own labels and tags (50 each)" },
            { "/api/v1/admin/members/{id}", "Member detail: Me bounds plus nested previews of at most 5 objects each with totalRecords and the enumerate path" },
            { "/api/v1/admin/subscriptions/{id}", "Subscription detail: events preview of at most 5 with the enumerate path" },
            { "/api/v1/admin/signup-codes/{id}", "Sign-up code detail: redemptionsByDay is exactly 90 days" },
            { "/api/v1/membership", "Membership: history preview of at most 5 with the enumerate path" },
            { "/api/v1/property", "Public property configuration from property.json (rank values, goals, reactions, redirects)" },
            { "/api/v1/admin/property/branding", "Brand palette: 8 color keys, 7 contrast pairs per scheme, at most one override per scheme and pair" },
            { "/api/v1/platform/properties/{propertyId}/branding", "Brand palette of any property: same bounds as /api/v1/admin/property/branding" },
            { "/api/v1/admin/content-review/{id}", "Content change: categories at most the 8 categories, mediaRefs at most 50, labels and tags 50 each" },
            { "/api/v1/users/{id}", "Public profile: rankHistory at most profile.rankHistory.maxEntries" },
            { "/api/v1/comments/{id}", "Permalink: ancestors at most comments.maxDepth (max 20); children bounded by depth and childLimit" },
            { "/api/v1/admin/forums/{id}", "Forum detail (admin): moderators preview of at most 10 with totalRecords and the enumerate path" },
            { "/api/v1/admin/moderation/queue/{targetType}/{targetId}", "Moderation queue detail: revisions, reports, and actions previews of at most 5 objects each with totalRecords and the enumerate path" },
            { "/api/v1/platform/auth/me", "Platform administrator: own labels and tags (50 each)" },
            { "/api/v1/platform/properties/{propertyId}", "Property record: domains at most 20, apps at most 10, labels and tags 50 each" },
            { "/api/v1/admin/profile-fields/{id}", "Profile field: options at most 200, sub-fields at most 10, labels and tags 50 each" },
            { "/api/v1/admin/email/templates/{key}", "One template: its tokens (fixed by the engine version)" },
            { "/api/v1/admin/email/test/catalog", "POST: one result per template of the engine catalog plus the property's own templates (fixed by the engine version and the property directory)" },
            { "/api/v1/admin/email/messages/{id}", "Email record: labels and tags 50 each" },
            { "/api/v1/admin/update-groups/{id}", "Update group: updates at most 2 (one per platform), rolloutPlan at most 10 stages, assets preview at most 20 with totalRecords and the enumerate path" },
            { "/api/v1/admin/app/flags/{id}", "Flag detail: rules at most appConfig.maxRulesPerFlag, history preview at most 10 with totalRecords and the enumerate path" },
            { "/api/v1/admin/app/strings/{key}", "Remote string detail: tokens of one catalog key, history preview at most 10 with totalRecords and the enumerate path" },
            { "/api/v1/admin/app/banners/{id}", "Banner detail: clients (2), rules at most appConfig.maxRulesPerFlag, history preview at most 10 with totalRecords and the enumerate path" },
            { "/api/v1/admin/app/assets/{id}", "Remote asset detail: history preview at most 10 with totalRecords and the enumerate path" },
            { "/api/v1/admin/app/versions/{version}", "Content version: changes preview at most 50 with totalRecords and the enumerate path" },
            { "/api/v1/app/manifest", "App manifest: flags at most appConfig.maxFlags, kill switches one per engine feature key, strings, banners, and assets empty" },
            { "/api/v1/admin/property/access", "Access model: gatedFeatures at most the 9 feature keys" },
            { "/api/v1/tickets/{id}", "Ticket: messages preview of at most 20 with totalRecords and the enumerate path" },
            { "/api/v1/admin/tickets/{id}", "Ticket (staff): messages preview of at most 20 with totalRecords and the enumerate path" },
            { "/api/v1/admin/broadcasts/{id}", "Broadcast: segment.filters at most 20 with at most 20 values each, labels and tags 50 each" },
            { "/api/v1/admin/broadcasts/segments/{id}", "Broadcast segment: filters at most 20 with at most 20 values each, labels and tags 50 each" },
            { "/api/v1/admin/moderation/messaging/reports/{reportId}/snapshot", "Report snapshot: messages at most messaging.reportContextMessages + 1 (51)" }
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Enumeration enforcement (" + TestEnvironment.Provider + ")", new[]
            {
                SuiteCases.Http(Id, "OpenApi.NoUnboundedGets", "Every GET declares a 200 schema, arrays appear only in EnumerationResult or reviewed fixed-bound responses, and every .Bounded route is reviewed", async (f, ct) =>
                {
                    JsonElement doc = await Api.ExpectAsync(f, HttpMethod.Get, "/openapi.json", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    List<string> problems = Scan(doc, out int scanned);
                    Check.True(scanned > 20, "Scanned the GET operations (" + scanned + ")");
                    Check.True(problems.Count == 0, "Unbounded or undeclared GET routes:\n  " + String.Join("\n  ", problems));
                }),
                SuiteCases.Http(Id, "Runtime.CollectionContracts", "Successful GET retrievals never return raw arrays; declared collections honor page limits and return complete enumeration envelopes", async (f, ct) =>
                {
                    JsonElement doc = await Api.ExpectAsync(f, HttpMethod.Get, "/openapi.json", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    List<string> problems = new List<string>();
                    int checkedCollections = 0;
                    foreach (JsonProperty path in doc.GetProperty("paths").EnumerateObject())
                    {
                        if (!path.Name.StartsWith("/api/v1/", StringComparison.Ordinal) || path.Name.Contains('{') || !path.Value.TryGetProperty("get", out JsonElement get)) continue;
                        bool collection = get.TryGetProperty("responses", out JsonElement responses)
                            && responses.TryGetProperty("200", out JsonElement ok) && ok.TryGetProperty("content", out JsonElement content)
                            && content.TryGetProperty("application/json", out JsonElement json) && json.TryGetProperty("schema", out JsonElement schema)
                            && Description(schema).StartsWith("EnumerationResult<", StringComparison.Ordinal);
                        foreach (string bearer in new[] { HttpFixture.AdminToken, member.AccessToken })
                        {
                            ApiResult response = await Api.SendAsync(f, HttpMethod.Get, path.Name + (collection ? "?maxResults=1" : String.Empty), bearer, null, ct).ConfigureAwait(false);
                            if (response.Status != HttpStatusCode.OK) continue;
                            if (response.Body.ValueKind == JsonValueKind.Array) problems.Add(path.Name + ": raw array response");
                            if (!collection) continue;
                            checkedCollections++;
                            if (response.Body.ValueKind != JsonValueKind.Object) { problems.Add(path.Name + ": missing enumeration object"); continue; }
                            foreach (string field in new[] { "success", "maxResults", "skip", "continuationToken", "endOfResults", "totalRecords", "recordsRemaining", "timestampUtc", "objects" })
                                if (!response.Body.TryGetProperty(field, out _)) problems.Add(path.Name + ": missing " + field);
                            if (response.Body.TryGetProperty("objects", out JsonElement objects) && objects.ValueKind == JsonValueKind.Array && objects.GetArrayLength() > 1)
                                problems.Add(path.Name + ": ignored maxResults=1");
                            if (response.Body.TryGetProperty("maxResults", out JsonElement maximum) && maximum.GetInt32() != 1) problems.Add(path.Name + ": wrong effective page size");
                            if (response.Body.TryGetProperty("endOfResults", out JsonElement end) && end.GetBoolean()
                                && response.Body.TryGetProperty("continuationToken", out JsonElement cursor) && cursor.ValueKind != JsonValueKind.Null)
                                problems.Add(path.Name + ": terminal page still carries a cursor");
                        }
                    }
                    Check.True(checkedCollections > 20, "Checked collection retrievals at runtime: " + checkedCollections);
                    Check.True(problems.Count == 0, "Collection contract violations:\n" + String.Join("\n", problems));
                }),
                SuiteCases.Http(Id, "OpenApi.EveryEnumerationHonorsCreatedWindow", "Every POST .../enumerate route accepts the standard createdAfterUtc and createdBeforeUtc fields: a future window returns no objects, an inverted window is 400 fieldErrors.createdBeforeUtc, and no route fails", async (f, ct) =>
                {
                    JsonElement doc = await Api.ExpectAsync(f, HttpMethod.Get, "/openapi.json", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/metadata/User/" + member.Id, member.AccessToken, new { labels = new[] { "window-check" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    DateTime future = DateTime.UtcNow.AddYears(5);
                    List<string> problems = new List<string>();
                    int checkedRoutes = 0;
                    foreach (JsonProperty path in doc.GetProperty("paths").EnumerateObject())
                    {
                        if (!path.Name.EndsWith("/enumerate", StringComparison.Ordinal) || !path.Value.TryGetProperty("post", out JsonElement _)) continue;
                        string route = path.Name.Replace("{", "x_").Replace("}", String.Empty);
                        foreach (string bearer in new[] { HttpFixture.AdminToken, member.AccessToken })
                        {
                            ApiResult window = await Api.SendAsync(f, HttpMethod.Post, route, bearer, new { createdAfterUtc = future, createdBeforeUtc = future.AddDays(1), key = "level", search = "a" }, ct).ConfigureAwait(false);
                            if ((int)window.Status >= 500) problems.Add(path.Name + ": " + (int)window.Status + " " + window.Text);
                            else if (window.Status == HttpStatusCode.BadRequest && MentionsWindow(window)) problems.Add(path.Name + ": rejected the created window: " + window.Text);
                            else if (window.Status == HttpStatusCode.OK)
                            {
                                checkedRoutes++;
                                if (window.Body.GetProperty("objects").GetArrayLength() != 0) problems.Add(path.Name + ": a future created window still returned objects");
                                ApiResult inverted = await Api.SendAsync(f, HttpMethod.Post, route, bearer, new { createdAfterUtc = future, createdBeforeUtc = future.AddDays(-1), key = "level", search = "a" }, ct).ConfigureAwait(false);
                                if (inverted.Status != HttpStatusCode.BadRequest || !MentionsWindow(inverted)) problems.Add(path.Name + ": an inverted window was not 400 createdBeforeUtc (" + (int)inverted.Status + ")");
                            }
                        }
                    }
                    Check.True(checkedRoutes > 15, "Checked enumerations end to end (" + checkedRoutes + ")");
                    Check.True(problems.Count == 0, "Enumerations that do not honor the created window:\n  " + String.Join("\n  ", problems));
                }),
                SuiteCases.Unit(Id, "Scanner.DetectsViolations", "The scanner flags a missing 200 schema, a nested array, an unreviewed Bounded route, and accepts an EnumerationResult", ct =>
                {
                    string json = "{\"paths\":{" +
                        "\"/a\":{\"get\":{\"responses\":{\"200\":{\"description\":\"no schema\"}}}}," +
                        "\"/b\":{\"get\":{\"responses\":{\"200\":{\"content\":{\"application/json\":{\"schema\":{\"type\":\"object\",\"properties\":{\"x\":{\"type\":\"object\",\"properties\":{\"list\":{\"type\":\"array\",\"items\":{\"type\":\"object\"}}}}}}}}}}}}," +
                        "\"/c\":{\"get\":{\"responses\":{\"200\":{\"content\":{\"application/json\":{\"schema\":{\"type\":\"object\",\"description\":\"Fixed-bound: test\",\"properties\":{\"items\":{\"type\":\"array\"}}}}}}}}}," +
                        "\"/d\":{\"get\":{\"responses\":{\"200\":{\"content\":{\"application/json\":{\"schema\":{\"type\":\"object\",\"description\":\"EnumerationResult<X>: ...\",\"properties\":{\"objects\":{\"type\":\"array\"}}}}}}}}}," +
                        "\"/e\":{\"get\":{\"responses\":{\"200\":{\"content\":{\"text/csv\":{\"schema\":{\"type\":\"string\",\"format\":\"binary\"}}}}}}}," +
                        "\"/f\":{\"post\":{\"responses\":{\"200\":{\"description\":\"posts are not scanned\"}}}}}}";
                    using (JsonDocument parsed = JsonDocument.Parse(json))
                    {
                        List<string> problems = Scan(parsed.RootElement, out int scanned);
                        Check.Equal(5, scanned, "GET operations scanned");
                        Check.True(problems.Any(p => p.StartsWith("GET /a ", StringComparison.Ordinal)), "Missing schema flagged");
                        Check.True(problems.Any(p => p.StartsWith("GET /b ", StringComparison.Ordinal)), "Nested array flagged");
                        Check.True(problems.Any(p => p.StartsWith("GET /c ", StringComparison.Ordinal)), "Unreviewed Bounded flagged");
                        Check.True(!problems.Any(p => p.StartsWith("GET /d ", StringComparison.Ordinal)), "EnumerationResult accepted");
                        Check.True(!problems.Any(p => p.StartsWith("GET /e ", StringComparison.Ordinal)), "File response accepted");
                    }
                    return Task.CompletedTask;
                })
            });
        }

        #endregion

        #region Private-Methods

        private static List<string> Scan(JsonElement doc, out int scanned)
        {
            List<string> problems = new List<string>();
            scanned = 0;
            if (!doc.TryGetProperty("paths", out JsonElement paths) || paths.ValueKind != JsonValueKind.Object)
            {
                problems.Add("The document has no paths.");
                return problems;
            }
            foreach (JsonProperty path in paths.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                if (path.Value.ValueKind != JsonValueKind.Object || !TryGet(path.Value, "get", out JsonElement get)) continue;
                scanned++;
                string route = "GET " + path.Name;
                bool allowed = AllowList.ContainsKey(path.Name);
                List<JsonElement> schemas = new List<JsonElement>();
                if (TryGet(get, "responses", out JsonElement responses) && TryGet(responses, "200", out JsonElement ok) && TryGet(ok, "content", out JsonElement content) && content.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty media in content.EnumerateObject())
                    {
                        if (TryGet(media.Value, "schema", out JsonElement schema) && schema.ValueKind == JsonValueKind.Object) schemas.Add(schema);
                    }
                }
                if (schemas.Count == 0)
                {
                    problems.Add(route + " declares no 200 response schema (use .Enumeration, .Returns, .Bounded, or .File).");
                    continue;
                }
                foreach (JsonElement schema in schemas)
                {
                    string description = Description(schema);
                    bool isEnumeration = description.StartsWith("EnumerationResult<", StringComparison.Ordinal);
                    bool isBounded = description.StartsWith("Fixed-bound:", StringComparison.Ordinal);
                    if (isBounded && !allowed) problems.Add(route + " is declared .Bounded but is not on the reviewed allow-list.");
                    if (allowed && !isBounded) problems.Add(route + " is on the allow-list but its 200 schema is not declared .Bounded.");
                    if (!isEnumeration && !(allowed && isBounded) && ContainsArray(schema)) problems.Add(route + " returns an array outside an EnumerationResult.");
                }
            }
            return problems;
        }

        private static bool MentionsWindow(ApiResult result)
        {
            if (result.Body.ValueKind != JsonValueKind.Object || !result.Body.TryGetProperty("fieldErrors", out JsonElement errors) || errors.ValueKind != JsonValueKind.Object) return false;
            return errors.EnumerateObject().Any(e => e.Name.Contains("createdAfterUtc", StringComparison.OrdinalIgnoreCase) || e.Name.Contains("createdBeforeUtc", StringComparison.OrdinalIgnoreCase));
        }

        private static bool ContainsArray(JsonElement schema)
        {
            if (schema.ValueKind != JsonValueKind.Object) return false;
            if (TryGet(schema, "type", out JsonElement type))
            {
                if (type.ValueKind == JsonValueKind.String && type.GetString() == "array") return true;
                if (type.ValueKind == JsonValueKind.Array && type.EnumerateArray().Any(t => t.ValueKind == JsonValueKind.String && t.GetString() == "array")) return true;
            }
            if (TryGet(schema, "items", out JsonElement _)) return true;
            if (TryGet(schema, "properties", out JsonElement properties) && properties.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in properties.EnumerateObject()) if (ContainsArray(property.Value)) return true;
            }
            if (TryGet(schema, "additionalProperties", out JsonElement additional) && ContainsArray(additional)) return true;
            foreach (string composition in new[] { "allOf", "anyOf", "oneOf" })
            {
                if (TryGet(schema, composition, out JsonElement parts) && parts.ValueKind == JsonValueKind.Array && parts.EnumerateArray().Any(ContainsArray)) return true;
            }
            return false;
        }

        private static string Description(JsonElement schema)
        {
            return TryGet(schema, "description", out JsonElement d) && d.ValueKind == JsonValueKind.String ? d.GetString() ?? String.Empty : String.Empty;
        }

        private static bool TryGet(JsonElement element, string name, out JsonElement value)
        {
            value = default;
            return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value);
        }

        #endregion
    }
}
