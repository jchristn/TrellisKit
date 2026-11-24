namespace Test.Shared.Fakes
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// An HTTP handler that stands in for the Mailgun messages API: it records each request (URL, authorization, and the
    /// decoded form fields, repeated names kept) and answers with a fixed status and body.
    /// Thread safety: not intended for concurrent use.
    /// </summary>
    public sealed class RecordingMailgunHandler : HttpMessageHandler
    {
        #region Public-Members

        /// <summary>
        /// Status code returned. Default 200.
        /// </summary>
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        /// <summary>
        /// Body returned. Default a Mailgun "Queued" response with the id &lt;mg-test-1@example.test&gt;.
        /// </summary>
        public string Body { get; set; } = "{\"id\":\"<mg-test-1@example.test>\",\"message\":\"Queued. Thank you.\"}";

        /// <summary>
        /// Last request URL, or null.
        /// </summary>
        public string? Url { get; private set; } = null;

        /// <summary>
        /// Last Authorization header value, or null.
        /// </summary>
        public string? Authorization { get; private set; } = null;

        /// <summary>
        /// Last request's form fields in order. Never null.
        /// </summary>
        public List<KeyValuePair<string, string>> Fields { get; } = new List<KeyValuePair<string, string>>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Values of a form field.
        /// </summary>
        /// <param name="name">Field name.</param>
        /// <returns>Values in order.</returns>
        public List<string> All(string name)
        {
            List<string> values = new List<string>();
            foreach (KeyValuePair<string, string> field in Fields)
                if (String.Equals(field.Key, name, StringComparison.Ordinal)) values.Add(field.Value);
            return values;
        }

        /// <summary>
        /// First value of a form field, or null.
        /// </summary>
        /// <param name="name">Field name.</param>
        /// <returns>Value, or null.</returns>
        public string? One(string name)
        {
            List<string> values = All(name);
            return values.Count == 0 ? null : values[0];
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri?.ToString();
            Authorization = request.Headers.Authorization?.ToString();
            Fields.Clear();
            string body = request.Content == null ? String.Empty : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            foreach (string pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                string key = eq < 0 ? pair : pair.Substring(0, eq);
                string value = eq < 0 ? String.Empty : pair.Substring(eq + 1);
                Fields.Add(new KeyValuePair<string, string>(WebUtility.UrlDecode(key), WebUtility.UrlDecode(value)));
            }
            return new HttpResponseMessage(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") };
        }

        #endregion
    }
}
