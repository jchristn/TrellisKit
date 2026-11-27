namespace TrellisKit.Server.Services.Email
{
    using System;
    using System.Text.RegularExpressions;

    /// <summary>
    /// A short browser and operating system summary of a user agent for staff emails (docs/EMAIL.md, "Staff email details"), for example
    /// "Chrome 131 on macOS" or "Safari on iOS 18.1". Best effort: an unrecognized agent yields its first 60 characters.
    /// Thread safety: stateless.
    /// </summary>
    public static class UserAgentSummary
    {
        #region Private-Members

        private static readonly TimeSpan _Timeout = TimeSpan.FromMilliseconds(50);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Summarize a user agent.
        /// </summary>
        /// <param name="userAgent">User agent, or null.</param>
        /// <returns>Summary, or null when the agent is blank.</returns>
        public static string? Describe(string? userAgent)
        {
            if (String.IsNullOrWhiteSpace(userAgent)) return null;
            string agent = userAgent.Trim();
            string? browser = Browser(agent);
            string? os = OperatingSystem(agent);
            if (browser == null && os == null) return agent.Length > 60 ? agent.Substring(0, 60) : agent;
            if (browser == null) return os;
            if (os == null) return browser;
            return browser + " on " + os;
        }

        #endregion

        #region Private-Methods

        private static string? Browser(string agent)
        {
            string? edge = Version(agent, @"Edg(?:e|A|iOS)?/(\d+)");
            if (edge != null) return "Edge " + edge;
            string? opera = Version(agent, @"OPR/(\d+)");
            if (opera != null) return "Opera " + opera;
            string? firefox = Version(agent, @"(?:Firefox|FxiOS)/(\d+)");
            if (firefox != null) return "Firefox " + firefox;
            string? chrome = Version(agent, @"(?:Chrome|CriOS)/(\d+)");
            if (chrome != null) return "Chrome " + chrome;
            if (agent.Contains("Safari/", StringComparison.Ordinal))
            {
                string? safari = Version(agent, @"Version/(\d+(?:\.\d+)?)");
                return safari == null ? "Safari" : "Safari " + safari;
            }
            if (agent.StartsWith("okhttp/", StringComparison.OrdinalIgnoreCase)) return "Android app (okhttp)";
            if (agent.Contains("CFNetwork/", StringComparison.Ordinal)) return "iOS app (CFNetwork)";
            if (agent.Contains("Expo", StringComparison.Ordinal)) return "Expo app";
            if (agent.StartsWith("curl/", StringComparison.OrdinalIgnoreCase)) return "curl";
            if (agent.StartsWith("PostmanRuntime/", StringComparison.OrdinalIgnoreCase)) return "Postman";
            return null;
        }

        private static string? OperatingSystem(string agent)
        {
            string? ios = Version(agent, @"(?:iPhone|CPU) OS (\d+(?:_\d+)?)");
            if (ios != null) return (agent.Contains("iPad", StringComparison.Ordinal) ? "iPadOS " : "iOS ") + ios.Replace('_', '.');
            string? android = Version(agent, @"Android (\d+(?:\.\d+)?)");
            if (android != null) return "Android " + android;
            if (agent.Contains("Windows NT", StringComparison.Ordinal)) return "Windows";
            if (agent.Contains("Mac OS X", StringComparison.Ordinal) || agent.Contains("Macintosh", StringComparison.Ordinal)) return "macOS";
            if (agent.Contains("CrOS", StringComparison.Ordinal)) return "ChromeOS";
            if (agent.Contains("Linux", StringComparison.Ordinal)) return "Linux";
            return null;
        }

        private static string? Version(string agent, string pattern)
        {
            try
            {
                Match match = Regex.Match(agent, pattern, RegexOptions.CultureInvariant, _Timeout);
                return match.Success ? match.Groups[1].Value : null;
            }
            catch (RegexMatchTimeoutException)
            {
                return null;
            }
        }

        #endregion
    }
}
