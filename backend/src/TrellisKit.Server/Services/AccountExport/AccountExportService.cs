namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Integrations;
    using TrellisKit.Core.Integrations.Implementations;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Asynchronous account data export: members request an export, the export worker streams every registered
    /// <see cref="IAccountExportSection"/> to a local work file and stores it through the media object store (the
    /// Retained location: the private retained bucket in S3 mode, storage.directory/retained locally) under the
    /// property-prefixed key {propertyId}/exports/{id}.json (MediaKeys.AccountExport), so any server instance can serve
    /// the download and replacing an instance loses nothing. The member downloads it through a short-lived HMAC-signed
    /// URL (account.exportDownloadLinkMinutes); the worker deletes the stored object after account.exportRetentionHours.
    /// Thread safety: safe to call concurrently; section registration is locked.
    /// </summary>
    public class AccountExportService
    {

        #region Public-Members

        /// <summary>
        /// Administrative email (export ready notice). Set by the composition root; null sends none.
        /// </summary>
        public EmailService? Email { get; set; } = null;

        /// <summary>
        /// Wakes the export worker when an export is requested, so generation starts at once instead of on the next
        /// timed pass (the worker still runs on its interval for expiry and retries).
        /// </summary>
        public MediaWorkSignal Signal { get; } = new MediaWorkSignal();

        /// <summary>
        /// Folder under storage.directory for local work files (an export being written, or a download being served).
        /// Stored exports live in the object store, never only here.
        /// </summary>
        public const string ExportsFolder = "exports";

        /// <summary>
        /// Object store location of stored export files (private; never served directly).
        /// </summary>
        public const MediaLocationEnum StorageLocation = MediaLocationEnum.Retained;

        /// <summary>
        /// Exports a member may request per rolling day: the live setting account.exportsPerDay (default 3, minimum 1,
        /// maximum 100).
        /// </summary>
        public int ExportsPerDay
        {
            get { return _Settings.Account.ExportsPerDay; }
        }

        /// <summary>
        /// Minutes a signed download URL stays valid: the live setting account.exportDownloadLinkMinutes (default 15,
        /// minimum 1, maximum 1440).
        /// </summary>
        public int DownloadUrlMinutes
        {
            get { return _Settings.Account.ExportDownloadLinkMinutes; }
        }

        /// <summary>
        /// Minutes a worker claim on an export lasts before another worker may retry it. Default 15, minimum 1, maximum 1440.
        /// </summary>
        public int ClaimMinutes
        {
            get { return _ClaimMinutes; }
            set { _ClaimMinutes = Math.Clamp(value, 1, 1440); }
        }

        /// <summary>
        /// Registered sections in file order.
        /// </summary>
        public IReadOnlyList<IAccountExportSection> Sections
        {
            get
            {
                lock (_Lock) return _Sections.ToList();
            }
        }

        #endregion

        #region Private-Members

        private const string _KeyPurpose = "trelliskit/account-export-download/v1";
        private readonly DatabaseDriverBase _Database;
        private readonly IntegrationSet _Integrations;
        private readonly string _PropertyId;
        private readonly AppSettings _Settings;
        private readonly SlidingWindowRateLimiter _Limiter;
        private readonly AppLogger _Logger;
        private readonly List<IAccountExportSection> _Sections = new List<IAccountExportSection>();
        private readonly object _Lock = new object();
        private int _ClaimMinutes = 15;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service (no sections; register them with <see cref="Register"/>).
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="integrations">Integrations (the object store is read on every use).</param>
        /// <param name="propertyId">Property id (first segment of every export key).</param>
        /// <param name="settings">Running settings.</param>
        /// <param name="limiter">Rate limiter.</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public AccountExportService(DatabaseDriverBase database, IntegrationSet integrations, string propertyId, AppSettings settings, SlidingWindowRateLimiter limiter, AppLogger logger)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Integrations = integrations ?? throw new ArgumentNullException(nameof(integrations));
            if (String.IsNullOrEmpty(propertyId)) throw new ArgumentNullException(nameof(propertyId));
            _PropertyId = propertyId;
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Limiter = limiter ?? throw new ArgumentNullException(nameof(limiter));
            _Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register a section (extension point for later slices). Sections are written in registration order.
        /// </summary>
        /// <param name="section">Section.</param>
        /// <exception cref="ArgumentNullException">The section is null.</exception>
        /// <exception cref="ArgumentException">A section with the same name is registered.</exception>
        public void Register(IAccountExportSection section)
        {
            ArgumentNullException.ThrowIfNull(section);
            if (String.IsNullOrWhiteSpace(section.Name)) throw new ArgumentException("A section needs a name.", nameof(section));
            lock (_Lock)
            {
                if (_Sections.Any(s => String.Equals(s.Name, section.Name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("A section named " + section.Name + " is already registered.", nameof(section));
                _Sections.Add(section);
            }
        }

        /// <summary>
        /// POST /api/v1/me/export: queue an export.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The queued export.</returns>
        /// <exception cref="ArgumentNullException">The member identifier is null or empty.</exception>
        /// <exception cref="ApiErrorException">409 ExportInProgress, 429 RateLimited.</exception>
        public async Task<AccountExportCreatedResponse> RequestAsync(string userId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            using (Activity? activity = TrellisKitTelemetry.StartSpan("account_export.request"))
            {
                DateTime now = DateTime.UtcNow;
                string key = "account-export:" + userId;
                if (_Limiter.IsLimited(key, ExportsPerDay, RateLimitService.Day, now))
                {
                    activity?.SetTag("outcome", "rate_limited");
                    throw ApiErrorException.RateLimited(_Limiter.RetryAfterSeconds(key, ExportsPerDay, RateLimitService.Day, now), "You can request " + ExportsPerDay + " exports per day. Try again later.");
                }
                AccountExport export = new AccountExport
                {
                    Id = IdGenerator.Generate(Constants.AccountExportIdPrefix),
                    UserId = userId,
                    Status = AccountExportStatusEnum.Pending,
                    CreatedUtc = now
                };
                try
                {
                    await _Database.AccountExports.CreateAsync(export, token).ConfigureAwait(false);
                }
                catch (DuplicateRecordException)
                {
                    activity?.SetTag("outcome", "in_progress");
                    AccountExport? running = await _Database.AccountExports.ReadActiveByUserAsync(userId, token).ConfigureAwait(false);
                    const string message = "An export is already being prepared. Check its status and try again when it finishes.";
                    throw new ApiErrorException(409, "ExportInProgress", message, null,
                        new ExportInProgressErrorResponse { Error = "ExportInProgress", Message = message, ExportId = running?.Id });
                }
                _Limiter.TryAcquire(key, ExportsPerDay, RateLimitService.Day, now);
                Signal.Signal();
                activity?.SetTag("outcome", "queued");
                return new AccountExportCreatedResponse { ExportId = export.Id, Status = export.Status };
            }
        }

        /// <summary>
        /// GET /api/v1/me/export/{id}: the member's own export (others get 404).
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="id">Export identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View with a signed download URL when Ready.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<AccountExportView> ReadAsync(string userId, string id, CancellationToken token = default)
        {
            AccountExport? export = String.IsNullOrWhiteSpace(id) ? null : await _Database.AccountExports.ReadAsync(id.Trim(), token).ConfigureAwait(false);
            if (export == null || export.UserId != userId) throw ApiErrorException.NotFound("Export");
            AccountExportView view = new AccountExportView
            {
                Id = export.Id,
                Status = export.Status,
                CreatedUtc = export.CreatedUtc,
                CompletedUtc = export.CompletedUtc,
                ExpiresUtc = export.ExpiresUtc
            };
            if (export.Status == AccountExportStatusEnum.Ready)
            {
                view.SizeBytes = export.SizeBytes;
                view.DownloadUrl = SignedUrl(export.Id, DateTime.UtcNow.AddMinutes(DownloadUrlMinutes));
            }
            return view;
        }

        /// <summary>
        /// Signed download path for an export.
        /// </summary>
        /// <param name="id">Export identifier.</param>
        /// <param name="expiresUtc">When the URL stops working.</param>
        /// <returns>Path with exp and sig query parameters.</returns>
        /// <exception cref="ArgumentNullException">The identifier is null or empty.</exception>
        public string SignedUrl(string id, DateTime expiresUtc)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            long exp = new DateTimeOffset(DateTime.SpecifyKind(expiresUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();
            string expText = exp.ToString(CultureInfo.InvariantCulture);
            return Constants.ApiPrefix + "/me/export/" + Uri.EscapeDataString(id) + "/download?exp=" + expText + "&sig=" + Sign(id, expText);
        }

        /// <summary>
        /// Verify a download signature and fetch the stored export into a local copy for the route to stream (dispose
        /// the result to delete the copy).
        /// </summary>
        /// <param name="id">Export identifier.</param>
        /// <param name="exp">Expiry (Unix seconds) from the URL.</param>
        /// <param name="sig">Signature from the URL.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The download.</returns>
        /// <exception cref="ApiErrorException">403 InvalidSignature (bad or expired signature), 404 NotFound (not Ready or no stored file).</exception>
        public async Task<AccountExportDownload> AuthorizeDownloadAsync(string id, string? exp, string? sig, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id) || String.IsNullOrWhiteSpace(exp) || String.IsNullOrWhiteSpace(sig)) throw InvalidSignature();
            if (!Int64.TryParse(exp, NumberStyles.Integer, CultureInfo.InvariantCulture, out long seconds)) throw InvalidSignature();
            byte[] expected = Encoding.ASCII.GetBytes(Sign(id, exp.Trim()));
            byte[] presented = Encoding.ASCII.GetBytes(sig.Trim());
            if (!CryptographicOperations.FixedTimeEquals(expected, presented)) throw InvalidSignature();
            if (DateTimeOffset.FromUnixTimeSeconds(Math.Clamp(seconds, 0, 253402300799L)).UtcDateTime < DateTime.UtcNow) throw InvalidSignature();
            AccountExport? export = await _Database.AccountExports.ReadAsync(id, token).ConfigureAwait(false);
            if (export == null || export.Status != AccountExportStatusEnum.Ready) throw ApiErrorException.NotFound("Export");
            string key = StorageKey(export.Id);
            if (await _Integrations.ObjectStore.HeadAsync(StorageLocation, key, token).ConfigureAwait(false) == null) throw ApiErrorException.NotFound("Export");
            Directory.CreateDirectory(ExportDirectory());
            string copy = Path.Combine(ExportDirectory(), export.Id + "." + Guid.NewGuid().ToString("N") + ".download");
            AccountExportDownload download = new AccountExportDownload(export.Id, copy);
            try
            {
                await _Integrations.ObjectStore.DownloadToFileAsync(StorageLocation, key, copy, token).ConfigureAwait(false);
            }
            catch (FileNotFoundException)
            {
                download.Dispose();
                throw ApiErrorException.NotFound("Export");
            }
            catch
            {
                download.Dispose();
                throw;
            }
            return download;
        }

        /// <summary>
        /// Object store key of an export file (Retained location): {propertyId}/exports/{id}.json.
        /// </summary>
        /// <param name="id">Export identifier.</param>
        /// <returns>Key.</returns>
        /// <exception cref="ArgumentException">The identifier is not a valid export identifier.</exception>
        public string StorageKey(string id)
        {
            return MediaKeys.AccountExport(_PropertyId, id);
        }

        /// <summary>
        /// Worker stage: claim pending exports and write their files.
        /// </summary>
        /// <param name="holder">Worker instance identifier.</param>
        /// <param name="limit">Most exports per pass.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Exports finished (Ready or Failed).</returns>
        /// <exception cref="ArgumentNullException">The holder is null or empty.</exception>
        public async Task<int> ProcessAsync(string holder, int limit, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(holder)) throw new ArgumentNullException(nameof(holder));
            DateTime now = DateTime.UtcNow;
            List<AccountExport> claimed = await _Database.AccountExports.ClaimAsync(holder, now, now.AddMinutes(ClaimMinutes), limit, token).ConfigureAwait(false);
            foreach (AccountExport export in claimed)
            {
                await GenerateAsync(export, holder, token).ConfigureAwait(false);
                if (Email == null) continue;
                AccountExport? done = await _Database.AccountExports.ReadAsync(export.Id, token).ConfigureAwait(false);
                if (done?.Status != AccountExportStatusEnum.Ready) continue;
                User? user = await _Database.Users.ReadAsync(done.UserId, token).ConfigureAwait(false);
                await Email.SendToMemberAsync("export_ready", user, "export_ready:" + done.Id, token, ("link", Email.Link("/app/settings")),
                    ("expiresMinutes", _Settings.Account.ExportDownloadLinkMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture))).ConfigureAwait(false);
            }
            return claimed.Count;
        }

        /// <summary>
        /// Worker stage: delete stored files whose retention ended and mark them Expired.
        /// </summary>
        /// <param name="limit">Most exports per pass.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Exports expired.</returns>
        public async Task<int> ExpireAsync(int limit, CancellationToken token = default)
        {
            using (Activity? activity = TrellisKitTelemetry.StartSpan("account_export.expire"))
            {
                int count = 0;
                foreach (AccountExport export in await _Database.AccountExports.EnumerateExpiredAsync(DateTime.UtcNow, limit, token).ConfigureAwait(false))
                {
                    await DeleteStoredAsync(export.Id, token).ConfigureAwait(false);
                    if (await _Database.AccountExports.MarkExpiredAsync(export.Id, token).ConfigureAwait(false)) count++;
                }
                activity?.SetTag("expired", count);
                return count;
            }
        }

        /// <summary>
        /// Delete every export file and row of a member (account deletion).
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The member identifier is null or empty.</exception>
        public async Task DeleteForUserAsync(string userId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            foreach (string id in await _Database.AccountExports.DeleteByUserAsync(userId, token).ConfigureAwait(false)) await DeleteStoredAsync(id, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private async Task GenerateAsync(AccountExport export, string holder, CancellationToken token)
        {
            using (Activity? activity = TrellisKitTelemetry.StartSpan("account_export.generate"))
            {
                string temp = Path.Combine(ExportDirectory(), export.Id + "." + Guid.NewGuid().ToString("N") + ".tmp");
                string key = StorageKey(export.Id);
                bool stored = false;
                try
                {
                    _Integrations.ObjectStore.RequireConfigured(StorageLocation);
                    Directory.CreateDirectory(ExportDirectory());
                    DateTime started = DateTime.UtcNow;
                    FileStream stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true);
                    await using (stream.ConfigureAwait(false))
                    {
                        Utf8JsonWriter writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
                        await using (writer.ConfigureAwait(false))
                        {
                            writer.WriteStartObject();
                            writer.WriteString("exportId", export.Id);
                            writer.WriteString("exportedUtc", started.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
                            foreach (IAccountExportSection section in Sections)
                            {
                                writer.WritePropertyName(section.Name);
                                await section.WriteAsync(writer, export.UserId, token).ConfigureAwait(false);
                                await writer.FlushAsync(token).ConfigureAwait(false);
                            }
                            writer.WriteEndObject();
                            await writer.FlushAsync(token).ConfigureAwait(false);
                        }
                    }
                    long size = new FileInfo(temp).Length;
                    await _Integrations.ObjectStore.PutFileAsync(StorageLocation, key, temp, "application/json", token).ConfigureAwait(false);
                    stored = true;
                    TryDelete(temp);
                    DateTime completed = DateTime.UtcNow;
                    if (!await _Database.AccountExports.CompleteAsync(export.Id, holder, size, completed, completed.AddHours(_Settings.Account.ExportRetentionHours), token).ConfigureAwait(false))
                    {
                        await DeleteStoredAsync(export.Id, token).ConfigureAwait(false);
                        activity?.SetTag("outcome", "claim_lost");
                        return;
                    }
                    activity?.SetTag("outcome", "ready");
                    activity?.SetTag("bytes", size);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    _Logger.Error("[AccountExportService] export " + export.Id + " failed", e);
                    TryDelete(temp);
                    if (stored) await DeleteStoredAsync(export.Id, token).ConfigureAwait(false);
                    activity?.SetTag("outcome", "failed");
                    await _Database.AccountExports.FailAsync(export.Id, holder, e.GetType().Name + ": " + e.Message, DateTime.UtcNow, token).ConfigureAwait(false);
                }
            }
        }

        private string Sign(string id, string exp)
        {
            using (HMACSHA256 hmac = new HMACSHA256(DerivedKey()))
            {
                return Base64Url.Encode(hmac.ComputeHash(Encoding.UTF8.GetBytes(id + "|" + exp)));
            }
        }

        private byte[] DerivedKey()
        {
            byte[] root;
            try
            {
                root = Convert.FromBase64String(_Settings.Auth.TokenEncryptionKey ?? String.Empty);
            }
            catch (FormatException e)
            {
                throw new InvalidOperationException("auth.tokenEncryptionKey is not valid base64.", e);
            }
            if (root.Length < 16) throw new InvalidOperationException("auth.tokenEncryptionKey is not set.");
            using (HMACSHA256 hmac = new HMACSHA256(root))
            {
                return hmac.ComputeHash(Encoding.UTF8.GetBytes(_KeyPurpose));
            }
        }

        private string ExportDirectory()
        {
            return Path.Combine(Path.GetFullPath(_Settings.Storage.Directory), ExportsFolder);
        }

        private async Task DeleteStoredAsync(string id, CancellationToken token)
        {
            try
            {
                await _Integrations.ObjectStore.DeleteAsync(StorageLocation, StorageKey(id), token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException && e is not ArgumentException)
            {
                _Logger.Warn("[AccountExportService] could not delete stored export " + id + ": " + e.Message);
            }
        }

        private void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException e)
            {
                _Logger.Warn("[AccountExportService] could not delete " + path + ": " + e.Message);
            }
            catch (UnauthorizedAccessException e)
            {
                _Logger.Warn("[AccountExportService] could not delete " + path + ": " + e.Message);
            }
        }

        private static ApiErrorException InvalidSignature()
        {
            return new ApiErrorException(403, "InvalidSignature", "The download link is invalid or has expired. Request a new link from the export status.");
        }

        #endregion
    }
}
