namespace Test.Shared.IntegrationTests
{
    using System;
    using System.Security.Cryptography;
    using System.Security.Cryptography.X509Certificates;
    using System.Text.Json.Nodes;

    /// <summary>
    /// A self-signed root, an intermediate with the Apple WWDR marker extension, and a leaf with the App Store signing
    /// marker extension, generated at runtime to test App Store JWS verification offline.
    /// </summary>
    public sealed class TestAppleChain : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Apple WWDR intermediate marker extension.
        /// </summary>
        public const string IntermediateOid = "1.2.840.113635.100.6.2.1";

        /// <summary>
        /// App Store signing leaf marker extension.
        /// </summary>
        public const string LeafOid = "1.2.840.113635.100.6.11.1";

        /// <summary>
        /// Root certificate (public part only), for the client's rootCertificate override.
        /// </summary>
        public X509Certificate2 Root { get; }

        /// <summary>
        /// Intermediate certificate (public part only).
        /// </summary>
        public X509Certificate2 Intermediate { get; }

        /// <summary>
        /// Leaf certificate (public part only).
        /// </summary>
        public X509Certificate2 Leaf { get; }

        /// <summary>
        /// Leaf private key used to sign JWS objects.
        /// </summary>
        public ECDsa LeafKey { get; }

        #endregion

        #region Private-Members

        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Generate the chain.
        /// </summary>
        /// <param name="leafHasMarker">Whether the leaf carries the App Store marker extension.</param>
        public TestAppleChain(bool leafHasMarker = true)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            using (ECDsa rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP384))
            using (ECDsa intermediateKey = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            {
                CertificateRequest rootRequest = new CertificateRequest("CN=Test Apple Root CA, O=Touchstone Tests", rootKey, HashAlgorithmName.SHA384);
                rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
                rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
                rootRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(rootRequest.PublicKey, false));
                using (X509Certificate2 rootWithKey = rootRequest.CreateSelfSigned(now.AddDays(-2), now.AddYears(2)))
                {
                    CertificateRequest intermediateRequest = new CertificateRequest("CN=Test Apple WWDR CA, O=Touchstone Tests", intermediateKey, HashAlgorithmName.SHA384);
                    intermediateRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
                    intermediateRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
                    intermediateRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(intermediateRequest.PublicKey, false));
                    intermediateRequest.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(rootWithKey, true, false));
                    intermediateRequest.CertificateExtensions.Add(new X509Extension(IntermediateOid, new byte[] { 0x05, 0x00 }, false));
                    using (X509Certificate2 intermediatePublic = intermediateRequest.Create(rootWithKey, now.AddDays(-1), now.AddYears(1), new byte[] { 0x01, 0x02 }))
                    using (X509Certificate2 intermediateWithKey = intermediatePublic.CopyWithPrivateKey(intermediateKey))
                    {
                        LeafKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                        CertificateRequest leafRequest = new CertificateRequest("CN=Test App Store Signing, O=Touchstone Tests", LeafKey, HashAlgorithmName.SHA256);
                        leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
                        leafRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
                        leafRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(leafRequest.PublicKey, false));
                        leafRequest.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(intermediateWithKey, true, false));
                        if (leafHasMarker) leafRequest.CertificateExtensions.Add(new X509Extension(LeafOid, new byte[] { 0x05, 0x00 }, false));
                        using (X509Certificate2 leaf = leafRequest.Create(intermediateWithKey, now.AddHours(-12), now.AddMonths(6), new byte[] { 0x03, 0x04 }))
                        {
                            Leaf = X509CertificateLoader.LoadCertificate(leaf.RawData);
                        }
                        Intermediate = X509CertificateLoader.LoadCertificate(intermediatePublic.RawData);
                    }
                    Root = X509CertificateLoader.LoadCertificate(rootWithKey.RawData);
                }
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Sign a payload as an App Store JWS with the x5c header (leaf, intermediate, root).
        /// </summary>
        /// <param name="payload">Payload.</param>
        /// <returns>Compact JWS.</returns>
        public string Sign(JsonObject payload)
        {
            JsonObject header = new JsonObject
            {
                ["x5c"] = new JsonArray(
                    Convert.ToBase64String(Leaf.RawData),
                    Convert.ToBase64String(Intermediate.RawData),
                    Convert.ToBase64String(Root.RawData))
            };
            return TestTokens.SignEs256(LeafKey, header, payload);
        }

        /// <summary>
        /// Dispose the certificates and key.
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
            if (disposing)
            {
                Root.Dispose();
                Intermediate.Dispose();
                Leaf.Dispose();
                LeafKey.Dispose();
            }
            _Disposed = true;
        }

        #endregion
    }
}
