namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Amazon;
    using Amazon.MediaConvert;
    using Amazon.MediaConvert.Model;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// AWS Elemental MediaConvert video processor (contract section 4.6). CreateJob uses the job template
    /// media.mediaConvertJobTemplate, role media.mediaConvertRoleArn, and the optional queue, with input clipping at
    /// the duration cap, userMetadata.mediaId, the HLS destination s3://&lt;transcoded&gt;/video/{id}/master and the
    /// frame capture destination s3://&lt;images&gt;/media/{id}/poster. The job template must define the HLS group as
    /// its first output group and the frame capture file group as its second; only their destinations are overridden.
    /// With media.mediaConvertDownloadRendition the template's opt-in third group (download_720p) also gets its
    /// destination s3://&lt;transcoded&gt;/&lt;propertyId&gt;/video/{id}/download.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class MediaConvertTranscoder : IVideoTranscoder, IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Destination base name of the download output group; MediaConvert appends the template's name modifier (_720p)
        /// and extension (.mp4), producing MediaKeys.DownloadFileName.
        /// </summary>
        public const string DownloadBaseName = "download";

        /// <inheritdoc />
        public VideoProcessorEnum Processor
        {
            get { return VideoProcessorEnum.MediaConvert; }
        }

        /// <inheritdoc />
        public string Label
        {
            get { return "mediaconvert"; }
        }

        #endregion

        #region Private-Members

        private const string _Service = "mediaconvert";
        private readonly MediaSettings _Settings;
        private readonly Lazy<IAmazonMediaConvert> _Client;
        private readonly bool _OwnsClient;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the processor with a client for media.s3Region (created on first use).
        /// </summary>
        /// <param name="settings">Media settings (read on each job).</param>
        /// <exception cref="ArgumentNullException">The settings are null.</exception>
        public MediaConvertTranscoder(MediaSettings settings)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Client = new Lazy<IAmazonMediaConvert>(() => new AmazonMediaConvertClient(RegionEndpoint.GetBySystemName(_Settings.S3Region)), LazyThreadSafetyMode.ExecutionAndPublication);
            _OwnsClient = true;
        }

        /// <summary>
        /// Instantiate the processor with a supplied client.
        /// </summary>
        /// <param name="settings">Media settings.</param>
        /// <param name="client">Client. Not disposed by this processor.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public MediaConvertTranscoder(MediaSettings settings, IAmazonMediaConvert client)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            ArgumentNullException.ThrowIfNull(client);
            _Client = new Lazy<IAmazonMediaConvert>(() => client);
            _OwnsClient = false;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the CreateJob request for a video (exposed for tests).
        /// </summary>
        /// <param name="request">Transcode request.</param>
        /// <returns>CreateJob request.</returns>
        /// <exception cref="ArgumentNullException">The request is null.</exception>
        /// <exception cref="IntegrationNotConfiguredException">The role, template, or a bucket is not set.</exception>
        public CreateJobRequest BuildCreateJob(VideoTranscodeRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (String.IsNullOrWhiteSpace(_Settings.MediaConvertRoleArn)) throw new IntegrationNotConfiguredException("MediaConvert needs media.mediaConvertRoleArn.");
            if (String.IsNullOrWhiteSpace(_Settings.MediaConvertJobTemplate)) throw new IntegrationNotConfiguredException("MediaConvert needs media.mediaConvertJobTemplate.");
            if (String.IsNullOrWhiteSpace(_Settings.VideoSourceBucket) || String.IsNullOrWhiteSpace(_Settings.TranscodedBucket) || String.IsNullOrWhiteSpace(_Settings.ImagesBucket))
                throw new IntegrationNotConfiguredException("MediaConvert needs media.videoSourceBucket, media.transcodedBucket, and media.imagesBucket.");

            Input input = new Input
            {
                FileInput = "s3://" + _Settings.VideoSourceBucket + "/" + request.SourceKey,
                TimecodeSource = InputTimecodeSource.ZEROBASED
            };
            if (request.DurationCapSeconds > 0) input.InputClippings = new List<InputClipping> { new InputClipping { EndTimecode = Timecode(request.DurationCapSeconds) } };

            CreateJobRequest create = new CreateJobRequest
            {
                JobTemplate = _Settings.MediaConvertJobTemplate,
                Role = _Settings.MediaConvertRoleArn,
                UserMetadata = new Dictionary<string, string> { { "mediaId", request.MediaId } },
                Settings = new JobSettings
                {
                    Inputs = new List<Input> { input },
                    OutputGroups = new List<OutputGroup>
                    {
                        new OutputGroup
                        {
                            OutputGroupSettings = new OutputGroupSettings
                            {
                                Type = OutputGroupType.HLS_GROUP_SETTINGS,
                                HlsGroupSettings = new HlsGroupSettings { Destination = "s3://" + _Settings.TranscodedBucket + "/" + MediaKeys.VideoPrefix(request.PropertyId, request.MediaId) + "master" }
                            }
                        },
                        new OutputGroup
                        {
                            OutputGroupSettings = new OutputGroupSettings
                            {
                                Type = OutputGroupType.FILE_GROUP_SETTINGS,
                                FileGroupSettings = new FileGroupSettings { Destination = "s3://" + _Settings.ImagesBucket + "/" + MediaKeys.ImagePrefix(request.PropertyId, request.MediaId) + "poster" }
                            }
                        }
                    }
                }
            };
            if (_Settings.MediaConvertDownloadRendition)
            {
                // Output group 2 of the template (opt-in): one MP4 with name modifier _720p, written as download_720p.mp4.
                create.Settings.OutputGroups.Add(new OutputGroup
                {
                    OutputGroupSettings = new OutputGroupSettings
                    {
                        Type = OutputGroupType.FILE_GROUP_SETTINGS,
                        FileGroupSettings = new FileGroupSettings { Destination = "s3://" + _Settings.TranscodedBucket + "/" + MediaKeys.VideoPrefix(request.PropertyId, request.MediaId) + DownloadBaseName }
                    }
                });
            }
            if (!String.IsNullOrWhiteSpace(_Settings.MediaConvertQueueArn)) create.Queue = _Settings.MediaConvertQueueArn;
            return create;
        }

        /// <inheritdoc />
        public async Task<VideoTranscodeResult> TranscodeAsync(VideoTranscodeRequest request, CancellationToken token = default)
        {
            CreateJobRequest create = BuildCreateJob(request);
            Activity? activity = TrellisKitTelemetry.StartIntegrationSpan(_Service, "CreateJob");
            Stopwatch stopwatch = Stopwatch.StartNew();
            string outcome = "failure";
            try
            {
                CreateJobResponse response = await _Client.Value.CreateJobAsync(create, token).ConfigureAwait(false);
                outcome = "success";
                return new VideoTranscodeResult { IsComplete = false, JobId = response.Job?.Id };
            }
            finally
            {
                TrellisKitTelemetry.RecordIntegration(activity, _Service, "CreateJob", stopwatch.Elapsed, outcome);
                activity?.Dispose();
            }
        }

        /// <inheritdoc />
        public async Task<VideoJobStatus> GetJobStatusAsync(string jobId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(jobId)) throw new ArgumentNullException(nameof(jobId));
            Activity? activity = TrellisKitTelemetry.StartIntegrationSpan(_Service, "GetJob");
            Stopwatch stopwatch = Stopwatch.StartNew();
            string outcome = "failure";
            try
            {
                GetJobResponse response = await _Client.Value.GetJobAsync(new GetJobRequest { Id = jobId }, token).ConfigureAwait(false);
                outcome = "success";
                Job job = response.Job ?? new Job();
                VideoJobStatus status = new VideoJobStatus { State = MapState(job.Status?.Value), ErrorMessage = job.ErrorMessage };
                foreach (OutputGroupDetail group in job.OutputGroupDetails ?? new List<OutputGroupDetail>())
                {
                    foreach (OutputDetail detail in group.OutputDetails ?? new List<OutputDetail>())
                    {
                        int? width = detail.VideoDetails?.WidthInPx;
                        int? height = detail.VideoDetails?.HeightInPx;
                        if (width != null && height != null && (status.Height == null || height > status.Height))
                        {
                            status.Width = width;
                            status.Height = height;
                        }
                    }
                }
                return status;
            }
            finally
            {
                TrellisKitTelemetry.RecordIntegration(activity, _Service, "GetJob", stopwatch.Elapsed, outcome);
                activity?.Dispose();
            }
        }

        /// <summary>
        /// Map a MediaConvert status (SUBMITTED, PROGRESSING, COMPLETE, CANCELED, ERROR).
        /// </summary>
        /// <param name="status">Status text, or null.</param>
        /// <returns>State.</returns>
        public static VideoJobStateEnum MapState(string? status)
        {
            switch ((status ?? String.Empty).ToUpperInvariant())
            {
                case "COMPLETE":
                    return VideoJobStateEnum.Complete;
                case "ERROR":
                    return VideoJobStateEnum.Error;
                case "CANCELED":
                case "CANCELLED":
                    return VideoJobStateEnum.Canceled;
                default:
                    return VideoJobStateEnum.Running;
            }
        }

        /// <summary>
        /// Dispose the client when this processor created it.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Dispose resources.
        /// </summary>
        /// <param name="disposing">Whether managed resources are disposed.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            _Disposed = true;
            if (disposing && _OwnsClient && _Client.IsValueCreated) _Client.Value.Dispose();
        }

        #endregion

        #region Private-Methods

        private static string Timecode(double seconds)
        {
            TimeSpan span = TimeSpan.FromSeconds(Math.Ceiling(seconds));
            return ((int)span.TotalHours).ToString("00", CultureInfo.InvariantCulture) + ":" + span.Minutes.ToString("00", CultureInfo.InvariantCulture) + ":" + span.Seconds.ToString("00", CultureInfo.InvariantCulture) + ":00";
        }

        #endregion
    }
}
