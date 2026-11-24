namespace TrellisKit.Infra.Config
{
    using System;
    using System.Collections.Generic;
    using Amazon.CDK.AWS.CloudFront;

    /// <summary>
    /// Media CDN and transcoding pipeline settings.
    /// </summary>
    public class MediaSettings
    {
        /// <summary>
        /// Placeholder RSA public key whose private half was discarded. It lets the stack deploy, but signed cookies
        /// cannot be produced until it is replaced with the public half of a key pair you control.
        /// </summary>
        public static readonly string PlaceholderSigningPublicKeyPem =
            "-----BEGIN PUBLIC KEY-----\n" +
            "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEApk5VTaShvon0sUUWsTwc\n" +
            "f5PX/OU5AvN8Y2gL8u9agtBXXVF/95cmIQPjleKEQ6/wFFbzK1WmLXvr1d5AKP+p\n" +
            "uAVtYdfW+pVTYBOczjMJiG5W+xFDsZqgJAkgpwTGF4XEOPSu1dpFvhH24RoOOB7g\n" +
            "pkR5njnjP1d5p+1KckWJJaZdX7REKjE9cQFBaQ5Z11Na77CDqLBbCwYYc+48WP0x\n" +
            "QUoLsXEQ8D5ASo6PAYGnWQ9dTQDTely8mc0c9gKzTSbmnpESdBRhlQnG4ltwsLun\n" +
            "tSSQ+NQ3VtBGFSu1Eovf2W5/bHt4Oi23KaXDsuEExDMO4PM9Knb/rf9lIXdsvRvO\n" +
            "EwIDAQAB\n" +
            "-----END PUBLIC KEY-----\n";

        private string _SigningPublicKeyPem = PlaceholderSigningPublicKeyPem;
        private string _WebhookPath = "/api/v1/webhooks/mediaconvert";
        private string _PublicPathPattern = "/*/public/*";
        private string _VideoPathPattern = "/*/video/*";
        private string _PublicVideoPathPattern = "/*/public/video/*";
        private string _DocumentPathPattern = "/*/media/*.pdf";
        private string _PublicDocumentPathPattern = "/*/public/media/*.pdf";
        private int _DownloadMaxBitrate = 3000000;
        private List<string> _JobStatuses = new List<string> { "PROGRESSING", "COMPLETE", "ERROR", "CANCELED" };
        private int _CorsMaxAgeSeconds = 3600;
        private string _JobTemplateSuffix = "hls";
        private List<HlsRendition> _HlsRenditions = new List<HlsRendition>
        {
            new HlsRendition(1080, 6000000),
            new HlsRendition(720, 3000000),
            new HlsRendition(360, 800000)
        };
        private int _HlsSegmentSeconds = 6;
        private int _QvbrQualityLevel = 7;
        private int _AudioBitrate = 96000;
        private int _PosterWidth = 1280;
        private int _PosterThumbWidth = 320;

        /// <summary>
        /// PEM public key of the CloudFront key group that validates signed cookies. Default is a placeholder
        /// (see <see cref="PlaceholderSigningPublicKeyPem"/>). Override with the mediaSigningPublicKeyPem context value.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string SigningPublicKeyPem
        {
            get { return _SigningPublicKeyPem; }
            set { _SigningPublicKeyPem = Guard.NotEmpty(value, nameof(SigningPublicKeyPem)); }
        }

        /// <summary>
        /// API path that receives MediaConvert job state changes through the SNS HTTPS subscription.
        /// Default /api/v1/webhooks/mediaconvert.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string WebhookPath
        {
            get { return _WebhookPath; }
            set { _WebhookPath = Guard.NotEmpty(value, nameof(WebhookPath)); }
        }

        /// <summary>
        /// Path pattern served from the images bucket without signed cookies (public sample posts and marketing previews). Default /*/public/*
        /// (every media key starts with the property id, see the backend's MediaKeys helper).
        /// Everything else on the media host requires a valid signed cookie.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string PublicPathPattern
        {
            get { return _PublicPathPattern; }
            set { _PublicPathPattern = Guard.NotEmpty(value, nameof(PublicPathPattern)); }
        }

        /// <summary>
        /// Path pattern served from the transcoded bucket (HLS renditions) with signed cookies. Default /*/video/*
        /// (property id first).
        /// Every other path is served from the images bucket.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string VideoPathPattern
        {
            get { return _VideoPathPattern; }
            set { _VideoPathPattern = Guard.NotEmpty(value, nameof(VideoPathPattern)); }
        }

        /// <summary>
        /// Path pattern served from the transcoded bucket without signed cookies (public sample posts).
        /// Default /*/public/video/* (property id first). Must precede <see cref="VideoPathPattern"/> and
        /// <see cref="PublicPathPattern"/>, which also match these paths.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string PublicVideoPathPattern
        {
            get { return _PublicVideoPathPattern; }
            set { _PublicVideoPathPattern = Guard.NotEmpty(value, nameof(PublicVideoPathPattern)); }
        }

        /// <summary>
        /// Path pattern of document media (PDF) in the images bucket, served with signed cookies like images and with the
        /// document response headers policy (Content-Disposition: attachment, Content-Security-Policy: sandbox,
        /// X-Content-Type-Options: nosniff), the headers the backend sends for PDFs (docs/PHASE4_CONTRACT.md section 5.3).
        /// Default /*/media/*.pdf (property id first; the backend stores documents as media/{id}/file.pdf).
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string DocumentPathPattern
        {
            get { return _DocumentPathPattern; }
            set { _DocumentPathPattern = Guard.NotEmpty(value, nameof(DocumentPathPattern)); }
        }

        /// <summary>
        /// Path pattern of public document copies (documents of a Public lesson), served without signed cookies and with
        /// the document response headers policy. Default /*/public/media/*.pdf. Must precede
        /// <see cref="DocumentPathPattern"/> and <see cref="PublicPathPattern"/>, which also match these paths.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string PublicDocumentPathPattern
        {
            get { return _PublicDocumentPathPattern; }
            set { _PublicDocumentPathPattern = Guard.NotEmpty(value, nameof(PublicDocumentPathPattern)); }
        }

        /// <summary>
        /// Add output group 2 to the job template: one progressive MP4 (download_720p.mp4, H.264 and AAC, 720 lines) for
        /// offline lesson downloads (docs/PHASE4_CONTRACT.md section 5.9). Default false, and keep it false until the
        /// backend's MediaConvertTranscoder overrides the destination of output group 2 with
        /// s3://&lt;transcoded&gt;/&lt;propertyId&gt;/video/&lt;id&gt;/download: the template carries no destinations, and
        /// today the backend overrides only groups 0 and 1, so jobs would fail. Groups 0 (HLS) and 1 (poster) never move.
        /// </summary>
        public bool DownloadRendition { get; set; } = false;

        /// <summary>
        /// Peak bitrate of the download rendition in bits per second. Default 3000000. Minimum 500000, maximum 20000000.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int DownloadMaxBitrate
        {
            get { return _DownloadMaxBitrate; }
            set { _DownloadMaxBitrate = Guard.Range(value, 500000, 20000000, nameof(DownloadMaxBitrate)); }
        }

        /// <summary>
        /// MediaConvert job statuses forwarded to the webhook. Default PROGRESSING, COMPLETE, ERROR, CANCELED.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public List<string> JobStatuses
        {
            get { return _JobStatuses; }
            set { _JobStatuses = Guard.NotNull(value, nameof(JobStatuses)); }
        }

        /// <summary>
        /// Browser cache time for CORS preflight responses from the media host. Default 3600. Minimum 0, maximum 86400.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int CorsMaxAgeSeconds
        {
            get { return _CorsMaxAgeSeconds; }
            set { _CorsMaxAgeSeconds = Guard.Range(value, 0, 86400, nameof(CorsMaxAgeSeconds)); }
        }

        /// <summary>
        /// Suffix of the MediaConvert job template and queue names (resource prefix plus suffix, for example
        /// example-prod-hls). Default hls.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string JobTemplateSuffix
        {
            get { return _JobTemplateSuffix; }
            set { _JobTemplateSuffix = Guard.NotEmpty(value, nameof(JobTemplateSuffix)); }
        }

        /// <summary>
        /// HLS ladder of the job template, highest first. Default 1080p (6 Mbps peak), 720p (3 Mbps), 360p (0.8 Mbps).
        /// At least one, at most eight.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The list is empty or has more than eight renditions.</exception>
        public List<HlsRendition> HlsRenditions
        {
            get { return _HlsRenditions; }
            set
            {
                ArgumentNullException.ThrowIfNull(value, nameof(HlsRenditions));
                if (value.Count < 1 || value.Count > 8) throw new ArgumentOutOfRangeException(nameof(HlsRenditions), "Between one and eight HLS renditions are required.");
                _HlsRenditions = value;
            }
        }

        /// <summary>
        /// HLS segment length in seconds. Default 6. Minimum 1, maximum 30.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int HlsSegmentSeconds
        {
            get { return _HlsSegmentSeconds; }
            set { _HlsSegmentSeconds = Guard.Range(value, 1, 30, nameof(HlsSegmentSeconds)); }
        }

        /// <summary>
        /// QVBR quality level of every rendition (higher is better and larger). Default 7. Minimum 1, maximum 10.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int QvbrQualityLevel
        {
            get { return _QvbrQualityLevel; }
            set { _QvbrQualityLevel = Guard.Range(value, 1, 10, nameof(QvbrQualityLevel)); }
        }

        /// <summary>
        /// AAC stereo audio bitrate of every rendition in bits per second. Default 96000. Minimum 64000, maximum 320000.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int AudioBitrate
        {
            get { return _AudioBitrate; }
            set { _AudioBitrate = Guard.Range(value, 64000, 320000, nameof(AudioBitrate)); }
        }

        /// <summary>
        /// Width of the poster frame capture in pixels (backend poster.jpg). Default 1280. Minimum 160, maximum 3840.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int PosterWidth
        {
            get { return _PosterWidth; }
            set { _PosterWidth = Guard.Range(value, 160, 3840, nameof(PosterWidth)); }
        }

        /// <summary>
        /// Width of the poster thumbnail frame capture in pixels (backend poster_thumb.jpg). Default 320. Minimum 64,
        /// maximum 1280.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int PosterThumbWidth
        {
            get { return _PosterThumbWidth; }
            set { _PosterThumbWidth = Guard.Range(value, 64, 1280, nameof(PosterThumbWidth)); }
        }

        /// <summary>
        /// CloudFront edge locations used by the media distribution. Default PRICE_CLASS_100 (North America and Europe).
        /// </summary>
        public PriceClass PriceClass { get; set; } = PriceClass.PRICE_CLASS_100;
    }
}
