namespace Test.Shared.Suites
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Reflection;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading.Tasks;
    using Touchstone.Core;

    /// <summary>
    /// Route descriptions must name real fields (clients follow the types, so a description that disagrees misleads
    /// them). Scans /openapi.json: in each operation description, the field list after "Body {" is checked against the
    /// request type named by the request body (a class in TrellisKit.Core.Requests), and the field list after
    /// "carries {" against the response type named by the 200 or 201 response (Name, NameView, NameResponse, or NameItem
    /// in TrellisKit.Core.Responses; for an EnumerationResult the item type). Nested lists ("field { ... }" and
    /// "field [{ ... }]") are checked against the property's type. Field names before ":" or "(" are checked; enum
    /// values and explanations are not. Every offending route and field is named in the failure.
    /// </summary>
    public static class OpenApiDocsSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "OpenApiDocs";

        #endregion

        #region Private-Members

        private static readonly Assembly _Core = typeof(TrellisKit.Core.Responses.ForumView).Assembly;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "OpenAPI descriptions (" + TestEnvironment.Provider + ")", new[]
            {
                SuiteCases.Http(Id, "Descriptions.FieldsExistOnTypes", "Every field named in a route description's Body { ... } or carries { ... } list is a JSON property of the documented request or response type", async (f, ct) =>
                {
                    JsonElement doc = await Api.ExpectAsync(f, HttpMethod.Get, "/openapi.json", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    List<string> problems = Scan(doc, out int checkedFields);
                    Check.True(checkedFields > 60, "Checked documented fields (" + checkedFields + ")");
                    Check.True(problems.Count == 0, "Descriptions that disagree with the types:\n  " + String.Join("\n  ", problems));
                }),
                SuiteCases.Unit(Id, "Scanner.DetectsMismatches", "The scanner flags an unknown body field, an unknown nested field, and an unknown response field, and accepts real ones", ct =>
                {
                    string json = "{\"paths\":{\"/x\":{\"post\":{\"description\":\"Body { title, excerpt?, pin, bogus }. Each item carries { id, unreadThreadCount, viewer { canFollow, nope } }.\"," +
                        "\"requestBody\":{\"description\":\"PromoteRequest\"},\"responses\":{\"201\":{\"description\":\"Forum\"}}}}," +
                        "\"/y\":{\"post\":{\"description\":\"Body { type, poll { options [{ label, wrong }], multipleChoice } }.\",\"requestBody\":{\"description\":\"ThreadRequest\"},\"responses\":{\"200\":{\"description\":\"EnumerationResult<ThreadSummary>\"}}}}}}";
                    using (JsonDocument parsed = JsonDocument.Parse(json))
                    {
                        List<string> problems = Scan(parsed.RootElement, out int checkedFields);
                        Check.True(problems.Any(p => p.Contains("'bogus'", StringComparison.Ordinal)), "Unknown body field flagged: " + String.Join("; ", problems));
                        Check.True(problems.Any(p => p.Contains("'nope'", StringComparison.Ordinal)), "Unknown nested response field flagged");
                        Check.True(problems.Any(p => p.Contains("'wrong'", StringComparison.Ordinal)), "Unknown nested list field flagged");
                        Check.Equal(3, problems.Count, "Only the three wrong fields: " + String.Join("; ", problems));
                        Check.True(checkedFields >= 13, "Fields checked (" + checkedFields + ")");
                    }
                    return Task.CompletedTask;
                })
            });
        }

        #endregion

        #region Private-Methods

        private static List<string> Scan(JsonElement doc, out int checkedFields)
        {
            List<string> problems = new List<string>();
            checkedFields = 0;
            if (!doc.TryGetProperty("paths", out JsonElement paths) || paths.ValueKind != JsonValueKind.Object) return problems;
            foreach (JsonProperty path in paths.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                if (path.Value.ValueKind != JsonValueKind.Object) continue;
                foreach (JsonProperty op in path.Value.EnumerateObject())
                {
                    if (op.Value.ValueKind != JsonValueKind.Object) continue;
                    string route = op.Name.ToUpperInvariant() + " " + path.Name;
                    string description = Str(op.Value, "description");
                    if (description.Length == 0) continue;
                    Type? request = op.Value.TryGetProperty("requestBody", out JsonElement body) ? RequestType(Str(body, "description")) : null;
                    Type? response = ResponseType(op.Value);
                    foreach (string list in Lists(description, "Body {"))
                    {
                        if (request == null) continue;
                        checkedFields += CheckFields(route, "Body", request, list, problems);
                    }
                    foreach (string list in Lists(description, "carries {"))
                    {
                        if (response == null)
                        {
                            problems.Add(route + ": the description lists response fields but the response names no known type");
                            continue;
                        }
                        checkedFields += CheckFields(route, "Response", response, list, problems);
                    }
                }
            }
            return problems;
        }

        private static int CheckFields(string route, string where, Type type, string list, List<string> problems)
        {
            int count = 0;
            foreach (string token in SplitTopLevel(list))
            {
                string t = token.Trim();
                if (t.StartsWith("and ", StringComparison.Ordinal)) t = t.Substring(4).Trim();
                if (t.StartsWith("...", StringComparison.Ordinal) || t.Length == 0) continue;
                int end = 0;
                while (end < t.Length && (Char.IsLetterOrDigit(t[end]) || t[end] == '_')) end++;
                if (end == 0 || !Char.IsLetter(t[0])) continue;
                string name = t.Substring(0, end);
                string rest = t.Substring(end).TrimStart('?').TrimStart();
                count++;
                PropertyInfo? property = Find(type, name);
                if (property == null)
                {
                    problems.Add(route + ": " + where + " field '" + name + "' is not a property of " + type.Name);
                    continue;
                }
                string? nested = null;
                if (rest.StartsWith("{", StringComparison.Ordinal)) nested = Group(rest, 0);
                else if (rest.StartsWith("[{", StringComparison.Ordinal)) nested = Group(rest, 1);
                if (nested == null) continue;
                Type? inner = ElementType(property.PropertyType);
                if (inner == null) continue;
                count += CheckFields(route, where + "." + name, inner, nested, problems);
            }
            return count;
        }

        private static IEnumerable<string> Lists(string description, string marker)
        {
            int at = 0;
            while ((at = description.IndexOf(marker, at, StringComparison.Ordinal)) >= 0)
            {
                int open = at + marker.Length - 1;
                string? group = Group(description, open);
                if (group == null) yield break;
                yield return group;
                at = open + 1;
            }
        }

        private static string? Group(string text, int open)
        {
            if (open >= text.Length || text[open] != '{') return null;
            int depth = 0;
            for (int i = open; i < text.Length; i++)
            {
                if (text[i] == '{') depth++;
                else if (text[i] == '}')
                {
                    depth--;
                    if (depth == 0) return text.Substring(open + 1, i - open - 1);
                }
            }
            return null;
        }

        private static List<string> SplitTopLevel(string list)
        {
            List<string> parts = new List<string>();
            int depth = 0;
            int start = 0;
            for (int i = 0; i < list.Length; i++)
            {
                char c = list[i];
                if (c == '{' || c == '(' || c == '[') depth++;
                else if (c == '}' || c == ')' || c == ']') depth--;
                else if (c == ',' && depth == 0)
                {
                    parts.Add(list.Substring(start, i - start));
                    start = i + 1;
                }
            }
            parts.Add(list.Substring(start));
            return parts;
        }

        private static PropertyInfo? Find(Type type, string jsonName)
        {
            foreach (PropertyInfo p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;
                JsonPropertyNameAttribute? attribute = p.GetCustomAttribute<JsonPropertyNameAttribute>();
                string name = attribute?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(p.Name);
                if (String.Equals(name, jsonName, StringComparison.Ordinal)) return p;
            }
            return null;
        }

        private static Type? ElementType(Type type)
        {
            Type t = Nullable.GetUnderlyingType(type) ?? type;
            if (t == typeof(string)) return null;
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>)) return null;
            if (t.IsArray) return t.GetElementType();
            if (typeof(IEnumerable).IsAssignableFrom(t) && t.IsGenericType) return t.GetGenericArguments()[0];
            if (t.IsGenericType && t.Name.StartsWith("BoundedPreview", StringComparison.Ordinal)) return t.GetGenericArguments()[0];
            return t.IsClass ? t : null;
        }

        private static Type? RequestType(string description)
        {
            string name = description.Trim();
            if (name.Length == 0 || !name.All(c => Char.IsLetterOrDigit(c))) return null;
            return _Core.GetType("TrellisKit.Core.Requests." + name);
        }

        private static Type? ResponseType(JsonElement op)
        {
            if (!op.TryGetProperty("responses", out JsonElement responses) || responses.ValueKind != JsonValueKind.Object) return null;
            foreach (string code in new[] { "200", "201" })
            {
                if (!responses.TryGetProperty(code, out JsonElement response)) continue;
                string description = Str(response, "description").Trim();
                if (description.StartsWith("EnumerationResult<", StringComparison.Ordinal)) description = description.Substring("EnumerationResult<".Length);
                int end = 0;
                while (end < description.Length && Char.IsLetterOrDigit(description[end])) end++;
                string name = description.Substring(0, end);
                if (name.Length == 0) continue;
                foreach (string candidate in new[] { name + "View", name, name + "Response", name + "Item" })
                {
                    Type? type = _Core.GetType("TrellisKit.Core.Responses." + candidate);
                    if (type != null) return type;
                }
            }
            return null;
        }

        private static string Str(JsonElement element, string name)
        {
            return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? String.Empty : String.Empty;
        }

        #endregion
    }
}
