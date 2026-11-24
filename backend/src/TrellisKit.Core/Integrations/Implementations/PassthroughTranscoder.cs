namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;

    /// <summary>
    /// Local fallback video processor (contract sections 4.8 and 17): copies the original to
    /// transcoded/video/{id}/original.mp4 and reads the display size and duration from the container header. Video
    /// metadata is NOT stripped; the composition root logs a warning at startup when this processor is selected.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class PassthroughTranscoder : IVideoTranscoder
    {
        #region Public-Members

        /// <summary>
        /// Output file name.
        /// </summary>
        public const string OutputName = "original.mp4";

        /// <inheritdoc />
        public VideoProcessorEnum Processor
        {
            get { return VideoProcessorEnum.Passthrough; }
        }

        /// <inheritdoc />
        public string Label
        {
            get { return "passthrough"; }
        }

        #endregion

        #region Private-Members

        private readonly IObjectStore _Store;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the processor.
        /// </summary>
        /// <param name="store">Object store.</param>
        /// <exception cref="ArgumentNullException">The store is null.</exception>
        public PassthroughTranscoder(IObjectStore store)
        {
            _Store = store ?? throw new ArgumentNullException(nameof(store));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<VideoTranscodeResult> TranscodeAsync(VideoTranscodeRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ObjectHead? head = await _Store.HeadAsync(MediaLocationEnum.VideoSource, request.SourceKey, token).ConfigureAwait(false);
            if (head == null) throw new MediaProcessingException(MediaFailureReasonEnum.ProcessingError, "The original video is missing.");
            Mp4Info? info = await Mp4HeaderReader.ReadAsync((o, l, t) => _Store.ReadRangeAsync(MediaLocationEnum.VideoSource, request.SourceKey, o, l, t), head.SizeBytes, 64L * 1024 * 1024, token).ConfigureAwait(false);
            if (info == null) throw new MediaProcessingException(MediaFailureReasonEnum.Corrupt, "The video has no readable movie header.");
            await _Store.CopyAsync(MediaLocationEnum.VideoSource, request.SourceKey, MediaLocationEnum.Transcoded, MediaKeys.VideoPrefix(request.PropertyId, request.MediaId) + OutputName, token).ConfigureAwait(false);
            VideoTranscodeResult result = new VideoTranscodeResult { IsComplete = true, Mp4File = OutputName, Width = info.Width, Height = info.Height, DurationSeconds = info.DurationSeconds };
            if (request.DownloadRendition)
            {
                // Development path: the original MP4 is served unchanged as the download rendition (not downscaled to 720p).
                await _Store.CopyAsync(MediaLocationEnum.VideoSource, request.SourceKey, MediaLocationEnum.Transcoded, MediaKeys.VideoPrefix(request.PropertyId, request.MediaId) + MediaKeys.DownloadFileName, token).ConfigureAwait(false);
                result.DownloadFile = MediaKeys.DownloadFileName;
                result.DownloadSizeBytes = head.SizeBytes;
            }
            return result;
        }

        /// <inheritdoc />
        public Task<VideoJobStatus> GetJobStatusAsync(string jobId, CancellationToken token = default)
        {
            throw new NotSupportedException("The passthrough processor has no asynchronous jobs.");
        }

        #endregion
    }
}
