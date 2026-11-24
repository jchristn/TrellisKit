namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using SkiaSharp;
    using TrellisKit.Server.Workers;

    /// <summary>
    /// Fixtures and HTTP helpers for the media suites: images drawn with SkiaSharp, JPEGs with an injected EXIF block,
    /// minimal ISO BMFF files (ftyp, moov with mvhd and tkhd, mdat), raw requests, and upload flows.
    /// </summary>
    internal static class MediaTestKit
    {
        internal const string ExifMarker = "TK-GPS-37.7749N-122.4194W";

        internal static byte[] Png(int width, int height)
        {
            return Draw(width, height, SKEncodedImageFormat.Png, 100);
        }

        internal static byte[] PlainJpeg(int width, int height)
        {
            return Draw(width, height, SKEncodedImageFormat.Jpeg, 90);
        }

        /// <summary>
        /// A JPEG whose left half is red and right half blue, with an APP1 EXIF block carrying the orientation and a
        /// marker string (standing in for GPS data) that must not survive processing.
        /// </summary>
        internal static byte[] ExifJpeg(int width, int height, int orientation)
        {
            byte[] jpeg = Draw(width, height, SKEncodedImageFormat.Jpeg, 90);
            byte[] marker = Encoding.ASCII.GetBytes(ExifMarker + "\0");
            List<byte> tiff = new List<byte>();
            tiff.AddRange(new byte[] { (byte)'I', (byte)'I', 42, 0, 8, 0, 0, 0 });
            tiff.AddRange(new byte[] { 2, 0 });
            tiff.AddRange(new byte[] { 0x12, 0x01, 3, 0, 1, 0, 0, 0, (byte)orientation, 0, 0, 0 });
            int dataOffset = 8 + 2 + 24 + 4;
            tiff.AddRange(new byte[] { 0x0E, 0x01, 2, 0 });
            tiff.AddRange(BitConverter.GetBytes(marker.Length));
            tiff.AddRange(BitConverter.GetBytes(dataOffset));
            tiff.AddRange(new byte[] { 0, 0, 0, 0 });
            tiff.AddRange(marker);
            List<byte> app1 = new List<byte> { (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0 };
            app1.AddRange(tiff);
            int length = app1.Count + 2;
            List<byte> output = new List<byte> { 0xFF, 0xD8, 0xFF, 0xE1, (byte)(length >> 8), (byte)(length & 0xFF) };
            output.AddRange(app1);
            output.AddRange(jpeg.Skip(2));
            return output.ToArray();
        }

        internal static SKColor PixelAt(byte[] encoded, double x, double y)
        {
            using (SKBitmap bitmap = SKBitmap.Decode(encoded))
            {
                return bitmap.GetPixel((int)(x * (bitmap.Width - 1)), (int)(y * (bitmap.Height - 1)));
            }
        }

        internal static bool Contains(byte[] haystack, byte[] needle)
        {
            for (int i = 0; i + needle.Length <= haystack.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length && match; j++) match = haystack[i + j] == needle[j];
                if (match) return true;
            }
            return false;
        }

        /// <summary>
        /// A minimal MP4: ftyp, moov (mvhd with the duration, trak/tkhd with the size and optional 90 degree rotation),
        /// then an mdat filling the file to totalSize.
        /// </summary>
        internal static byte[] Mp4(double durationSeconds, int width, int height, bool rotated, long totalSize)
        {
            List<byte> head = new List<byte>();
            Box(head, "ftyp", Concat(Encoding.ASCII.GetBytes("isom"), U32(512), Encoding.ASCII.GetBytes("isommp41")));
            List<byte> mvhd = new List<byte>();
            mvhd.AddRange(U32(0));
            mvhd.AddRange(U32(0));
            mvhd.AddRange(U32(0));
            mvhd.AddRange(U32(1000));
            mvhd.AddRange(U32((uint)Math.Round(durationSeconds * 1000)));
            mvhd.AddRange(U32(0x00010000));
            mvhd.AddRange(new byte[] { 1, 0 });
            mvhd.AddRange(new byte[10]);
            mvhd.AddRange(Matrix(false));
            mvhd.AddRange(new byte[24]);
            mvhd.AddRange(U32(2));
            List<byte> tkhd = new List<byte>();
            tkhd.AddRange(U32(3));
            tkhd.AddRange(U32(0));
            tkhd.AddRange(U32(0));
            tkhd.AddRange(U32(1));
            tkhd.AddRange(U32(0));
            tkhd.AddRange(U32((uint)Math.Round(durationSeconds * 1000)));
            tkhd.AddRange(new byte[8]);
            tkhd.AddRange(new byte[8]);
            tkhd.AddRange(Matrix(rotated));
            tkhd.AddRange(U32((uint)width << 16));
            tkhd.AddRange(U32((uint)height << 16));
            List<byte> trak = new List<byte>();
            Box(trak, "tkhd", tkhd.ToArray());
            List<byte> moov = new List<byte>();
            Box(moov, "mvhd", mvhd.ToArray());
            Box(moov, "trak", trak.ToArray());
            Box(head, "moov", moov.ToArray());
            long mdat = Math.Max(8, totalSize - head.Count);
            byte[] file = new byte[head.Count + mdat];
            head.CopyTo(file);
            byte[] size = U32((uint)mdat);
            Array.Copy(size, 0, file, head.Count, 4);
            Array.Copy(Encoding.ASCII.GetBytes("mdat"), 0, file, head.Count + 4, 4);
            return file;
        }

        internal static async Task<MediaHttpResult> SendAsync(HttpFixture f, HttpMethod method, string url, string? bearer, HttpContent? content, CancellationToken token, Dictionary<string, string>? headers = null)
        {
            Uri target = url.StartsWith("http", StringComparison.Ordinal) ? new Uri(url) : new Uri(new Uri(f.BaseUrl), url);
            using (HttpClient client = new HttpClient(new SocketsHttpHandler { UseCookies = false }) { Timeout = TimeSpan.FromSeconds(60) })
            using (HttpRequestMessage request = new HttpRequestMessage(method, target))
            {
                if (bearer != null) request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + bearer);
                if (headers != null) foreach (KeyValuePair<string, string> h in headers) request.Headers.TryAddWithoutValidation(h.Key, h.Value);
                f.Decorate(request);
                if (content != null) request.Content = content;
                using (HttpResponseMessage response = await client.SendAsync(request, token).ConfigureAwait(false))
                {
                    MediaHttpResult result = new MediaHttpResult { Status = response.StatusCode, Bytes = await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false) };
                    foreach (KeyValuePair<string, IEnumerable<string>> h in response.Headers.Concat(response.Content.Headers))
                    {
                        if (!result.Headers.TryGetValue(h.Key, out List<string>? list))
                        {
                            list = new List<string>();
                            result.Headers[h.Key] = list;
                        }
                        list.AddRange(h.Value);
                    }
                    return result;
                }
            }
        }

        internal static async Task<MediaHttpResult> JsonAsync(HttpFixture f, HttpMethod method, string path, string? bearer, object? body, CancellationToken token)
        {
            HttpContent? content = body == null ? null : new StringContent(body is string s ? s : JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            return await SendAsync(f, method, path, bearer, content, token, new Dictionary<string, string> { { "X-Client", "mobile" } }).ConfigureAwait(false);
        }

        internal static async Task<MediaHttpResult> PutBytesAsync(HttpFixture f, string url, byte[] data, string? contentType, CancellationToken token)
        {
            ByteArrayContent content = new ByteArrayContent(data);
            if (contentType != null) content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
            return await SendAsync(f, HttpMethod.Put, url, null, content, token).ConfigureAwait(false);
        }

        internal static async Task<JsonElement> CreateSessionAsync(HttpFixture f, string bearer, string kind, string contentType, long size, double? duration, CancellationToken token)
        {
            MediaHttpResult result = await JsonAsync(f, HttpMethod.Post, "/api/v1/media/uploads", bearer, new { kind, contentType, sizeBytes = size, fileName = "upload.bin", durationSeconds = duration }, token).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Created, result.Status, "Create session (" + result.Text + ")");
            return result.Json;
        }

        internal static async Task<string> UploadSingleAsync(HttpFixture f, string bearer, string kind, string contentType, byte[] data, CancellationToken token, double? duration = null, bool complete = true)
        {
            JsonElement session = await CreateSessionAsync(f, bearer, kind, contentType, data.Length, duration, token).ConfigureAwait(false);
            string id = session.GetProperty("media").GetProperty("id").GetString()!;
            string url = session.GetProperty("upload").GetProperty("single").GetProperty("url").GetString()!;
            MediaHttpResult put = await PutBytesAsync(f, url, data, contentType, token).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, put.Status, "Single PUT (" + put.Text + ")");
            if (complete)
            {
                MediaHttpResult done = await JsonAsync(f, HttpMethod.Post, "/api/v1/media/" + id + "/complete", bearer, new { }, token).ConfigureAwait(false);
                Check.Equal(HttpStatusCode.Accepted, done.Status, "Complete (" + done.Text + ")");
            }
            return id;
        }

        internal static Task<int> ProcessAsync(HttpFixture f, CancellationToken token)
        {
            MediaProcessingWorker worker = new MediaProcessingWorker(f.Database, f.Services.Media, f.Settings.Media, f.Services.Logger);
            return worker.RunOnceAsync(token);
        }

        internal static async Task<JsonElement> GetMediaAsync(HttpFixture f, string bearer, string id, CancellationToken token)
        {
            MediaHttpResult result = await JsonAsync(f, HttpMethod.Get, "/api/v1/media/" + id, bearer, null, token).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, result.Status, "GET media (" + result.Text + ")");
            return result.Json;
        }

        internal static string LocalPath(HttpFixture f, string folder, string key)
        {
            string scoped = key.StartsWith(f.Property.Id + "/", StringComparison.Ordinal) ? key : f.Property.Id + "/" + key;
            return Path.Combine(Path.GetFullPath(f.Settings.Storage.Directory), folder, scoped.Replace('/', Path.DirectorySeparatorChar));
        }

        private static byte[] Draw(int width, int height, SKEncodedImageFormat format, int quality)
        {
            using (SKBitmap bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul)))
            {
                using (SKCanvas canvas = new SKCanvas(bitmap))
                using (SKPaint red = new SKPaint { Color = new SKColor(255, 0, 0) })
                using (SKPaint blue = new SKPaint { Color = new SKColor(0, 0, 255) })
                {
                    canvas.DrawRect(new SKRect(0, 0, width / 2f, height), red);
                    canvas.DrawRect(new SKRect(width / 2f, 0, width, height), blue);
                }
                using (SKImage image = SKImage.FromBitmap(bitmap))
                using (SKData data = image.Encode(format, quality))
                {
                    return data.ToArray();
                }
            }
        }

        private static void Box(List<byte> target, string type, byte[] payload)
        {
            target.AddRange(U32((uint)(payload.Length + 8)));
            target.AddRange(Encoding.ASCII.GetBytes(type));
            target.AddRange(payload);
        }

        private static byte[] Matrix(bool rotated)
        {
            List<byte> m = new List<byte>();
            if (rotated)
            {
                m.AddRange(U32(0)); m.AddRange(U32(0x00010000)); m.AddRange(U32(0));
                m.AddRange(U32(0xFFFF0000)); m.AddRange(U32(0)); m.AddRange(U32(0));
            }
            else
            {
                m.AddRange(U32(0x00010000)); m.AddRange(U32(0)); m.AddRange(U32(0));
                m.AddRange(U32(0)); m.AddRange(U32(0x00010000)); m.AddRange(U32(0));
            }
            m.AddRange(U32(0)); m.AddRange(U32(0)); m.AddRange(U32(0x40000000));
            return m.ToArray();
        }

        private static byte[] U32(uint value)
        {
            return new byte[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value };
        }

        private static byte[] Concat(params byte[][] parts)
        {
            return parts.SelectMany(p => p).ToArray();
        }
    }
}
