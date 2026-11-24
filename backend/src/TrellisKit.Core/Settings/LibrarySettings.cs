namespace TrellisKit.Core.Settings
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Settings of the library (series, lessons, progress, downloads, document text) (docs/PHASE4_CONTRACT.md section 12).
    /// </summary>
    public class LibrarySettings
    {
        #region Public-Members

        /// <summary>
        /// Series a property may have. Key library.maxSeries. Default 500. Minimum 1, maximum 5000.
        /// </summary>
        public int MaxSeries
        {
            get { return _MaxSeries; }
            set { _MaxSeries = Math.Clamp(value, 1, 5000); }
        }

        /// <summary>
        /// Lessons one series may hold. Key library.maxLessonsPerSeries. Default 200. Minimum 1, maximum 1000.
        /// </summary>
        public int MaxLessonsPerSeries
        {
            get { return _MaxLessonsPerSeries; }
            set { _MaxLessonsPerSeries = Math.Clamp(value, 1, 1000); }
        }

        /// <summary>
        /// Chapter markers one lesson may carry. Key library.maxMarkersPerLesson. Default 50. Minimum 0, maximum 200.
        /// </summary>
        public int MaxMarkersPerLesson
        {
            get { return _MaxMarkersPerLesson; }
            set { _MaxMarkersPerLesson = Math.Clamp(value, 0, 200); }
        }

        /// <summary>
        /// Documents, links, and references one lesson may carry. Key library.maxResourcesPerLesson. Default 10. Minimum 0, maximum 50.
        /// </summary>
        public int MaxResourcesPerLesson
        {
            get { return _MaxResourcesPerLesson; }
            set { _MaxResourcesPerLesson = Math.Clamp(value, 0, 50); }
        }

        /// <summary>
        /// Longest lesson description (articles in full), in characters. Key library.maxArticleLength. Default 50000. Minimum 1000, maximum 200000.
        /// </summary>
        public int MaxArticleLength
        {
            get { return _MaxArticleLength; }
            set { _MaxArticleLength = Math.Clamp(value, 1000, 200000); }
        }

        /// <summary>
        /// A lesson completes when min(furthest position, watched time) reaches this share of its duration. Key library.completionThresholdPercent. Default 90. Minimum 50, maximum 100.
        /// </summary>
        public int CompletionThresholdPercent
        {
            get { return _CompletionThresholdPercent; }
            set { _CompletionThresholdPercent = Math.Clamp(value, 50, 100); }
        }

        /// <summary>
        /// How often players report progress; playedSeconds above twice this value is clamped. Key library.progressIntervalSeconds. Default 15. Minimum 5, maximum 120.
        /// </summary>
        public int ProgressIntervalSeconds
        {
            get { return _ProgressIntervalSeconds; }
            set { _ProgressIntervalSeconds = Math.Clamp(value, 5, 120); }
        }

        /// <summary>
        /// A position within this many seconds of the end resumes from the start. Key library.resumeTailSeconds. Default 10. Minimum 0, maximum 120.
        /// </summary>
        public int ResumeTailSeconds
        {
            get { return _ResumeTailSeconds; }
            set { _ResumeTailSeconds = Math.Clamp(value, 0, 120); }
        }

        /// <summary>
        /// Members follow a series automatically when they complete a lesson in it. Key library.autoFollowOnProgress. Default true.
        /// </summary>
        public bool AutoFollowOnProgress { get; set; } = true;

        /// <summary>
        /// Default commentsEnabled of new lessons. Key library.commentsEnabledByDefault. Default true.
        /// </summary>
        public bool CommentsEnabledByDefault { get; set; } = true;

        /// <summary>
        /// Members may attach media to lesson comments. Key library.commentMedia. Default true.
        /// </summary>
        public bool CommentMedia { get; set; } = true;

        /// <summary>
        /// Moderator holders with resourceId * moderate lesson comments and reports. Key library.forumModeratorsModerateComments. Default true.
        /// </summary>
        public bool ForumModeratorsModerateComments { get; set; } = true;

        /// <summary>
        /// Members with progress keep Continue watching and direct links to archived lessons for this many days. Key library.archivedPlaybackDays. Default 365. Minimum 0, maximum 3650.
        /// </summary>
        public int ArchivedPlaybackDays
        {
            get { return _ArchivedPlaybackDays; }
            set { _ArchivedPlaybackDays = Math.Clamp(value, 0, 3650); }
        }

        /// <summary>
        /// Serve Public lessons on the anonymous public routes. Key library.publicSamplesEnabled. Default true.
        /// </summary>
        public bool PublicSamplesEnabled { get; set; } = true;

        /// <summary>
        /// Mobile apps may download lessons for offline viewing (files are not DRM protected; the terms of use should forbid redistribution). Key library.downloads.enabled. Default false.
        /// </summary>
        public bool DownloadsEnabled { get; set; } = false;

        /// <summary>
        /// How long a download license lasts before renewal. Key library.downloads.licenseDays. Default 30. Minimum 1, maximum 365.
        /// </summary>
        public int DownloadLicenseDays
        {
            get { return _DownloadLicenseDays; }
            set { _DownloadLicenseDays = Math.Clamp(value, 1, 365); }
        }

        /// <summary>
        /// Lifetime of the signed download URL. Key library.downloads.urlMinutes. Default 30. Minimum 1, maximum 1440.
        /// </summary>
        public int DownloadUrlMinutes
        {
            get { return _DownloadUrlMinutes; }
            set { _DownloadUrlMinutes = Math.Clamp(value, 1, 1440); }
        }

        /// <summary>
        /// Active download licenses one member may hold. Key library.downloads.maxActivePerMember. Default 25. Minimum 1, maximum 1000.
        /// </summary>
        public int MaxActiveDownloadsPerMember
        {
            get { return _MaxActiveDownloadsPerMember; }
            set { _MaxActiveDownloadsPerMember = Math.Clamp(value, 1, 1000); }
        }

        /// <summary>
        /// Devices with active download licenses per member. Key library.downloads.maxDevicesPerMember. Default 3. Minimum 1, maximum 20.
        /// </summary>
        public int MaxDownloadDevicesPerMember
        {
            get { return _MaxDownloadDevicesPerMember; }
            set { _MaxDownloadDevicesPerMember = Math.Clamp(value, 1, 20); }
        }

        /// <summary>
        /// Download licenses one member may request per day. Key library.downloads.perDay. Default 20. Minimum 1, maximum 1000.
        /// </summary>
        public int DownloadsPerDay
        {
            get { return _DownloadsPerDay; }
            set { _DownloadsPerDay = Math.Clamp(value, 1, 1000); }
        }

        /// <summary>
        /// Revoked and expired licenses are deleted after this many days. Key library.downloads.pruneDays. Default 30. Minimum 1, maximum 3650.
        /// </summary>
        public int DownloadPruneDays
        {
            get { return _DownloadPruneDays; }
            set { _DownloadPruneDays = Math.Clamp(value, 1, 3650); }
        }

        /// <summary>
        /// Progress updates per member and lesson per minute. Key library.progressPerMinute. Default 12. Minimum 1, maximum 600.
        /// </summary>
        public int ProgressPerMinute
        {
            get { return _ProgressPerMinute; }
            set { _ProgressPerMinute = Math.Clamp(value, 1, 600); }
        }

        /// <summary>
        /// Progress updates per member per hour. Key library.progressPerHour. Default 600. Minimum 1, maximum 100000.
        /// </summary>
        public int ProgressPerHour
        {
            get { return _ProgressPerHour; }
            set { _ProgressPerHour = Math.Clamp(value, 1, 100000); }
        }

        /// <summary>
        /// Progress replayed from an offline queue is accepted when its clientUpdatedUtc is at most this old. Key library.offlineReplayDays. Default 7. Minimum 1, maximum 30.
        /// </summary>
        public int OfflineReplayDays
        {
            get { return _OfflineReplayDays; }
            set { _OfflineReplayDays = Math.Clamp(value, 1, 30); }
        }

        /// <summary>
        /// How often scheduled and processing lessons are checked for publication. Key library.publishIntervalSeconds. Default 60. Minimum 5, maximum 3600.
        /// </summary>
        public int PublishIntervalSeconds
        {
            get { return _PublishIntervalSeconds; }
            set { _PublishIntervalSeconds = Math.Clamp(value, 5, 3600); }
        }

        /// <summary>
        /// Hours between library_digest emails for members who turned Library email on (168 is weekly). Key library.digestIntervalHours. Default 168. Minimum 24, maximum 720.
        /// </summary>
        public int DigestIntervalHours
        {
            get { return _DigestIntervalHours; }
            set { _DigestIntervalHours = Math.Clamp(value, 24, 720); }
        }

        /// <summary>
        /// Most lessons listed in one digest; the rest are counted. Key library.digestMaxItems. Default 20. Minimum 1, maximum 100.
        /// </summary>
        public int DigestMaxItems
        {
            get { return _DigestMaxItems; }
            set { _DigestMaxItems = Math.Clamp(value, 1, 100); }
        }

        /// <summary>
        /// Extract the text of lesson captions and PDF resources for search and assistant grounding. Key documents.textEnabled. Default true.
        /// </summary>
        public bool DocumentTextEnabled { get; set; } = true;

        /// <summary>
        /// Directory for temporary files during PDF extraction. Empty uses the system temporary directory. Key documents.tempDirectory. Default empty. Never null.
        /// </summary>
        public string DocumentTempDirectory
        {
            get { return _DocumentTempDirectory; }
            set { _DocumentTempDirectory = value ?? String.Empty; }
        }

        /// <summary>
        /// Extracted text beyond this length is dropped. Key documents.maxCharacters. Default 200000. Minimum 1000, maximum 2000000.
        /// </summary>
        public int DocumentMaxCharacters
        {
            get { return _DocumentMaxCharacters; }
            set { _DocumentMaxCharacters = Math.Clamp(value, 1000, 2000000); }
        }

        /// <summary>
        /// Character budget of the local paragraph and sentence chunker. Key documents.chunkCharacters. Default 1500. Minimum 200, maximum 20000.
        /// </summary>
        public int DocumentChunkCharacters
        {
            get { return _DocumentChunkCharacters; }
            set { _DocumentChunkCharacters = Math.Clamp(value, 200, 20000); }
        }

        /// <summary>
        /// Attempts before a document extraction is marked Failed. Key documents.maxAttempts. Default 3. Minimum 1, maximum 20.
        /// </summary>
        public int DocumentMaxAttempts
        {
            get { return _DocumentMaxAttempts; }
            set { _DocumentMaxAttempts = Math.Clamp(value, 1, 20); }
        }

        /// <summary>
        /// Chunk (and optionally summarize) document text with Partio; the local chunker is used when off or when Partio fails. Key documents.partio.enabled. Default false.
        /// </summary>
        public bool PartioEnabled { get; set; } = false;

        /// <summary>
        /// Base URL of the Partio REST API, for example https://partio.example.test. Key documents.partio.baseUrl. Default empty. Never null.
        /// </summary>
        public string PartioBaseUrl
        {
            get { return _PartioBaseUrl; }
            set { _PartioBaseUrl = value ?? String.Empty; }
        }

        /// <summary>
        /// Bearer token for Partio. Supply it through the environment from a secret store. Key documents.partio.apiKey. Default empty. Never null.
        /// </summary>
        public string PartioApiKey
        {
            get { return _PartioApiKey; }
            set { _PartioApiKey = value ?? String.Empty; }
        }

        /// <summary>
        /// FixedTokenCount, SentenceBased, or ParagraphBased. Key documents.partio.strategy. Default FixedTokenCount.
        /// </summary>
        public PartioChunkingStrategyEnum PartioStrategy { get; set; } = PartioChunkingStrategyEnum.FixedTokenCount;

        /// <summary>
        /// FixedTokenCount of the chunking configuration. Key documents.partio.chunkTokens. Default 256. Minimum 16, maximum 8192.
        /// </summary>
        public int PartioChunkTokens
        {
            get { return _PartioChunkTokens; }
            set { _PartioChunkTokens = Math.Clamp(value, 16, 8192); }
        }

        /// <summary>
        /// OverlapCount of the chunking configuration. Key documents.partio.overlapTokens. Default 0. Minimum 0, maximum 1024.
        /// </summary>
        public int PartioOverlapTokens
        {
            get { return _PartioOverlapTokens; }
            set { _PartioOverlapTokens = Math.Clamp(value, 0, 1024); }
        }

        /// <summary>
        /// Store a Partio summary of each document (needs a completion endpoint id). Key documents.partio.summarize. Default false.
        /// </summary>
        public bool PartioSummarize { get; set; } = false;

        /// <summary>
        /// CompletionEndpointId used for summaries (for example cep_...). Key documents.partio.completionEndpointId. Default empty. Never null.
        /// </summary>
        public string PartioCompletionEndpointId
        {
            get { return _PartioCompletionEndpointId; }
            set { _PartioCompletionEndpointId = value ?? String.Empty; }
        }

        /// <summary>
        /// MaxSummaryTokens of the summarization configuration. Key documents.partio.maxSummaryTokens. Default 1024. Minimum 64, maximum 8192.
        /// </summary>
        public int PartioMaxSummaryTokens
        {
            get { return _PartioMaxSummaryTokens; }
            set { _PartioMaxSummaryTokens = Math.Clamp(value, 64, 8192); }
        }

        /// <summary>
        /// Timeout of one Partio request. Key documents.partio.timeoutSeconds. Default 30. Minimum 1, maximum 600.
        /// </summary>
        public int PartioTimeoutSeconds
        {
            get { return _PartioTimeoutSeconds; }
            set { _PartioTimeoutSeconds = Math.Clamp(value, 1, 600); }
        }

        #endregion

        #region Private-Members

        private int _MaxSeries = 500;
        private int _MaxLessonsPerSeries = 200;
        private int _MaxMarkersPerLesson = 50;
        private int _MaxResourcesPerLesson = 10;
        private int _MaxArticleLength = 50000;
        private int _CompletionThresholdPercent = 90;
        private int _ProgressIntervalSeconds = 15;
        private int _ResumeTailSeconds = 10;
        private int _ArchivedPlaybackDays = 365;
        private int _DownloadLicenseDays = 30;
        private int _DownloadUrlMinutes = 30;
        private int _MaxActiveDownloadsPerMember = 25;
        private int _MaxDownloadDevicesPerMember = 3;
        private int _DownloadsPerDay = 20;
        private int _DownloadPruneDays = 30;
        private int _ProgressPerMinute = 12;
        private int _ProgressPerHour = 600;
        private int _OfflineReplayDays = 7;
        private int _PublishIntervalSeconds = 60;
        private int _DigestIntervalHours = 168;
        private int _DigestMaxItems = 20;
        private string _DocumentTempDirectory = String.Empty;
        private int _DocumentMaxCharacters = 200000;
        private int _DocumentChunkCharacters = 1500;
        private int _DocumentMaxAttempts = 3;
        private string _PartioBaseUrl = String.Empty;
        private string _PartioApiKey = String.Empty;
        private int _PartioChunkTokens = 256;
        private int _PartioOverlapTokens = 0;
        private string _PartioCompletionEndpointId = String.Empty;
        private int _PartioMaxSummaryTokens = 1024;
        private int _PartioTimeoutSeconds = 30;

        #endregion
    }
}
