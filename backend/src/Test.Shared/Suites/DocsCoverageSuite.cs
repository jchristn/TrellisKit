namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;

    /// <summary>
    /// Living documentation (docs/INTEGRATION.md): every route the server registers appears in
    /// REST_API.md, so a route change without its documentation fails the build.
    /// </summary>
    public static class DocsCoverageSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "DocsCoverage";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Living documentation (" + TestEnvironment.Provider + ")", new[]
            {
                SuiteCases.Http(Id, "RestApi.EveryRouteDocumented", "Every method and path in the server's OpenAPI document is listed in REST_API.md", async (f, ct) =>
                {
                    JsonElement doc = await Api.ExpectAsync(f, HttpMethod.Get, "/openapi.json", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    HashSet<string> documented = Documented(File.ReadAllText(RestApiPath()));
                    List<string> missing = new List<string>();
                    int routes = 0;
                    foreach (JsonProperty path in doc.GetProperty("paths").EnumerateObject())
                    {
                        foreach (JsonProperty op in path.Value.EnumerateObject())
                        {
                            string method = op.Name.ToUpperInvariant();
                            if (method != "GET" && method != "POST" && method != "PUT" && method != "PATCH" && method != "DELETE") continue;
                            routes++;
                            if (!IsDocumented(documented, method, path.Name)) missing.Add(method + " " + path.Name);
                        }
                    }
                    Check.True(routes > 100, "Scanned the server's routes (" + routes + ")");
                    Check.True(missing.Count == 0, missing.Count + " routes are missing from REST_API.md:\n  " + String.Join("\n  ", missing.OrderBy(m => m, StringComparer.Ordinal)));
                }),
                SuiteCases.Http(Id, "Postman.EveryEnumerationDocumented", "Every canonical enumeration route is represented in the saved Postman collection", async (f, ct) =>
                {
                    JsonElement api = await Api.ExpectAsync(f, HttpMethod.Get, "/openapi.json", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    string path = Path.Combine(Path.GetDirectoryName(RestApiPath())!, "assets", "postman", "TrellisKit.postman_collection.json");
                    using JsonDocument collection = JsonDocument.Parse(File.ReadAllText(path));
                    HashSet<string> documented = new HashSet<string>(StringComparer.Ordinal);
                    void Visit(JsonElement items)
                    {
                        foreach (JsonElement item in items.EnumerateArray())
                        {
                            if (item.TryGetProperty("item", out JsonElement children)) { Visit(children); continue; }
                            if (!item.TryGetProperty("request", out JsonElement request) || !request.TryGetProperty("url", out JsonElement url)) continue;
                            string raw = url.ValueKind == JsonValueKind.String ? url.GetString() ?? String.Empty : url.GetProperty("raw").GetString() ?? String.Empty;
                            raw = raw.Replace("{{baseUrl}}", String.Empty).Split('?')[0];
                            raw = Regex.Replace(raw, @"\{\{[^}]+\}\}", "{}");
                            raw = Regex.Replace(raw, @"\{[^}]*\}", "{}");
                            documented.Add((request.GetProperty("method").GetString() ?? String.Empty).ToUpperInvariant() + " " + raw);
                        }
                    }
                    Visit(collection.RootElement.GetProperty("item"));
                    List<string> missing = new List<string>();
                    foreach (JsonProperty route in api.GetProperty("paths").EnumerateObject())
                    {
                        if (!route.Name.EndsWith("/enumerate", StringComparison.Ordinal) || !route.Value.TryGetProperty("post", out _)) continue;
                        string normalized = Regex.Replace(route.Name, @"\{[^}]*\}", "{}");
                        if (!documented.Contains("POST " + normalized)) missing.Add("POST " + route.Name);
                    }
                    Check.True(missing.Count == 0, "Postman enumeration requests missing:\n" + String.Join("\n", missing));
                }),
                SuiteCases.Http(Id, "RestApi.ParserResolvesShorthand", "The REST_API.md parser reads full paths, path parameters under any name, and .../suffix shorthand", (f, ct) =>
                {
                    HashSet<string> set = Documented("| `GET /api/v1/admin/jobs/runs` and `POST .../runs/enumerate` | x |\n| `POST /api/v1/admin/jobs/items/{itemId}/retry` | `{}` |\n| `GET /api/v1/things`, `POST /api/v1/things/enumerate` | y |\n| `POST .../enumerate` | z |");
                    Check.True(set.Contains("GET /api/v1/admin/jobs/runs"), "Full path");
                    Check.True(set.Contains("POST /api/v1/admin/jobs/runs/enumerate"), "Overlapping shorthand");
                    Check.True(set.Contains("POST /api/v1/admin/jobs/items/{}/retry"), "Parameters by any name");
                    Check.True(set.Contains("POST /api/v1/things/enumerate"), "Comma list");
                    Check.True(!set.Contains("POST /api/v1/things/enumerate/enumerate"), "Shorthand after a suffix does not double up");
                    HashSet<string> more = Documented("| `GET`, `PUT`, `DELETE /api/v1/admin/venues/{id}` | x |\n| `POST /api/v1/admin/venues/{id}/verify`, `.../unverify` | y |\n| `PUT /api/v1/a/{id}/b/{bId}`, `DELETE` same | z |\n| `POST /api/v1/u/enumerate` (and `GET`) | w |\n| `GET /api/v1/directory/tiles/{*path}` | t |\nThe GET call returns it.");
                    Check.True(more.Contains("PUT /api/v1/admin/venues/{}") && more.Contains("DELETE /api/v1/admin/venues/{}"), "Method list before a path");
                    Check.True(more.Contains("POST /api/v1/admin/venues/{}/unverify"), "Bare shorthand inherits the method");
                    Check.True(more.Contains("DELETE /api/v1/a/{}/b/{}"), "METHOD same");
                    Check.True(more.Contains("GET /api/v1/u/enumerate"), "(and GET) after a path");
                    Check.True(IsDocumented(more, "GET", "/api/v1/directory/tiles/{p1}/{p2}/{p3}"), "Catch-all covers deeper paths");
                    Check.True(!IsDocumented(more, "POST", "/api/v1/directory/tiles/{p1}"), "Catch-all keeps its method");
                    return Task.CompletedTask;
                })
            });
        }

        #endregion

        #region Private-Methods

        private static readonly Regex _Token = new Regex(@"\b(GET|POST|PUT|PATCH|DELETE)\b|((?:/api/v1|/openapi|/swagger)[^\s`|,;)]*)|(\.\.\./[^\s`|,;)]*)|\b(same)\b", RegexOptions.Compiled);

        private static string RestApiPath()
        {
            string root = Path.GetFullPath(Path.Combine(TestProperty.SampleDirectory, "..", ".."));
            string path = Path.Combine(root, "REST_API.md");
            if (!File.Exists(path)) throw new FileNotFoundException("REST_API.md was not found at the repository root.", path);
            return path;
        }

        /// <summary>
        /// Method and normalized path pairs documented in REST_API.md. Understands a method list before a path
        /// ("GET, PUT, DELETE /path"), a method after a path in a table row ("/path (and GET)"), "METHOD same",
        /// ".../suffix" shorthand (with or without a method; without one it inherits the previous methods), and catch-all
        /// parameters ("{*path}", stored as a prefix ending in "/*").
        /// </summary>
        internal static HashSet<string> Documented(string markdown)
        {
            HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
            string? lastPath = null;
            List<string> lastMethods = new List<string>();
            foreach (string line in markdown.Split('\n'))
            {
                bool tableRow = line.TrimStart().StartsWith("|", StringComparison.Ordinal);
                List<string> pending = new List<string>();
                bool pathInLine = false;
                foreach (Match m in _Token.Matches(line))
                {
                    if (m.Groups[1].Success)
                    {
                        pending.Add(m.Groups[1].Value);
                    }
                    else if (m.Groups[4].Success)
                    {
                        if (lastPath != null && pending.Count > 0) Add(result, pending, lastPath);
                        pending.Clear();
                    }
                    else
                    {
                        string path;
                        if (m.Groups[3].Success)
                        {
                            if (lastPath == null) continue;
                            string relative = m.Groups[3].Value.Substring(3).TrimEnd('.', ':');
                            path = Resolve(lastPath, relative);
                            string? sibling = Sibling(lastPath, relative);
                            List<string> siblingMethods = pending.Count > 0 ? pending : lastMethods;
                            if (sibling != null && siblingMethods.Count > 0) Add(result, siblingMethods, sibling);
                        }
                        else
                        {
                            path = m.Groups[2].Value.TrimEnd('.', ':');
                        }
                        List<string> methods = pending.Count > 0 ? new List<string>(pending) : (m.Groups[3].Success ? new List<string>(lastMethods) : new List<string>());
                        if (methods.Count == 0)
                        {
                            // A bare path (prose or a table cell without a method): remember it for "(and GET)" or "same".
                            lastPath = path;
                            pathInLine = true;
                            continue;
                        }
                        Add(result, methods, path);
                        lastPath = path;
                        lastMethods = methods;
                        pending.Clear();
                        pathInLine = true;
                    }
                }
                if (tableRow && pathInLine && pending.Count > 0 && lastPath != null) Add(result, pending, lastPath);
            }

            return result;
        }

        private static void Add(HashSet<string> set, List<string> methods, string path)
        {
            foreach (string method in methods) set.Add(method + " " + Normalize(path));
        }

        internal static bool IsDocumented(HashSet<string> documented, string method, string path)
        {
            string key = method + " " + Normalize(path);
            if (documented.Contains(key)) return true;
            foreach (string entry in documented)
            {
                if (!entry.EndsWith("/*", StringComparison.Ordinal) || !entry.StartsWith(method + " ", StringComparison.Ordinal)) continue;
                string prefix = entry.Substring(0, entry.Length - 1);
                if (key.StartsWith(prefix, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static string Resolve(string previous, string relative)
        {
            List<string> prev = previous.Trim('/').Split('/').ToList();
            List<string> rel = relative.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
            int overlap = 0;
            for (int k = Math.Min(prev.Count, rel.Count); k > 0; k--)
            {
                if (prev.Skip(prev.Count - k).SequenceEqual(rel.Take(k), StringComparer.Ordinal))
                {
                    overlap = k;
                    break;
                }
            }
            return "/" + String.Join("/", prev.Take(prev.Count - overlap).Concat(rel));
        }

        private static string? Sibling(string previous, string relative)
        {
            // ".../unverify" after ".../verify" names a sibling: replace the last segment (only when Resolve found no overlap).
            List<string> prev = previous.Trim('/').Split('/').ToList();
            List<string> rel = relative.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (prev.Count < 2 || rel.Count == 0 || prev.Contains(rel[0], StringComparer.Ordinal)) return null;
            return "/" + String.Join("/", prev.Take(prev.Count - 1).Concat(rel));
        }

        private static string Normalize(string path)
        {
            string p = Regex.Replace(path.Split('?')[0], @"\{\*[^}]*\}.*$", "*");
            p = Regex.Replace(p, @"\{[^}]*\}", "{}");
            return p.Length > 1 ? p.TrimEnd('/') : p;
        }

        #endregion
    }
}
