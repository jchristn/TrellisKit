namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Security;
    using TrellisKit.Core.Settings;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Settings;

    /// <summary>
    /// The engine secrets key: the startup check (a missing or invalid key stops startup unless local development allows
    /// a generated key), the settings store only generating a key when allowed, and the re-encryption pass after a
    /// rotation (bounded, property-bound, idempotent, audited, with the remaining-values count).
    /// </summary>
    public static class SecretKeySuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "SecretKey";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Secrets key (" + TestEnvironment.Provider + ")", new[]
            {
                SuiteCases.Unit(Id, "StartupCheck.FailsFast", "A missing key stops startup unless secrets.allowGeneratedKey; an invalid current or previous key always does", ct =>
                {
                    Check.True(SecretKeyCheck.Problems(new SecretSettings()).Count == 1, "Missing key without the development flag");
                    Check.True(SecretKeyCheck.Problems(new SecretSettings())[0].Contains("TK_SECRETS_ENCRYPTION_KEY", StringComparison.Ordinal), "The message names the variable");
                    Check.True(SecretKeyCheck.Problems(new SecretSettings { AllowGeneratedKey = true }).Count == 0, "Local development may generate one");
                    Check.True(SecretKeyCheck.Problems(new SecretSettings { EncryptionKey = "not-a-key", AllowGeneratedKey = true }).Count == 1, "An invalid key is never replaced");
                    Check.True(SecretKeyCheck.Problems(new SecretSettings { EncryptionKey = SecretBox.GenerateKey() }).Count == 0, "A valid key starts");
                    Check.True(SecretKeyCheck.Problems(new SecretSettings { EncryptionKey = SecretBox.GenerateKey(), PreviousEncryptionKeys = SecretBox.GenerateKey() + ",bad" }).Count == 1, "An invalid previous key stops startup");
                    return Task.CompletedTask;
                }),
                SuiteCases.Unit(Id, "Store.GeneratesOnlyWhenAllowed", "The settings store generates and saves a secrets key only with TK_SECRETS_ALLOW_GENERATED_KEY=true; a configured key is kept", ct =>
                {
                    string strict = NewPath();
                    AppSettings none = new SettingsStore(strict, name => null).LoadEffective(out string? _);
                    Check.Equal(String.Empty, none.Secrets.EncryptionKey, "No key generated outside local development");
                    Check.True(SecretKeyCheck.Problems(none.Secrets).Count == 1, "So startup fails");
                    string dev = NewPath();
                    AppSettings generated = new SettingsStore(dev, name => name == "TK_SECRETS_ALLOW_GENERATED_KEY" ? "true" : null).LoadEffective(out string? _);
                    Check.True(SecretBox.IsValidKey(generated.Secrets.EncryptionKey) && generated.Secrets.AllowGeneratedKey, "Generated in local development");
                    AppSettings again = new SettingsStore(dev, name => name == "TK_SECRETS_ALLOW_GENERATED_KEY" ? "true" : null).LoadEffective(out string? _);
                    Check.Equal(generated.Secrets.EncryptionKey, again.Secrets.EncryptionKey, "Saved and reused");
                    string key = SecretBox.GenerateKey();
                    AppSettings supplied = new SettingsStore(NewPath(), name => name == "TK_SECRETS_ENCRYPTION_KEY" ? key : null).LoadEffective(out string? _);
                    Check.Equal(key, supplied.Secrets.EncryptionKey, "The environment key is used");
                    foreach (string path in new[] { strict, dev }) File.Delete(path);
                    return Task.CompletedTask;
                }),
                SuiteCases.Http(Id, "Rotation.ReencryptsInBatches", "After a rotation, values encrypted with the previous key are rewritten with the current key in bounded batches, idempotently, per property, audited, with the remaining count", async (f, ct) =>
                {
                    string oldKey = SecretBox.GenerateKey();
                    string newKey = SecretBox.GenerateKey();
                    f.Settings.Secrets.EncryptionKey = oldKey;
                    f.Settings.Secrets.PreviousEncryptionKeys = String.Empty;
                    string pid = f.Property.Id;
                    SecretBox before = SecretBoxProvider.For(f.Settings.Secrets);
                    SecretCiphertext setting = before.Encrypt("mailgun-secret", pid + "|email.mailgunApiKey");
                    await f.Database.PropertySettings.WriteAsync(new List<PropertySettingRow>
                    {
                        new PropertySettingRow { Key = "email.mailgunApiKey", Ciphertext = setting.Ciphertext, Nonce = setting.Nonce, KeyId = setting.KeyId, UpdatedUtc = DateTime.UtcNow }
                    }, new List<string>(), ct).ConfigureAwait(false);
                    SecretCiphertext payload = before.Encrypt("{\"to\":\"a@example.com\"}", pid + "|email|ems_rotation");
                    await f.Raw.ExecuteQueryAsync("INSERT INTO emailmessages (id, propertyid, template, category, recipient, recipientnormalized, subject, status, payloadciphertext, payloadnonce, payloadkeyid, createdutc, updatedutc) VALUES ('ems_rotation', '" + pid +
                        "', 'Welcome', 'Account', 'a@example.com', 'a@example.com', 'Hi', 'Queued', '" + payload.Ciphertext + "', '" + payload.Nonce + "', '" + payload.KeyId + "', now(), now());", false, ct).ConfigureAwait(false);

                    Check.Equal(0, await f.Services.SecretRotation.RunPassAsync(ct).ConfigureAwait(false), "Nothing to do without a previous key");
                    f.Settings.Secrets.EncryptionKey = newKey;
                    f.Settings.Secrets.PreviousEncryptionKeys = oldKey;
                    f.Settings.Secrets.ReencryptBatchSize = 1;
                    Check.Equal(2L, await f.Services.SecretRotation.RemainingAsync(ct).ConfigureAwait(false), "Two values use the previous key");
                    Check.Equal(1, await f.Services.SecretRotation.RunPassAsync(ct).ConfigureAwait(false), "A pass is bounded by the batch size");
                    Check.Equal(1L, await f.Services.SecretRotation.RemainingAsync(ct).ConfigureAwait(false), "One remaining");
                    Check.Equal(1, await f.Services.SecretRotation.RunPassAsync(ct).ConfigureAwait(false), "The next pass continues");
                    Check.Equal(0L, await f.Services.SecretRotation.RemainingAsync(ct).ConfigureAwait(false), "None remaining");
                    Check.Equal(0, await f.Services.SecretRotation.RunPassAsync(ct).ConfigureAwait(false), "Idempotent once done");

                    SecretBox current = new SecretBox(newKey);
                    PropertySettingRow row = (await f.Database.PropertySettings.ReadAllAsync(ct).ConfigureAwait(false)).Find(r => r.Key == "email.mailgunApiKey")!;
                    Check.Equal(current.KeyId, row.KeyId, "Setting uses the current key");
                    Check.Equal("mailgun-secret", current.Decrypt(row.Ciphertext, row.Nonce, row.KeyId, pid + "|email.mailgunApiKey"), "Setting decrypts with the current key alone");
                    Check.Equal("mailgun-secret", await f.Services.PropertySettings.GetAsync("email.mailgunApiKey", ct).ConfigureAwait(false), "The settings service reads it");
                    System.Data.DataTable email = await f.Raw.ExecuteQueryAsync("SELECT payloadciphertext, payloadnonce, payloadkeyid FROM emailmessages WHERE id = 'ems_rotation';", false, ct).ConfigureAwait(false);
                    Check.Equal("{\"to\":\"a@example.com\"}", current.Decrypt(TrellisKit.Core.Database.RowReader.GetString(email.Rows[0], "payloadciphertext"), TrellisKit.Core.Database.RowReader.GetString(email.Rows[0], "payloadnonce"),
                        TrellisKit.Core.Database.RowReader.GetString(email.Rows[0], "payloadkeyid"), pid + "|email|ems_rotation"), "Email payload re-encrypted");
                    Check.Equal(2L, await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM auditrecords WHERE action = 'secrets.reencrypt' AND propertyid = '" + pid + "';", ct).ConfigureAwait(false), "Each pass that rewrote is audited");
                }),
                SuiteCases.Http(Id, "Rotation.ReencryptsUpdateSigningKeys", "A stored live update signing key encrypted with the previous key is rewritten with the current key and still decrypts with its binding", async (f, ct) =>
                {
                    string oldKey = SecretBox.GenerateKey();
                    string newKey = SecretBox.GenerateKey();
                    string pid = f.Property.Id;
                    string keyId = "rotation-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                    string binding = pid + "|updates.signingKey|" + keyId;
                    SecretCiphertext stored = new SecretBox(oldKey).Encrypt("-----BEGIN PRIVATE KEY-----test-----END PRIVATE KEY-----", binding);
                    await f.Raw.ExecuteQueryAsync("INSERT INTO updatesigningkeys (propertyid, id, keyid, algorithm, keybits, source, certificatepem, certificatesha256, privatekeyciphertext, privatekeynonce, privatekeykeyid, status, createdutc) VALUES ('" + pid +
                        "', 'usk_rotation" + keyId.Substring(9) + "', '" + keyId + "', 'rsa-v1_5-sha256', 2048, 'Stored', 'pem', 'sha', '" + stored.Ciphertext + "', '" + stored.Nonce + "', '" + stored.KeyId + "', 'Retired', now());", false, ct).ConfigureAwait(false);
                    f.Settings.Secrets.EncryptionKey = newKey;
                    f.Settings.Secrets.PreviousEncryptionKeys = oldKey;
                    Check.Equal(1L, await f.Services.SecretRotation.RemainingAsync(ct).ConfigureAwait(false), "The signing key uses the previous key");
                    Check.Equal(1, await f.Services.SecretRotation.RunPassAsync(ct).ConfigureAwait(false), "Rewritten");
                    Check.Equal(0L, await f.Services.SecretRotation.RemainingAsync(ct).ConfigureAwait(false), "None remaining");
                    System.Data.DataTable row = await f.Raw.ExecuteQueryAsync("SELECT privatekeyciphertext, privatekeynonce, privatekeykeyid FROM updatesigningkeys WHERE propertyid = '" + pid + "' AND keyid = '" + keyId + "';", false, ct).ConfigureAwait(false);
                    SecretBox current = new SecretBox(newKey);
                    string recordKeyId = TrellisKit.Core.Database.RowReader.GetString(row.Rows[0], "privatekeykeyid");
                    Check.Equal(current.KeyId, recordKeyId, "Uses the current key");
                    Check.Equal("-----BEGIN PRIVATE KEY-----test-----END PRIVATE KEY-----", current.Decrypt(TrellisKit.Core.Database.RowReader.GetString(row.Rows[0], "privatekeyciphertext"),
                        TrellisKit.Core.Database.RowReader.GetString(row.Rows[0], "privatekeynonce"), recordKeyId, binding), "Decrypts with the current key alone and the same binding");
                })
            });
        }

        #endregion

        #region Private-Methods

        private static string NewPath()
        {
            Directory.CreateDirectory(TestEnvironment.TempRoot);
            return Path.Combine(TestEnvironment.TempRoot, "secret-settings-" + Guid.NewGuid().ToString("N") + ".json");
        }

        #endregion
    }
}
