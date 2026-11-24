namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Security.Cryptography;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.PropertyConfig;
    using TrellisKit.Core.Settings;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Live update test helpers: a fixture whose property folder holds a throwaway certificate chain and whose
    /// deployment settings hold the matching throwaway keys, uploads shaped exactly like
    /// mobile/scripts/publish-update.js, manifest requests with the expo-updates headers, and signature verification
    /// the way expo-updates (certificate-chain mode) does it.
    /// </summary>
    internal static class UpdateTestKit
    {
        internal const string Runtime = "rt-test-fingerprint-0001";

        internal static async Task<HttpFixture> StartAsync(CancellationToken token, TestSigningChain chain, Action<AppSettings>? customize = null, bool keys = true)
        {
            string directory = TestProperty.CopySample();
            string updates = Path.Combine(directory, "mobile", "updates");
            Directory.CreateDirectory(updates);
            await File.WriteAllTextAsync(Path.Combine(updates, "certificate.pem"), chain.MainCertificatePem, token).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(updates, "standby-certificate.pem"), chain.StandbyCertificatePem, token).ConfigureAwait(false);
            return await HttpFixture.StartAsync(token, p => p.Directory = directory, s =>
            {
                if (keys)
                {
                    s.Updates.SigningKeyPem = chain.MainKeyPem;
                    s.Updates.StandbySigningKeyPem = chain.StandbyKeyPem;
                }
                s.Updates.Bucket = String.Empty;
                customize?.Invoke(s);
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// The descriptor fields of one file as the publish script writes them (describeFile).
        /// </summary>
        internal static Dictionary<string, object?> Describe(byte[] bytes, string path, string ext, string contentType)
        {
            string key = Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant();
            return new Dictionary<string, object?>
            {
                ["part"] = key,
                ["path"] = path,
                ["key"] = key,
                ["hash"] = LiveUpdateRules.Base64Url(SHA256.HashData(bytes)),
                ["contentType"] = contentType,
                ["fileExtension"] = "." + ext,
                ["size"] = bytes.Length
            };
        }

        /// <summary>
        /// A descriptor in the publish script's exact shape (buildUpdate), with a launch asset, one image, and one
        /// source map, and the bytes for each part name.
        /// </summary>
        internal static Dictionary<string, object?> Descriptor(string platform, string channel, string groupId, string message, string seed, Dictionary<string, byte[]> parts, decimal rollout = 100, bool critical = false, string? criticalMessage = null)
        {
            byte[] bundle = Encoding.UTF8.GetBytes("var __BUNDLE__ = '" + seed + "-" + platform + "';\n" + new string('x', 70000));
            byte[] image = Encoding.UTF8.GetBytes("PNG-" + seed);
            byte[] map = Encoding.UTF8.GetBytes("{\"version\":3,\"sources\":[\"" + seed + "\"],\"mappings\":\"\"}");
            Dictionary<string, object?> launch = Describe(bundle, "_expo/static/js/" + platform + "/index-" + seed + ".hbc", "hbc", "application/javascript");
            launch["contentType"] = "application/javascript";
            launch["fileExtension"] = ".bundle";
            Dictionary<string, object?> asset = Describe(image, "assets/" + seed, "png", "image/png");
            Dictionary<string, object?> sourceMap = Describe(map, "_expo/static/js/" + platform + "/index-" + seed + ".hbc.map", "map", "application/json");
            sourceMap.Remove("fileExtension");
            sourceMap["part"] = "sourcemap-" + sourceMap["key"];
            sourceMap["bundleKey"] = launch["key"];
            parts[(string)launch["part"]!] = bundle;
            parts[(string)asset["part"]!] = image;
            parts[(string)sourceMap["part"]!] = map;
            return new Dictionary<string, object?>
            {
                ["platform"] = platform,
                ["runtimeVersion"] = Runtime,
                ["channel"] = channel,
                ["message"] = message,
                ["critical"] = critical,
                ["criticalMessage"] = critical ? criticalMessage : null,
                ["rolloutPercentage"] = rollout,
                ["gitCommit"] = "4e1d2c9a7f0b1c2d3e4f5a6b7c8d9e0f1a2b3c4d",
                ["createdUtc"] = DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                ["updateGroupId"] = groupId,
                ["expoConfig"] = new Dictionary<string, object?> { ["name"] = "Example", ["slug"] = "example", ["version"] = "1.0.0", ["extra"] = new Dictionary<string, object?> { ["updates"] = new Dictionary<string, object?> { ["enabled"] = true } } },
                ["launchAsset"] = launch,
                ["assets"] = new List<object?> { asset },
                ["sourceMaps"] = new List<object?> { sourceMap }
            };
        }

        /// <summary>
        /// POST /api/v1/admin/updates exactly as the publish script sends it (FormData: update.json then each file).
        /// </summary>
        internal static async Task<ApiResult> UploadAsync(HttpFixture f, string bearer, Dictionary<string, object?> descriptor, Dictionary<string, byte[]> parts, CancellationToken token, IEnumerable<string>? skip = null)
        {
            MultipartFormDataContent form = new MultipartFormDataContent("----tk-test-" + Guid.NewGuid().ToString("N"));
            ByteArrayContent update = new ByteArrayContent(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(descriptor)));
            update.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            form.Add(update, "update", "update.json");
            HashSet<string> skipped = new HashSet<string>(skip ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            List<Dictionary<string, object?>> files = new List<Dictionary<string, object?>> { (Dictionary<string, object?>)descriptor["launchAsset"]! };
            files.AddRange(((List<object?>)descriptor["assets"]!).Cast<Dictionary<string, object?>>());
            files.AddRange(((List<object?>)descriptor["sourceMaps"]!).Cast<Dictionary<string, object?>>());
            foreach (Dictionary<string, object?> file in files)
            {
                string part = (string)file["part"]!;
                if (skipped.Contains(part)) continue;
                ByteArrayContent content = new ByteArrayContent(parts[part]);
                content.Headers.ContentType = new MediaTypeHeaderValue((string)file["contentType"]!);
                form.Add(content, part, Path.GetFileName((string)file["path"]!));
            }
            string platform = (string)descriptor["platform"]!;
            Dictionary<string, string> headers = new Dictionary<string, string>
            {
                ["X-TK-Property"] = f.Property.Id,
                ["X-TK-App"] = platform == "android" ? f.Property.Mobile.AndroidPackage : f.Property.Mobile.BundleId
            };
            return await Api.SendAsync(f, HttpMethod.Post, "/api/v1/admin/updates", bearer, null, token, "mobile", headers, form).ConfigureAwait(false);
        }

        internal static async Task<TestManifestResponse> ManifestAsync(HttpFixture f, CancellationToken token, string platform = "ios", string channel = "production", string? installId = null,
            string? currentId = null, string? embeddedId = null, string? expectKeyId = "main", string? failed = null, int protocol = 1, string runtime = Runtime)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/updates/manifest"))
            {
                if (protocol > 0) request.Headers.TryAddWithoutValidation("expo-protocol-version", protocol.ToString(System.Globalization.CultureInfo.InvariantCulture));
                request.Headers.TryAddWithoutValidation("expo-platform", platform);
                request.Headers.TryAddWithoutValidation("expo-runtime-version", runtime);
                request.Headers.TryAddWithoutValidation("expo-channel-name", channel);
                request.Headers.TryAddWithoutValidation("expo-sfv-version", "0");
                request.Headers.TryAddWithoutValidation("accept", protocol > 0 ? "multipart/mixed,application/expo+json,application/json" : "application/expo+json");
                if (currentId != null) request.Headers.TryAddWithoutValidation("expo-current-update-id", currentId);
                if (embeddedId != null) request.Headers.TryAddWithoutValidation("expo-embedded-update-id", embeddedId);
                if (expectKeyId != null) request.Headers.TryAddWithoutValidation("expo-expect-signature", "sig, keyid=\"" + expectKeyId + "\", alg=\"rsa-v1_5-sha256\"");
                if (failed != null) request.Headers.TryAddWithoutValidation("expo-recent-failed-update-ids", failed);
                request.Headers.TryAddWithoutValidation("X-TK-Property", f.Property.Id);
                request.Headers.TryAddWithoutValidation("X-TK-App", platform == "android" ? f.Property.Mobile.AndroidPackage : f.Property.Mobile.BundleId);
                request.Headers.TryAddWithoutValidation("X-TK-Install-Id", installId ?? String.Empty);
                request.Headers.TryAddWithoutValidation("X-Client-Version", "1.2.0");
                using (HttpResponseMessage response = await f.Client.SendAsync(request, token).ConfigureAwait(false))
                {
                    byte[] body = await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
                    TestManifestResponse result = new TestManifestResponse
                    {
                        Status = response.StatusCode,
                        ContentType = response.Content.Headers.ContentType?.ToString() ?? String.Empty,
                        Text = Encoding.UTF8.GetString(body)
                    };
                    foreach (KeyValuePair<string, IEnumerable<string>> h in response.Headers) result.Headers[h.Key] = String.Join(",", h.Value);
                    foreach (KeyValuePair<string, IEnumerable<string>> h in response.Content.Headers) result.Headers[h.Key] = String.Join(",", h.Value);
                    string? boundary = response.Content.Headers.ContentType?.Parameters.FirstOrDefault(p => p.Name == "boundary")?.Value?.Trim('"');
                    if (boundary != null) Parse(body, boundary, result.Parts);
                    return result;
                }
            }
        }

        /// <summary>
        /// Verify a part signature the way expo-updates does with the certificate chain option on: the chain (leaf
        /// first) plus the embedded certificate must link, the root must be self-signed and a CA when there is a chain,
        /// the leaf must allow code signing, and the leaf key must verify the exact part bytes.
        /// </summary>
        internal static bool VerifyLikeExpo(string embeddedPem, string? chainPem, byte[] data, string signatureHeader)
        {
            Dictionary<string, string> fields = StructuredFields.ParseDictionary(signatureHeader);
            if (!fields.TryGetValue("sig", out string? sig) || fields.GetValueOrDefault("alg") != "rsa-v1_5-sha256") return false;
            List<X509Certificate2> chain = new List<X509Certificate2>();
            if (!String.IsNullOrEmpty(chainPem))
            {
                X509Certificate2Collection collection = new X509Certificate2Collection();
                collection.ImportFromPem(chainPem);
                chain.AddRange(collection.Cast<X509Certificate2>());
            }
            X509Certificate2 embedded = X509Certificate2.CreateFromPem(embeddedPem);
            chain.Add(embedded);
            if (!embedded.SubjectName.RawData.AsSpan().SequenceEqual(embedded.IssuerName.RawData)) return false;
            for (int i = 0; i < chain.Count - 1; i++)
            {
                if (!UpdateSigningService.IsAuthority(chain[i + 1])) return false;
                using (X509Chain link = new X509Chain())
                {
                    link.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                    link.ChainPolicy.CustomTrustStore.Add(chain[i + 1]);
                    link.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                    if (!link.Build(chain[i]) || link.ChainElements.Count != 2) return false;
                }
            }
            X509Certificate2 leaf = chain[0];
            if (!UpdateSigningService.AllowsCodeSigning(leaf)) return false;
            using (RSA? key = leaf.GetRSAPublicKey())
            {
                return key != null && key.VerifyData(data, Convert.FromBase64String(sig), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
        }

        internal static void Parse(byte[] body, string boundary, List<TestManifestPart> parts)
        {
            string text = Encoding.UTF8.GetString(body);
            string delimiter = "--" + boundary;
            foreach (string chunk in text.Split(delimiter).Skip(1))
            {
                if (chunk.StartsWith("--", StringComparison.Ordinal)) break;
                string section = chunk.StartsWith("\r\n", StringComparison.Ordinal) ? chunk.Substring(2) : chunk;
                int split = section.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                if (split < 0) continue;
                TestManifestPart part = new TestManifestPart();
                foreach (string line in section.Substring(0, split).Split("\r\n"))
                {
                    int colon = line.IndexOf(':');
                    if (colon > 0) part.Headers[line.Substring(0, colon).Trim()] = line.Substring(colon + 1).Trim();
                }
                string content = section.Substring(split + 4);
                if (content.EndsWith("\r\n", StringComparison.Ordinal)) content = content.Substring(0, content.Length - 2);
                part.Body = Encoding.UTF8.GetBytes(content);
                string disposition = part.Headers.GetValueOrDefault("content-disposition") ?? String.Empty;
                int name = disposition.IndexOf("name=\"", StringComparison.Ordinal);
                if (name >= 0) part.Name = disposition.Substring(name + 6, disposition.IndexOf('"', name + 6) - name - 6);
                parts.Add(part);
            }
        }

        internal static string GroupId(JsonElement group)
        {
            return group.GetProperty("id").GetString()!;
        }

        internal static string Uuid(JsonElement group, string platform)
        {
            foreach (JsonElement update in group.GetProperty("updates").EnumerateArray())
                if (update.GetProperty("platform").GetString() == platform) return update.GetProperty("uuid").GetString()!;
            throw new InvalidOperationException("No " + platform + " update in the group.");
        }

        internal static string InstallIn(string groupId, int basisPoints, bool inside)
        {
            for (int i = 0; i < 100000; i++)
            {
                string id = "install-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if ((LiveUpdateRules.Bucket(groupId, id) < basisPoints) == inside) return id;
            }
            throw new InvalidOperationException("No install found.");
        }
    }
}
