namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Local ffmpeg video processor (contract section 4.8): HLS renditions (default 360p and 720p, rungs above the
    /// source height skipped), H.264 with -preset veryfast and AAC, 6 second segments, -map_metadata -1, flat names
    /// (master.m3u8, hls_360p.m3u8, hls_360p_00001.ts), and posters at 1 second (poster.jpg 1280 wide,
    /// poster_thumb.jpg 320 wide). ffprobe is looked up next to media.ffmpegPath. The worker limits concurrency
    /// (media.videoConcurrency). Thread safety: safe to call concurrently; each job uses its own temporary folder.
    /// </summary>
    public class FfmpegTranscoder : IVideoTranscoder
    {
        #region Public-Members

        /// <summary>
        /// Master playlist name.
        /// </summary>
        public const string MasterName = "master.m3u8";

        /// <summary>
        /// Poster name.
        /// </summary>
        public const string PosterName = "poster.jpg";

        /// <summary>
        /// Poster thumbnail name.
        /// </summary>
        public const string PosterThumbName = "poster_thumb.jpg";

        /// <inheritdoc />
        public VideoProcessorEnum Processor
        {
            get { return VideoProcessorEnum.Ffmpeg; }
        }

        /// <inheritdoc />
        public string Label
        {
            get { return "ffmpeg"; }
        }

        /// <summary>
        /// HLS rung heights, ascending. Default 360 and 720. Never null or empty.
        /// </summary>
        public List<int> Rungs
        {
            get { return _Rungs; }
            set { _Rungs = value == null || value.Count == 0 ? new List<int> { 360, 720 } : value.Where(v => v > 0).OrderBy(v => v).ToList(); }
        }

        /// <summary>
        /// Longest a single ffmpeg or ffprobe run may take, in minutes. Default 60. Minimum 1, maximum 1440.
        /// </summary>
        public int TimeoutMinutes
        {
            get { return _TimeoutMinutes; }
            set { _TimeoutMinutes = Math.Clamp(value, 1, 1440); }
        }

        /// <summary>
        /// Folder for temporary job files. Default the system temporary folder. Never null.
        /// </summary>
        public string WorkDirectory
        {
            get { return _WorkDirectory; }
            set { _WorkDirectory = String.IsNullOrWhiteSpace(value) ? Path.GetTempPath() : value; }
        }

        #endregion

        #region Private-Members

        private readonly IObjectStore _Store;
        private readonly MediaSettings _Settings;
        private List<int> _Rungs = new List<int> { 360, 720 };
        private int _TimeoutMinutes = 60;
        private string _WorkDirectory = Path.GetTempPath();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the processor.
        /// </summary>
        /// <param name="store">Object store.</param>
        /// <param name="settings">Media settings (media.ffmpegPath is read on each job).</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public FfmpegTranscoder(IObjectStore store, MediaSettings settings)
        {
            _Store = store ?? throw new ArgumentNullException(nameof(store));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The ffprobe executable next to an ffmpeg path.
        /// </summary>
        /// <param name="ffmpegPath">ffmpeg path or command name.</param>
        /// <returns>ffprobe path or command name.</returns>
        public static string ProbePathFor(string ffmpegPath)
        {
            string path = String.IsNullOrWhiteSpace(ffmpegPath) ? "ffmpeg" : ffmpegPath;
            string? directory = Path.GetDirectoryName(path);
            string extension = path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? ".exe" : String.Empty;
            return String.IsNullOrEmpty(directory) ? "ffprobe" + extension : Path.Combine(directory, "ffprobe" + extension);
        }

        /// <inheritdoc />
        public async Task<VideoTranscodeResult> TranscodeAsync(VideoTranscodeRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            string ffmpeg = String.IsNullOrWhiteSpace(_Settings.FfmpegPath) ? "ffmpeg" : _Settings.FfmpegPath;
            string ffprobe = ProbePathFor(ffmpeg);
            string work = Path.Combine(_WorkDirectory, "trelliskit-ffmpeg-" + request.MediaId + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string output = Path.Combine(work, "out");
            Directory.CreateDirectory(output);
            try
            {
                string input = Path.Combine(work, "input");
                await _Store.DownloadToFileAsync(MediaLocationEnum.VideoSource, request.SourceKey, input, token).ConfigureAwait(false);

                string probe = await RunAsync(ffprobe, new List<string> { "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height:stream_tags=rotate:stream_side_data=rotation:format=duration", "-of", "json", input }, token).ConfigureAwait(false);
                ReadProbe(probe, out int width, out int height, out double duration);
                if (width <= 0 || height <= 0) throw new MediaProcessingException(MediaFailureReasonEnum.Corrupt, "The video has no video stream.");

                List<int> rungs = _Rungs.Where(r => r <= height).ToList();
                if (rungs.Count == 0) rungs.Add(height - (height % 2));
                StringBuilder master = new StringBuilder("#EXTM3U\n#EXT-X-VERSION:3\n");
                foreach (int rung in rungs)
                {
                    string name = "hls_" + rung.ToString(CultureInfo.InvariantCulture) + "p";
                    List<string> args = new List<string> { "-y", "-v", "error", "-i", input, "-map_metadata", "-1", "-map", "0:v:0", "-map", "0:a:0?" };
                    if (request.DurationCapSeconds > 0) { args.Add("-t"); args.Add(request.DurationCapSeconds.ToString("0.###", CultureInfo.InvariantCulture)); }
                    args.AddRange(new[]
                    {
                        "-vf", "scale=-2:" + rung.ToString(CultureInfo.InvariantCulture), "-c:v", "libx264", "-preset", "veryfast", "-profile:v", "main", "-crf", "23", "-pix_fmt", "yuv420p",
                        "-g", "48", "-keyint_min", "48", "-sc_threshold", "0", "-c:a", "aac", "-b:a", "128k", "-ac", "2",
                        "-f", "hls", "-hls_time", "6", "-hls_playlist_type", "vod", "-start_number", "1",
                        "-hls_segment_filename", Path.Combine(output, name + "_%05d.ts"), Path.Combine(output, name + ".m3u8")
                    });
                    await RunAsync(ffmpeg, args, token).ConfigureAwait(false);
                    int rungWidth = (int)Math.Round(width * (rung / (double)height));
                    if (rungWidth % 2 == 1) rungWidth++;
                    master.Append("#EXT-X-STREAM-INF:BANDWIDTH=" + (rung * 4000).ToString(CultureInfo.InvariantCulture) + ",RESOLUTION=" + rungWidth.ToString(CultureInfo.InvariantCulture) + "x" + rung.ToString(CultureInfo.InvariantCulture) + "\n" + name + ".m3u8\n");
                }
                await File.WriteAllTextAsync(Path.Combine(output, MasterName), master.ToString(), token).ConfigureAwait(false);

                string? download = null;
                if (request.DownloadRendition)
                {
                    // Progressive MP4 for offline lesson downloads (docs/PHASE4_CONTRACT.md section 5.9): H.264 and AAC, at most 720p.
                    int target = Math.Min(720, height - (height % 2));
                    List<string> args = new List<string> { "-y", "-v", "error", "-i", input, "-map_metadata", "-1", "-map", "0:v:0", "-map", "0:a:0?" };
                    if (request.DurationCapSeconds > 0) { args.Add("-t"); args.Add(request.DurationCapSeconds.ToString("0.###", CultureInfo.InvariantCulture)); }
                    args.AddRange(new[] { "-vf", "scale=-2:" + target.ToString(CultureInfo.InvariantCulture), "-c:v", "libx264", "-preset", "veryfast", "-profile:v", "main", "-crf", "23", "-pix_fmt", "yuv420p",
                        "-c:a", "aac", "-b:a", "128k", "-ac", "2", "-movflags", "+faststart", Path.Combine(output, MediaKeys.DownloadFileName) });
                    await RunAsync(ffmpeg, args, token).ConfigureAwait(false);
                    download = Path.Combine(output, MediaKeys.DownloadFileName);
                }

                string seek = duration >= 1.5 ? "1" : "0";
                await RunAsync(ffmpeg, new List<string> { "-y", "-v", "error", "-ss", seek, "-i", input, "-map_metadata", "-1", "-frames:v", "1", "-vf", "scale=" + Math.Min(1280, width - (width % 2)).ToString(CultureInfo.InvariantCulture) + ":-2", "-q:v", "3", Path.Combine(work, PosterName) }, token).ConfigureAwait(false);
                await RunAsync(ffmpeg, new List<string> { "-y", "-v", "error", "-ss", seek, "-i", input, "-map_metadata", "-1", "-frames:v", "1", "-vf", "scale=" + Math.Min(320, width - (width % 2)).ToString(CultureInfo.InvariantCulture) + ":-2", "-q:v", "4", Path.Combine(work, PosterThumbName) }, token).ConfigureAwait(false);

                string videoPrefix = MediaKeys.VideoPrefix(request.PropertyId, request.MediaId);
                foreach (string file in Directory.GetFiles(output).OrderBy(f => f, StringComparer.Ordinal))
                {
                    string name = Path.GetFileName(file);
                    await _Store.PutFileAsync(MediaLocationEnum.Transcoded, videoPrefix + name, file, MediaKeys.ContentTypeFor(name), token).ConfigureAwait(false);
                }
                string imagePrefix = MediaKeys.ImagePrefix(request.PropertyId, request.MediaId);
                await _Store.PutFileAsync(MediaLocationEnum.Images, imagePrefix + PosterName, Path.Combine(work, PosterName), "image/jpeg", token).ConfigureAwait(false);
                await _Store.PutFileAsync(MediaLocationEnum.Images, imagePrefix + PosterThumbName, Path.Combine(work, PosterThumbName), "image/jpeg", token).ConfigureAwait(false);

                double finalDuration = request.DurationCapSeconds > 0 ? Math.Min(duration, request.DurationCapSeconds) : duration;
                long downloadSize = 0;
                string? downloadSha = null;
                if (download != null && File.Exists(download))
                {
                    downloadSize = new FileInfo(download).Length;
                    using (FileStream stream = File.OpenRead(download))
                    {
                        downloadSha = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(stream, token).ConfigureAwait(false)).ToLowerInvariant();
                    }
                }
                return new VideoTranscodeResult
                {
                    DownloadFile = downloadSha == null ? null : MediaKeys.DownloadFileName,
                    DownloadSizeBytes = downloadSize,
                    DownloadSha256 = downloadSha,
                    IsComplete = true,
                    HlsManifestFile = MasterName,
                    PosterFile = PosterName,
                    PosterThumbFile = PosterThumbName,
                    Width = width,
                    Height = height,
                    DurationSeconds = finalDuration
                };
            }
            finally
            {
                try
                {
                    if (Directory.Exists(work)) Directory.Delete(work, true);
                }
                catch (IOException)
                {
                    // Temporary files are harmless.
                }
            }
        }

        /// <inheritdoc />
        public Task<VideoJobStatus> GetJobStatusAsync(string jobId, CancellationToken token = default)
        {
            throw new NotSupportedException("The ffmpeg processor has no asynchronous jobs.");
        }

        #endregion

        #region Private-Methods

        private static void ReadProbe(string json, out int width, out int height, out double duration)
        {
            width = 0;
            height = 0;
            duration = 0;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    JsonElement root = doc.RootElement;
                    if (root.TryGetProperty("streams", out JsonElement streams) && streams.ValueKind == JsonValueKind.Array && streams.GetArrayLength() > 0)
                    {
                        JsonElement stream = streams[0];
                        if (stream.TryGetProperty("width", out JsonElement w) && w.ValueKind == JsonValueKind.Number) width = w.GetInt32();
                        if (stream.TryGetProperty("height", out JsonElement h) && h.ValueKind == JsonValueKind.Number) height = h.GetInt32();
                        int rotation = 0;
                        if (stream.TryGetProperty("side_data_list", out JsonElement sides) && sides.ValueKind == JsonValueKind.Array)
                        {
                            foreach (JsonElement side in sides.EnumerateArray())
                            {
                                if (side.TryGetProperty("rotation", out JsonElement r) && r.ValueKind == JsonValueKind.Number) rotation = (int)r.GetDouble();
                            }
                        }
                        if (rotation == 0 && stream.TryGetProperty("tags", out JsonElement tags) && tags.TryGetProperty("rotate", out JsonElement rotate))
                        {
                            Int32.TryParse(rotate.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out rotation);
                        }
                        if (Math.Abs(rotation) % 180 == 90)
                        {
                            int swap = width;
                            width = height;
                            height = swap;
                        }
                    }
                    if (root.TryGetProperty("format", out JsonElement format) && format.TryGetProperty("duration", out JsonElement d))
                    {
                        Double.TryParse(d.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out duration);
                    }
                }
            }
            catch (JsonException e)
            {
                throw new MediaProcessingException(MediaFailureReasonEnum.Corrupt, "ffprobe output could not be read.", e);
            }
        }

        private async Task<string> RunAsync(string executable, List<string> arguments, CancellationToken token)
        {
            string operation = Path.GetFileNameWithoutExtension(executable);
            Activity? activity = TrellisKitTelemetry.StartSpan("ffmpeg " + operation);
            ProcessStartInfo start = new ProcessStartInfo(executable)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            try
            {
                using (Process process = new Process { StartInfo = start })
                {
                    try
                    {
                        process.Start();
                    }
                    catch (System.ComponentModel.Win32Exception e)
                    {
                        throw new IntegrationNotConfiguredException("The " + operation + " executable (" + executable + ") could not be started; set media.ffmpegPath or install ffmpeg.", e);
                    }
                    Task<string> stdout = process.StandardOutput.ReadToEndAsync(token);
                    Task<string> stderr = process.StandardError.ReadToEndAsync(token);
                    using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                    {
                        timeout.CancelAfter(TimeSpan.FromMinutes(_TimeoutMinutes));
                        try
                        {
                            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            try
                            {
                                process.Kill(true);
                            }
                            catch (InvalidOperationException)
                            {
                                // Already exited.
                            }
                            if (token.IsCancellationRequested) throw;
                            throw new MediaProcessingException(MediaFailureReasonEnum.ProcessingError, operation + " timed out after " + _TimeoutMinutes + " minutes.");
                        }
                    }
                    string output = await stdout.ConfigureAwait(false);
                    string error = await stderr.ConfigureAwait(false);
                    if (process.ExitCode != 0)
                    {
                        string detail = error.Length > 500 ? error.Substring(error.Length - 500) : error;
                        throw new MediaProcessingException(MediaFailureReasonEnum.ProcessingError, operation + " exited with code " + process.ExitCode + ": " + detail.Trim());
                    }
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    return output;
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                activity?.SetStatus(ActivityStatusCode.Error, e.GetType().Name);
                throw;
            }
            finally
            {
                activity?.Dispose();
            }
        }

        #endregion
    }
}
