namespace Test.Shared.IntegrationTests
{
    using System;
    using System.Net.Http;

    /// <summary>
    /// A <see cref="FakeHttpHandler"/> and an <see cref="HttpClient"/> that uses it.
    /// </summary>
    public class TestHttp : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Fake handler.
        /// </summary>
        public FakeHttpHandler Handler { get; } = new FakeHttpHandler();

        /// <summary>
        /// HTTP client sending to <see cref="Handler"/>.
        /// </summary>
        public HttpClient Client { get; }

        #endregion

        #region Private-Members

        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the pair.
        /// </summary>
        public TestHttp()
        {
            Client = new HttpClient(Handler, false);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dispose the client and handler.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Dispose resources.
        /// </summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing)
            {
                Client.Dispose();
                Handler.Dispose();
            }
            _Disposed = true;
        }

        #endregion
    }
}
