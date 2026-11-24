namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Security.Cryptography;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Core.Settings;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Live updates (docs/LIVE_UPDATES_CONTRACT.md): the Expo Updates protocol endpoint with signatures verified the way
    /// expo-updates does (throwaway keys generated at test time, chained like the sample certificates), key rotation via
    /// the certificate chain, uploads in the publish script's exact shape, grouping, rollout buckets, pause, rollout
    /// plans, the automatic halt, rollback, rollback to embedded, promotion, deploy keys, events and adoption, and the
    /// protocol parsers.
    /// </summary>
    public static class LiveUpdatesSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "LiveUpdates";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Live updates (" + TestEnvironment.Provider + ")", new[]
            {
                Unit("StructuredFields.Parse", "expo-expect-signature, expo-extra-params, and expo-recent-failed-update-ids parse like RFC 8941", () =>
                {
                    Dictionary<string, string> d = StructuredFields.ParseDictionary("sig, keyid=\"main\", alg=\"rsa-v1_5-sha256\"");
                    Check.Equal("true", d["sig"], "Bare key");
                    Check.Equal("main", d["keyid"], "keyid");
                    Check.Equal("rsa-v1_5-sha256", d["alg"], "alg");
                    Check.Equal("2f1c", StructuredFields.ParseDictionary("installid=\"2f1c\";p=1, other=?0")["installid"], "Parameters ignored");
                    List<string> list = StructuredFields.ParseList("\"a-1\", \"b-2\";x=1,c");
                    Check.Equal("a-1,b-2,c", String.Join(",", list), "List");
                    Check.Equal(0, StructuredFields.ParseList(null).Count, "Null list");
                    Check.Equal("\"a\\\"b\"", StructuredFields.Quote("a\"b"), "Quote escapes");
                }),
                Unit("Rules.BucketsAndPercent", "Rollout buckets are deterministic, uniform, and independent per group; percentages map to basis points", () =>
                {
                    Check.Equal(LiveUpdateRules.Bucket("upg_a", "install-1"), LiveUpdateRules.Bucket("upg_a", "install-1"), "Deterministic");
                    int inside = Enumerable.Range(0, 10000).Count(i => LiveUpdateRules.InRollout("upg_a", "i" + i, 2500));
                    Check.True(inside > 2200 && inside < 2800, "About 25 percent inside (" + inside + ")");
                    Check.True(!LiveUpdateRules.InRollout("upg_a", null, 9999), "No install id is outside partial rollouts");
                    Check.True(LiveUpdateRules.InRollout("upg_a", null, 10000), "No install id is inside 100 percent");
                    Check.Equal(1050, LiveUpdateRules.ToBasisPoints(10.5m), "10.5 percent");
                    Check.Equal(-1, LiveUpdateRules.ToBasisPoints(10.555m), "Three decimals rejected");
                    Check.Equal(-1, LiveUpdateRules.ToBasisPoints(101m), "Above 100 rejected");
                    Check.Equal("2026-12-01T17:00:00.123Z", LiveUpdateRules.Iso(new DateTime(2026, 12, 1, 17, 0, 0, 123, DateTimeKind.Utc)), "Millisecond ISO");
                }),
                Unit("Semver.Ranges", "appVersion targeting supports comparators, caret, tilde, x-ranges, hyphen ranges, and unions", () =>
                {
                    Check.True(SemverRange.TryParse(">=1.3.0 <2.0.0", out SemverRange? r) && r!.Satisfies("1.3.0") && r.Satisfies("1.9.9") && !r.Satisfies("2.0.0") && !r.Satisfies("1.2.9"), "Comparators");
                    Check.True(SemverRange.TryParse("^1.2", out SemverRange? c) && c!.Satisfies("1.9.0") && !c.Satisfies("2.0.0"), "Caret");
                    Check.True(SemverRange.TryParse("~1.2.3", out SemverRange? t) && t!.Satisfies("1.2.9") && !t.Satisfies("1.3.0"), "Tilde");
                    Check.True(SemverRange.TryParse("1.x || >=3", out SemverRange? u) && u!.Satisfies("1.4.0") && u.Satisfies("3.1.0") && !u.Satisfies("2.0.0"), "Union and x-range");
                    Check.True(SemverRange.TryParse("1.0.0 - 1.5", out SemverRange? h) && h!.Satisfies("1.5.9") && !h.Satisfies("1.6.0"), "Hyphen");
                    Check.True(!SemverRange.TryParse(">=banana", out SemverRange? _), "Invalid");
                }),
                Unit("Multipart.Reader", "The streaming multipart reader handles boundaries across buffer edges, binary bodies, and the closing boundary", () =>
                {
                    byte[] binary = Enumerable.Range(0, 200000).Select(i => (byte)(i % 251)).ToArray();
                    MultipartFormDataContent form = new MultipartFormDataContent("XyZboundary");
                    form.Add(new StringContent("{\"a\":1}", Encoding.UTF8, "application/json"), "update", "update.json");
                    form.Add(new ByteArrayContent(binary), "abc", "file.bin");
                    form.Add(new ByteArrayContent(Array.Empty<byte>()), "empty", "empty.bin");
                    byte[] raw = form.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                    MultipartFormReader reader = new MultipartFormReader(new MemoryStream(raw), "XyZboundary", 4096);
                    MultipartSection? first = reader.NextAsync().GetAwaiter().GetResult();
                    Check.Equal("update", first!.Name, "First part");
                    MemoryStream json = new MemoryStream();
                    reader.ReadBodyAsync((b, o, n) => json.WriteAsync(b, o, n), 1000).GetAwaiter().GetResult();
                    Check.Equal("{\"a\":1}", Encoding.UTF8.GetString(json.ToArray()), "JSON body");
                    MultipartSection? second = reader.NextAsync().GetAwaiter().GetResult();
                    Check.Equal("abc", second!.Name, "Second part");
                    Check.Equal("file.bin", second.FileName, "File name");
                    MemoryStream bytes = new MemoryStream();
                    long size = reader.ReadBodyAsync((b, o, n) => bytes.WriteAsync(b, o, n), 1000000).GetAwaiter().GetResult();
                    Check.Equal((long)binary.Length, size, "Binary size");
                    Check.True(bytes.ToArray().SequenceEqual(binary), "Binary bytes");
                    Check.Equal("empty", reader.NextAsync().GetAwaiter().GetResult()!.Name, "Empty part");
                    Check.True(reader.NextAsync().GetAwaiter().GetResult() == null, "End");
                    MultipartFormReader limited = new MultipartFormReader(new MemoryStream(raw), "XyZboundary");
                    limited.NextAsync().GetAwaiter().GetResult();
                    limited.NextAsync().GetAwaiter().GetResult();
                    Check.Throws<PayloadTooLargeException>(() => limited.ReadBodyAsync(null, 1000).GetAwaiter().GetResult(), "Part size limit");
                }),
                Unit("Certificates.SampleChain", "The sample property's committed certificates form the rotation chain the server expects: certificate.pem is a self-signed code signing CA (main) and standby-certificate.pem is issued by it", () =>
                {
                    string folder = Path.Combine(TestProperty.SampleDirectory, "mobile", "updates");
                    X509Certificate2 main = X509Certificate2.CreateFromPem(File.ReadAllText(Path.Combine(folder, "certificate.pem")));
                    X509Certificate2 standby = X509Certificate2.CreateFromPem(File.ReadAllText(Path.Combine(folder, "standby-certificate.pem")));
                    Check.True(UpdateSigningService.IsAuthority(main), "certificate.pem is a CA");
                    Check.True(UpdateSigningService.AllowsCodeSigning(main) && UpdateSigningService.AllowsCodeSigning(standby), "Code signing usage");
                    Check.True(main.SubjectName.RawData.AsSpan().SequenceEqual(main.IssuerName.RawData), "certificate.pem is self-signed");
                    using (X509Chain chain = new X509Chain())
                    {
                        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                        chain.ChainPolicy.CustomTrustStore.Add(main);
                        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                        Check.True(chain.Build(standby) && chain.ChainElements.Count == 2, "standby-certificate.pem chains to certificate.pem");
                    }
                    TestSigningChain test = new TestSigningChain();
                    X509Certificate2 testMain = X509Certificate2.CreateFromPem(test.MainCertificatePem);
                    Check.True(UpdateSigningService.IsAuthority(testMain) && UpdateSigningService.AllowsCodeSigning(testMain), "The throwaway chain has the same shape");
                    using (RSA? key = UpdateSigningService.ParseKey(test.MainKeyPem)) Check.True(key != null && UpdateSigningService.Matches(testMain, key), "Throwaway key matches its certificate");
                    using (RSA? other = UpdateSigningService.ParseKey(test.StandbyKeyPem)) Check.True(other != null && !UpdateSigningService.Matches(testMain, other), "Mismatched key detected");
                }),
                Case("Manifest.EndToEnd", "A script-shaped upload publishes; the signed manifest verifies against the embedded certificate; assets serve with Range; a current app gets a signed noUpdateAvailable; source maps are never served", async (f, chain, ct) =>
                {
                    string groupUuid = Guid.NewGuid().ToString();
                    Dictionary<string, byte[]> parts = new Dictionary<string, byte[]>();
                    Dictionary<string, object?> ios = UpdateTestKit.Descriptor("ios", "production", groupUuid, "Fix comment composer", "e2e", parts, 100, true, "Restart for the fix");
                    ApiResult created = await UpdateTestKit.UploadAsync(f, HttpFixture.AdminToken, ios, parts, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.Created, created.Status, "Upload (" + created.Text + ")");
                    string groupId = UpdateTestKit.GroupId(created.Body);
                    Check.True(groupId.StartsWith("upg_", StringComparison.Ordinal), "Group id");
                    Check.Equal("Published", Api.Str(created.Body, "status"), "Published at 100");
                    Check.Equal(groupUuid, Api.Str(created.Body, "updateGroupUuid"), "Client group id kept");
                    Dictionary<string, byte[]> androidParts = new Dictionary<string, byte[]>();
                    Dictionary<string, object?> android = UpdateTestKit.Descriptor("android", "production", groupUuid, "Fix comment composer", "e2e", androidParts, 100, true, "Restart for the fix");
                    string imagePart = (string)((Dictionary<string, object?>)((List<object?>)android["assets"]!)[0]!)["part"]!;
                    ApiResult joined = await UpdateTestKit.UploadAsync(f, HttpFixture.AdminToken, android, androidParts, ct, new[] { imagePart }).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.Created, joined.Status, "Second platform with an already stored asset omitted (" + joined.Text + ")");
                    Check.Equal(groupId, UpdateTestKit.GroupId(joined.Body), "Joined the same group");
                    Check.Equal(2, joined.Body.GetProperty("updates").GetArrayLength(), "Two platforms");
                    ApiResult again = await UpdateTestKit.UploadAsync(f, HttpFixture.AdminToken, android, androidParts, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.Conflict, again.Status, "A platform uploads once per group");

                    string bundleHash = (string)((Dictionary<string, object?>)ios["launchAsset"]!)["hash"]!;
                    string bundleHex = LiveUpdateRules.HexOfBase64Url(bundleHash)!;
                    ApiResult head = await Api.SendAsync(f, HttpMethod.Head, "/api/v1/admin/updates/assets/" + bundleHex, HttpFixture.AdminToken, null, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.OK, head.Status, "HEAD stored asset");
                    ApiResult missing = await Api.SendAsync(f, HttpMethod.Head, "/api/v1/admin/updates/assets/" + new string('a', 64), HttpFixture.AdminToken, null, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.NotFound, missing.Status, "HEAD missing asset");

                    string iosUuid = UpdateTestKit.Uuid(joined.Body, "ios");
                    string embedded = Guid.NewGuid().ToString();
                    TestManifestResponse response = await UpdateTestKit.ManifestAsync(f, ct, "ios", "production", "install-a", embedded, embedded).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.OK, response.Status, "Manifest status (" + response.Text + ")");
                    Check.True(response.ContentType.StartsWith("multipart/mixed", StringComparison.Ordinal), "multipart/mixed");
                    Check.Equal("1", response.Headers["expo-protocol-version"], "expo-protocol-version");
                    Check.Equal("0", response.Headers["expo-sfv-version"], "expo-sfv-version");
                    Check.Equal("channel=\"production\"", response.Headers["expo-manifest-filters"], "Manifest filters");
                    Check.True(response.Headers["cache-control"].Contains("private", StringComparison.Ordinal) && response.Headers["cache-control"].Contains("max-age=0", StringComparison.Ordinal), "Cache-Control");
                    TestManifestPart manifestPart = response.Part("manifest")!;
                    Check.True(manifestPart.Headers["content-type"].StartsWith("application/json", StringComparison.Ordinal), "Manifest part type");
                    string signature = manifestPart.Headers["expo-signature"];
                    Check.Equal("main", StructuredFields.ParseDictionary(signature)["keyid"], "Signed by main");
                    Check.True(response.Part("certificate_chain") == null, "No chain when the embedded key signs");
                    Check.True(UpdateTestKit.VerifyLikeExpo(chain.MainCertificatePem, null, manifestPart.Body, signature), "Signature verifies against the embedded certificate");
                    byte[] tampered = (byte[])manifestPart.Body.Clone();
                    tampered[10] ^= 1;
                    Check.True(!UpdateTestKit.VerifyLikeExpo(chain.MainCertificatePem, null, tampered, signature), "A tampered manifest fails");
                    Check.True(!UpdateTestKit.VerifyLikeExpo(chain.StandbyCertificatePem, null, manifestPart.Body, signature), "Another certificate fails");
                    JsonElement manifest = response.Json("manifest");
                    Check.Equal(iosUuid, Api.Str(manifest, "id"), "Manifest id is the update UUID");
                    Check.Equal(UpdateTestKit.Runtime, Api.Str(manifest, "runtimeVersion"), "Runtime version");
                    Check.True(Api.Str(manifest, "createdAt").EndsWith("Z", StringComparison.Ordinal) && Api.Str(manifest, "createdAt").Length == 24, "createdAt has milliseconds");
                    JsonElement launch = manifest.GetProperty("launchAsset");
                    Check.Equal(bundleHash, Api.Str(launch, "hash"), "Launch asset hash");
                    Check.Equal("application/javascript", Api.Str(launch, "contentType"), "Launch content type");
                    Check.Equal(".bundle", Api.Str(launch, "fileExtension"), "Launch extension");
                    Check.Equal(1, manifest.GetProperty("assets").GetArrayLength(), "One asset");
                    Check.Equal("production", Api.Str(manifest.GetProperty("metadata"), "channel"), "metadata.channel");
                    Check.Equal("true", Api.Str(manifest.GetProperty("metadata"), "critical"), "metadata.critical");
                    JsonElement extra = manifest.GetProperty("extra");
                    Check.True(extra.GetProperty("critical").GetBoolean(), "extra.critical");
                    Check.Equal("Restart for the fix", Api.Str(extra, "criticalMessage"), "extra.criticalMessage");
                    Check.Equal("Example", Api.Str(extra.GetProperty("expoClient"), "name"), "extra.expoClient");
                    Check.Equal(groupId, Api.Str(extra.GetProperty("trelliskit"), "updateGroupId"), "extra.trelliskit");
                    JsonElement extensions = response.Json("extensions");
                    Check.True(extensions.GetProperty("assetRequestHeaders").ValueKind == JsonValueKind.Object, "Extensions part");

                    string url = Api.Str(launch, "url");
                    Check.True(url.StartsWith("http", StringComparison.Ordinal) && url.EndsWith("/" + bundleHex, StringComparison.Ordinal), "Absolute content-addressed URL");
                    using (HttpResponseMessage asset = await f.Client.GetAsync(new Uri(url).PathAndQuery, ct).ConfigureAwait(false))
                    {
                        Check.Equal(HttpStatusCode.OK, asset.StatusCode, "Asset GET");
                        byte[] bytes = await asset.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                        Check.Equal(bundleHash, LiveUpdateRules.Base64Url(SHA256.HashData(bytes)), "Asset bytes match the manifest hash");
                        Check.True(asset.Headers.CacheControl!.ToString().Contains("immutable", StringComparison.Ordinal), "Immutable caching");
                    }
                    using (HttpRequestMessage range = new HttpRequestMessage(HttpMethod.Get, new Uri(url).PathAndQuery))
                    {
                        range.Headers.TryAddWithoutValidation("Range", "bytes=0-9");
                        using (HttpResponseMessage partial = await f.Client.SendAsync(range, ct).ConfigureAwait(false))
                        {
                            Check.Equal(HttpStatusCode.PartialContent, partial.StatusCode, "Range 206");
                            Check.Equal(10, (await partial.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false)).Length, "Ten bytes");
                        }
                    }
                    string mapHash = (string)((Dictionary<string, object?>)((List<object?>)ios["sourceMaps"]!)[0]!)["hash"]!;
                    ApiResult map = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/updates/assets/" + f.Property.Id + "/" + LiveUpdateRules.HexOfBase64Url(mapHash), null, null, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.NotFound, map.Status, "Source maps are never served");
                    Check.True(!File.Exists(Path.Combine(f.Settings.Storage.Directory, "updates", "source", "updates", "assets", f.Property.Id, LiveUpdateRules.HexOfBase64Url(mapHash)!)), "Source map not under the public prefix");
                    JsonElement detail = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/update-groups/" + groupId, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1L, detail.GetProperty("updates")[0].GetProperty("sourceMapCount").GetInt64(), "Source map stored privately");

                    TestManifestResponse current = await UpdateTestKit.ManifestAsync(f, ct, "ios", "production", "install-a", iosUuid, embedded).ConfigureAwait(false);
                    TestManifestPart directive = current.Part("directive")!;
                    Check.Equal("noUpdateAvailable", Api.Str(current.Json("directive"), "type"), "noUpdateAvailable");
                    Check.True(UpdateTestKit.VerifyLikeExpo(chain.MainCertificatePem, null, directive.Body, directive.Headers["expo-signature"]), "Directive is signed");

                    TestManifestResponse failed = await UpdateTestKit.ManifestAsync(f, ct, "ios", "production", "install-b", embedded, embedded, "main", "\"" + iosUuid + "\"").ConfigureAwait(false);
                    Check.Equal("noUpdateAvailable", Api.Str(failed.Json("directive"), "type"), "A recently failed update is not offered again");
                    TestManifestResponse later = await UpdateTestKit.ManifestAsync(f, ct, "ios", "production", "install-b", embedded, embedded).ConfigureAwait(false);
                    Check.True(later.Part("directive") != null, "The failure is remembered for the install");

                    TestManifestResponse protocol0 = await UpdateTestKit.ManifestAsync(f, ct, "ios", "production", "install-c", embedded, embedded, "main", null, 0).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.OK, protocol0.Status, "Protocol 0");
                    Check.True(protocol0.ContentType.StartsWith("application/expo+json", StringComparison.Ordinal), "JSON manifest");
                    Check.True(UpdateTestKit.VerifyLikeExpo(chain.MainCertificatePem, null, Encoding.UTF8.GetBytes(protocol0.Text), protocol0.Headers["expo-signature"]), "Protocol 0 signature header");

                    System.Data.DataTable installs = await f.Raw.ExecuteQueryAsync("SELECT installidhash, keyid, appversion FROM updateinstalls WHERE propertyid = '" + f.Property.Id + "';", false, ct).ConfigureAwait(false);
                    Check.True(installs.Rows.Count >= 3, "Install rows recorded");
                    Check.True(installs.Rows.Cast<System.Data.DataRow>().All(r => !r["installidhash"].ToString()!.Contains("install-", StringComparison.Ordinal) && r["installidhash"].ToString()!.Length == 64), "Install ids are hashed at rest");
                    Check.Equal("main", installs.Rows[0]["keyid"].ToString(), "Embedded key id recorded");
                }),
                Case("Manifest.Errors", "Missing headers, unsupported platforms, unknown channels are plain text 400s; without signing keys the endpoint refuses (503) and uploads stay Draft", async (f, chain, ct) =>
                {
                    TestManifestResponse noRuntime = await UpdateTestKit.ManifestAsync(f, ct, "ios", "production", "x", null, null, "main", null, 1, "").ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.BadRequest, noRuntime.Status, "Missing runtime");
                    Check.True(noRuntime.Text.StartsWith("400 MissingProtocolHeader", StringComparison.Ordinal) && noRuntime.ContentType.StartsWith("text/plain", StringComparison.Ordinal), "Plain text error: " + noRuntime.Text);
                    TestManifestResponse web = await UpdateTestKit.ManifestAsync(f, ct, "web").ConfigureAwait(false);
                    Check.True(web.Text.StartsWith("400 UnsupportedPlatform", StringComparison.Ordinal), "Unsupported platform");
                    TestManifestResponse channel = await UpdateTestKit.ManifestAsync(f, ct, "ios", "nope").ConfigureAwait(false);
                    Check.True(channel.Text.StartsWith("400 UnknownChannel", StringComparison.Ordinal), "Unknown channel");
                    TestManifestResponse unknownKey = await UpdateTestKit.ManifestAsync(f, ct, "ios", "production", "x", null, null, "other").ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.ServiceUnavailable, unknownKey.Status, "Unknown keyid is 503 (" + unknownKey.Text + ")");
                    Check.True(unknownKey.Text.Contains("SigningUnavailable", StringComparison.Ordinal), "SigningUnavailable");
                }, keys: true),
                Case("Signing.MissingKeys", "Without private keys the manifest endpoint answers 503 SigningUnavailable and an upload is kept as Draft", async (f, chain, ct) =>
                {
                    TestManifestResponse none = await UpdateTestKit.ManifestAsync(f, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.ServiceUnavailable, none.Status, "503 without keys");
                    JsonElement health = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/updates/health", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("Missing", Api.Str(health, "signing"), "Health reports Missing");
                    Dictionary<string, byte[]> parts = new Dictionary<string, byte[]>();
                    ApiResult upload = await UpdateTestKit.UploadAsync(f, HttpFixture.AdminToken, UpdateTestKit.Descriptor("ios", "preview", Guid.NewGuid().ToString(), "No keys", "nokeys", parts), parts, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.ServiceUnavailable, upload.Status, "Upload refused to publish");
                    JsonElement list = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/update-groups/enumerate", HttpFixture.AdminToken, new { filters = new { status = "Draft" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1, list.GetProperty("objects").GetArrayLength(), "Kept as Draft");
                }, keys: false),
                Case("Signing.RotationChain", "Switching to the standby key sends the certificate chain; old binaries still verify; a retired embedded key id gets 503; stored keys are encrypted and never returned", async (f, chain, ct) =>
                {
                    Dictionary<string, byte[]> parts = new Dictionary<string, byte[]>();
                    ApiResult created = await UpdateTestKit.UploadAsync(f, HttpFixture.AdminToken, UpdateTestKit.Descriptor("ios", "production", Guid.NewGuid().ToString(), "Rotate", "rot", parts), parts, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.Created, created.Status, "Upload (" + created.Text + ")");
                    JsonElement keys = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/updates/signing-keys/enumerate", HttpFixture.AdminToken, new { ordering = "KeyIdAscending" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement[] ring = keys.GetProperty("objects").EnumerateArray().ToArray();
                    Check.Equal(2, ring.Length, "Folder certificates seeded");
                    JsonElement mainKey = ring.First(k => Api.Str(k, "keyId") == "main");
                    JsonElement standbyKey = ring.First(k => Api.Str(k, "keyId") == "standby");
                    Check.Equal("Active", Api.Str(mainKey, "status"), "main Active");
                    Check.Equal("Standby", Api.Str(standbyKey, "status"), "standby Standby");
                    Check.Equal("main", Api.Str(standbyKey, "issuedByKeyId"), "standby issued by main");
                    Check.True(standbyKey.GetProperty("privateKeyAvailable").GetBoolean(), "Standby key available from the environment");
                    Check.True(!keys.ToString().Contains("PRIVATE KEY", StringComparison.Ordinal), "No private material");

                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/updates/signing-keys/" + Api.Str(standbyKey, "id") + "/activate", HttpFixture.AdminToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
                    TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/updates/signing-keys/" + Api.Str(standbyKey, "id") + "/activate", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    TestManifestResponse rotated = await UpdateTestKit.ManifestAsync(f, ct, "ios", "production", "install-r").ConfigureAwait(false);
                    TestManifestPart manifest = rotated.Part("manifest")!;
                    string header = manifest.Headers["expo-signature"];
                    Check.Equal("standby", StructuredFields.ParseDictionary(header)["keyid"], "Actual signing key id in expo-signature");
                    TestManifestPart? chainPart = rotated.Part("certificate_chain");
                    Check.True(chainPart != null, "certificate_chain part present");
                    Check.Equal("application/x-pem-file", chainPart!.Headers["content-type"], "PEM part");
                    string chainPem = Encoding.UTF8.GetString(chainPart.Body);
                    Check.True(chainPem.Contains("BEGIN CERTIFICATE", StringComparison.Ordinal) && X509Certificate2.CreateFromPem(chainPem).Thumbprint == X509Certificate2.CreateFromPem(chain.StandbyCertificatePem).Thumbprint, "Chain is the standby certificate");
                    Check.True(UpdateTestKit.VerifyLikeExpo(chain.MainCertificatePem, chainPem, manifest.Body, header), "A binary embedding certificate.pem accepts the rotated signature");
                    Check.True(!UpdateTestKit.VerifyLikeExpo(chain.MainCertificatePem, null, manifest.Body, header), "Without the chain it would fail");
                    TestManifestResponse standbyBinary = await UpdateTestKit.ManifestAsync(f, ct, "ios", "production", "install-s", null, null, "standby").ConfigureAwait(false);
                    Check.True(standbyBinary.Part("certificate_chain") == null, "No chain for a binary that embeds the standby certificate");
                    using (RSA? standbyPublic = X509Certificate2.CreateFromPem(chain.StandbyCertificatePem).GetRSAPublicKey())
                        Check.True(standbyPublic!.VerifyData(standbyBinary.Part("manifest")!.Body, Convert.FromBase64String(StructuredFields.ParseDictionary(standbyBinary.Part("manifest")!.Headers["expo-signature"])["sig"]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1), "Signed by the standby key itself");

                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/updates/signing-keys/" + Api.Str(mainKey, "id") + "/retire", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    TestManifestResponse retired = await UpdateTestKit.ManifestAsync(f, ct, "ios", "production", "install-t").ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.ServiceUnavailable, retired.Status, "Retired embedded key id is 503");

                    TestSigningChain spare = new TestSigningChain();
                    // The settings store generates the secrets key on first start; without a valid key a stored signing key is refused.
                    string secretsKey = f.Settings.Secrets.EncryptionKey;
                    f.Settings.Secrets.EncryptionKey = String.Empty;
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/updates/signing-keys", admin.AccessToken, new { keyId = "spare", certificatePem = spare.MainCertificatePem, privateKeyPem = spare.MainKeyPem }, HttpStatusCode.ServiceUnavailable, "IntegrationNotConfigured", ct).ConfigureAwait(false);
                    f.Settings.Secrets.EncryptionKey = String.IsNullOrWhiteSpace(secretsKey) ? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) : secretsKey;
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/updates/signing-keys", HttpFixture.AdminToken, new { keyId = "spare", certificatePem = spare.MainCertificatePem, privateKeyPem = spare.MainKeyPem }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/updates/signing-keys", admin.AccessToken, new { keyId = "spare", certificatePem = spare.MainCertificatePem, privateKeyPem = chain.MainKeyPem }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    JsonElement stored = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/updates/signing-keys", admin.AccessToken, new { keyId = "spare", certificatePem = spare.MainCertificatePem, privateKeyPem = spare.MainKeyPem }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    Check.Equal("Stored", Api.Str(stored, "source"), "Stored key");
                    Check.True(stored.GetProperty("privateKeyAvailable").GetBoolean(), "Decrypts");
                    System.Data.DataTable row = await f.Raw.ExecuteQueryAsync("SELECT privatekeyciphertext FROM updatesigningkeys WHERE propertyid = '" + f.Property.Id + "' AND keyid = 'spare';", false, ct).ConfigureAwait(false);
                    string cipher = row.Rows[0]["privatekeyciphertext"].ToString()!;
                    Check.True(cipher.Length > 100 && !cipher.Contains("PRIVATE", StringComparison.Ordinal), "Encrypted at rest");
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/updates/signing-keys/" + Api.Str(stored, "id") + "/activate", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    TestManifestResponse spareSigned = await UpdateTestKit.ManifestAsync(f, ct, "ios", "production", "install-u", null, null, "spare").ConfigureAwait(false);
                    Check.True(UpdateTestKit.VerifyLikeExpo(spare.MainCertificatePem, null, spareSigned.Part("manifest")!.Body, spareSigned.Part("manifest")!.Headers["expo-signature"]), "Stored key signs");
                }),
                Case("Rollout.BucketsPauseAndPlan", "Partial rollouts follow the install bucket; raising adds installs; pause stops new installs; a plan stage applies at its time", async (f, chain, ct) =>
                {
                    Dictionary<string, byte[]> parts = new Dictionary<string, byte[]>();
                    ApiResult created = await UpdateTestKit.UploadAsync(f, HttpFixture.AdminToken, UpdateTestKit.Descriptor("ios", "preview", Guid.NewGuid().ToString(), "Half", "half", parts, 50), parts, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.Created, created.Status, "Upload (" + created.Text + ")");
                    string groupId = UpdateTestKit.GroupId(created.Body);
                    string uuid = UpdateTestKit.Uuid(created.Body, "ios");
                    string inside = UpdateTestKit.InstallIn(groupId, 5000, true);
                    string outside = UpdateTestKit.InstallIn(groupId, 5000, false);
                    Check.True((await UpdateTestKit.ManifestAsync(f, ct, "ios", "preview", inside).ConfigureAwait(false)).Part("manifest") != null, "Inside the bucket gets the manifest");
                    Check.True((await UpdateTestKit.ManifestAsync(f, ct, "ios", "preview", outside).ConfigureAwait(false)).Part("directive") != null, "Outside gets noUpdateAvailable");
                    Check.True((await UpdateTestKit.ManifestAsync(f, ct, "ios", "preview", null).ConfigureAwait(false)).Part("directive") != null, "No install id is outside");

                    Dictionary<string, byte[]> parts2 = new Dictionary<string, byte[]>();
                    ApiResult conflict = await UpdateTestKit.UploadAsync(f, HttpFixture.AdminToken, UpdateTestKit.Descriptor("ios", "preview", Guid.NewGuid().ToString(), "Next", "next", parts2, 10), parts2, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.Conflict, conflict.Status, "RolloutConflict while another update is mid-rollout");
                    Check.Equal("RolloutConflict", conflict.Error, "Error code");

                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/update-groups/" + groupId + "/rollout", HttpFixture.AdminToken, new { rolloutPercentage = 100 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True((await UpdateTestKit.ManifestAsync(f, ct, "ios", "preview", outside).ConfigureAwait(false)).Part("manifest") != null, "Raised to 100 reaches everyone");
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/update-groups/" + groupId + "/pause", HttpFixture.AdminToken, new { reason = "Investigating" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("noUpdateAvailable", Api.Str((await UpdateTestKit.ManifestAsync(f, ct, "ios", "preview", "new-install").ConfigureAwait(false)).Json("directive"), "type"), "Paused: new installs get nothing");
                    Check.Equal("noUpdateAvailable", Api.Str((await UpdateTestKit.ManifestAsync(f, ct, "ios", "preview", inside, uuid).ConfigureAwait(false)).Json("directive"), "type"), "Paused: installs on it keep it");
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/update-groups/" + groupId + "/resume", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/update-groups/" + groupId + "/rollout", HttpFixture.AdminToken, new { rolloutPercentage = 5 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement planned = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/update-groups/" + groupId + "/rollout-plan", HttpFixture.AdminToken,
                        new { stages = new object[] { new { percentage = 25, startsUtc = DateTime.UtcNow.AddMinutes(-1) }, new { percentage = 100, startsUtc = DateTime.UtcNow.AddDays(2) } }, autoHaltFailurePercent = 10 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(25m, planned.GetProperty("rolloutPercentage").GetDecimal(), "Due stage applied");
                    Check.Equal(2, planned.GetProperty("rolloutPlan").GetArrayLength(), "Two stages");
                    Check.True(planned.GetProperty("rolloutPlan")[1].GetProperty("appliedUtc").ValueKind == JsonValueKind.Null, "Future stage pending");
                    Check.Equal(10, planned.GetProperty("autoHalt").GetProperty("failurePercent").GetInt32(), "Auto-halt threshold");
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/admin/update-groups/" + groupId + "/rollout-plan", HttpFixture.AdminToken,
                        new { stages = new object[] { new { percentage = 50, hoursAfterPublish = 1 }, new { percentage = 40, hoursAfterPublish = 2 } } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    JsonElement template = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/update-groups/" + groupId + "/rollout-plan", HttpFixture.AdminToken, new { useTemplate = true }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(3, template.GetProperty("rolloutPlan").GetArrayLength(), "Template stages");
                }),
                Case("Rollout.AutoHalt", "When the launch failure rate of a rollout exceeds its threshold the worker pauses it and records why", async (f, chain, ct) =>
                {
                    Dictionary<string, byte[]> parts = new Dictionary<string, byte[]>();
                    ApiResult created = await UpdateTestKit.UploadAsync(f, HttpFixture.AdminToken, UpdateTestKit.Descriptor("android", "preview", Guid.NewGuid().ToString(), "Crashy", "crash", parts), parts, ct).ConfigureAwait(false);
                    string groupId = UpdateTestKit.GroupId(created.Body);
                    string uuid = UpdateTestKit.Uuid(created.Body, "android");
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/update-groups/" + groupId + "/rollout-plan", HttpFixture.AdminToken, new { autoHaltFailurePercent = 20, autoHaltMinInstalls = 5 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    for (int i = 0; i < 4; i++) await UpdateTestKit.ManifestAsync(f, ct, "android", "preview", "ok-" + i, uuid).ConfigureAwait(false);
                    Check.Equal(0, await f.Services.LiveUpdates.Publish.AutoHaltAsync(DateTime.UtcNow, ct).ConfigureAwait(false), "Too few installs to judge");
                    for (int i = 0; i < 2; i++) await UpdateTestKit.ManifestAsync(f, ct, "android", "preview", "bad-" + i, null, null, "main", "\"" + uuid + "\"").ConfigureAwait(false);
                    Check.Equal(1, await f.Services.LiveUpdates.Publish.AutoHaltAsync(DateTime.UtcNow, ct).ConfigureAwait(false), "Halted (2 of 6 is above 20 percent)");
                    JsonElement group = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/update-groups/" + groupId, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("Paused", Api.Str(group, "status"), "Paused");
                    Check.True(group.GetProperty("autoHalt").GetProperty("halted").GetBoolean(), "Halted flag");
                    Check.True(Api.Str(group.GetProperty("autoHalt"), "haltReason").Contains("exceeded 20%", StringComparison.Ordinal), "Reason");
                    Check.Equal(2L, group.GetProperty("autoHalt").GetProperty("failures").GetInt64(), "Failures counted per install");
                    JsonElement resumed = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/update-groups/" + groupId + "/resume", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(!resumed.GetProperty("autoHalt").GetProperty("halted").GetBoolean(), "Resume clears the halt");
                }),
                Case("Rollback.PromoteAndEmbedded", "Rollback republishes with a newer createdAt; promotion copies preview to production and never from development; rollback to embedded answers the signed directive", async (f, chain, ct) =>
                {
                    Dictionary<string, byte[]> p1 = new Dictionary<string, byte[]>();
                    Dictionary<string, object?> d1 = UpdateTestKit.Descriptor("ios", "preview", Guid.NewGuid().ToString(), "First", "one", p1);
                    ApiResult first = await UpdateTestKit.UploadAsync(f, HttpFixture.AdminToken, d1, p1, ct).ConfigureAwait(false);
                    Dictionary<string, byte[]> p2 = new Dictionary<string, byte[]>();
                    ApiResult second = await UpdateTestKit.UploadAsync(f, HttpFixture.AdminToken, UpdateTestKit.Descriptor("ios", "preview", Guid.NewGuid().ToString(), "Second", "two", p2), p2, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.Created, second.Status, "Second publish (" + second.Text + ")");
                    string firstId = UpdateTestKit.GroupId(first.Body);
                    string secondId = UpdateTestKit.GroupId(second.Body);
                    JsonElement firstDetail = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/update-groups/" + firstId, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("Superseded", Api.Str(firstDetail, "status"), "A newer 100 percent group supersedes the older");

                    JsonElement rollback = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/update-groups/" + secondId + "/rollback", HttpFixture.AdminToken, new { targetUpdateGroupId = firstId, message = "Back" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    Check.Equal("Republish", Api.Str(rollback, "kind"), "Republish");
                    Check.Equal(firstId, Api.Str(rollback, "republishOf"), "republishOf");
                    string republished = UpdateTestKit.Uuid(rollback, "ios");
                    Check.True(republished != UpdateTestKit.Uuid(first.Body, "ios"), "New UUID");
                    Check.True(rollback.GetProperty("updates")[0].GetProperty("createdAt").GetDateTime() > second.Body.GetProperty("updates")[0].GetProperty("createdAt").GetDateTime(), "Newer createdAt");
                    Check.Equal("RolledBack", Api.Str(await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/update-groups/" + secondId, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false), "status"), "Current RolledBack");
                    TestManifestResponse served = await UpdateTestKit.ManifestAsync(f, ct, "ios", "preview", "install-rb").ConfigureAwait(false);
                    Check.Equal(republished, Api.Str(served.Json("manifest"), "id"), "Installs get the republish");
                    Check.Equal((string)((Dictionary<string, object?>)d1["launchAsset"]!)["hash"]!, Api.Str(served.Json("manifest").GetProperty("launchAsset"), "hash"), "Same bundle as the target");

                    JsonElement promoted = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/update-groups/" + Api.Str(rollback, "id") + "/promote", HttpFixture.AdminToken, new { channel = "production", rolloutPercentage = 100 }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    Check.Equal("production", Api.Str(promoted, "channel"), "Promoted channel");
                    Check.Equal(Api.Str(rollback, "id"), Api.Str(promoted, "promotedFrom"), "promotedFrom");
                    TestManifestResponse production = await UpdateTestKit.ManifestAsync(f, ct, "ios", "production", "install-p").ConfigureAwait(false);
                    Check.Equal("production", Api.Str(production.Json("manifest").GetProperty("metadata"), "channel"), "Served with the production channel");
                    Dictionary<string, byte[]> p3 = new Dictionary<string, byte[]>();
                    ApiResult dev = await UpdateTestKit.UploadAsync(f, HttpFixture.AdminToken, UpdateTestKit.Descriptor("ios", "development", Guid.NewGuid().ToString(), "Dev", "dev", p3), p3, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/update-groups/" + UpdateTestKit.GroupId(dev.Body) + "/promote", HttpFixture.AdminToken, new { channel = "production" }, HttpStatusCode.BadRequest, "PromotionNotAllowed", ct).ConfigureAwait(false);

                    JsonElement embedded = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/update-groups/rollback-to-embedded", HttpFixture.AdminToken, new { platform = "ios", runtimeVersion = UpdateTestKit.Runtime, channel = "production" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    Check.Equal("RollBackToEmbedded", Api.Str(embedded, "kind"), "Kind");
                    string embeddedId = Guid.NewGuid().ToString();
                    TestManifestResponse directive = await UpdateTestKit.ManifestAsync(f, ct, "ios", "production", "install-e", Guid.NewGuid().ToString(), embeddedId).ConfigureAwait(false);
                    JsonElement body = directive.Json("directive");
                    Check.Equal("rollBackToEmbedded", Api.Str(body, "type"), "Directive");
                    Check.True(Api.Str(body.GetProperty("parameters"), "commitTime").EndsWith("Z", StringComparison.Ordinal), "commitTime");
                    Check.True(UpdateTestKit.VerifyLikeExpo(chain.MainCertificatePem, null, directive.Part("directive")!.Body, directive.Part("directive")!.Headers["expo-signature"]), "Directive signed");
                    Check.Equal("noUpdateAvailable", Api.Str((await UpdateTestKit.ManifestAsync(f, ct, "ios", "production", "install-e", embeddedId, embeddedId).ConfigureAwait(false)).Json("directive"), "type"), "Already on the embedded bundle");
                    JsonElement list = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/update-groups/enumerate", HttpFixture.AdminToken, new { filters = new { channel = "preview", platform = "ios" }, search = "back" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1, list.GetProperty("objects").GetArrayLength(), "Filter by channel and platform with search");
                    JsonElement kind = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/update-groups?kind=RollBackToEmbedded", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1, kind.GetProperty("objects").GetArrayLength(), "GET form with the kind filter");
                    await Api.ExpectErrorAsync(f, HttpMethod.Delete, "/api/v1/admin/update-groups/" + firstId, HttpFixture.AdminToken, null, HttpStatusCode.Conflict, "UpdateNotDraft", ct).ConfigureAwait(false);
                }),
                Case("DeployKeys.Scopes", "Deploy keys never cover production, upload and publish only on their channels, and reach no other admin route", async (f, chain, ct) =>
                {
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/updates/deploy-keys", HttpFixture.AdminToken, new { name = "ci", channels = new[] { "production" } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    JsonElement key = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/updates/deploy-keys", HttpFixture.AdminToken, new { name = "ci-preview", channels = new[] { "preview" }, labels = new[] { "ci" } }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    string secret = Api.Str(key, "secret");
                    Check.True(secret.StartsWith("tkdk_", StringComparison.Ordinal), "Secret returned once");
                    JsonElement keys = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/updates/deploy-keys/enumerate", HttpFixture.AdminToken, new { labels = new[] { "ci" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(keys.GetProperty("objects")[0].GetProperty("secret").ValueKind == JsonValueKind.Null, "Never listed again");
                    Dictionary<string, byte[]> parts = new Dictionary<string, byte[]>();
                    ApiResult ok = await UpdateTestKit.UploadAsync(f, secret, UpdateTestKit.Descriptor("ios", "preview", Guid.NewGuid().ToString(), "CI", "ci", parts), parts, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.Created, ok.Status, "Preview upload (" + ok.Text + ")");
                    Check.Equal("DeployKey", Api.Str(ok.Body.GetProperty("createdBy"), "type"), "Created by the deploy key");
                    Dictionary<string, byte[]> parts2 = new Dictionary<string, byte[]>();
                    ApiResult prod = await UpdateTestKit.UploadAsync(f, secret, UpdateTestKit.Descriptor("ios", "production", Guid.NewGuid().ToString(), "CI", "ci2", parts2), parts2, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.Forbidden, prod.Status, "Production refused");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/update-groups/enumerate", secret, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/admin/members", secret, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/updates/deploy-keys/" + Api.Str(key, "id") + "/revoke", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Dictionary<string, byte[]> parts3 = new Dictionary<string, byte[]>();
                    ApiResult revoked = await UpdateTestKit.UploadAsync(f, secret, UpdateTestKit.Descriptor("ios", "preview", Guid.NewGuid().ToString(), "CI", "ci3", parts3), parts3, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.Unauthorized, revoked.Status, "Revoked key");
                }),
                Case("Upload.Validation", "Hash mismatches, missing assets, unknown channels, and bad descriptors are refused with field errors", async (f, chain, ct) =>
                {
                    Dictionary<string, byte[]> parts = new Dictionary<string, byte[]>();
                    Dictionary<string, object?> d = UpdateTestKit.Descriptor("ios", "preview", Guid.NewGuid().ToString(), "Bad", "bad", parts);
                    string bundlePart = (string)((Dictionary<string, object?>)d["launchAsset"]!)["part"]!;
                    Dictionary<string, byte[]> wrong = new Dictionary<string, byte[]>(parts) { [bundlePart] = Encoding.UTF8.GetBytes("tampered") };
                    ApiResult mismatch = await UpdateTestKit.UploadAsync(f, HttpFixture.AdminToken, d, wrong, ct).ConfigureAwait(false);
                    Check.Equal("AssetMismatch", mismatch.Error, "AssetMismatch (" + mismatch.Text + ")");
                    ApiResult missing = await UpdateTestKit.UploadAsync(f, HttpFixture.AdminToken, d, parts, ct, new[] { bundlePart }).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.Conflict, missing.Status, "AssetsMissing status");
                    Check.Equal("AssetsMissing", missing.Error, "AssetsMissing");
                    d["channel"] = "nope";
                    Check.Equal("UnknownChannel", (await UpdateTestKit.UploadAsync(f, HttpFixture.AdminToken, d, parts, ct).ConfigureAwait(false)).Error, "UnknownChannel");
                    d["channel"] = "preview";
                    d["platform"] = "web";
                    Check.Equal("UnsupportedPlatform", (await UpdateTestKit.UploadAsync(f, HttpFixture.AdminToken, d, parts, ct).ConfigureAwait(false)).Error, "UnsupportedPlatform");
                    d["platform"] = "ios";
                    d["criticalMessage"] = new string('x', 201);
                    Check.Equal("ValidationFailed", (await UpdateTestKit.UploadAsync(f, HttpFixture.AdminToken, d, parts, ct).ConfigureAwait(false)).Error, "criticalMessage too long");
                    JsonElement channel = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/updates/channels", HttpFixture.AdminToken, new { name = "staff" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    Check.True(!channel.GetProperty("builtIn").GetBoolean(), "Custom channel");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/updates/channels", HttpFixture.AdminToken, new { name = "Bad Name" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    JsonElement channels = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/updates/channels/enumerate", HttpFixture.AdminToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(4, channels.GetProperty("objects").GetArrayLength(), "Built-in and custom channels");
                    Check.True(!channels.GetProperty("objects").EnumerateArray().First(c => Api.Str(c, "name") == "development").GetProperty("promotable").GetBoolean(), "Development not promotable");
                    await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/admin/updates/channels/staff", HttpFixture.AdminToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Delete, "/api/v1/admin/updates/channels/production", HttpFixture.AdminToken, null, HttpStatusCode.Conflict, "Conflict", ct).ConfigureAwait(false);
                }),
                Case("Events.Adoption", "Client events are validated, counted per day, and feed the adoption view and runtime versions", async (f, chain, ct) =>
                {
                    Dictionary<string, byte[]> parts = new Dictionary<string, byte[]>();
                    ApiResult created = await UpdateTestKit.UploadAsync(f, HttpFixture.AdminToken, UpdateTestKit.Descriptor("ios", "production", Guid.NewGuid().ToString(), "Adopt", "adopt", parts), parts, ct).ConfigureAwait(false);
                    string groupId = UpdateTestKit.GroupId(created.Body);
                    string uuid = UpdateTestKit.Uuid(created.Body, "ios");
                    await UpdateTestKit.ManifestAsync(f, ct, "ios", "production", "adopter-1").ConfigureAwait(false);
                    await UpdateTestKit.ManifestAsync(f, ct, "ios", "production", "adopter-1", uuid).ConfigureAwait(false);
                    ApiResult noId = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/updates/events", null, new { events = new[] { new { type = "Downloaded", updateId = uuid } } }, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.BadRequest, noId.Status, "Install id required");
                    Dictionary<string, string> headers = new Dictionary<string, string> { ["X-TK-Install-Id"] = "adopter-1" };
                    ApiResult posted = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/updates/events", null, new
                    {
                        events = new object[]
                        {
                            new { type = "Downloaded", updateId = uuid, occurredUtc = DateTime.UtcNow },
                            new { type = "Applied", updateId = uuid, occurredUtc = DateTime.UtcNow, launchDurationMs = 840 },
                            new { type = "DownloadFailed", updateId = uuid, errorCode = "HashMismatch", occurredUtc = DateTime.UtcNow.AddDays(-30) }
                        }
                    }, ct, "mobile", headers).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.Accepted, posted.Status, "202 (" + posted.Text + ")");
                    Check.Equal(2, posted.Body.GetProperty("accepted").GetInt32(), "Accepted");
                    Check.Equal(1, posted.Body.GetProperty("dropped").GetInt32(), "Old event dropped");
                    ApiResult bad = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/updates/events", null, new { events = new[] { new { type = "Nope" } } }, ct, "mobile", headers).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.BadRequest, bad.Status, "Unknown type");
                    JsonElement adoption = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/update-groups/" + groupId + "/adoption?days=7", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement ios = adoption.GetProperty("byPlatform").GetProperty("ios");
                    Check.Equal(1L, ios.GetProperty("onThisUpdate").GetInt64(), "On this update");
                    Check.Equal(1L, ios.GetProperty("downloaded").GetInt64(), "Downloaded");
                    Check.Equal(1L, ios.GetProperty("applied").GetInt64(), "Applied");
                    Check.Equal(7, adoption.GetProperty("objects").GetArrayLength(), "Seven days");
                    Check.True(adoption.GetProperty("objects")[6].GetProperty("checks").GetInt64() >= 1, "Checks today");
                    JsonElement runtimes = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/updates/runtime-versions/enumerate", HttpFixture.AdminToken, new { filters = new { platform = "ios" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement row = runtimes.GetProperty("objects")[0];
                    Check.Equal(UpdateTestKit.Runtime, Api.Str(row, "runtimeVersion"), "Runtime version");
                    Check.Equal("1.2.0", Api.Str(row.GetProperty("binaryVersions").GetProperty("objects")[0], "key"), "Binary version from X-Client-Version");
                    JsonElement byKey = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/updates/signing-keys/installs", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("main", Api.Str(byKey.GetProperty("objects")[0], "key"), "Installs by key id");
                })
            });
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string name, Func<HttpFixture, TestSigningChain, CancellationToken, Task> body, bool keys = true)
        {
            return new TestCaseDescriptor(Id, caseId, name, async ct =>
            {
                TestSigningChain chain = new TestSigningChain();
                HttpFixture fixture = await UpdateTestKit.StartAsync(ct, chain, null, keys).ConfigureAwait(false);
                await using (fixture.ConfigureAwait(false))
                {
                    await body(fixture, chain, ct).ConfigureAwait(false);
                }
            }, new[] { "http", "integration", TestEnvironment.Provider.ToString().ToLowerInvariant() });
        }

        private static TestCaseDescriptor Unit(string caseId, string name, Action body)
        {
            return SuiteCases.Unit(Id, caseId, name, ct =>
            {
                body();
                return Task.CompletedTask;
            });
        }

        #endregion
    }
}
