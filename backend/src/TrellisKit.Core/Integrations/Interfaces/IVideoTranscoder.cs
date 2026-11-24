namespace TrellisKit.Core.Integrations.Interfaces
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Integrations.Models;

    /// <summary>
    /// Video transcoding (contract section 4.6). Synchronous processors (Passthrough, Ffmpeg) write the outputs before
    /// returning; asynchronous ones (MediaConvert) submit a job and report completion by webhook or polling.
    /// Thread safety: implementations are safe to call concurrently.
    /// </summary>
    public interface IVideoTranscoder
    {
        /// <summary>
        /// Which processor this is.
        /// </summary>
        VideoProcessorEnum Processor { get; }

        /// <summary>
        /// Telemetry label: passthrough, ffmpeg, or mediaconvert.
        /// </summary>
        string Label { get; }

        /// <summary>
        /// Transcode (or submit a job for) a video.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Outputs, or a job id when <see cref="VideoTranscodeResult.IsComplete"/> is false.</returns>
        /// <exception cref="ArgumentNullException">The request is null.</exception>
        /// <exception cref="MediaProcessingException">The video cannot be processed (not retried).</exception>
        /// <exception cref="IntegrationNotConfiguredException">The processor is not configured.</exception>
        Task<VideoTranscodeResult> TranscodeAsync(VideoTranscodeRequest request, CancellationToken token = default);

        /// <summary>
        /// Status of an asynchronous job.
        /// </summary>
        /// <param name="jobId">Job identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Status.</returns>
        /// <exception cref="NotSupportedException">The processor is synchronous.</exception>
        Task<VideoJobStatus> GetJobStatusAsync(string jobId, CancellationToken token = default);
    }
}
