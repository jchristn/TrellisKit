namespace TrellisKit.Infra.Config
{
    using System;
    using System.Globalization;

    /// <summary>
    /// One rung of the HLS ladder in the MediaConvert job template. The width follows the input aspect ratio.
    /// </summary>
    public class HlsRendition
    {
        private int _Height = 720;
        private int _MaxBitrate = 3000000;

        /// <summary>
        /// Output height in pixels; the width scales evenly from the input. Default 720. Minimum 144, maximum 2160.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int Height
        {
            get { return _Height; }
            set { _Height = Guard.Range(value, 144, 2160, nameof(Height)); }
        }

        /// <summary>
        /// Peak video bitrate in bits per second (QVBR ceiling). Default 3000000. Minimum 100000, maximum 20000000.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int MaxBitrate
        {
            get { return _MaxBitrate; }
            set { _MaxBitrate = Guard.Range(value, 100000, 20000000, nameof(MaxBitrate)); }
        }

        /// <summary>
        /// Name modifier of the rendition's playlist and segments, for example _720p (master_720p.m3u8,
        /// master_720p_00001.ts).
        /// </summary>
        public string NameModifier
        {
            get { return "_" + _Height.ToString(CultureInfo.InvariantCulture) + "p"; }
        }

        /// <summary>
        /// Instantiate a rendition with the defaults.
        /// </summary>
        public HlsRendition()
        {
        }

        /// <summary>
        /// Instantiate a rendition.
        /// </summary>
        /// <param name="height">Output height in pixels.</param>
        /// <param name="maxBitrate">Peak video bitrate in bits per second.</param>
        /// <exception cref="ArgumentOutOfRangeException">A value is out of range.</exception>
        public HlsRendition(int height, int maxBitrate)
        {
            Height = height;
            MaxBitrate = maxBitrate;
        }
    }
}
