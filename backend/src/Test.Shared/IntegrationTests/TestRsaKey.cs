namespace Test.Shared.IntegrationTests
{
    using System;
    using System.Security.Cryptography;

    /// <summary>
    /// A runtime-generated RSA signing key with a kid, for building JWKS documents and signing test tokens.
    /// </summary>
    public sealed class TestRsaKey : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Key identifier.
        /// </summary>
        public string Kid { get; }

        /// <summary>
        /// RSA key pair.
        /// </summary>
        public RSA Rsa { get; }

        #endregion

        #region Private-Members

        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Generate a 2048-bit key.
        /// </summary>
        /// <param name="kid">Key identifier.</param>
        /// <exception cref="ArgumentNullException">The kid is null.</exception>
        public TestRsaKey(string kid)
        {
            Kid = kid ?? throw new ArgumentNullException(nameof(kid));
            Rsa = RSA.Create(2048);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dispose the key.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private-Methods

        private void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing) Rsa.Dispose();
            _Disposed = true;
        }

        #endregion
    }
}
