namespace Test.Shared.IntegrationTests
{
    using System;

    /// <summary>
    /// A route registered on <see cref="FakeHttpHandler"/>.
    /// </summary>
    public sealed class FakeHttpRoute
    {
        #region Public-Members

        /// <summary>
        /// HTTP method to match (case-insensitive).
        /// </summary>
        public string Method { get; }

        /// <summary>
        /// URL prefix to match (ordinal), including scheme and host.
        /// </summary>
        public string UrlPrefix { get; }

        /// <summary>
        /// Produces the response.
        /// </summary>
        public Func<RecordedRequest, FakeHttpResponse> Responder { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the route.
        /// </summary>
        /// <param name="method">HTTP method.</param>
        /// <param name="urlPrefix">URL prefix.</param>
        /// <param name="responder">Response factory.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public FakeHttpRoute(string method, string urlPrefix, Func<RecordedRequest, FakeHttpResponse> responder)
        {
            Method = method ?? throw new ArgumentNullException(nameof(method));
            UrlPrefix = urlPrefix ?? throw new ArgumentNullException(nameof(urlPrefix));
            Responder = responder ?? throw new ArgumentNullException(nameof(responder));
        }

        #endregion
    }
}
