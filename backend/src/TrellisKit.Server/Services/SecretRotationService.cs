namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Security;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Re-encryption after a secrets key rotation, for one property: values encrypted with a key listed in
    /// secrets.previousEncryptionKeys (TK_SECRETS_PREVIOUS_ENCRYPTION_KEYS) are decrypted and rewritten with the current
    /// key (TK_SECRETS_ENCRYPTION_KEY) in bounded keyset batches of secrets.reencryptBatchSize. Each rewrite is
    /// conditional on the row still holding the ciphertext that was read, so passes are idempotent and safe on several
    /// instances. A pass that rewrote values writes one audit record (secrets.reencrypt); every pass sets the gauge
    /// trelliskit.secrets.reencrypt_remaining. Values that do not decrypt are left as they are, logged, and counted in
    /// trelliskit.secrets.decrypt_failures. Once the gauge is 0 on every property the previous key can be removed.
    /// Thread safety: one pass at a time per instance (the keyset position is per instance).
    /// </summary>
    public class SecretRotationService
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly AppSettings _Settings;
        private readonly string _PropertyId;
        private readonly AuditService _Audit;
        private readonly AppLogger _Logger;
        private readonly SemaphoreSlim _Lock = new SemaphoreSlim(1, 1);
        private string? _AfterStore = null;
        private string? _AfterRowKey = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="database">Database driver bound to the property.</param>
        /// <param name="settings">Running settings.</param>
        /// <param name="propertyId">Property id (associated data of the ciphertexts).</param>
        /// <param name="audit">Audit service.</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public SecretRotationService(DatabaseDriverBase database, AppSettings settings, string propertyId, AuditService audit, AppLogger logger)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _PropertyId = propertyId ?? throw new ArgumentNullException(nameof(propertyId));
            _Audit = audit ?? throw new ArgumentNullException(nameof(audit));
            _Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Values of this property still encrypted with a previous key.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        public Task<long> RemainingAsync(CancellationToken token = default)
        {
            SecretBox box = SecretBoxProvider.For(_Settings.Secrets);
            return _Database.SecretRotation.CountAsync(box.PreviousKeyIds, token);
        }

        /// <summary>
        /// Run one bounded pass: read up to secrets.reencryptBatchSize values after the last position, rewrite each
        /// with the current key, audit, and update the gauge. The position wraps to the start when a page is empty.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Values rewritten.</returns>
        public async Task<int> RunPassAsync(CancellationToken token = default)
        {
            await _Lock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                SecretBox box = SecretBoxProvider.For(_Settings.Secrets);
                IReadOnlyList<string> previous = box.PreviousKeyIds;
                if (previous.Count == 0)
                {
                    TrellisKitTelemetry.SetSecretsReencryptRemaining(0);
                    return 0;
                }
                List<StoredSecret> batch = await _Database.SecretRotation.ReadBatchAsync(previous, _AfterStore, _AfterRowKey, _Settings.Secrets.ReencryptBatchSize, token).ConfigureAwait(false);
                if (batch.Count == 0 && _AfterStore != null)
                {
                    _AfterStore = null;
                    _AfterRowKey = null;
                    batch = await _Database.SecretRotation.ReadBatchAsync(previous, null, null, _Settings.Secrets.ReencryptBatchSize, token).ConfigureAwait(false);
                }
                int rewritten = 0;
                int failed = 0;
                foreach (StoredSecret value in batch)
                {
                    token.ThrowIfCancellationRequested();
                    _AfterStore = value.Store.ToString();
                    _AfterRowKey = value.RowKey;
                    string associated = AssociatedData(value);
                    string? plain = box.Decrypt(value.Ciphertext, value.Nonce, value.KeyId, associated);
                    if (plain == null)
                    {
                        failed++;
                        TrellisKitTelemetry.Increment(TrellisKitTelemetry.SecretDecryptFailures, "key", FailureKey(value));
                        _Logger.Warn("[SecretRotationService] [p:" + _PropertyId + "] " + value.Store + " " + value.RowKey + " does not decrypt with key " + value.KeyId + "; left unchanged");
                        continue;
                    }
                    if (await _Database.SecretRotation.RewriteAsync(value, box.Encrypt(plain, associated), token).ConfigureAwait(false)) rewritten++;
                }
                long remaining = await _Database.SecretRotation.CountAsync(previous, token).ConfigureAwait(false);
                TrellisKitTelemetry.SetSecretsReencryptRemaining(remaining);
                if (rewritten > 0 || failed > 0)
                {
                    string summary = "re-encrypted " + rewritten + " values to key " + box.KeyId + "; " + failed + " did not decrypt; " + remaining + " remaining";
                    await _Audit.WriteAsync((RequestContext?)null, null, "secrets.reencrypt", "Property", _PropertyId, summary, token).ConfigureAwait(false);
                    _Logger.Info("[SecretRotationService] [p:" + _PropertyId + "] " + summary);
                }
                return rewritten;
            }
            finally
            {
                _Lock.Release();
            }
        }

        #endregion

        #region Private-Methods

        private static string FailureKey(StoredSecret value)
        {
            switch (value.Store)
            {
                case SecretStoreEnum.PropertySetting:
                    return value.RowKey;
                case SecretStoreEnum.UpdateSigningKey:
                    return "updates.signingKey";
                case SecretStoreEnum.AiEndpointAuthValue:
                case SecretStoreEnum.AiEndpointHeader:
                    return "ai.endpointSecret";
                case SecretStoreEnum.AiHarnessCredential:
                    return "ai.harnessCredential";
                default:
                    return "email.payload";
            }
        }

        private string AssociatedData(StoredSecret value)
        {
            // Must match PropertySettingsService, EmailService, UpdateSigningService, and the AI endpoint services exactly.
            switch (value.Store)
            {
                case SecretStoreEnum.PropertySetting:
                    return _PropertyId + "|" + value.RowKey;
                case SecretStoreEnum.UpdateSigningKey:
                    return _PropertyId + "|updates.signingKey|" + value.RowKey;
                case SecretStoreEnum.AiEndpointAuthValue:
                    return SecretBoxProtector.EndpointAssociatedData(_PropertyId, value.RowKey, "authValue");
                case SecretStoreEnum.AiEndpointHeader:
                    int bar = value.RowKey.IndexOf('|');
                    return SecretBoxProtector.EndpointAssociatedData(_PropertyId, value.RowKey.Substring(0, bar), "extraHeader:" + value.RowKey.Substring(bar + 1));
                case SecretStoreEnum.AiHarnessCredential:
                    return SecretBoxProtector.EndpointAssociatedData(_PropertyId, "harness:" + value.RowKey, "credential");
                default:
                    return _PropertyId + "|email|" + value.RowKey;
            }
        }

        #endregion
    }
}
