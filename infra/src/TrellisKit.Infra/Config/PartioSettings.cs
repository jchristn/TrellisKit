namespace TrellisKit.Infra.Config
{
    using System;
    using System.Text.Json;

    /// <summary>
    /// Optional Partio document text service (settings documents.partio.baseUrl and documents.partio.apiKey, both live).
    /// Read from the partio section of a property's deploy/infra.json; in the shared topology the first listed
    /// property's section applies (deployment-level settings). Unset by default: nothing is passed and no secret
    /// exists, so the admin console decides. When baseUrl is set the server receives TK_PARTIO_BASE_URL and the Api
    /// stack creates {prefix}/partio, injected as TK_PARTIO_API_KEY (filled by hand, never in a file). Turning Partio on
    /// (documents.partio.enabled) stays a console setting.
    /// Thread safety: not thread safe; configure before building stacks.
    /// </summary>
    public class PartioSettings
    {
        private string _BaseUrl = String.Empty;

        /// <summary>
        /// Base URL of the Partio REST API (TK_PARTIO_BASE_URL), for example https://partio.example.test, or empty.
        /// Default empty.
        /// </summary>
        /// <exception cref="ArgumentException">The value is not an absolute https URL.</exception>
        public string BaseUrl
        {
            get
            {
                return _BaseUrl;
            }
            set
            {
                string text = (value ?? String.Empty).Trim();
                if (text.Length > 0
                    && (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps || String.IsNullOrEmpty(uri.Host)))
                {
                    throw new ArgumentException("partio.baseUrl must be an absolute https URL, got '" + text + "'.", nameof(BaseUrl));
                }

                _BaseUrl = text;
            }
        }

        /// <summary>
        /// Whether Partio is configured (a base URL is set).
        /// </summary>
        public bool Enabled
        {
            get { return _BaseUrl.Length > 0; }
        }

        /// <summary>
        /// Read the partio section of a deploy/infra.json file (baseUrl). A missing or non-object section yields empty
        /// settings.
        /// </summary>
        /// <param name="root">Root element of the file.</param>
        /// <param name="file">File path, used in error messages.</param>
        /// <returns>Settings.</returns>
        /// <exception cref="ArgumentException">A value is invalid.</exception>
        public static PartioSettings FromJson(JsonElement root, string file)
        {
            PartioSettings settings = new PartioSettings();
            if (root.ValueKind != JsonValueKind.Object) return settings;
            if (!root.TryGetProperty("partio", out JsonElement partio) || partio.ValueKind != JsonValueKind.Object) return settings;

            try
            {
                if (partio.TryGetProperty("baseUrl", out JsonElement url) && url.ValueKind == JsonValueKind.String) settings.BaseUrl = url.GetString() ?? String.Empty;
            }
            catch (ArgumentException e)
            {
                throw new ArgumentException("Invalid partio section in " + file + ": " + e.Message, e);
            }

            return settings;
        }
    }
}
