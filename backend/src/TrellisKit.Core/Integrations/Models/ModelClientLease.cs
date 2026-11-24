namespace TrellisKit.Core.Integrations.Models
{
    using System;
    using System.Net.Http;
    using PolyPrompt.Clients;
    using TrellisKit.Core.Integrations.Implementations;

    /// <summary>
    /// A PolyPrompt client created for one call, with the redactor of its endpoint. Disposing the lease disposes the
    /// client and, for uncached (unsaved) endpoints, the HTTP client the lease owns.
    /// </summary>
    /// <typeparam name="TClient">PolyPrompt client type.</typeparam>
    public sealed class ModelClientLease<TClient> : IDisposable where TClient : HttpClientBase
    {
        #region Public-Members

        /// <summary>
        /// The PolyPrompt client.
        /// </summary>
        public TClient Client { get; }

        /// <summary>
        /// Redactor for this endpoint's secrets.
        /// </summary>
        public AiSecretRedactor Redactor { get; }

        #endregion

        #region Private-Members

        private readonly HttpClient? _Owned;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a lease.
        /// </summary>
        /// <param name="client">Client.</param>
        /// <param name="redactor">Redactor.</param>
        /// <param name="owned">HTTP client owned by the lease, or null when it is cached by the factory.</param>
        /// <exception cref="ArgumentNullException">The client or redactor is null.</exception>
        public ModelClientLease(TClient client, AiSecretRedactor redactor, HttpClient? owned)
        {
            Client = client ?? throw new ArgumentNullException(nameof(client));
            Redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
            _Owned = owned;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dispose the client (and the owned HTTP client).
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            Client.Dispose();
            _Owned?.Dispose();
        }

        #endregion
    }
}
