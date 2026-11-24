namespace TrellisKit.Infra.Config
{
    using System;
    using System.Text.Json;

    /// <summary>
    /// How a property's DNS is managed: Route 53 hosted zones created by the stacks (the default), or records the
    /// operator creates at an external DNS provider from the stack outputs. Read from the dns section of a property's
    /// deploy/infra.json, the dns section of a deployment file (wins for every listed property), and the dnsMode
    /// context value (wins over both).
    /// Thread safety: not thread safe; configure before building stacks.
    /// </summary>
    public class DnsSettings
    {
        private int _CertificateWaitSeconds = 600;
        private int _ValidationRecordWaitSeconds = 120;

        /// <summary>
        /// DNS mode. Default <see cref="DnsModeEnum.Route53"/>, which keeps every record inside AWS.
        /// </summary>
        public DnsModeEnum Mode { get; set; } = DnsModeEnum.Route53;

        /// <summary>
        /// True when the operator manages DNS outside AWS.
        /// </summary>
        public bool IsExternal
        {
            get { return Mode == DnsModeEnum.External; }
        }

        /// <summary>
        /// External mode only: how long the certificate check of a stack that uses a certificate (Api, Web, Media,
        /// Redirect) waits for the certificate to be issued before it fails with an explanation. Default 600.
        /// Minimum 0 (check once), maximum 840 (the check runs in a Lambda function, limited to 900 seconds).
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int CertificateWaitSeconds
        {
            get { return _CertificateWaitSeconds; }
            set { _CertificateWaitSeconds = Guard.Range(value, 0, 840, nameof(CertificateWaitSeconds)); }
        }

        /// <summary>
        /// External mode only: how long a certificate request waits for ACM to publish the validation records it
        /// returns as stack outputs (normally a few seconds). Default 120. Minimum 10, maximum 600.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int ValidationRecordWaitSeconds
        {
            get { return _ValidationRecordWaitSeconds; }
            set { _ValidationRecordWaitSeconds = Guard.Range(value, 10, 600, nameof(ValidationRecordWaitSeconds)); }
        }

        /// <summary>
        /// A copy of these settings (each environment gets its own instance).
        /// </summary>
        /// <returns>Copy.</returns>
        public DnsSettings Clone()
        {
            DnsSettings copy = new DnsSettings();
            copy.Mode = Mode;
            copy.CertificateWaitSeconds = CertificateWaitSeconds;
            copy.ValidationRecordWaitSeconds = ValidationRecordWaitSeconds;
            return copy;
        }

        /// <summary>
        /// Parse a DNS mode name (Route53 or External, case-insensitive).
        /// </summary>
        /// <param name="value">Mode name.</param>
        /// <param name="source">Where the value came from, used in the error message.</param>
        /// <returns>Mode.</returns>
        /// <exception cref="ArgumentException">The value is not a known mode.</exception>
        public static DnsModeEnum ParseMode(string? value, string source)
        {
            string text = (value ?? String.Empty).Trim();
            if (String.Equals(text, DnsModeEnum.Route53.ToString(), StringComparison.OrdinalIgnoreCase)) return DnsModeEnum.Route53;
            if (String.Equals(text, DnsModeEnum.External.ToString(), StringComparison.OrdinalIgnoreCase)) return DnsModeEnum.External;
            throw new ArgumentException("dns.mode in " + source + " must be Route53 or External, got '" + text + "'.");
        }

        /// <summary>
        /// Read the dns section of a deploy/infra.json or deployment file. Returns null when the file has no dns.mode,
        /// so the caller can tell an explicit value from the default.
        /// </summary>
        /// <param name="root">Root element of the file.</param>
        /// <param name="file">File path, used in error messages.</param>
        /// <returns>Settings, or null when dns.mode is absent.</returns>
        /// <exception cref="ArgumentException">A value is invalid.</exception>
        public static DnsSettings? FromJson(JsonElement root, string file)
        {
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("dns", out JsonElement dns) || dns.ValueKind != JsonValueKind.Object) return null;
            if (!dns.TryGetProperty("mode", out JsonElement mode)) return null;
            if (mode.ValueKind != JsonValueKind.String) throw new ArgumentException("dns.mode in " + file + " must be the string Route53 or External.");

            DnsSettings settings = new DnsSettings();
            settings.Mode = ParseMode(mode.GetString(), file);
            try
            {
                if (dns.TryGetProperty("certificateWaitSeconds", out JsonElement wait) && wait.ValueKind == JsonValueKind.Number)
                {
                    settings.CertificateWaitSeconds = wait.GetInt32();
                }
            }
            catch (ArgumentException e)
            {
                throw new ArgumentException("Invalid dns section in " + file + ": " + e.Message, e);
            }

            return settings;
        }
    }
}
