namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.PropertyConfig;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Security;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Live update code signing (docs/LIVE_UPDATES_CONTRACT.md section 6 and client implementation note 3). Keeps the
    /// property's key ring: registry records, their certificates (registered in the console, or read from the
    /// property folder's mobile/updates/certificate.pem and standby-certificate.pem), and private keys (stored
    /// encrypted with secrets.encryptionKey, or from the deployment environment in SingleProperty mode; never from the
    /// repository). Signs with the Active key. When the binary embeds another certificate (its expo-expect-signature
    /// keyid) the signature carries the certificate chain from the signing key up to, not including, the embedded
    /// certificate, so builds accept a rotated key without a new binary (expo-updates certificate-chain mode).
    /// Thread safety: safe to call concurrently; the ring is cached per property and rebuilt after changes.
    /// </summary>
    public class UpdateSigningService
    {
        #region Public-Members

        /// <summary>
        /// The only algorithm expo-updates implements.
        /// </summary>
        public const string Algorithm = "rsa-v1_5-sha256";

        /// <summary>
        /// Default key id of the embedded certificate.
        /// </summary>
        public const string DefaultActiveKeyId = "main";

        /// <summary>
        /// Default key id of the standby certificate.
        /// </summary>
        public const string DefaultStandbyKeyId = "standby";

        /// <summary>
        /// Seconds the key ring is cached. Default 30. Minimum 0, maximum 3600.
        /// </summary>
        public int CacheSeconds
        {
            get { return _CacheSeconds; }
            set { _CacheSeconds = Math.Clamp(value, 0, 3600); }
        }

        #endregion

        #region Private-Members

        private static readonly Regex _KeyId = new Regex("^[A-Za-z0-9._-]{1,64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private const string _CodeSigningOid = "1.3.6.1.5.5.7.3.3";
        private readonly DatabaseDriverBase _Database;
        private readonly AppSettings _Settings;
        private readonly PropertyConfiguration _Property;
        private readonly AppLogger _Logger;
        private readonly SemaphoreSlim _Lock = new SemaphoreSlim(1, 1);
        private List<UpdateSigningMaterial>? _Ring = null;
        private DateTime _LoadedUtc = DateTime.MinValue;
        private DateTime _LastTouchUtc = DateTime.MinValue;
        private bool _MissingLogged = false;
        private int _CacheSeconds = 30;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="database">Property-bound database driver.</param>
        /// <param name="settings">Running settings.</param>
        /// <param name="property">Property (its folder supplies the committed certificates).</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public UpdateSigningService(DatabaseDriverBase database, AppSettings settings, PropertyConfiguration property, AppLogger logger)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Property = property ?? throw new ArgumentNullException(nameof(property));
            _Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The key ring (cached), seeding registry records for the property folder's certificates on first use.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <param name="refresh">Rebuild even when cached.</param>
        /// <returns>Keys in registry order.</returns>
        public async Task<List<UpdateSigningMaterial>> LoadAsync(CancellationToken token = default, bool refresh = false)
        {
            List<UpdateSigningMaterial>? cached = _Ring;
            if (!refresh && cached != null && (DateTime.UtcNow - _LoadedUtc).TotalSeconds < _CacheSeconds) return cached;
            await _Lock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (!refresh && _Ring != null && (DateTime.UtcNow - _LoadedUtc).TotalSeconds < _CacheSeconds) return _Ring;
                List<UpdateSigningKey> records = await _Database.LiveUpdates.ListSigningKeysAsync(token).ConfigureAwait(false);
                if (await SeedFolderCertificatesAsync(records, token).ConfigureAwait(false))
                    records = await _Database.LiveUpdates.ListSigningKeysAsync(token).ConfigureAwait(false);
                List<UpdateSigningMaterial> ring = records.Select(Build).ToList();
                foreach (UpdateSigningMaterial material in ring) material.IssuedByKeyId = IssuerOf(material, ring);
                _Ring = ring;
                _LoadedUtc = DateTime.UtcNow;
                return ring;
            }
            finally
            {
                _Lock.Release();
            }
        }

        /// <summary>
        /// Forget the cached ring (after a registry change).
        /// </summary>
        public void Invalidate()
        {
            _Ring = null;
        }

        /// <summary>
        /// The signing key, or null with the reason.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Active key that can sign, or null.</returns>
        public async Task<UpdateSigningMaterial?> ActiveAsync(CancellationToken token = default)
        {
            List<UpdateSigningMaterial> ring = await LoadAsync(token).ConfigureAwait(false);
            return ring.FirstOrDefault(m => m.Record.Status == UpdateSigningKeyStatusEnum.Active && m.CanSign);
        }

        /// <summary>
        /// Why signing is unavailable, or null when an Active key can sign.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Problem text, or null.</returns>
        public async Task<string?> ProblemAsync(CancellationToken token = default)
        {
            List<UpdateSigningMaterial> ring = await LoadAsync(token).ConfigureAwait(false);
            UpdateSigningMaterial? active = ring.FirstOrDefault(m => m.Record.Status == UpdateSigningKeyStatusEnum.Active);
            if (active == null) return ring.Count == 0 ? "No signing key is registered." : "No signing key is Active.";
            if (!active.CanSign) return "The Active key " + active.Record.KeyId + " cannot sign: " + (active.Problem ?? "no private key is available") + ".";
            return null;
        }

        /// <summary>
        /// Sign a part body for a binary that embeds the certificate named by its expo-expect-signature keyid.
        /// </summary>
        /// <param name="body">Exact UTF-8 bytes of the part body.</param>
        /// <param name="embeddedKeyId">keyid from expo-expect-signature, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Signature, with the certificate chain when the signing key is not the embedded one.</returns>
        /// <exception cref="ApiErrorException">503 SigningUnavailable: no key can produce a signature this binary accepts.</exception>
        public async Task<UpdateSignature> SignAsync(byte[] body, string? embeddedKeyId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(body);
            List<UpdateSigningMaterial> ring = await LoadAsync(token).ConfigureAwait(false);
            UpdateSigningMaterial? active = ring.FirstOrDefault(m => m.Record.Status == UpdateSigningKeyStatusEnum.Active && m.CanSign);
            if (active == null)
            {
                Count("missing_key");
                if (!_MissingLogged)
                {
                    _MissingLogged = true;
                    _Logger.Error("[UpdateSigningService] [p:" + _Property.Id + "] live updates have no usable Active signing key; the manifest endpoint answers 503 SigningUnavailable.");
                }
                throw Unavailable("No active signing key is available for this property.");
            }
            _MissingLogged = false;
            UpdateSigningMaterial signer = active;
            string? chain = null;
            if (!String.IsNullOrEmpty(embeddedKeyId) && !String.Equals(embeddedKeyId, active.Record.KeyId, StringComparison.Ordinal))
            {
                UpdateSigningMaterial? embedded = ring.FirstOrDefault(m => String.Equals(m.Record.KeyId, embeddedKeyId, StringComparison.Ordinal));
                if (embedded == null || embedded.Certificate == null || embedded.Record.Status == UpdateSigningKeyStatusEnum.Retired)
                {
                    Count("unknown_keyid");
                    throw Unavailable("The binary's key id " + embeddedKeyId + " is unknown or retired.");
                }
                chain = ChainPem(active, embedded, ring);
                if (chain == null)
                {
                    if (!embedded.CanSign)
                    {
                        Count("unknown_keyid");
                        throw Unavailable("The Active key " + active.Record.KeyId + " does not chain to the binary's certificate " + embeddedKeyId + ", and that key is not available.");
                    }
                    signer = embedded;
                }
            }
            byte[] signature;
            try
            {
                signature = signer.PrivateKey!.SignData(body, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
            catch (CryptographicException e)
            {
                Count("error");
                _Logger.Error("[UpdateSigningService] [p:" + _Property.Id + "] signing with " + signer.Record.KeyId + " failed", e);
                throw Unavailable("Signing failed.");
            }
            Count("signed");
            await TouchAsync(signer, token).ConfigureAwait(false);
            return new UpdateSignature { Signature = Convert.ToBase64String(signature), KeyId = signer.Record.KeyId, CertificateChainPem = chain };
        }

        /// <summary>
        /// Register a signing key (section 6.3). With a private key the key is stored encrypted (Stored); without one
        /// the deployment supplies it (Environment).
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="memberSession">Whether the caller is a member session (required to import a private key or to
        /// make the key Active).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The record.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 409 Conflict.</exception>
        /// <exception cref="IntegrationNotConfiguredException">A private key was sent but secrets.encryptionKey is not set.</exception>
        public async Task<UpdateSigningKey> RegisterAsync(UpdateSigningKeyRequest request, bool memberSession, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            string keyId = (request.KeyId ?? String.Empty).Trim();
            if (!_KeyId.IsMatch(keyId)) errors["keyId"] = "Must be 1 to 64 letters, digits, dots, dashes, or underscores.";
            X509Certificate2? certificate = ParseCertificate(request.CertificatePem);
            if (certificate == null) errors["certificatePem"] = "Must be an X.509 certificate in PEM form.";
            else if (certificate.GetRSAPublicKey() == null) errors["certificatePem"] = "The certificate must hold an RSA public key.";
            RSA? privateKey = null;
            if (!String.IsNullOrWhiteSpace(request.PrivateKeyPem))
            {
                if (!memberSession) throw ApiErrorException.Forbidden("Importing a private signing key needs a member session.");
                privateKey = ParseKey(request.PrivateKeyPem);
                if (privateKey == null) errors["privateKeyPem"] = "Must be an RSA private key in PEM form.";
                else if (certificate != null && !Matches(certificate, privateKey)) errors["privateKeyPem"] = "The private key does not match the certificate.";
            }
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            List<UpdateSigningKey> existing = await _Database.LiveUpdates.ListSigningKeysAsync(token).ConfigureAwait(false);
            if (existing.Any(k => String.Equals(k.KeyId, keyId, StringComparison.Ordinal))) throw new ApiErrorException(409, "Conflict", "A signing key with key id " + keyId + " exists.");
            UpdateSigningKey record = Describe(certificate!, keyId);
            record.Id = IdGenerator.Generate("usk_");
            if (privateKey != null)
            {
                // A stored signing key must survive a restart, so the process-wide fallback key of SecretBoxProvider is not accepted.
                if (!SecretBox.IsValidKey(_Settings.Secrets.EncryptionKey)) throw new IntegrationNotConfiguredException("Storing secrets needs the secrets.encryptionKey setting (TK_SECRETS_ENCRYPTION_KEY, a base64 32-byte key).");
                SecretCiphertext secret = SecretBoxProvider.For(_Settings.Secrets).Encrypt(privateKey.ExportPkcs8PrivateKeyPem(), Binding(keyId));
                record.Source = UpdateSigningKeySourceEnum.Stored;
                record.PrivateKeyCiphertext = secret.Ciphertext;
                record.PrivateKeyNonce = secret.Nonce;
                record.PrivateKeyKeyId = secret.KeyId;
            }
            bool hasActive = existing.Any(k => k.Status == UpdateSigningKeyStatusEnum.Active);
            UpdateSigningKeyStatusEnum status = request.Status ?? (hasActive ? UpdateSigningKeyStatusEnum.Standby : UpdateSigningKeyStatusEnum.Active);
            if (status == UpdateSigningKeyStatusEnum.Active && hasActive && !memberSession) throw ApiErrorException.Forbidden("Switching the signing key needs a member session.");
            record.Status = status == UpdateSigningKeyStatusEnum.Active && hasActive ? UpdateSigningKeyStatusEnum.Standby : status;
            await _Database.LiveUpdates.CreateSigningKeyAsync(record, token).ConfigureAwait(false);
            if (status == UpdateSigningKeyStatusEnum.Active && hasActive) await _Database.LiveUpdates.SetSigningKeyStatusAsync(record.Id, UpdateSigningKeyStatusEnum.Active, DateTime.UtcNow, token).ConfigureAwait(false);
            Invalidate();
            return (await _Database.LiveUpdates.ReadSigningKeyAsync(record.Id, token).ConfigureAwait(false)) ?? record;
        }

        /// <summary>
        /// Make a key the signing key (rotation). The key must be able to sign.
        /// </summary>
        /// <param name="id">Record id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The record.</returns>
        /// <exception cref="ApiErrorException">404 NotFound, 409 Conflict (retired or no private key).</exception>
        public async Task<UpdateSigningKey> ActivateAsync(string id, CancellationToken token = default)
        {
            List<UpdateSigningMaterial> ring = await LoadAsync(token, true).ConfigureAwait(false);
            UpdateSigningMaterial material = ring.FirstOrDefault(m => m.Record.Id == id) ?? throw ApiErrorException.NotFound("Signing key");
            if (material.Record.Status == UpdateSigningKeyStatusEnum.Retired) throw new ApiErrorException(409, "Conflict", "A retired key cannot sign again.");
            if (!material.CanSign) throw new ApiErrorException(409, "Conflict", "The key cannot sign: " + (material.Problem ?? "no private key is available") + ".");
            await _Database.LiveUpdates.SetSigningKeyStatusAsync(id, UpdateSigningKeyStatusEnum.Active, DateTime.UtcNow, token).ConfigureAwait(false);
            Invalidate();
            return (await _Database.LiveUpdates.ReadSigningKeyAsync(id, token).ConfigureAwait(false))!;
        }

        /// <summary>
        /// Retire a key: it never signs again and manifests requested with its key id get 503 SigningUnavailable.
        /// </summary>
        /// <param name="id">Record id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The record.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<UpdateSigningKey> RetireAsync(string id, CancellationToken token = default)
        {
            UpdateSigningKey record = await _Database.LiveUpdates.ReadSigningKeyAsync(id, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Signing key");
            await _Database.LiveUpdates.SetSigningKeyStatusAsync(record.Id, UpdateSigningKeyStatusEnum.Retired, DateTime.UtcNow, token).ConfigureAwait(false);
            Invalidate();
            return (await _Database.LiveUpdates.ReadSigningKeyAsync(id, token).ConfigureAwait(false))!;
        }

        /// <summary>
        /// Parse a PEM certificate.
        /// </summary>
        /// <param name="pem">PEM text.</param>
        /// <returns>Certificate, or null.</returns>
        public static X509Certificate2? ParseCertificate(string? pem)
        {
            if (String.IsNullOrWhiteSpace(pem)) return null;
            try
            {
                return X509Certificate2.CreateFromPem(pem);
            }
            catch (CryptographicException)
            {
                return null;
            }
        }

        /// <summary>
        /// Parse a PEM RSA private key (PKCS#8 or PKCS#1).
        /// </summary>
        /// <param name="pem">PEM text.</param>
        /// <returns>Key, or null.</returns>
        public static RSA? ParseKey(string? pem)
        {
            if (String.IsNullOrWhiteSpace(pem)) return null;
            RSA rsa = RSA.Create();
            try
            {
                rsa.ImportFromPem(pem);
                rsa.ExportParameters(true);
                return rsa;
            }
            catch (Exception e) when (e is CryptographicException || e is ArgumentException)
            {
                rsa.Dispose();
                return null;
            }
        }

        /// <summary>
        /// Whether a private key belongs to a certificate.
        /// </summary>
        /// <param name="certificate">Certificate.</param>
        /// <param name="key">Private key.</param>
        /// <returns>True when the moduli and exponents match.</returns>
        public static bool Matches(X509Certificate2 certificate, RSA key)
        {
            ArgumentNullException.ThrowIfNull(certificate);
            ArgumentNullException.ThrowIfNull(key);
            using (RSA? publicKey = certificate.GetRSAPublicKey())
            {
                if (publicKey == null) return false;
                RSAParameters a = publicKey.ExportParameters(false);
                RSAParameters b = key.ExportParameters(false);
                return a.Modulus != null && b.Modulus != null && a.Modulus.AsSpan().SequenceEqual(b.Modulus) && a.Exponent != null && b.Exponent != null && a.Exponent.AsSpan().SequenceEqual(b.Exponent);
            }
        }

        /// <summary>
        /// Whether a certificate carries the codeSigning extended key usage (or no extended key usage at all).
        /// </summary>
        /// <param name="certificate">Certificate.</param>
        /// <returns>True when usable for code signing.</returns>
        public static bool AllowsCodeSigning(X509Certificate2 certificate)
        {
            ArgumentNullException.ThrowIfNull(certificate);
            X509EnhancedKeyUsageExtension? eku = certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().FirstOrDefault();
            if (eku == null) return true;
            foreach (Oid oid in eku.EnhancedKeyUsages)
                if (oid.Value == _CodeSigningOid) return true;
            return false;
        }

        /// <summary>
        /// Whether a certificate is a CA (basic constraints).
        /// </summary>
        /// <param name="certificate">Certificate.</param>
        /// <returns>True for a CA certificate.</returns>
        public static bool IsAuthority(X509Certificate2 certificate)
        {
            ArgumentNullException.ThrowIfNull(certificate);
            X509BasicConstraintsExtension? basic = certificate.Extensions.OfType<X509BasicConstraintsExtension>().FirstOrDefault();
            return basic != null && basic.CertificateAuthority;
        }

        #endregion

        #region Private-Methods

        private async Task<bool> SeedFolderCertificatesAsync(List<UpdateSigningKey> records, CancellationToken token)
        {
            if (String.IsNullOrEmpty(_Property.Directory)) return false;
            string folder = Path.Combine(_Property.Directory, "mobile", "updates");
            if (!Directory.Exists(folder)) return false;
            string activeId = DefaultActiveKeyId;
            string standbyId = DefaultStandbyKeyId;
            string signingJson = Path.Combine(folder, "signing.json");
            if (File.Exists(signingJson))
            {
                try
                {
                    using (JsonDocument doc = JsonDocument.Parse(await File.ReadAllTextAsync(signingJson, token).ConfigureAwait(false)))
                    {
                        if (doc.RootElement.TryGetProperty("activeKeyId", out JsonElement a) && a.ValueKind == JsonValueKind.String && _KeyId.IsMatch(a.GetString() ?? String.Empty)) activeId = a.GetString()!;
                        if (doc.RootElement.TryGetProperty("standbyKeyId", out JsonElement s) && s.ValueKind == JsonValueKind.String && _KeyId.IsMatch(s.GetString() ?? String.Empty)) standbyId = s.GetString()!;
                    }
                }
                catch (JsonException)
                {
                    _Logger.Warn("[UpdateSigningService] [p:" + _Property.Id + "] ignoring invalid " + signingJson);
                }
            }
            bool changed = false;
            bool hasActive = records.Any(r => r.Status == UpdateSigningKeyStatusEnum.Active);
            foreach (KeyValuePair<string, string> file in new[] { new KeyValuePair<string, string>(activeId, "certificate.pem"), new KeyValuePair<string, string>(standbyId, "standby-certificate.pem") })
            {
                if (records.Any(r => String.Equals(r.KeyId, file.Key, StringComparison.Ordinal))) continue;
                string path = Path.Combine(folder, file.Value);
                if (!File.Exists(path)) continue;
                X509Certificate2? certificate = ParseCertificate(await File.ReadAllTextAsync(path, token).ConfigureAwait(false));
                if (certificate == null) continue;
                UpdateSigningKey record = Describe(certificate, file.Key);
                record.Id = IdGenerator.Generate("usk_");
                record.Source = UpdateSigningKeySourceEnum.Environment;
                record.Status = file.Key == activeId && !hasActive ? UpdateSigningKeyStatusEnum.Active : UpdateSigningKeyStatusEnum.Standby;
                if (record.Status == UpdateSigningKeyStatusEnum.Active) hasActive = true;
                try
                {
                    await _Database.LiveUpdates.CreateSigningKeyAsync(record, token).ConfigureAwait(false);
                    changed = true;
                }
                catch (DuplicateRecordException)
                {
                    changed = true;
                }
            }
            return changed;
        }

        private UpdateSigningMaterial Build(UpdateSigningKey record)
        {
            UpdateSigningMaterial material = new UpdateSigningMaterial { Record = record, Certificate = ParseCertificate(record.CertificatePem) };
            if (material.Certificate == null)
            {
                material.Problem = "the certificate is not valid PEM";
                return material;
            }
            string? pem = null;
            if (record.Source == UpdateSigningKeySourceEnum.Stored)
            {
                pem = SecretBoxProvider.For(_Settings.Secrets).Decrypt(record.PrivateKeyCiphertext, record.PrivateKeyNonce, record.PrivateKeyKeyId, Binding(record.KeyId));
                if (pem == null)
                {
                    material.Problem = "the stored private key cannot be decrypted with secrets.encryptionKey";
                    return material;
                }
            }
            else pem = EnvironmentKey(record.KeyId);
            if (pem == null) return material;
            RSA? key = ParseKey(pem);
            if (key == null)
            {
                material.Problem = "the private key is not a valid RSA PEM";
                return material;
            }
            if (!Matches(material.Certificate, key))
            {
                key.Dispose();
                material.Problem = "the private key does not match the certificate";
                return material;
            }
            material.PrivateKey = key;
            DateTime now = DateTime.UtcNow;
            if (material.Certificate.NotAfter.ToUniversalTime() < now || material.Certificate.NotBefore.ToUniversalTime() > now) material.Problem = "the certificate is not currently valid";
            else if (!AllowsCodeSigning(material.Certificate)) material.Problem = "the certificate lacks the codeSigning extended key usage";
            return material;
        }

        private string? EnvironmentKey(string keyId)
        {
            if (_Settings.Deployment.Mode == DeploymentModeEnum.MultiProperty) return null;
            UpdatesSettings u = _Settings.Updates;
            if (String.Equals(keyId, u.SigningKeyId, StringComparison.Ordinal)) return ReadKey(u.SigningKeyPem, u.SigningKeyFile);
            if (String.Equals(keyId, u.StandbySigningKeyId, StringComparison.Ordinal)) return ReadKey(u.StandbySigningKeyPem, u.StandbySigningKeyFile);
            return null;
        }

        private string? ReadKey(string pem, string file)
        {
            if (!String.IsNullOrWhiteSpace(pem)) return pem.Replace("\\n", "\n");
            if (String.IsNullOrWhiteSpace(file)) return null;
            try
            {
                return File.Exists(file) ? File.ReadAllText(file) : null;
            }
            catch (IOException e)
            {
                _Logger.Warn("[UpdateSigningService] cannot read the signing key file: " + e.Message);
                return null;
            }
            catch (UnauthorizedAccessException e)
            {
                _Logger.Warn("[UpdateSigningService] cannot read the signing key file: " + e.Message);
                return null;
            }
        }

        private static UpdateSigningKey Describe(X509Certificate2 certificate, string keyId)
        {
            int bits;
            using (RSA? rsa = certificate.GetRSAPublicKey()) bits = rsa?.KeySize ?? 0;
            return new UpdateSigningKey
            {
                KeyId = keyId,
                Algorithm = Algorithm,
                KeyBits = bits,
                CertificatePem = certificate.ExportCertificatePem(),
                CertificateSha256 = Convert.ToHexString(SHA256.HashData(certificate.RawData)).ToLowerInvariant(),
                CertificateNotAfterUtc = certificate.NotAfter.ToUniversalTime(),
                IsCertificateAuthority = IsAuthority(certificate),
                CreatedUtc = DateTime.UtcNow
            };
        }

        private static string? IssuerOf(UpdateSigningMaterial material, List<UpdateSigningMaterial> ring)
        {
            if (material.Certificate == null) return null;
            if (material.Certificate.SubjectName.RawData.AsSpan().SequenceEqual(material.Certificate.IssuerName.RawData)) return null;
            foreach (UpdateSigningMaterial candidate in ring)
            {
                if (candidate == material || candidate.Certificate == null) continue;
                if (IssuedBy(material.Certificate, candidate.Certificate)) return candidate.Record.KeyId;
            }
            return null;
        }

        private static bool IssuedBy(X509Certificate2 child, X509Certificate2 issuer)
        {
            if (!child.IssuerName.RawData.AsSpan().SequenceEqual(issuer.SubjectName.RawData)) return false;
            using (X509Chain chain = new X509Chain())
            {
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.Add(issuer);
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                chain.ChainPolicy.VerificationFlags = X509VerificationFlags.IgnoreNotTimeValid;
                return chain.Build(child) && chain.ChainElements.Count == 2;
            }
        }

        private static string? ChainPem(UpdateSigningMaterial leaf, UpdateSigningMaterial embedded, List<UpdateSigningMaterial> ring)
        {
            if (leaf.Certificate == null || embedded.Certificate == null || !IsAuthority(embedded.Certificate)) return null;
            using (X509Chain chain = new X509Chain())
            {
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.Add(embedded.Certificate);
                foreach (UpdateSigningMaterial other in ring)
                {
                    if (other.Certificate != null && other != leaf && other != embedded && other.Record.Status != UpdateSigningKeyStatusEnum.Retired && IsAuthority(other.Certificate))
                        chain.ChainPolicy.ExtraStore.Add(other.Certificate);
                }
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                if (!chain.Build(leaf.Certificate)) return null;
                int count = chain.ChainElements.Count;
                if (count < 2 || !chain.ChainElements[count - 1].Certificate.RawData.AsSpan().SequenceEqual(embedded.Certificate.RawData)) return null;
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < count - 1; i++) builder.Append(chain.ChainElements[i].Certificate.ExportCertificatePem()).Append('\n');
                return builder.ToString();
            }
        }

        private async Task TouchAsync(UpdateSigningMaterial signer, CancellationToken token)
        {
            DateTime now = DateTime.UtcNow;
            if ((now - _LastTouchUtc).TotalSeconds < 60) return;
            _LastTouchUtc = now;
            try
            {
                await _Database.LiveUpdates.TouchSigningKeyAsync(signer.Record.Id, now, token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _Logger.Debug("[UpdateSigningService] could not record key use: " + e.Message);
            }
        }

        private string Binding(string keyId)
        {
            return _Property.Id + "|updates.signingKey|" + keyId;
        }

        private void Count(string outcome)
        {
            TrellisKitTelemetry.Add(LiveUpdatesTelemetry.Signing, 1, "outcome", outcome, "trelliskit.property", _Property.Id);
        }

        private static ApiErrorException Unavailable(string message)
        {
            return new ApiErrorException(503, "SigningUnavailable", message);
        }

        #endregion
    }
}
