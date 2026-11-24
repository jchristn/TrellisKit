namespace TrellisKit.Server.Serialization
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// JSON conventions for the REST API (camelCase, string enums) and for the settings file (PascalCase, indented).
    /// Thread safety: options instances are immutable after first use and safe to share.
    /// </summary>
    public static class ApiJson
    {
        #region Public-Members

        /// <summary>
        /// Options for API request and response bodies.
        /// </summary>
        public static readonly JsonSerializerOptions Api = CreateApiOptions();

        /// <summary>
        /// Options for the settings file.
        /// </summary>
        public static readonly JsonSerializerOptions File = CreateFileOptions();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Serialize an object for an API response.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>JSON.</returns>
        public static string Serialize(object? value)
        {
            return JsonSerializer.Serialize(value, Api);
        }

        /// <summary>
        /// Deserialize an API request body into a typed request.
        /// </summary>
        /// <typeparam name="T">Request type.</typeparam>
        /// <param name="json">JSON text.</param>
        /// <returns>Request, or null when the body is empty or the literal null.</returns>
        /// <exception cref="JsonException">The body is not valid JSON for the type.</exception>
        public static T? Deserialize<T>(string? json) where T : class
        {
            if (String.IsNullOrWhiteSpace(json)) return null;
            return JsonSerializer.Deserialize<T>(json, Api);
        }

        #endregion

        #region Private-Methods

        private static JsonSerializerOptions CreateApiOptions()
        {
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DictionaryKeyPolicy = null,
                PropertyNameCaseInsensitive = true,
                WriteIndented = false
            };
            options.Converters.Add(new JsonStringEnumConverter());
            options.Converters.Add(new NulStrippingStringConverter());
            return options;
        }

        private static JsonSerializerOptions CreateFileOptions()
        {
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };
            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }

        #endregion
    }
}
