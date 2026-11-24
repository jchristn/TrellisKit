namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using System.Text;

    /// <summary>
    /// Magic-byte checks of uploaded media (contract section 4.4): JPEG FF D8 FF, the PNG signature, WebP
    /// RIFF....WEBP, and an ISO BMFF ftyp box for MP4 and QuickTime (QuickTime files without ftyp may also start with
    /// a moov, mdat, wide, free, or skip atom). Thread safety: stateless; safe to call concurrently.
    /// </summary>
    public static class MediaSniffer
    {
        #region Public-Members

        /// <summary>
        /// Bytes needed for a sniff.
        /// </summary>
        public const int HeaderLength = 16;

        #endregion

        #region Public-Methods

        /// <summary>
        /// The content type the bytes really are, from the accepted set.
        /// </summary>
        /// <param name="header">First bytes of the file (at least 12).</param>
        /// <returns>image/jpeg, image/png, image/webp, video/mp4 (any ISO BMFF with ftyp), video/quicktime (QuickTime without ftyp), or null.</returns>
        public static string? Detect(byte[]? header)
        {
            if (header == null) return null;
            // Document media (docs/PHASE4_CONTRACT.md section 5.3): PDF starts with %PDF-, WebVTT with WEBVTT (after an optional UTF-8 BOM).
            if (header.Length >= 5 && header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46 && header[4] == 0x2D) return "application/pdf";
            int bom = header.Length >= 3 && header[0] == 0xEF && header[1] == 0xBB && header[2] == 0xBF ? 3 : 0;
            if (header.Length >= bom + 6 && Encoding.ASCII.GetString(header, bom, 6) == "WEBVTT") return "text/vtt";
            if (header.Length < 12) return null;
            if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF) return "image/jpeg";
            if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A) return "image/png";
            if (header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 && header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50) return "image/webp";
            string box = Encoding.ASCII.GetString(header, 4, 4);
            if (box == "ftyp") return "video/mp4";
            if (box == "moov" || box == "mdat" || box == "wide" || box == "free" || box == "skip") return "video/quicktime";
            return null;
        }

        /// <summary>
        /// Whether the bytes match a declared content type. MP4 and QuickTime both accept any ISO BMFF file with an
        /// ftyp box; QuickTime also accepts the older atom layouts.
        /// </summary>
        /// <param name="contentType">Declared content type.</param>
        /// <param name="header">First bytes.</param>
        /// <returns>True when they match.</returns>
        public static bool Matches(string contentType, byte[]? header)
        {
            string? detected = Detect(header);
            if (detected == null) return false;
            string declared = (contentType ?? String.Empty).Trim().ToLowerInvariant();
            if (declared == "video/mp4") return detected == "video/mp4";
            if (declared == "video/quicktime") return detected == "video/mp4" || detected == "video/quicktime";
            return declared == detected;
        }

        #endregion
    }
}
