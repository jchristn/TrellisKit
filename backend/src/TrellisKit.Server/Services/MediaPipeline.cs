namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Integrations;
    using TrellisKit.Core.Integrations.Implementations;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Media processing (contract section 4.6), shared by the media worker and the MediaConvert webhook. Each job runs
    /// under a "media.process &lt;kind&gt;" span with "stage:&lt;name&gt;" children and records
    /// trelliskit.media.stage.duration per stage (queued, verify, decode, resize, encode, store, transcode_submit,
    /// transcode_wait, transcode, poster, cleanup) and trelliskit.media.processing per outcome. Unexpected errors are
    /// retried up to <see cref="MaxAttempts"/> times (attempts column) with a growing delay; failures that retrying
    /// cannot fix (<see cref="MediaProcessingException"/>) fail the media at once and delete partial outputs.
    /// Thread safety: safe to call concurrently for different media items.
    /// </summary>
    public class MediaPipeline
    {
        #region Public-Members

        /// <summary>
        /// Attempts before a media item fails with ProcessingError. Default 3. Minimum 1, maximum 20.
        /// </summary>
        public int MaxAttempts
        {
            get { return _MaxAttempts; }
            set { _MaxAttempts = Math.Clamp(value, 1, 20); }
        }

        /// <summary>
        /// How long a submitted MediaConvert job waits for its webhook before the worker polls GetJob, in minutes.
        /// Default 15. Minimum 1, maximum 1440.
        /// </summary>
        public int WebhookGraceMinutes
        {
            get { return _WebhookGraceMinutes; }
            set { _WebhookGraceMinutes = Math.Clamp(value, 1, 1440); }
        }

        /// <summary>
        /// Delay between GetJob polls of a running MediaConvert job, in minutes. Default 5. Minimum 1, maximum 60.
        /// </summary>
        public int PollMinutes
        {
            get { return _PollMinutes; }
            set { _PollMinutes = Math.Clamp(value, 1, 60); }
        }

        /// <summary>
        /// Invoked after a media item becomes Ready or Failed. Set by <see cref="MediaService"/>.
        /// </summary>
        public Func<MediaAttachment, CancellationToken, Task>? Settled { get; set; } = null;

        /// <summary>
        /// Whether a video also gets the progressive MP4 download rendition (set by the library module: downloads are
        /// enabled and the video belongs to library staff or a lesson). Null means never.
        /// </summary>
        public Func<MediaAttachment, CancellationToken, Task<bool>>? WantsDownloadRendition { get; set; } = null;

        #endregion

        #region Private-Members

        private const int _WebpQuality = 82;
        private const int _JpegQuality = 85;
        private readonly AppSettings _Settings;
        private readonly DatabaseDriverBase _Database;
        private readonly IntegrationSet _Integrations;
        private readonly MediaStorageService _Storage;
        private readonly AppLogger _Logger;
        private readonly string _PropertyId;
        private int _MaxAttempts = 3;
        private int _WebhookGraceMinutes = 15;
        private int _PollMinutes = 5;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the pipeline.
        /// </summary>
        /// <param name="settings">Running settings.</param>
        /// <param name="database">Database.</param>
        /// <param name="integrations">Integrations (store, image processor, and video processor are read on each job).</param>
        /// <param name="storage">Media storage operations.</param>
        /// <param name="propertyId">Property id: the first segment of every object key.</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public MediaPipeline(AppSettings settings, DatabaseDriverBase database, IntegrationSet integrations, MediaStorageService storage, string propertyId, AppLogger logger)
        {
            _PropertyId = propertyId ?? throw new ArgumentNullException(nameof(propertyId));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Integrations = integrations ?? throw new ArgumentNullException(nameof(integrations));
            _Storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Process one claimed Processing item (image, synchronous video, MediaConvert submit, or MediaConvert poll).
        /// Never throws for media errors; they are recorded on the media.
        /// </summary>
        /// <param name="media">Claimed media.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The media afterwards.</returns>
        /// <exception cref="ArgumentNullException">The media is null.</exception>
        /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
        public async Task<MediaAttachment> ProcessAsync(MediaAttachment media, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(media);
            string kind = KindLabel(media.Kind);
            using (Activity? root = TrellisKitTelemetry.StartSpan("media.process " + kind))
            {
                root?.SetTag("media.kind", kind);
                root?.SetTag("media.id", media.Id);
                DateTime now = DateTime.UtcNow;
                if (media.ProcessorJobId == null)
                {
                    Stage(kind, "queued", Math.Max(0, (now - media.UpdatedUtc).TotalSeconds));
                    media.Attempts++;
                    media.ProcessingStartedUtc ??= now;
                    await _Database.Media.UpdateAsync(media, MediaStatusEnum.Processing, token).ConfigureAwait(false);
                }
                try
                {
                    if (media.Kind == MediaKindEnum.Image) await ProcessImageAsync(media, kind, token).ConfigureAwait(false);
                    else if (media.Kind == MediaKindEnum.Document) await ProcessDocumentAsync(media, kind, token).ConfigureAwait(false);
                    else await ProcessVideoAsync(media, kind, token).ConfigureAwait(false);
                    root?.SetStatus(ActivityStatusCode.Ok);
                }
                catch (MediaProcessingException e)
                {
                    root?.SetStatus(ActivityStatusCode.Error, e.Reason.ToString());
                    await FailAsync(media, e.Reason, e.Message, token).ConfigureAwait(false);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    root?.SetStatus(ActivityStatusCode.Error, e.GetType().Name);
                    if (media.Attempts >= _MaxAttempts)
                    {
                        _Logger.Error("[MediaPipeline] " + media.Id + " failed after " + media.Attempts + " attempts", e);
                        await FailAsync(media, MediaFailureReasonEnum.ProcessingError, e.Message, token).ConfigureAwait(false);
                    }
                    else
                    {
                        _Logger.Warn("[MediaPipeline] " + media.Id + " attempt " + media.Attempts + " failed, retrying: " + e.Message);
                        TrellisKitTelemetry.Add(TrellisKitTelemetry.MediaProcessing, 1, "kind", kind, "processor", media.Processor ?? ProcessorLabel(media), "outcome", "retry", "failure_reason", "none");
                        media.ClaimedBy = null;
                        media.ClaimUntilUtc = DateTime.UtcNow.AddSeconds(30 * media.Attempts);
                        media.UpdatedUtc = DateTime.UtcNow;
                        await _Database.Media.UpdateAsync(media, MediaStatusEnum.Processing, token).ConfigureAwait(false);
                    }
                }
                return media;
            }
        }

        /// <summary>
        /// Apply a MediaConvert job state change (from the webhook). Idempotent: anything but a Processing item with
        /// that job id is ignored.
        /// </summary>
        /// <param name="jobId">Job identifier.</param>
        /// <param name="mediaId">userMetadata.mediaId, or null.</param>
        /// <param name="state">New state.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the media changed.</returns>
        public async Task<bool> HandleJobStateAsync(string jobId, string? mediaId, VideoJobStateEnum state, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(jobId) || state == VideoJobStateEnum.Running) return false;
            MediaAttachment? media = await _Database.Media.ReadByJobIdAsync(jobId, token).ConfigureAwait(false);
            if (media == null && !String.IsNullOrEmpty(mediaId))
            {
                MediaAttachment? byId = await _Database.Media.ReadAsync(mediaId, token).ConfigureAwait(false);
                if (byId != null && byId.ProcessorJobId == jobId) media = byId;
            }
            if (media == null || media.IsDeleted || media.Status != MediaStatusEnum.Processing) return false;
            string kind = KindLabel(media.Kind);
            using (Activity? root = TrellisKitTelemetry.StartSpan("media.process " + kind))
            {
                root?.SetTag("media.kind", kind);
                root?.SetTag("media.id", media.Id);
                if (state == VideoJobStateEnum.Complete)
                {
                    VideoJobStatus status = new VideoJobStatus { State = VideoJobStateEnum.Complete };
                    try
                    {
                        status = await _Integrations.VideoTranscoder.GetJobStatusAsync(jobId, token).ConfigureAwait(false);
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        _Logger.Warn("[MediaPipeline] GetJob for " + jobId + " failed; using the poster size: " + e.Message);
                    }
                    try
                    {
                        await FinishAsyncJobAsync(media, kind, status, token).ConfigureAwait(false);
                    }
                    catch (MediaProcessingException e)
                    {
                        await FailAsync(media, e.Reason, e.Message, token).ConfigureAwait(false);
                    }
                }
                else
                {
                    await FailAsync(media, MediaFailureReasonEnum.ProcessingError, "The transcode job ended with " + state + ".", token).ConfigureAwait(false);
                }
                return true;
            }
        }

        #endregion

        #region Private-Methods

        private static string KindLabel(MediaKindEnum kind)
        {
            return kind == MediaKindEnum.Image ? "image" : kind == MediaKindEnum.Document ? "document" : "video";
        }

        private string ProcessorLabel(MediaAttachment media)
        {
            if (media.Kind == MediaKindEnum.Image) return "skia";
            if (media.Kind == MediaKindEnum.Document) return "document";
            return _Integrations.VideoTranscoder.Label;
        }

        private static void Stage(string kind, string stage, double seconds)
        {
            TrellisKitTelemetry.Record(TrellisKitTelemetry.MediaStageDuration, seconds, "kind", kind, "stage", stage);
        }

        private static async Task<T> StageAsync<T>(string kind, string stage, Func<Task<T>> body)
        {
            using (Activity? activity = TrellisKitTelemetry.StartSpan("stage:" + stage))
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                try
                {
                    T result = await body().ConfigureAwait(false);
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    return result;
                }
                catch (Exception e)
                {
                    activity?.SetStatus(ActivityStatusCode.Error, e.GetType().Name);
                    throw;
                }
                finally
                {
                    Stage(kind, stage, stopwatch.Elapsed.TotalSeconds);
                }
            }
        }

        private static T StageSync<T>(string kind, string stage, Func<T> body)
        {
            return StageAsync(kind, stage, () => Task.FromResult(body())).GetAwaiter().GetResult();
        }

        private async Task ProcessImageAsync(MediaAttachment media, string kind, CancellationToken token)
        {
            IObjectStore store = _Integrations.ObjectStore;
            IImageProcessor processor = _Integrations.ImageProcessor;
            media.Processor = "skia";
            byte[] original = await StageAsync(kind, "verify", async () =>
            {
                try
                {
                    return await store.ReadAllAsync(media.SourceLocation, media.SourceKey, token).ConfigureAwait(false);
                }
                catch (FileNotFoundException e)
                {
                    throw new MediaProcessingException(MediaFailureReasonEnum.ProcessingError, "The original is missing.", e);
                }
            }).ConfigureAwait(false);

            List<MediaVariant> variants = new List<MediaVariant>();
            List<EncodedImage> outputs = new List<EncodedImage>();
            string? blurhash = null;
            int width;
            int height;
            using (IDecodedImage decoded = StageSync(kind, "decode", () => processor.Decode(original, _Settings.Media.MaxImagePixels)))
            {
                width = decoded.Width;
                height = decoded.Height;
                foreach (KeyValuePair<string, int> size in MediaUrlBuilder.VariantSizes)
                {
                    using (IDecodedImage resized = StageSync(kind, "resize", () => processor.Resize(decoded, size.Value)))
                    {
                        EncodedImage webp = StageSync(kind, "encode", () => processor.EncodeWebp(resized, _WebpQuality));
                        EncodedImage jpeg = StageSync(kind, "encode", () => processor.EncodeJpeg(resized, _JpegQuality));
                        if (blurhash == null) blurhash = StageSync(kind, "encode", () => processor.Blurhash(resized));
                        outputs.Add(webp);
                        outputs.Add(jpeg);
                        variants.Add(new MediaVariant { MediaId = media.Id, Name = size.Key, Format = "webp", Width = webp.Width, Height = webp.Height, SizeBytes = webp.Data.Length, FileName = size.Key + ".webp" });
                        variants.Add(new MediaVariant { MediaId = media.Id, Name = size.Key, Format = "jpeg", Width = jpeg.Width, Height = jpeg.Height, SizeBytes = jpeg.Data.Length, FileName = size.Key + ".jpg" });
                    }
                }
            }

            await StageAsync(kind, "store", async () =>
            {
                string prefix = MediaKeys.ImagePrefix(_PropertyId, media.Id);
                for (int i = 0; i < variants.Count; i++)
                {
                    await store.PutAsync(MediaLocationEnum.Images, prefix + variants[i].FileName, outputs[i].Data, outputs[i].ContentType, token).ConfigureAwait(false);
                }
                DateTime now = DateTime.UtcNow;
                media.Width = width;
                media.Height = height;
                media.Blurhash = blurhash;
                media.Status = MediaStatusEnum.Ready;
                media.FailureReason = null;
                media.OutputPrefix = prefix;
                media.ReadyUtc = now;
                media.UpdatedUtc = now;
                media.ClaimedBy = null;
                media.ClaimUntilUtc = null;
                bool written = await _Database.Media.CompleteProcessingAsync(media, variants, token).ConfigureAwait(false);
                return written;
            }).ConfigureAwait(false);
            await SettledAsync(media, kind, "ready", "none", token).ConfigureAwait(false);
        }

        private async Task ProcessDocumentAsync(MediaAttachment media, string kind, CancellationToken token)
        {
            // Document media (docs/PHASE4_CONTRACT.md section 5.3): validated and served unchanged from the images
            // location as media/{id}/file.pdf or file.vtt (PDF metadata is not stripped).
            IObjectStore store = _Integrations.ObjectStore;
            media.Processor = "document";
            byte[] original = await StageAsync(kind, "verify", async () =>
            {
                try
                {
                    return await store.ReadAllAsync(media.SourceLocation, media.SourceKey, token).ConfigureAwait(false);
                }
                catch (FileNotFoundException e)
                {
                    throw new MediaProcessingException(MediaFailureReasonEnum.ProcessingError, "The original is missing.", e);
                }
            }).ConfigureAwait(false);
            if (original.Length > _Settings.Media.MaxDocumentBytes) throw new MediaProcessingException(MediaFailureReasonEnum.UnsupportedFormat, "The document is larger than media.maxDocumentBytes.");
            string type = (media.ContentType ?? String.Empty).ToLowerInvariant();
            string file;
            if (type == "application/pdf")
            {
                if (MediaSniffer.Detect(original) != "application/pdf") throw new MediaProcessingException(MediaFailureReasonEnum.UnsupportedFormat, "The file does not start with %PDF-.");
                file = "file.pdf";
            }
            else if (type == "text/vtt")
            {
                string text;
                try
                {
                    text = new System.Text.UTF8Encoding(false, true).GetString(original);
                }
                catch (System.Text.DecoderFallbackException e)
                {
                    throw new MediaProcessingException(MediaFailureReasonEnum.UnsupportedFormat, "A WebVTT file must be UTF-8 text.", e);
                }
                if (!WebVttParser.Validate(text, out string? error)) throw new MediaProcessingException(MediaFailureReasonEnum.UnsupportedFormat, error ?? "Not a WebVTT file.");
                file = "file.vtt";
            }
            else
            {
                throw new MediaProcessingException(MediaFailureReasonEnum.UnsupportedFormat, "Unsupported document type " + type + ".");
            }
            await StageAsync(kind, "store", async () =>
            {
                string prefix = MediaKeys.ImagePrefix(_PropertyId, media.Id);
                await store.PutAsync(MediaLocationEnum.Images, prefix + file, original, MediaKeys.ContentTypeFor(file), token).ConfigureAwait(false);
                DateTime now = DateTime.UtcNow;
                media.Status = MediaStatusEnum.Ready;
                media.FailureReason = null;
                media.OutputPrefix = prefix;
                media.ReadyUtc = now;
                media.UpdatedUtc = now;
                media.ClaimedBy = null;
                media.ClaimUntilUtc = null;
                List<MediaVariant> variants = new List<MediaVariant> { new MediaVariant { MediaId = media.Id, Name = MediaUrlBuilder.DocumentVariantName, Format = file.Substring(5), SizeBytes = original.Length, FileName = file } };
                return await _Database.Media.CompleteProcessingAsync(media, variants, token).ConfigureAwait(false);
            }).ConfigureAwait(false);
            await SettledAsync(media, kind, "ready", "none", token).ConfigureAwait(false);
        }

        private async Task<bool> WantsDownloadAsync(MediaAttachment media, CancellationToken token)
        {
            Func<MediaAttachment, CancellationToken, Task<bool>>? hook = WantsDownloadRendition;
            if (hook == null) return false;
            try
            {
                return await hook(media, token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _Logger.Warn("[MediaPipeline] download rendition check for " + media.Id + " failed: " + e.Message);
                return false;
            }
        }

        private async Task<MediaVariant?> DownloadVariantAsync(MediaAttachment media, VideoTranscodeResult result, CancellationToken token)
        {
            if (result.DownloadFile == null) return null;
            string key = MediaKeys.VideoPrefix(_PropertyId, media.Id) + result.DownloadFile;
            long size = result.DownloadSizeBytes;
            string? sha = result.DownloadSha256;
            if (sha == null)
            {
                string temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "trelliskit-download-" + media.Id + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                try
                {
                    await _Integrations.ObjectStore.DownloadToFileAsync(MediaLocationEnum.Transcoded, key, temp, token).ConfigureAwait(false);
                    size = new FileInfo(temp).Length;
                    using (FileStream stream = File.OpenRead(temp))
                    {
                        sha = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(stream, token).ConfigureAwait(false)).ToLowerInvariant();
                    }
                }
                catch (Exception e) when (e is IOException || e is InvalidOperationException)
                {
                    _Logger.Warn("[MediaPipeline] download rendition of " + media.Id + " unreadable: " + e.Message);
                    return null;
                }
                finally
                {
                    try
                    {
                        if (File.Exists(temp)) File.Delete(temp);
                    }
                    catch (IOException)
                    {
                        // Temporary files are harmless.
                    }
                }
            }
            return new MediaVariant { MediaId = media.Id, Name = MediaKeys.DownloadVariantName, Format = "mp4", SizeBytes = size, FileName = result.DownloadFile, Sha256 = sha };
        }

        private async Task ProcessVideoAsync(MediaAttachment media, string kind, CancellationToken token)
        {
            IVideoTranscoder transcoder = _Integrations.VideoTranscoder;
            media.Processor = transcoder.Label;
            if (!String.IsNullOrEmpty(media.ProcessorJobId))
            {
                VideoJobStatus status = await StageAsync(kind, "transcode_wait", () => transcoder.GetJobStatusAsync(media.ProcessorJobId, token)).ConfigureAwait(false);
                if (status.State == VideoJobStateEnum.Complete)
                {
                    await FinishAsyncJobAsync(media, kind, status, token).ConfigureAwait(false);
                    return;
                }
                if (status.State != VideoJobStateEnum.Running)
                {
                    throw new MediaProcessingException(MediaFailureReasonEnum.ProcessingError, "The transcode job ended with " + status.State + (status.ErrorMessage == null ? "." : ": " + status.ErrorMessage));
                }
                media.ClaimedBy = null;
                media.ClaimUntilUtc = DateTime.UtcNow.AddMinutes(_PollMinutes);
                await _Database.Media.UpdateAsync(media, MediaStatusEnum.Processing, token).ConfigureAwait(false);
                return;
            }

            ObjectHead? head = await StageAsync(kind, "verify", () => _Integrations.ObjectStore.HeadAsync(media.SourceLocation, media.SourceKey, token)).ConfigureAwait(false);
            if (head == null) throw new MediaProcessingException(MediaFailureReasonEnum.ProcessingError, "The original is missing.");

            VideoTranscodeRequest request = new VideoTranscodeRequest
            {
                MediaId = media.Id,
                PropertyId = _PropertyId,
                SourceKey = media.SourceKey,
                DurationCapSeconds = DurationCap(media),
                DownloadRendition = await WantsDownloadAsync(media, token).ConfigureAwait(false)
            };
            bool asynchronous = transcoder.Processor == VideoProcessorEnum.MediaConvert;
            VideoTranscodeResult result = await StageAsync(kind, asynchronous ? "transcode_submit" : "transcode", () => transcoder.TranscodeAsync(request, token)).ConfigureAwait(false);
            if (!result.IsComplete)
            {
                media.ProcessorJobId = result.JobId;
                media.ClaimedBy = null;
                media.ClaimUntilUtc = DateTime.UtcNow.AddMinutes(_WebhookGraceMinutes);
                media.UpdatedUtc = DateTime.UtcNow;
                await _Database.Media.UpdateAsync(media, MediaStatusEnum.Processing, token).ConfigureAwait(false);
                return;
            }
            await CompleteVideoAsync(media, kind, result, token).ConfigureAwait(false);
        }

        private double DurationCap(MediaAttachment media)
        {
            return media.DurationSeconds ?? media.DeclaredDurationSeconds ?? 0;
        }

        private async Task FinishAsyncJobAsync(MediaAttachment media, string kind, VideoJobStatus status, CancellationToken token)
        {
            IObjectStore store = _Integrations.ObjectStore;
            if (media.ProcessingStartedUtc != null) Stage(kind, "transcode_wait", Math.Max(0, (DateTime.UtcNow - media.ProcessingStartedUtc.Value).TotalSeconds));
            string imagePrefix = MediaKeys.ImagePrefix(_PropertyId, media.Id);
            string videoPrefix = MediaKeys.VideoPrefix(_PropertyId, media.Id);
            List<string> images = await store.ListKeysAsync(MediaLocationEnum.Images, imagePrefix, token).ConfigureAwait(false);
            List<string> videos = await store.ListKeysAsync(MediaLocationEnum.Transcoded, videoPrefix, token).ConfigureAwait(false);
            string? master = videos.Select(k => k.Substring(videoPrefix.Length)).Where(n => n.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
                .OrderBy(n => n == FfmpegTranscoder.MasterName ? 0 : n.Contains('_') ? 2 : 1).ThenBy(n => n.Length).FirstOrDefault();
            if (master == null) throw new MediaProcessingException(MediaFailureReasonEnum.ProcessingError, "The transcode job wrote no HLS playlist.");

            string? poster = await CanonicalAsync(store, images, imagePrefix, false, FfmpegTranscoder.PosterName, token).ConfigureAwait(false);
            string? posterThumb = await CanonicalAsync(store, images, imagePrefix, true, FfmpegTranscoder.PosterThumbName, token).ConfigureAwait(false);
            // The download rendition appears when the MediaConvert job template has a file output named download_720p
            // (docs/PHASE4_CONTRACT.md section 5.9); the job template is configured outside the engine.
            bool hasDownload = videos.Any(k => k.EndsWith("/" + MediaKeys.DownloadFileName, StringComparison.Ordinal));
            await CompleteVideoAsync(media, kind, new VideoTranscodeResult
            {
                DownloadFile = hasDownload ? MediaKeys.DownloadFileName : null,
                IsComplete = true,
                HlsManifestFile = master,
                PosterFile = poster,
                PosterThumbFile = posterThumb,
                Width = status.Width,
                Height = status.Height,
                DurationSeconds = media.DurationSeconds
            }, token).ConfigureAwait(false);
        }

        private static async Task<string?> CanonicalAsync(IObjectStore store, List<string> keys, string prefix, bool thumb, string canonical, CancellationToken token)
        {
            List<string> names = keys.Select(k => k.Substring(prefix.Length)).Where(n => n.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)).ToList();
            if (names.Contains(canonical, StringComparer.Ordinal)) return canonical;
            string? found = names.Where(n => n.Contains("thumb", StringComparison.OrdinalIgnoreCase) == thumb).OrderBy(n => n, StringComparer.Ordinal).FirstOrDefault();
            if (found == null) return null;
            await store.MoveAsync(MediaLocationEnum.Images, prefix + found, MediaLocationEnum.Images, prefix + canonical, token).ConfigureAwait(false);
            return canonical;
        }

        private async Task CompleteVideoAsync(MediaAttachment media, string kind, VideoTranscodeResult result, CancellationToken token)
        {
            MediaVariant? download = await DownloadVariantAsync(media, result, token).ConfigureAwait(false);
            int? width = result.Width;
            int? height = result.Height;
            string? blurhash = null;
            if (result.PosterFile != null)
            {
                await StageAsync(kind, "poster", async () =>
                {
                    byte[] poster = await _Integrations.ObjectStore.ReadAllAsync(MediaLocationEnum.Images, MediaKeys.ImagePrefix(_PropertyId, media.Id) + result.PosterFile, token).ConfigureAwait(false);
                    using (IDecodedImage decoded = _Integrations.ImageProcessor.Decode(poster, _Settings.Media.MaxImagePixels))
                    {
                        blurhash = _Integrations.ImageProcessor.Blurhash(decoded);
                        if (width == null || height == null)
                        {
                            width = decoded.Width;
                            height = decoded.Height;
                        }
                    }
                    return true;
                }).ConfigureAwait(false);
            }
            await StageAsync(kind, "store", async () =>
            {
                DateTime now = DateTime.UtcNow;
                media.Status = MediaStatusEnum.Ready;
                media.FailureReason = null;
                media.Width = width;
                media.Height = height;
                if (result.DurationSeconds != null) media.DurationSeconds = result.DurationSeconds;
                media.Blurhash = blurhash;
                media.HlsManifestFile = result.HlsManifestFile;
                media.Mp4File = result.Mp4File;
                media.PosterFile = result.PosterFile;
                media.PosterThumbFile = result.PosterThumbFile;
                media.OutputPrefix = MediaKeys.VideoPrefix(_PropertyId, media.Id);
                media.ReadyUtc = now;
                media.UpdatedUtc = now;
                media.ClaimedBy = null;
                media.ClaimUntilUtc = null;
                if (download == null) return await _Database.Media.UpdateAsync(media, MediaStatusEnum.Processing, token).ConfigureAwait(false);
                return await _Database.Media.CompleteProcessingAsync(media, new List<MediaVariant> { download }, token).ConfigureAwait(false);
            }).ConfigureAwait(false);
            await SettledAsync(media, kind, "ready", "none", token).ConfigureAwait(false);
        }

        private async Task FailAsync(MediaAttachment media, MediaFailureReasonEnum reason, string message, CancellationToken token)
        {
            string kind = KindLabel(media.Kind);
            _Logger.Warn("[MediaPipeline] " + media.Id + " failed (" + reason + "): " + message);
            await StageAsync(kind, "cleanup", async () =>
            {
                try
                {
                    await _Storage.DeleteOutputsAsync(media, token).ConfigureAwait(false);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    _Logger.Warn("[MediaPipeline] cleanup of " + media.Id + " failed: " + e.Message);
                }
                return true;
            }).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            media.Status = MediaStatusEnum.Failed;
            media.FailureReason = reason;
            media.ClaimedBy = null;
            media.ClaimUntilUtc = null;
            media.UpdatedUtc = now;
            await _Database.Media.UpdateAsync(media, MediaStatusEnum.Processing, token).ConfigureAwait(false);
            await SettledAsync(media, kind, "failed", reason.ToString(), token).ConfigureAwait(false);
        }

        private async Task SettledAsync(MediaAttachment media, string kind, string outcome, string reason, CancellationToken token)
        {
            TrellisKitTelemetry.Add(TrellisKitTelemetry.MediaProcessing, 1, "kind", kind, "processor", media.Processor ?? ProcessorLabel(media), "outcome", outcome, "failure_reason", reason);
            Func<MediaAttachment, CancellationToken, Task>? settled = Settled;
            if (settled == null) return;
            try
            {
                await settled(media, token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _Logger.Error("[MediaPipeline] settled callback for " + media.Id + " failed", e);
            }
        }

        #endregion
    }
}
