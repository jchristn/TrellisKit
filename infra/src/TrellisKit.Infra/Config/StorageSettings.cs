namespace TrellisKit.Infra.Config
{
    using System;

    /// <summary>
    /// S3 lifecycle settings.
    /// </summary>
    public class StorageSettings
    {
        private int _AbortIncompleteMultipartUploadDays = 1;
        private int _IngestInfrequentAccessDays = 30;
        private int _NoncurrentVersionExpirationDays = 30;
        private int _AccessLogExpirationDays = 90;
        private int _SourceArchiveDays = 180;
        private int _RetainedExpirationDays = 365;

        /// <summary>
        /// Abort multipart uploads that were never completed after this many days. Default 1. Minimum 1, maximum 30.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int AbortIncompleteMultipartUploadDays
        {
            get { return _AbortIncompleteMultipartUploadDays; }
            set { _AbortIncompleteMultipartUploadDays = Guard.Range(value, 1, 30, nameof(AbortIncompleteMultipartUploadDays)); }
        }

        /// <summary>
        /// Move uploaded originals in the source and video source buckets to S3 Standard-IA after this many days. Default 30.
        /// Minimum 30 (the S3 minimum for Standard-IA), maximum 3650.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int IngestInfrequentAccessDays
        {
            get { return _IngestInfrequentAccessDays; }
            set { _IngestInfrequentAccessDays = Guard.Range(value, 30, 3650, nameof(IngestInfrequentAccessDays)); }
        }

        /// <summary>
        /// Delete noncurrent object versions in versioned buckets after this many days. Default 30. Minimum 1, maximum 3650.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int NoncurrentVersionExpirationDays
        {
            get { return _NoncurrentVersionExpirationDays; }
            set { _NoncurrentVersionExpirationDays = Guard.Range(value, 1, 3650, nameof(NoncurrentVersionExpirationDays)); }
        }

        /// <summary>
        /// Delete S3, ALB, and CloudFront access logs after this many days. Default 90. Minimum 1, maximum 3650.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int AccessLogExpirationDays
        {
            get { return _AccessLogExpirationDays; }
            set { _AccessLogExpirationDays = Guard.Range(value, 1, 3650, nameof(AccessLogExpirationDays)); }
        }

        /// <summary>
        /// Move originals in the source and video source buckets to S3 Glacier Instant Retrieval after this many
        /// days. Originals are never expired (every original upload is kept). Default 180. Minimum 90 (at least
        /// 60 days after the Standard-IA transition), maximum 3650.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int SourceArchiveDays
        {
            get { return _SourceArchiveDays; }
            set { _SourceArchiveDays = Guard.Range(value, 90, 3650, nameof(SourceArchiveDays)); }
        }

        /// <summary>
        /// Delete objects in the retained bucket (content removed by moderators, kept for review and appeals) after
        /// this many days. Default 365. Minimum 30, maximum 3650.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int RetainedExpirationDays
        {
            get { return _RetainedExpirationDays; }
            set { _RetainedExpirationDays = Guard.Range(value, 30, 3650, nameof(RetainedExpirationDays)); }
        }
    }
}
