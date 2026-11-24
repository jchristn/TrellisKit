namespace Test.Shared.Suites
{
    using System;
    using System.Security.Cryptography;
    using System.Security.Cryptography.X509Certificates;

    /// <summary>
    /// A throwaway live update signing chain generated at test time, shaped like the sample property's committed one
    /// (mobile/scripts/updatesCertificates.js): an active self-signed CA certificate (key id main) with the codeSigning
    /// extended key usage, and a standby certificate issued by it (key id standby). Never the real keys.
    /// </summary>
    internal sealed class TestSigningChain
    {
        internal string MainKeyPem { get; }

        internal string MainCertificatePem { get; }

        internal string StandbyKeyPem { get; }

        internal string StandbyCertificatePem { get; }

        internal TestSigningChain()
        {
            using (RSA main = RSA.Create(2048))
            using (RSA standby = RSA.Create(2048))
            {
                DateTimeOffset notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
                DateTimeOffset notAfter = notBefore.AddYears(10);
                CertificateRequest rootRequest = new CertificateRequest("CN=Test updates", main, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                AddExtensions(rootRequest);
                using (X509Certificate2 root = rootRequest.CreateSelfSigned(notBefore, notAfter))
                {
                    CertificateRequest standbyRequest = new CertificateRequest("CN=Test updates standby", standby, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                    AddExtensions(standbyRequest);
                    standbyRequest.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(root, true, false));
                    using (X509Certificate2 issued = standbyRequest.Create(root, notBefore, notAfter.AddDays(-1), new byte[] { 0x01, 0x23, 0x45, 0x67, 0x09 }))
                    {
                        MainKeyPem = main.ExportPkcs8PrivateKeyPem();
                        MainCertificatePem = root.ExportCertificatePem();
                        StandbyKeyPem = standby.ExportPkcs8PrivateKeyPem();
                        StandbyCertificatePem = issued.ExportCertificatePem();
                    }
                }
            }
        }

        private static void AddExtensions(CertificateRequest request)
        {
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyCertSign, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.3") }, true));
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        }
    }
}
