namespace TrellisKit.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Reflection;
    using System.Text.Json;
    using System.Threading.Tasks;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Security;
    using TrellisKit.Server.Serialization;
    using TrellisKit.Server.Services;
    using WatsonWebserver.Core;

    /// <summary>
    /// Response and query-string helpers shared by route registrars.
    /// </summary>
    public static class RouteHelper
    {
        #region Public-Members

        /// <summary>
        /// Receives unexpected route exceptions for logging, or null. Set by the server host.
        /// </summary>
        public static Action<HttpContextBase, Exception>? UnhandledExceptionLogger { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Send a JSON body.
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        /// <param name="statusCode">Status code.</param>
        /// <param name="body">Body object.</param>
        /// <returns>Task.</returns>
        public static async Task SendJsonAsync(HttpContextBase ctx, int statusCode, object? body)
        {
            ArgumentNullException.ThrowIfNull(ctx);
            ctx.Response.StatusCode = statusCode;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.Send(ApiJson.Serialize(body), ctx.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Send an empty response, for example 204.
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        /// <param name="statusCode">Status code.</param>
        /// <returns>Task.</returns>
        public static async Task SendEmptyAsync(HttpContextBase ctx, int statusCode)
        {
            ArgumentNullException.ThrowIfNull(ctx);
            ctx.Response.StatusCode = statusCode;
            await ctx.Response.Send(ctx.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Send an error body.
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        /// <param name="statusCode">Status code.</param>
        /// <param name="error">Error code.</param>
        /// <param name="message">Message.</param>
        /// <param name="fieldErrors">Optional per-field errors.</param>
        /// <returns>Task.</returns>
        public static Task SendErrorAsync(HttpContextBase ctx, int statusCode, string error, string message, Dictionary<string, string>? fieldErrors = null)
        {
            return SendJsonAsync(ctx, statusCode, new ErrorResponse(error, message, fieldErrors));
        }

        /// <summary>
        /// Route exception handler: logs nothing sensitive and returns a generic 500 body.
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        /// <param name="exception">Exception.</param>
        /// <returns>Task.</returns>
        public static Task HandleExceptionAsync(HttpContextBase ctx, Exception exception)
        {
            if (exception is ApiErrorException api)
            {
                if (api.StatusCode == 429) ctx.Response.Headers["Retry-After"] = (api.RetryAfterSeconds ?? 60).ToString(CultureInfo.InvariantCulture);
                if (api.Body != null) return SendJsonAsync(ctx, api.StatusCode, api.Body);
                return SendErrorAsync(ctx, api.StatusCode, api.Error, api.Message, api.FieldErrors);
            }
            if (exception is JsonException json)
            {
                ApiErrorException? typed = TypeError(json, null);
                if (typed != null) return SendErrorAsync(ctx, typed.StatusCode, typed.Error, typed.Message, typed.FieldErrors);
                return SendErrorAsync(ctx, 400, "InvalidJson", "The request body is not valid JSON for this route.");
            }
            if (exception is IntegrationNotConfiguredException) return SendErrorAsync(ctx, 503, "IntegrationNotConfigured", exception.Message);
            if (exception is OperationCanceledException) return SendErrorAsync(ctx, 499, "Cancelled", "The request was cancelled.");
            UnhandledExceptionLogger?.Invoke(ctx, exception);
            return SendErrorAsync(ctx, 500, "InternalError", "An unexpected error occurred. The error has been logged.");
        }

        /// <summary>
        /// Deserialize the JSON body, or a new instance when the body is empty.
        /// </summary>
        /// <typeparam name="T">Request type.</typeparam>
        /// <param name="ctx">HTTP context.</param>
        /// <returns>Request.</returns>
        /// <exception cref="ApiErrorException">400 InvalidJson.</exception>
        public static T ReadJson<T>(HttpContextBase ctx) where T : class, new()
        {
            return ReadJson<T>(ctx, out HashSet<string> _);
        }

        /// <summary>
        /// Deserialize the JSON body and report which top-level properties were present (camelCase).
        /// </summary>
        /// <typeparam name="T">Request type.</typeparam>
        /// <param name="ctx">HTTP context.</param>
        /// <param name="present">Present top-level property names, normalized to camelCase.</param>
        /// <returns>Request, or a new instance when the body is empty.</returns>
        /// <exception cref="ApiErrorException">400 InvalidJson.</exception>
        public static T ReadJson<T>(HttpContextBase ctx, out HashSet<string> present) where T : class, new()
        {
            ArgumentNullException.ThrowIfNull(ctx);
            present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string? body = ctx.Request.DataAsString;
            if (String.IsNullOrWhiteSpace(body)) return new T();
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(body))
                {
                    if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new ApiErrorException(400, "InvalidJson", "The request body must be a JSON object.");
                    foreach (JsonProperty property in doc.RootElement.EnumerateObject())
                    {
                        present.Add(property.Name.Length == 0 ? property.Name : Char.ToLowerInvariant(property.Name[0]) + property.Name.Substring(1));
                    }
                }
            }
            catch (JsonException)
            {
                throw new ApiErrorException(400, "InvalidJson", "The request body is not valid JSON for this route.");
            }
            try
            {
                return ApiJson.Deserialize<T>(body) ?? new T();
            }
            catch (JsonException e)
            {
                // Well-formed JSON whose value does not fit the field's type: name the field.
                throw TypeError(e, typeof(T)) ?? new ApiErrorException(400, "InvalidJson", "The request body is not valid JSON for this route.");
            }
        }

        /// <summary>
        /// Turn a deserialization error on well-formed JSON into 400 ValidationFailed naming the field (from
        /// <see cref="JsonException.Path"/>, for example rankLevel or filters.reason), or null when no field is known.
        /// </summary>
        /// <param name="exception">Deserialization error.</param>
        /// <param name="requestType">Request type, or null when unknown (generic message).</param>
        /// <returns>Exception, or null.</returns>
        /// <exception cref="ArgumentNullException">The exception is null.</exception>
        public static ApiErrorException? TypeError(JsonException exception, Type? requestType)
        {
            ArgumentNullException.ThrowIfNull(exception);
            string? path = exception.Path;
            if (String.IsNullOrWhiteSpace(path) || path == "$") return null;
            string field = path.StartsWith("$.", StringComparison.Ordinal) ? path.Substring(2) : path.TrimStart('$');
            if (field.StartsWith("['", StringComparison.Ordinal) && field.EndsWith("']", StringComparison.Ordinal)) field = field.Substring(2, field.Length - 4);
            if (field.Length == 0) return null;
            return ApiErrorException.Field(field, "Must be " + ExpectedKind(requestType, field) + ".");
        }

        /// <summary>
        /// The authenticated member's identifier.
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        /// <returns>Member identifier.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden for the admin token or anonymous callers.</exception>
        public static string RequireUserId(HttpContextBase ctx)
        {
            ArgumentNullException.ThrowIfNull(ctx);
            RequestContext? context = ctx.Metadata as RequestContext;
            if (context == null || String.IsNullOrEmpty(context.UserId)) throw ApiErrorException.Forbidden("This route needs a member session; the admin token cannot call it.");
            return context.UserId;
        }

        /// <summary>
        /// A URL parameter.
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        /// <param name="name">Parameter name.</param>
        /// <returns>Value, or empty.</returns>
        public static string Param(HttpContextBase ctx, string name)
        {
            ArgumentNullException.ThrowIfNull(ctx);
            string? value = ctx.Request.Url.Parameters?.Get(name);
            return String.IsNullOrEmpty(value) ? String.Empty : Uri.UnescapeDataString(value);
        }

        /// <summary>
        /// Read the GET form of an enumeration (docs/ENUMERATION_AND_METADATA.md section 2.1): maxResults,
        /// continuationToken, skip, ordering, search, createdAfterUtc, createdBeforeUtc, labels (repeatable, or comma
        /// separated), labelMatch, tag.&lt;key&gt;=&lt;value&gt; (repeatable), and every other parameter as a collection filter.
        /// </summary>
        /// <typeparam name="T">Query type (EnumerationQuery or a subclass).</typeparam>
        /// <param name="ctx">HTTP context.</param>
        /// <param name="routeParameters">Query parameters the route reads itself (not filters), for example depth.</param>
        /// <returns>Query.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed for malformed values.</exception>
        public static T ReadEnumerationQuery<T>(HttpContextBase ctx, params string[] routeParameters) where T : EnumerationQuery, new()
        {
            ArgumentNullException.ThrowIfNull(ctx);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            T query = new T();
            System.Collections.Specialized.NameValueCollection? elements = ctx.Request.Query.Elements;
            HashSet<string> skip = new HashSet<string>(routeParameters ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            System.Reflection.PropertyInfo[] own = typeof(T).GetProperties();
            List<KeyValuePair<string, string[]>> profile = new List<KeyValuePair<string, string[]>>();
            if (elements != null)
            {
                foreach (string? rawName in elements.AllKeys)
                {
                    if (String.IsNullOrEmpty(rawName)) continue;
                    string name = Uri.UnescapeDataString(rawName);
                    string[] values = (elements.GetValues(rawName) ?? Array.Empty<string>()).Select(v => Uri.UnescapeDataString((v ?? String.Empty).Replace('+', ' ')).Trim()).ToArray();
                    string value = values.Length == 0 ? String.Empty : values[values.Length - 1];
                    if (skip.Contains(name)) continue;
                    switch (name.ToLowerInvariant())
                    {
                        case "maxresults":
                            if (Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int max)) query.MaxResults = max; else errors["maxResults"] = "Must be a whole number.";
                            break;
                        case "continuationtoken":
                            query.ContinuationToken = value.Length == 0 ? null : value;
                            break;
                        case "skip":
                            if (Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int skipValue)) query.Skip = skipValue; else errors["skip"] = "Must be a whole number.";
                            break;
                        case "ordering":
                            query.Ordering = value.Length == 0 ? null : value;
                            break;
                        case "search":
                            query.Search = value.Length == 0 ? null : value;
                            break;
                        case "createdafterutc":
                            query.CreatedAfterUtc = QueryUtc(ctx, rawName, errors);
                            break;
                        case "createdbeforeutc":
                            query.CreatedBeforeUtc = QueryUtc(ctx, rawName, errors);
                            break;
                        case "labels":
                            foreach (string v in values) query.Labels.AddRange(v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                            break;
                        case "labelmatch":
                            if (Enum.TryParse(value, true, out TrellisKit.Core.Enums.LabelMatchEnum match) && Enum.IsDefined(match) && !Int32.TryParse(value, out int _)) query.LabelMatch = match; else errors["labelMatch"] = "Use All or Any.";
                            break;
                        default:
                            if (name.StartsWith("tag.", StringComparison.OrdinalIgnoreCase) && name.Length > 4) { query.Tags[name.Substring(4)] = value; break; }
                            if (name.StartsWith("profile.", StringComparison.OrdinalIgnoreCase) && name.Length > 8) { profile.Add(new KeyValuePair<string, string[]>(name.Substring(8), values)); break; }
                            System.Reflection.PropertyInfo? prop = own.FirstOrDefault(p => p.DeclaringType != typeof(EnumerationQuery) && p.CanWrite && p.PropertyType == typeof(string) && String.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                            if (prop != null) { prop.SetValue(query, value.Length == 0 ? null : value); break; }
                            query.SetFilter(name, value);
                            break;
                    }
                }
            }
            if (profile.Count > 0) query.Filters["profile"] = ProfileFilterJson(profile, errors);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors, "One or more query parameters are invalid.");
            return query;
        }

        private static System.Text.Json.JsonElement ProfileFilterJson(List<KeyValuePair<string, string[]>> parameters, Dictionary<string, string> errors)
        {
            // GET form of filters.profile (docs/MULTIPROPERTY_CONTRACT.md section 9.12): profile.<key>.<operator>=<value>,
            // repeatable for in, any, and all.
            Dictionary<string, Dictionary<string, object?>> map = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string[]> pair in parameters)
            {
                int dot = pair.Key.LastIndexOf('.');
                if (dot <= 0 || dot == pair.Key.Length - 1)
                {
                    errors["profile." + pair.Key] = "Use profile.<key>.<operator>=<value>.";
                    continue;
                }
                string key = pair.Key.Substring(0, dot);
                string op = pair.Key.Substring(dot + 1);
                if (!map.TryGetValue(key, out Dictionary<string, object?>? ops))
                {
                    ops = new Dictionary<string, object?>(StringComparer.Ordinal);
                    map[key] = ops;
                }
                string last = pair.Value.Length == 0 ? String.Empty : pair.Value[pair.Value.Length - 1];
                if (op == "in" || op == "any" || op == "all") ops[op] = pair.Value.SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToList();
                else if (last == "true" || last == "false") ops[op] = last == "true";
                else if ((op == "gte" || op == "lte" || op == "minLevel" || op == "eq") && Double.TryParse(last, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && !last.Contains('-', StringComparison.Ordinal)) ops[op] = number;
                else ops[op] = last;
            }
            return System.Text.Json.JsonSerializer.SerializeToElement(map);
        }

        /// <summary>
        /// Read the POST .../enumerate body (an empty body means the defaults).
        /// </summary>
        /// <typeparam name="T">Query type.</typeparam>
        /// <param name="ctx">HTTP context.</param>
        /// <returns>Query.</returns>
        /// <exception cref="ApiErrorException">400 InvalidJson.</exception>
        public static T ReadEnumerationBody<T>(HttpContextBase ctx) where T : EnumerationQuery, new()
        {
            return ReadJson<T>(ctx);
        }

        /// <summary>
        /// Read an optional enum query parameter, recording a field error when unrecognized.
        /// </summary>
        /// <typeparam name="T">Enum type.</typeparam>
        /// <param name="ctx">HTTP context.</param>
        /// <param name="name">Parameter name.</param>
        /// <param name="errors">Field errors.</param>
        /// <returns>Value, or null.</returns>
        public static T? QueryEnum<T>(HttpContextBase ctx, string name, Dictionary<string, string> errors) where T : struct, Enum
        {
            string? raw = Query(ctx, name);
            if (raw == null) return null;
            if (!Int32.TryParse(raw, out int _) && Enum.TryParse(raw, true, out T value) && Enum.IsDefined(value)) return value;
            errors[name] = "Use one of " + String.Join(", ", Enum.GetNames<T>()) + ".";
            return null;
        }

        /// <summary>
        /// Read an optional boolean query parameter, recording a field error when malformed.
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        /// <param name="name">Parameter name.</param>
        /// <param name="errors">Field errors.</param>
        /// <returns>Value, or null.</returns>
        public static bool? QueryBool(HttpContextBase ctx, string name, Dictionary<string, string> errors)
        {
            string? raw = Query(ctx, name);
            if (raw == null) return null;
            if (Boolean.TryParse(raw, out bool value)) return value;
            errors[name] = "Must be true or false.";
            return null;
        }

        /// <summary>
        /// Throw 400 ValidationFailed when any field error was recorded.
        /// </summary>
        /// <param name="errors">Field errors.</param>
        /// <exception cref="ApiErrorException">400 ValidationFailed.</exception>
        public static void ThrowIfErrors(Dictionary<string, string> errors)
        {
            if (errors != null && errors.Count > 0) throw ApiErrorException.Validation(errors, "One or more query parameters are invalid.");
        }

        /// <summary>
        /// Send one bounded CSV page: the X-Continuation-Token header carries the next page's token (absent on the last
        /// page) and X-Total-Records the total matching rows.
        /// </summary>
        /// <param name="ctx">Context.</param>
        /// <param name="fileName">Download file name.</param>
        /// <param name="page">Page.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static Task SendCsvPageAsync(HttpContextBase ctx, string fileName, CsvPage page)
        {
            ArgumentNullException.ThrowIfNull(ctx);
            ArgumentNullException.ThrowIfNull(page);
            if (!String.IsNullOrEmpty(page.ContinuationToken)) ctx.Response.Headers["X-Continuation-Token"] = page.ContinuationToken;
            if (page.TotalRecords != null) ctx.Response.Headers["X-Total-Records"] = page.TotalRecords.Value.ToString(CultureInfo.InvariantCulture);
            return SendCsvAsync(ctx, fileName, page.Csv);
        }

        /// <summary>
        /// Send a CSV attachment.
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        /// <param name="fileName">Download file name.</param>
        /// <param name="csv">CSV text.</param>
        /// <returns>Task.</returns>
        public static async Task SendCsvAsync(HttpContextBase ctx, string fileName, string csv)
        {
            ArgumentNullException.ThrowIfNull(ctx);
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "text/csv; charset=utf-8";
            ctx.Response.Headers["Content-Disposition"] = "attachment; filename=\"" + fileName + "\"";
            await ctx.Response.Send(csv ?? String.Empty, ctx.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Read a query-string value.
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        /// <param name="name">Parameter name (case-insensitive).</param>
        /// <returns>Value, or null when missing or empty.</returns>
        public static string? Query(HttpContextBase ctx, string name)
        {
            ArgumentNullException.ThrowIfNull(ctx);
            string? value = ctx.Request.Query.Elements?.Get(name);
            return String.IsNullOrWhiteSpace(value) ? null : Uri.UnescapeDataString(value.Replace('+', ' ')).Trim();
        }

        /// <summary>
        /// Read an optional integer query parameter, recording a field error when malformed.
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        /// <param name="name">Parameter name.</param>
        /// <param name="errors">Field error collection.</param>
        /// <returns>Value, or null.</returns>
        public static int? QueryInt(HttpContextBase ctx, string name, Dictionary<string, string> errors)
        {
            string? raw = Query(ctx, name);
            if (raw == null) return null;
            if (Int32.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)) return value;
            errors[name] = "Must be a whole number.";
            return null;
        }

        /// <summary>
        /// Read an optional ISO 8601 UTC timestamp query parameter, recording a field error when malformed.
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        /// <param name="name">Parameter name.</param>
        /// <param name="errors">Field error collection.</param>
        /// <returns>Value (UTC), or null.</returns>
        public static DateTime? QueryUtc(HttpContextBase ctx, string name, Dictionary<string, string> errors)
        {
            string? raw = Query(ctx, name);
            if (raw == null) return null;
            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime value)) return value;
            errors[name] = "Must be an ISO 8601 timestamp, for example 2026-11-11T00:00:00Z.";
            return null;
        }

        #endregion

        #region Private-Methods

        private static string ExpectedKind(Type? requestType, string field)
        {
            Type? type = requestType;
            foreach (string segment in field.Split('.'))
            {
                if (type == null) break;
                string name = segment;
                int bracket = name.IndexOf('[');
                bool indexed = bracket >= 0;
                if (indexed) name = name.Substring(0, bracket);
                PropertyInfo? property = type.GetProperties().FirstOrDefault(p => String.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                if (property == null)
                {
                    type = null;
                    break;
                }
                type = property.PropertyType;
                if (indexed) type = ElementType(type);
            }
            if (type == null) return "a value of the documented type";
            Type plain = Nullable.GetUnderlyingType(type) ?? type;
            if (plain.IsEnum) return "one of: " + String.Join(", ", Enum.GetNames(plain));
            if (plain == typeof(bool)) return "true or false";
            if (plain == typeof(int) || plain == typeof(long) || plain == typeof(short)) return "a whole number";
            if (plain == typeof(double) || plain == typeof(decimal) || plain == typeof(float)) return "a number";
            if (plain == typeof(string)) return "a string";
            if (plain == typeof(DateTime) || plain == typeof(DateTimeOffset)) return "an ISO 8601 timestamp";
            if (plain.IsGenericType && plain.GetGenericTypeDefinition() == typeof(Dictionary<,>)) return "an object";
            if (ElementType(plain) != null) return "an array";
            return "an object";
        }

        private static Type? ElementType(Type type)
        {
            if (type.IsArray) return type.GetElementType();
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) return type.GetGenericArguments()[0];
            return null;
        }

        #endregion
    }
}
