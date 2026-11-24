namespace TrellisKit.Server.Services.Symbolication
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text.RegularExpressions;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Parses JavaScript stack traces as Hermes, V8, and JavaScriptCore print them into frames with a file, a 1-based line,
    /// and a 1-based column: "at fn (file:line:col)", "at fn (address at file:line:col)", "at file:line:col", and
    /// "fn@file:line:col". Other lines (the error message, native frames) are kept as frames without a position.
    /// Thread safety: stateless.
    /// </summary>
    public static class StackTraceParser
    {
        #region Public-Members

        /// <summary>
        /// Most frames parsed from one stack trace.
        /// </summary>
        public const int MaxFrames = 200;

        #endregion

        #region Private-Members

        private static readonly Regex _Wrapped = new Regex("^\\s*at\\s+(?<fn>.*?)\\s+\\((?:address at\\s+)?(?<file>[^()]*?):(?<line>\\d+):(?<col>\\d+)\\)\\s*$", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        private static readonly Regex _Bare = new Regex("^\\s*at\\s+(?:address at\\s+)?(?<file>[^()\\s]+?):(?<line>\\d+):(?<col>\\d+)\\s*$", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        private static readonly Regex _Jsc = new Regex("^\\s*(?<fn>[^@\\s]*)@(?<file>.*?):(?<line>\\d+):(?<col>\\d+)\\s*$", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        #endregion

        #region Public-Methods

        /// <summary>
        /// Parse a stack trace into at most <see cref="MaxFrames"/> frames.
        /// </summary>
        /// <param name="stackTrace">Stack trace text.</param>
        /// <param name="truncated">True when lines beyond the bound were dropped.</param>
        /// <returns>Frames in order.</returns>
        public static List<SymbolicatedFrameView> Parse(string? stackTrace, out bool truncated)
        {
            List<SymbolicatedFrameView> frames = new List<SymbolicatedFrameView>();
            truncated = false;
            if (String.IsNullOrWhiteSpace(stackTrace)) return frames;
            foreach (string raw in stackTrace.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            {
                string line = raw.TrimEnd();
                if (line.Trim().Length == 0) continue;
                if (frames.Count >= MaxFrames)
                {
                    truncated = true;
                    break;
                }
                frames.Add(ParseLine(line));
            }
            return frames;
        }

        #endregion

        #region Private-Methods

        private static SymbolicatedFrameView ParseLine(string line)
        {
            SymbolicatedFrameView frame = new SymbolicatedFrameView { Raw = line.Length > 2000 ? line.Substring(0, 2000) : line };
            Match m = _Wrapped.Match(line);
            if (!m.Success) m = _Bare.Match(line);
            if (!m.Success) m = _Jsc.Match(line);
            if (!m.Success) return frame;
            string fn = m.Groups["fn"].Success ? m.Groups["fn"].Value.Trim() : String.Empty;
            frame.Function = fn.Length == 0 ? null : fn;
            frame.File = m.Groups["file"].Value.Trim();
            if (Int32.TryParse(m.Groups["line"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int number)) frame.Line = number;
            if (Int32.TryParse(m.Groups["col"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int column)) frame.Column = column;
            return frame;
        }

        #endregion
    }
}
