namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Amazon;
    using Amazon.Runtime;
    using Amazon.S3;
    using Amazon.MediaConvert.Model;
    using Test.Shared.IntegrationTests;
    using Touchstone.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Integrations.Implementations;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Settings;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Offline unit cases for the media integrations: S3 presigning with static credentials, CloudFront cookie
    /// signatures, SNS verification with a self-signed certificate, the MediaConvert job request, local upload
    /// signatures, the MP4 header reader, magic-byte sniffing, blurhash, and part sizing. No network.
    /// </summary>
    public static class MediaUnitSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "MediaUnit";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Media integrations (offline)", new[]
            {
                SuiteCases.Unit(Id, "Media.S3Presign", "S3 presigns single PUTs with Content-Type and part PUTs with partNumber and uploadId; empty buckets are 503", S3PresignAsync),
                SuiteCases.Unit(Id, "Media.CloudFrontCookies", "CloudFront cookies carry a custom policy for the media host, a verifiable RSA-SHA1 signature, and the key pair id", CloudFrontAsync),
                SuiteCases.Unit(Id, "Media.SnsVerification", "SNS messages verify with SignatureVersion 2 and 1; wrong topic, wrong certificate host, http, and tampering fail", SnsAsync),
                SuiteCases.Unit(Id, "Media.MediaConvertJob", "The CreateJob request uses the template, role, queue, clipping, userMetadata, and per-media destinations", MediaConvertAsync),
                SuiteCases.Unit(Id, "Media.LocalUploadSigner", "Local upload signatures are bound to the path and expiry and checked in constant time", LocalSignerAsync),
                SuiteCases.Unit(Id, "Media.Mp4Header", "The mvhd duration (version 0 and 1 timescales) and rotated track size are read from the moov box", Mp4Async),
                SuiteCases.Unit(Id, "Media.SniffAndSizing", "Magic bytes match the declared type; part sizes respect the 1000 part limit; blurhash is 28 characters", SniffAsync)
            });
        }

        #endregion

        #region Private-Methods

        private static MediaSettings Settings()
        {
            return new MediaSettings
            {
                StorageMode = MediaStorageModeEnum.S3,
                SourceBucket = "example-source",
                VideoSourceBucket = "example-video-source",
                ImagesBucket = "example-images",
                TranscodedBucket = "example-transcoded",
                RetainedBucket = "example-retained",
                MediaConvertRoleArn = "arn:aws:iam::123456789012:role/mediaconvert",
                MediaConvertQueueArn = "arn:aws:mediaconvert:us-west-2:123456789012:queues/Default",
                SnsTopicArn = SnsTestSigner.TopicArn
            };
        }

        private static async Task S3PresignAsync(CancellationToken ct)
        {
            MediaSettings settings = Settings();
            using (AmazonS3Client client = new AmazonS3Client(new BasicAWSCredentials("AKIDEXAMPLE", "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY"), RegionEndpoint.USWest2))
            using (S3ObjectStore store = new S3ObjectStore(settings, client))
            {
                UploadTarget target = new UploadTarget { MediaId = "med_x", Location = MediaLocationEnum.Source, Key = "in/med_x/original", ContentType = "image/jpeg", SizeBytes = 100 };
                PresignedUpload single = await store.PresignPutAsync(target, DateTime.UtcNow.AddMinutes(30), ct).ConfigureAwait(false);
                Uri url = new Uri(single.Url);
                Check.True(url.Host.StartsWith("example-source", StringComparison.Ordinal) || url.AbsolutePath.StartsWith("/example-source", StringComparison.Ordinal), "Source bucket (" + single.Url + ")");
                Check.True(url.Host.Contains("us-west-2", StringComparison.Ordinal), "Regional endpoint");
                Check.True(url.AbsolutePath.EndsWith("in/med_x/original", StringComparison.Ordinal), "Key");
                Check.True(single.Url.Contains("X-Amz-Signature=", StringComparison.Ordinal) && single.Url.Contains("X-Amz-Credential=AKIDEXAMPLE", StringComparison.Ordinal), "SigV4 query signature");
                Check.True(single.Url.Contains("content-type", StringComparison.OrdinalIgnoreCase), "Content-Type is a signed header");
                Check.Equal("image/jpeg", single.Headers["Content-Type"], "Client sends the Content-Type");

                target.Location = MediaLocationEnum.VideoSource;
                List<PresignedPart> parts = await store.PresignPartsAsync(target, "upload-123", new[] { 1, 2 }, DateTime.UtcNow.AddMinutes(30), ct).ConfigureAwait(false);
                Check.Equal(2, parts.Count, "Two parts");
                Check.True(parts[1].Url.Contains("partNumber=2", StringComparison.Ordinal) && parts[1].Url.Contains("uploadId=upload-123", StringComparison.Ordinal), "Part query (" + parts[1].Url + ")");
                Check.True(parts[1].Url.Contains("example-video-source", StringComparison.Ordinal), "Video source bucket");

                settings.ImagesBucket = String.Empty;
                IntegrationNotConfiguredException e = Check.Throws<IntegrationNotConfiguredException>(() => store.RequireConfigured(MediaLocationEnum.Images), "Empty bucket");
                Check.True(e.Message.Contains("media.imagesBucket", StringComparison.Ordinal), "Names the setting");
            }
        }

        private static Task CloudFrontAsync(CancellationToken ct)
        {
            using (RSA rsa = RSA.Create(2048))
            {
                MediaSettings settings = Settings();
                settings.PublicBaseUrl = "https://media.example.test";
                settings.CookieDomain = "example.test";
                settings.CloudFrontKeyPairId = "K2JCJMDEHXQW5F";
                settings.CloudFrontPrivateKeyPem = rsa.ExportRSAPrivateKeyPem();
                CloudFrontCookieSigner signer = new CloudFrontCookieSigner(settings, TestProperty.SampleId);
                DateTime expires = new DateTime(2026, 11, 12, 4, 0, 0, DateTimeKind.Utc);
                List<MediaCookie> cookies = signer.CreateCookies("usr_x", expires, false);
                Check.True(cookies.Select(c => c.Name).SequenceEqual(new[] { "CloudFront-Policy", "CloudFront-Signature", "CloudFront-Key-Pair-Id" }), "Cookie names");
                Check.True(cookies.All(c => c.Domain == "example.test" && c.Path == "/" && c.Secure && c.ExpiresUtc == expires), "Attributes");
                byte[] policy = CloudFrontCookieSigner.FromUrlSafeBase64(cookies[0].Value);
                string policyText = Encoding.UTF8.GetString(policy);
                Check.Equal("{\"Statement\":[{\"Resource\":\"https://media.example.test/" + TestProperty.SampleId + "/*\",\"Condition\":{\"DateLessThan\":{\"AWS:EpochTime\":1794456000}}}]}", policyText, "Custom policy");
                Check.True(!cookies[0].Value.Contains('+') && !cookies[0].Value.Contains('/') && !cookies[0].Value.Contains('='), "URL-safe base64");
                byte[] signature = CloudFrontCookieSigner.FromUrlSafeBase64(cookies[1].Value);
                Check.True(rsa.VerifyData(policy, signature, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1), "RSA-SHA1 signature verifies");
                Check.Equal("K2JCJMDEHXQW5F", cookies[2].Value, "Key pair id");
                settings.PublicBaseUrl = String.Empty;
                Check.Throws<IntegrationNotConfiguredException>(() => signer.CreateCookies("usr_x", expires, false), "Base URL required");
                settings.PublicBaseUrl = "https://media.example.test";
                settings.CloudFrontPrivateKeyPem = "not a key";
                Check.Throws<IntegrationNotConfiguredException>(() => signer.CreateCookies("usr_x", expires, false), "Bad PEM");
            }
            return Task.CompletedTask;
        }

        private static async Task SnsAsync(CancellationToken ct)
        {
            MediaSettings settings = Settings();
            using (SnsTestSigner signer = new SnsTestSigner("us-west-2"))
            using (SnsTestSigner wrongHost = new SnsTestSigner("us-west-2", "sns.attacker.example"))
            {
                FakeHttpHandler http = new FakeHttpHandler();
                http.Map("GET", signer.CertificateUrl, r => new FakeHttpResponse { Status = 200, Body = signer.CertificatePem, ContentType = "text/plain" });
                http.Map("GET", wrongHost.CertificateUrl, r => new FakeHttpResponse { Status = 200, Body = wrongHost.CertificatePem, ContentType = "text/plain" });
                SnsMessageVerifier verifier = new SnsMessageVerifier(settings, new HttpClient(http));
                SnsMessage v2 = await verifier.VerifyAsync(signer.Sign("Notification", "{\"detail\":{}}", null), ct).ConfigureAwait(false);
                Check.Equal("Notification", v2.Type, "Version 2 verifies");
                SnsMessage v1 = await verifier.VerifyAsync(signer.Sign("Notification", "hello", null, "1"), ct).ConfigureAwait(false);
                Check.Equal("hello", v1.Message, "Version 1 verifies");
                await verifier.VerifyAsync(signer.Sign("SubscriptionConfirmation", "confirm", "https://sns.us-west-2.amazonaws.com/?Action=ConfirmSubscription&Token=x"), ct).ConfigureAwait(false);
                Check.Equal(1, http.Count(signer.CertificateUrl), "Certificate cached");

                await Check.ThrowsAsync<WebhookSignatureException>(() => verifier.VerifyAsync(signer.Sign("Notification", "x", null, "2", "arn:aws:sns:us-west-2:123456789012:other"), ct), "Wrong topic").ConfigureAwait(false);
                await Check.ThrowsAsync<WebhookSignatureException>(() => verifier.VerifyAsync(wrongHost.Sign("Notification", "x", null), ct), "Wrong certificate host").ConfigureAwait(false);
                Check.Equal(0, http.Count(wrongHost.CertificateUrl), "Never fetches from a foreign host");
                await Check.ThrowsAsync<WebhookSignatureException>(() => verifier.VerifyAsync(signer.Sign("Notification", "x", null).Replace("https://sns.", "http://sns.", StringComparison.Ordinal), ct), "http certificate URL").ConfigureAwait(false);
                await Check.ThrowsAsync<WebhookSignatureException>(() => verifier.VerifyAsync(signer.Sign("Notification", "original", null).Replace("original", "changed", StringComparison.Ordinal), ct), "Tampered").ConfigureAwait(false);
                await Check.ThrowsAsync<WebhookSignatureException>(() => verifier.VerifyAsync(signer.Sign("Notification", "x", null, "3"), ct), "Unknown version").ConfigureAwait(false);
                await Check.ThrowsAsync<WebhookSignatureException>(() => verifier.VerifyAsync("not json", ct), "Not JSON").ConfigureAwait(false);
                using (SnsTestSigner other = new SnsTestSigner("us-west-2"))
                {
                    string forged = other.Sign("Notification", "x", null).Replace(other.CertificateUrl, signer.CertificateUrl, StringComparison.Ordinal);
                    await Check.ThrowsAsync<WebhookSignatureException>(() => verifier.VerifyAsync(forged, ct), "Signed with a different key").ConfigureAwait(false);
                }
                SnsMessage evil = new SnsMessage { SubscribeURL = "https://evil.example/confirm" };
                await Check.ThrowsAsync<WebhookSignatureException>(() => verifier.ConfirmSubscriptionAsync(evil, ct), "SubscribeURL must be SNS").ConfigureAwait(false);
            }
        }

        private static Task MediaConvertAsync(CancellationToken ct)
        {
            MediaSettings settings = Settings();
            using (MediaConvertTranscoder transcoder = new MediaConvertTranscoder(settings))
            {
                CreateJobRequest job = transcoder.BuildCreateJob(new VideoTranscodeRequest { MediaId = "med_v", PropertyId = TestProperty.SampleId, SourceKey = TestProperty.SampleId + "/in/med_v/original", DurationCapSeconds = 300 });
                Check.Equal("trelliskit-hls", job.JobTemplate, "Template");
                Check.Equal(settings.MediaConvertRoleArn, job.Role, "Role");
                Check.Equal(settings.MediaConvertQueueArn, job.Queue, "Queue");
                Check.Equal("med_v", job.UserMetadata["mediaId"], "userMetadata.mediaId");
                Check.Equal("s3://example-video-source/" + TestProperty.SampleId + "/in/med_v/original", job.Settings.Inputs[0].FileInput, "Input");
                Check.Equal("00:05:00:00", job.Settings.Inputs[0].InputClippings[0].EndTimecode, "Clipping at the cap");
                Check.Equal("s3://example-transcoded/" + TestProperty.SampleId + "/video/med_v/master", job.Settings.OutputGroups[0].OutputGroupSettings.HlsGroupSettings.Destination, "HLS destination");
                Check.Equal("s3://example-images/" + TestProperty.SampleId + "/media/med_v/poster", job.Settings.OutputGroups[1].OutputGroupSettings.FileGroupSettings.Destination, "Frame capture destination");
                Check.Equal(2, job.Settings.OutputGroups.Count, "No download group unless media.mediaConvertDownloadRendition");
                settings.MediaConvertDownloadRendition = true;
                CreateJobRequest withDownload = transcoder.BuildCreateJob(new VideoTranscodeRequest { MediaId = "med_v", PropertyId = TestProperty.SampleId, SourceKey = TestProperty.SampleId + "/in/med_v/original", DurationCapSeconds = 300 });
                Check.Equal(3, withDownload.Settings.OutputGroups.Count, "Download output group appended as group 2");
                Check.Equal("s3://example-transcoded/" + TestProperty.SampleId + "/video/med_v/master", withDownload.Settings.OutputGroups[0].OutputGroupSettings.HlsGroupSettings.Destination, "HLS group stays first");
                Check.Equal("s3://example-transcoded/" + TestProperty.SampleId + "/video/med_v/download", withDownload.Settings.OutputGroups[2].OutputGroupSettings.FileGroupSettings.Destination, "Download destination (written as download_720p.mp4)");
                Check.Equal(MediaKeys.DownloadFileName, MediaConvertTranscoder.DownloadBaseName + "_720p.mp4", "Name modifier and extension yield the download file name");
                settings.MediaConvertDownloadRendition = false;
                Check.Equal(VideoJobStateEnum.Complete, MediaConvertTranscoder.MapState("COMPLETE"), "COMPLETE");
                Check.Equal(VideoJobStateEnum.Canceled, MediaConvertTranscoder.MapState("CANCELED"), "CANCELED");
                Check.Equal(VideoJobStateEnum.Running, MediaConvertTranscoder.MapState("PROGRESSING"), "PROGRESSING");
                settings.MediaConvertRoleArn = String.Empty;
                Check.Throws<IntegrationNotConfiguredException>(() => transcoder.BuildCreateJob(new VideoTranscodeRequest { MediaId = "med_v", PropertyId = TestProperty.SampleId, SourceKey = "k" }), "Role required");
            }
            return Task.CompletedTask;
        }

        private static Task LocalSignerAsync(CancellationToken ct)
        {
            string key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            LocalUploadSigner signer = new LocalUploadSigner(() => key);
            string path = LocalUploadSigner.PartPath("med_a", 3);
            Check.Equal("/api/v1/media/local-uploads/med_a/parts/3", path, "Part path");
            DateTime expires = DateTime.UtcNow.AddMinutes(10);
            long exp = new DateTimeOffset(expires).ToUnixTimeSeconds();
            string sig = signer.Sign(path, exp);
            string expected = Convert.ToHexString(HMACSHA256.HashData(HKDF.DeriveKey(HashAlgorithmName.SHA256, Convert.FromBase64String(key), 32, Array.Empty<byte>(), Encoding.UTF8.GetBytes("tk-media-upload")),
                Encoding.UTF8.GetBytes("PUT\n" + path + "\n" + exp))).ToLowerInvariant();
            Check.Equal(expected, sig, "HMAC-SHA256 over PUT, path, exp with the HKDF key");
            string exps = exp.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Check.True(signer.Verify(path, exps, sig, DateTime.UtcNow), "Valid");
            Check.True(!signer.Verify(LocalUploadSigner.PartPath("med_a", 4), exps, sig, DateTime.UtcNow), "Other path");
            Check.True(!signer.Verify(path, (exp + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), sig, DateTime.UtcNow), "Other expiry");
            Check.True(!signer.Verify(path, exps, sig, expires.AddMinutes(1)), "Expired");
            Check.True(!signer.Verify(path, exps, null, DateTime.UtcNow) && !signer.Verify(path, "abc", sig, DateTime.UtcNow), "Missing or malformed");
            string url = signer.BuildUrl("http://127.0.0.1:8000/", path, expires);
            Check.True(url.StartsWith("http://127.0.0.1:8000" + path + "?exp=", StringComparison.Ordinal) && url.EndsWith("&sig=" + signer.Sign(path, exp), StringComparison.Ordinal), "URL shape");
            key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            Check.True(!signer.Verify(path, exps, sig, DateTime.UtcNow), "A rotated key invalidates old URLs");
            return Task.CompletedTask;
        }

        private static Task Mp4Async(CancellationToken ct)
        {
            Mp4Info? info = Mp4HeaderReader.Read(MediaTestKit.Mp4(142.6, 1920, 1080, false, 5000));
            Check.True(info != null, "Parsed");
            Check.Equal(142.6, Math.Round(info!.DurationSeconds, 3), "Duration");
            Check.Equal(1920, info.Width!.Value, "Width");
            Check.Equal(1080, info.Height!.Value, "Height");
            Mp4Info? rotated = Mp4HeaderReader.Read(MediaTestKit.Mp4(3, 1920, 1080, true, 200));
            Check.Equal(1080, rotated!.Width!.Value, "Rotated width");
            Check.True(Mp4HeaderReader.Read(new byte[64]) == null, "No boxes");
            byte[] noMoov = MediaTestKit.Mp4(3, 10, 10, false, 100).Take(24).ToArray();
            Check.True(Mp4HeaderReader.Read(noMoov) == null, "No moov");
            return Task.CompletedTask;
        }

        private static Task SniffAsync(CancellationToken ct)
        {
            Check.True(MediaSniffer.Matches("image/png", MediaTestKit.Png(4, 4)), "PNG");
            Check.True(MediaSniffer.Matches("image/jpeg", MediaTestKit.PlainJpeg(4, 4)), "JPEG");
            Check.True(!MediaSniffer.Matches("image/png", MediaTestKit.PlainJpeg(4, 4)), "JPEG is not PNG");
            Check.True(MediaSniffer.Matches("video/quicktime", MediaTestKit.Mp4(1, 2, 2, false, 100)), "ftyp as QuickTime");
            Check.True(MediaSniffer.Matches("video/mp4", MediaTestKit.Mp4(1, 2, 2, false, 100)), "ftyp as MP4");
            byte[] webp = Encoding.ASCII.GetBytes("RIFF\0\0\0\0WEBPVP8 ");
            Check.True(MediaSniffer.Matches("image/webp", webp), "WebP");
            Check.True(!MediaSniffer.Matches("video/mp4", webp), "WebP is not MP4");
            Check.Equal(16L * 1048576, MediaService.PartSize(100L * 1048576, 16), "Minimum part size");
            long size = 20000L * 1048576;
            long part = MediaService.PartSize(size, 16);
            Check.True(part % 1048576 == 0 && (size + part - 1) / part <= 1000, "Whole MiB and at most 1000 parts");
            byte[] rgba = new byte[8 * 8 * 4];
            for (int i = 0; i < rgba.Length; i += 4) { rgba[i] = (byte)(i % 255); rgba[i + 1] = 80; rgba[i + 2] = 160; rgba[i + 3] = 255; }
            string hash = BlurhashEncoder.Encode(rgba, 8, 8, 4, 3);
            Check.Equal(28, hash.Length, "4x3 blurhash length");
            Check.Equal('L', hash[0], "Size flag for 4x3");
            return Task.CompletedTask;
        }

        #endregion
    }
}
