namespace TrellisKit.Infra.Stacks
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Infra.Config;

    /// <summary>
    /// Settings of the MediaConvert job template the backend submits jobs from (setting media.mediaConvertJobTemplate,
    /// TK_MEDIACONVERT_JOB_TEMPLATE).
    ///
    /// Coupling with the backend (TrellisKit.Core MediaConvertTranscoder): CreateJob supplies the input and overrides
    /// only the destination of output group 0 and output group 1, matching them to this template by position. So:
    /// output group 0 must stay the HLS group (destination s3://&lt;transcoded&gt;/&lt;propertyId&gt;/video/&lt;id&gt;/master,
    /// written as master.m3u8 plus master_&lt;height&gt;p.m3u8 and master_&lt;height&gt;p_00001.ts in one flat folder) and
    /// output group 1 must stay the frame capture file group (destination s3://&lt;images&gt;/&lt;propertyId&gt;/media/&lt;id&gt;/poster,
    /// written as poster_frame.0000000.jpg and poster_thumb.0000000.jpg, which the backend renames to poster.jpg and
    /// poster_thumb.jpg). Reordering, inserting, or removing output groups breaks video processing. Output group 2, the
    /// progressive MP4 download rendition (download_720p.mp4, contract section 5.9), is appended only with
    /// MediaSettings.DownloadRendition, which waits for the backend to override that group's destination. The template carries
    /// no destinations of its own. Input 0 defines the audio selector the renditions reference; the job's input is
    /// merged with it.
    /// </summary>
    public static class MediaConvertJobTemplate
    {
        /// <summary>
        /// Name of the audio selector on the template input that every rendition reads.
        /// </summary>
        public static readonly string AudioSelectorName = "Audio Selector 1";

        /// <summary>
        /// Name of output group 0 (HLS).
        /// </summary>
        public static readonly string HlsGroupName = "HLS";

        /// <summary>
        /// Name of output group 1 (poster frame capture).
        /// </summary>
        public static readonly string PosterGroupName = "Poster";

        /// <summary>
        /// Name of output group 2 (progressive MP4 download, only with MediaSettings.DownloadRendition).
        /// </summary>
        public static readonly string DownloadGroupName = "Download";

        /// <summary>
        /// Name modifier of the download rendition: with the destination base name download it yields
        /// download_720p.mp4, the backend's MediaKeys.DownloadFileName.
        /// </summary>
        public static readonly string DownloadNameModifier = "_720p";

        /// <summary>
        /// Height of the download rendition (at most 720 lines, contract section 5.9).
        /// </summary>
        public static readonly int DownloadHeight = 720;

        /// <summary>
        /// Name modifier of the full-size poster capture.
        /// </summary>
        public static readonly string PosterNameModifier = "_frame";

        /// <summary>
        /// Name modifier of the poster thumbnail capture (the backend recognizes thumbnails by "thumb").
        /// </summary>
        public static readonly string PosterThumbNameModifier = "_thumb";

        /// <summary>
        /// Build the template settings (the JobTemplate SettingsJson, in MediaConvert's JSON field names).
        /// </summary>
        /// <param name="media">Media settings.</param>
        /// <returns>Settings object.</returns>
        /// <exception cref="ArgumentNullException">The settings are null.</exception>
        public static Dictionary<string, object> BuildSettings(MediaSettings media)
        {
            ArgumentNullException.ThrowIfNull(media);

            return new Dictionary<string, object>
            {
                ["TimecodeConfig"] = new Dictionary<string, object> { ["Source"] = "ZEROBASED" },
                ["Inputs"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["TimecodeSource"] = "ZEROBASED",
                        ["VideoSelector"] = new Dictionary<string, object> { ["Rotate"] = "AUTO" },
                        ["AudioSelectors"] = new Dictionary<string, object>
                        {
                            [AudioSelectorName] = new Dictionary<string, object> { ["DefaultSelection"] = "DEFAULT" }
                        }
                    }
                },
                ["OutputGroups"] = media.DownloadRendition
                    ? new object[] { HlsGroup(media), PosterGroup(media), DownloadGroup(media) }
                    : new object[] { HlsGroup(media), PosterGroup(media) }
            };
        }

        private static Dictionary<string, object> HlsGroup(MediaSettings media)
        {
            List<object> outputs = new List<object>();
            foreach (HlsRendition rendition in media.HlsRenditions)
            {
                outputs.Add(new Dictionary<string, object>
                {
                    ["NameModifier"] = rendition.NameModifier,
                    ["ContainerSettings"] = new Dictionary<string, object> { ["Container"] = "M3U8" },
                    ["VideoDescription"] = Video(media, rendition.Height, rendition.MaxBitrate),
                    ["AudioDescriptions"] = Audio(media)
                });
            }

            return new Dictionary<string, object>
            {
                ["Name"] = HlsGroupName,
                ["OutputGroupSettings"] = new Dictionary<string, object>
                {
                    ["Type"] = "HLS_GROUP_SETTINGS",
                    ["HlsGroupSettings"] = new Dictionary<string, object>
                    {
                        ["SegmentLength"] = media.HlsSegmentSeconds,
                        ["MinSegmentLength"] = 0,
                        ["SegmentControl"] = "SEGMENTED_FILES",
                        ["SegmentLengthControl"] = "GOP_MULTIPLE",
                        ["DirectoryStructure"] = "SINGLE_DIRECTORY",
                        ["OutputSelection"] = "MANIFESTS_AND_SEGMENTS",
                        ["ManifestDurationFormat"] = "INTEGER",
                        ["ManifestCompression"] = "NONE",
                        ["ProgramDateTime"] = "EXCLUDE",
                        ["CodecSpecification"] = "RFC_4281",
                        ["StreamInfResolution"] = "INCLUDE",
                        ["ClientCache"] = "ENABLED",
                        ["CaptionLanguageSetting"] = "OMIT"
                    }
                },
                ["Outputs"] = outputs.ToArray()
            };
        }

        private static Dictionary<string, object> Video(MediaSettings media, int height, int maxBitrate)
        {
            return new Dictionary<string, object>
            {
                // Height only: the width scales evenly from the input, so portrait video stays portrait.
                ["Height"] = height,
                ["ScalingBehavior"] = "DEFAULT",
                ["AntiAlias"] = "ENABLED",
                ["Sharpness"] = 50,
                ["TimecodeInsertion"] = "DISABLED",
                ["CodecSettings"] = new Dictionary<string, object>
                {
                    ["Codec"] = "H_264",
                    ["H264Settings"] = new Dictionary<string, object>
                    {
                        ["RateControlMode"] = "QVBR",
                        ["QvbrSettings"] = new Dictionary<string, object> { ["QvbrQualityLevel"] = media.QvbrQualityLevel },
                        ["MaxBitrate"] = maxBitrate,
                        ["CodecProfile"] = "HIGH",
                        ["CodecLevel"] = "AUTO",
                        ["GopSize"] = 2,
                        ["GopSizeUnits"] = "SECONDS",
                        ["SceneChangeDetect"] = "TRANSITION_DETECTION",
                        ["InterlaceMode"] = "PROGRESSIVE",
                        ["FramerateControl"] = "INITIALIZE_FROM_SOURCE",
                        ["ParControl"] = "INITIALIZE_FROM_SOURCE",
                        ["QualityTuningLevel"] = "SINGLE_PASS_HQ"
                    }
                }
            };
        }

        private static object[] Audio(MediaSettings media)
        {
            return new object[]
            {
                new Dictionary<string, object>
                {
                    ["AudioSourceName"] = AudioSelectorName,
                    ["CodecSettings"] = new Dictionary<string, object>
                    {
                        ["Codec"] = "AAC",
                        ["AacSettings"] = new Dictionary<string, object>
                        {
                            ["Bitrate"] = media.AudioBitrate,
                            ["CodingMode"] = "CODING_MODE_2_0",
                            ["SampleRate"] = 48000,
                            ["CodecProfile"] = "LC",
                            ["RateControlMode"] = "CBR"
                        }
                    }
                }
            };
        }

        private static Dictionary<string, object> DownloadGroup(MediaSettings media)
        {
            return new Dictionary<string, object>
            {
                ["Name"] = DownloadGroupName,
                ["OutputGroupSettings"] = new Dictionary<string, object>
                {
                    ["Type"] = "FILE_GROUP_SETTINGS",
                    ["FileGroupSettings"] = new Dictionary<string, object>()
                },
                ["Outputs"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["NameModifier"] = DownloadNameModifier,
                        ["Extension"] = "mp4",
                        ["ContainerSettings"] = new Dictionary<string, object>
                        {
                            ["Container"] = "MP4",
                            // Metadata up front, so playback and Range reads start before the whole file arrives.
                            ["Mp4Settings"] = new Dictionary<string, object> { ["MoovPlacement"] = "PROGRESSIVE_DOWNLOAD" }
                        },
                        ["VideoDescription"] = Video(media, DownloadHeight, media.DownloadMaxBitrate),
                        ["AudioDescriptions"] = Audio(media)
                    }
                }
            };
        }

        private static Dictionary<string, object> PosterGroup(MediaSettings media)
        {
            return new Dictionary<string, object>
            {
                ["Name"] = PosterGroupName,
                ["OutputGroupSettings"] = new Dictionary<string, object>
                {
                    ["Type"] = "FILE_GROUP_SETTINGS",
                    ["FileGroupSettings"] = new Dictionary<string, object>()
                },
                ["Outputs"] = new object[]
                {
                    FrameCapture(PosterNameModifier, media.PosterWidth),
                    FrameCapture(PosterThumbNameModifier, media.PosterThumbWidth)
                }
            };
        }

        private static Dictionary<string, object> FrameCapture(string nameModifier, int width)
        {
            return new Dictionary<string, object>
            {
                ["NameModifier"] = nameModifier,
                ["Extension"] = "jpg",
                ["ContainerSettings"] = new Dictionary<string, object> { ["Container"] = "RAW" },
                ["VideoDescription"] = new Dictionary<string, object>
                {
                    // Width only: the height scales evenly from the input.
                    ["Width"] = width,
                    ["ScalingBehavior"] = "DEFAULT",
                    ["AntiAlias"] = "ENABLED",
                    ["Sharpness"] = 50,
                    ["CodecSettings"] = new Dictionary<string, object>
                    {
                        ["Codec"] = "FRAME_CAPTURE",
                        ["FrameCaptureSettings"] = new Dictionary<string, object>
                        {
                            ["FramerateNumerator"] = 1,
                            ["FramerateDenominator"] = 1,
                            ["MaxCaptures"] = 1,
                            ["Quality"] = 80
                        }
                    }
                }
            };
        }
    }
}
