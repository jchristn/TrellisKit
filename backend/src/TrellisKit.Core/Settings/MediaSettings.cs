namespace TrellisKit.Core.Settings
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Media upload, processing, storage, and delivery settings (contract section 4).
    /// </summary>
    public class MediaSettings
    {
        #region Public-Members

        /// <summary>
        /// Local stores media in folders under storage.directory and serves it from the backend; S3 uses the buckets below and CloudFront. Default Local.
        /// </summary>
        public MediaStorageModeEnum StorageMode { get; set; } = MediaStorageModeEnum.Local;

        /// <summary>
        /// AWS region of the media buckets and MediaConvert. Default "us-west-2". Never null.
        /// </summary>
        public string S3Region
        {
            get { return _S3Region; }
            set { _S3Region = value ?? String.Empty; }
        }

        /// <summary>
        /// Private bucket for original image uploads (kept; never served). Changing it does not move existing objects. Default empty. Never null.
        /// </summary>
        public string SourceBucket
        {
            get { return _SourceBucket; }
            set { _SourceBucket = value ?? String.Empty; }
        }

        /// <summary>
        /// Private bucket for original video uploads (MediaConvert reads it). Changing it does not move existing objects. Default empty. Never null.
        /// </summary>
        public string VideoSourceBucket
        {
            get { return _VideoSourceBucket; }
            set { _VideoSourceBucket = value ?? String.Empty; }
        }

        /// <summary>
        /// Bucket for image variants, video posters, and link preview images, served by the media CDN. Changing it does not move existing objects. Default empty. Never null.
        /// </summary>
        public string ImagesBucket
        {
            get { return _ImagesBucket; }
            set { _ImagesBucket = value ?? String.Empty; }
        }

        /// <summary>
        /// Bucket for HLS playlists and segments, served by the media CDN. Changing it does not move existing objects. Default empty. Never null.
        /// </summary>
        public string TranscodedBucket
        {
            get { return _TranscodedBucket; }
            set { _TranscodedBucket = value ?? String.Empty; }
        }

        /// <summary>
        /// Private bucket for served objects of moderator-removed content, kept for moderation.retainRemovedDays. Changing it does not move existing objects. Default empty. Never null.
        /// </summary>
        public string RetainedBucket
        {
            get { return _RetainedBucket; }
            set { _RetainedBucket = value ?? String.Empty; }
        }

        /// <summary>
        /// Absolute https origin of the media CDN, for example https://media.example.test. Empty serves root-relative URLs from this backend (Local mode). Default empty. Never null.
        /// </summary>
        public string PublicBaseUrl
        {
            get { return _PublicBaseUrl; }
            set { _PublicBaseUrl = value ?? String.Empty; }
        }

        /// <summary>
        /// MediaConvert in production, Ffmpeg in the Docker stack, Passthrough (copy the original, metadata not stripped) without ffmpeg. Default Passthrough.
        /// </summary>
        public VideoProcessorEnum VideoProcessor { get; set; } = VideoProcessorEnum.Passthrough;

        /// <summary>
        /// ffmpeg executable for the Ffmpeg video processor (ffprobe is looked up next to it). Default "ffmpeg". Never null.
        /// </summary>
        public string FfmpegPath
        {
            get { return _FfmpegPath; }
            set { _FfmpegPath = value ?? String.Empty; }
        }

        /// <summary>
        /// IAM role MediaConvert assumes to read the video source bucket and write outputs. Default empty. Never null.
        /// </summary>
        public string MediaConvertRoleArn
        {
            get { return _MediaConvertRoleArn; }
            set { _MediaConvertRoleArn = value ?? String.Empty; }
        }

        /// <summary>
        /// Queue for transcode jobs. Empty uses the default queue. Default empty. Never null.
        /// </summary>
        public string MediaConvertQueueArn
        {
            get { return _MediaConvertQueueArn; }
            set { _MediaConvertQueueArn = value ?? String.Empty; }
        }

        /// <summary>
        /// Job template that defines the HLS ladder and frame capture. Default "trelliskit-hls". Never null.
        /// </summary>
        public string MediaConvertJobTemplate
        {
            get { return _MediaConvertJobTemplate; }
            set { _MediaConvertJobTemplate = value ?? String.Empty; }
        }

        /// <summary>
        /// Whether the MediaConvert job template has the opt-in third output group (the progressive download_720p MP4 of
        /// offline lesson downloads, infra MediaSettings.DownloadRendition). When true every job also sets that group's
        /// destination to s3://&lt;transcoded&gt;/&lt;propertyId&gt;/video/&lt;id&gt;/download; keep it equal to the infra
        /// flag, since a template group without a destination (or an override without a template group) fails the job.
        /// Default false.
        /// </summary>
        public bool MediaConvertDownloadRendition { get; set; } = false;

        /// <summary>
        /// Topic that delivers MediaConvert job state changes to POST /api/v1/webhooks/mediaconvert. Default empty. Never null.
        /// </summary>
        public string SnsTopicArn
        {
            get { return _SnsTopicArn; }
            set { _SnsTopicArn = value ?? String.Empty; }
        }

        /// <summary>
        /// Public key id of the CloudFront key group that verifies media cookies. Default empty. Never null.
        /// </summary>
        public string CloudFrontKeyPairId
        {
            get { return _CloudFrontKeyPairId; }
            set { _CloudFrontKeyPairId = value ?? String.Empty; }
        }

        /// <summary>
        /// PEM RSA private key that signs media cookies. Default empty. Never null.
        /// </summary>
        public string CloudFrontPrivateKeyPem
        {
            get { return _CloudFrontPrivateKeyPem; }
            set { _CloudFrontPrivateKeyPem = value ?? String.Empty; }
        }

        /// <summary>
        /// Domain attribute of media grant cookies, for example example.test. Empty sets host-only cookies. Default empty. Never null.
        /// </summary>
        public string CookieDomain
        {
            get { return _CookieDomain; }
            set { _CookieDomain = value ?? String.Empty; }
        }

        /// <summary>
        /// How long a media grant lasts; bounds media access after a membership ends. Default 360. Minimum 15, maximum 1440.
        /// </summary>
        public int GrantMinutes
        {
            get { return _GrantMinutes; }
            set { _GrantMinutes = Math.Clamp(value, 15, 1440); }
        }

        /// <summary>
        /// How long presigned or local upload URLs stay valid. Default 60. Minimum 5, maximum 720.
        /// </summary>
        public int UploadUrlMinutes
        {
            get { return _UploadUrlMinutes; }
            set { _UploadUrlMinutes = Math.Clamp(value, 5, 720); }
        }

        /// <summary>
        /// Upload sessions not completed within this time expire and are cleaned up. Default 24. Minimum 1, maximum 168.
        /// </summary>
        public int UploadExpiryHours
        {
            get { return _UploadExpiryHours; }
            set { _UploadExpiryHours = Math.Clamp(value, 1, 168); }
        }

        /// <summary>
        /// Ready media never attached (or detached by an edit) is deleted after this time. Default 168. Minimum 1, maximum 720.
        /// </summary>
        public int UnattachedRetentionHours
        {
            get { return _UnattachedRetentionHours; }
            set { _UnattachedRetentionHours = Math.Clamp(value, 1, 720); }
        }

        /// <summary>
        /// Largest accepted Document media (PDF or WebVTT) upload (media.maxDocumentBytes). Default 26214400. Minimum 1048576, maximum 104857600.
        /// </summary>
        public int MaxDocumentBytes
        {
            get { return _MaxDocumentBytes; }
            set { _MaxDocumentBytes = Math.Clamp(value, 1048576, 104857600); }
        }

        /// <summary>
        /// Largest accepted image upload. Default 20971520. Minimum 102400, maximum 104857600.
        /// </summary>
        public int MaxImageBytes
        {
            get { return _MaxImageBytes; }
            set { _MaxImageBytes = Math.Clamp(value, 102400, 104857600); }
        }

        /// <summary>
        /// Images with more pixels fail with ImageTooLarge. Default 40000000. Minimum 1000000, maximum 100000000.
        /// </summary>
        public int MaxImagePixels
        {
            get { return _MaxImagePixels; }
            set { _MaxImagePixels = Math.Clamp(value, 1000000, 100000000); }
        }

        /// <summary>
        /// Largest video a member may upload. Default 2048. Minimum 1, maximum 20480.
        /// </summary>
        public int MaxMemberVideoMegabytes
        {
            get { return _MaxMemberVideoMegabytes; }
            set { _MaxMemberVideoMegabytes = Math.Clamp(value, 1, 20480); }
        }

        /// <summary>
        /// Longest video a member may upload. Default 300. Minimum 5, maximum 3600.
        /// </summary>
        public int MaxMemberVideoSeconds
        {
            get { return _MaxMemberVideoSeconds; }
            set { _MaxMemberVideoSeconds = Math.Clamp(value, 5, 3600); }
        }

        /// <summary>
        /// Largest video a SystemAdmin may upload. 0 means uncapped. Default 0. Minimum 0, maximum 1048576.
        /// </summary>
        public int MaxAdminVideoMegabytes
        {
            get { return _MaxAdminVideoMegabytes; }
            set { _MaxAdminVideoMegabytes = Math.Clamp(value, 0, 1048576); }
        }

        /// <summary>
        /// Longest video a SystemAdmin may upload. 0 means uncapped. Default 0. Minimum 0, maximum 86400.
        /// </summary>
        public int MaxAdminVideoSeconds
        {
            get { return _MaxAdminVideoSeconds; }
            set { _MaxAdminVideoSeconds = Math.Clamp(value, 0, 86400); }
        }

        /// <summary>
        /// Videos larger than this upload in parts. Default 32. Minimum 5, maximum 5120.
        /// </summary>
        public int MultipartThresholdMegabytes
        {
            get { return _MultipartThresholdMegabytes; }
            set { _MultipartThresholdMegabytes = Math.Clamp(value, 5, 5120); }
        }

        /// <summary>
        /// Minimum part size; larger files use bigger parts so a session never exceeds 1000 parts. Default 16. Minimum 5, maximum 512.
        /// </summary>
        public int MultipartPartMegabytes
        {
            get { return _MultipartPartMegabytes; }
            set { _MultipartPartMegabytes = Math.Clamp(value, 5, 512); }
        }

        /// <summary>
        /// Bytes a member may upload per UTC day across images and video. 0 means no quota. Default 5120. Minimum 0, maximum 1048576.
        /// </summary>
        public int MemberDailyUploadMegabytes
        {
            get { return _MemberDailyUploadMegabytes; }
            set { _MemberDailyUploadMegabytes = Math.Clamp(value, 0, 1048576); }
        }

        /// <summary>
        /// Upload sessions a member may create each hour. Default 60. Minimum 1, maximum 10000.
        /// </summary>
        public int UploadsPerHour
        {
            get { return _UploadsPerHour; }
            set { _UploadsPerHour = Math.Clamp(value, 1, 10000); }
        }

        /// <summary>
        /// Images processed at the same time. Default 2. Minimum 1, maximum 16.
        /// </summary>
        public int ImageConcurrency
        {
            get { return _ImageConcurrency; }
            set { _ImageConcurrency = Math.Clamp(value, 1, 16); }
        }

        /// <summary>
        /// Videos transcoded at the same time. Default 1. Minimum 1, maximum 8.
        /// </summary>
        public int VideoConcurrency
        {
            get { return _VideoConcurrency; }
            set { _VideoConcurrency = Math.Clamp(value, 1, 8); }
        }

        /// <summary>
        /// How often the media worker looks for queued items (it is also signalled on complete). Default 5. Minimum 1, maximum 300.
        /// </summary>
        public int ProcessingIntervalSeconds
        {
            get { return _ProcessingIntervalSeconds; }
            set { _ProcessingIntervalSeconds = Math.Clamp(value, 1, 300); }
        }

        #endregion

        #region Private-Members

        private string _S3Region = "us-west-2";
        private string _SourceBucket = String.Empty;
        private string _VideoSourceBucket = String.Empty;
        private string _ImagesBucket = String.Empty;
        private string _TranscodedBucket = String.Empty;
        private string _RetainedBucket = String.Empty;
        private string _PublicBaseUrl = String.Empty;
        private string _FfmpegPath = "ffmpeg";
        private string _MediaConvertRoleArn = String.Empty;
        private string _MediaConvertQueueArn = String.Empty;
        private string _MediaConvertJobTemplate = "trelliskit-hls";
        private string _SnsTopicArn = String.Empty;
        private string _CloudFrontKeyPairId = String.Empty;
        private string _CloudFrontPrivateKeyPem = String.Empty;
        private string _CookieDomain = String.Empty;
        private int _GrantMinutes = 360;
        private int _UploadUrlMinutes = 60;
        private int _UploadExpiryHours = 24;
        private int _UnattachedRetentionHours = 168;
        private int _MaxImageBytes = 20971520;
        private int _MaxDocumentBytes = 26214400;
        private int _MaxImagePixels = 40000000;
        private int _MaxMemberVideoMegabytes = 2048;
        private int _MaxMemberVideoSeconds = 300;
        private int _MaxAdminVideoMegabytes = 0;
        private int _MaxAdminVideoSeconds = 0;
        private int _MultipartThresholdMegabytes = 32;
        private int _MultipartPartMegabytes = 16;
        private int _MemberDailyUploadMegabytes = 5120;
        private int _UploadsPerHour = 60;
        private int _ImageConcurrency = 2;
        private int _VideoConcurrency = 1;
        private int _ProcessingIntervalSeconds = 5;

        #endregion
    }
}
