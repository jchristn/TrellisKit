namespace TrellisKit.Server.Services.Symbolication
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// A source map (revision 3) decoder for server-side symbolication: reads sources, names, and the Base64 VLQ mappings,
    /// and maps generated positions back to original ones. Only the generated lines asked for are kept, so a single-line
    /// Hermes bundle map with millions of segments is scanned once without materializing it. Index maps (sections, each an
    /// embedded map at a generated line and column offset; nested up to 4 levels, url sections are not supported) are
    /// decoded section by section. Thread safety: immutable after construction.
    /// </summary>
    public class SourceMapDecoder
    {
        #region Public-Members

        /// <summary>
        /// Original sources (sourceRoot applied).
        /// </summary>
        public IReadOnlyList<string> Sources { get; }

        /// <summary>
        /// Original names.
        /// </summary>
        public IReadOnlyList<string> Names { get; }

        #endregion

        #region Private-Members

        private const string _Base64 = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
        private static readonly int[] _Digits = BuildDigits();
        private const int _MaxSectionDepth = 4;
        private const int _MaxSections = 10000;
        private readonly string _Mappings;
        private readonly List<int> _SectionLines = new List<int>();
        private readonly List<int> _SectionColumns = new List<int>();
        private readonly List<SourceMapDecoder> _Sections = new List<SourceMapDecoder>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a decoder over parsed source map fields.
        /// </summary>
        /// <param name="sources">Sources.</param>
        /// <param name="names">Names.</param>
        /// <param name="mappings">The mappings string.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public SourceMapDecoder(List<string> sources, List<string> names, string mappings)
        {
            Sources = sources ?? throw new ArgumentNullException(nameof(sources));
            Names = names ?? throw new ArgumentNullException(nameof(names));
            _Mappings = mappings ?? throw new ArgumentNullException(nameof(mappings));
        }

        /// <summary>
        /// Instantiate an index map decoder over its sections (generated offsets ascending).
        /// </summary>
        /// <param name="lines">Generated line offset of each section (0-based).</param>
        /// <param name="columns">Generated column offset of each section (0-based).</param>
        /// <param name="sections">The section maps.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ArgumentException">The lists differ in length.</exception>
        public SourceMapDecoder(List<int> lines, List<int> columns, List<SourceMapDecoder> sections)
        {
            ArgumentNullException.ThrowIfNull(lines);
            ArgumentNullException.ThrowIfNull(columns);
            ArgumentNullException.ThrowIfNull(sections);
            if (lines.Count != sections.Count || columns.Count != sections.Count) throw new ArgumentException("Every section needs a line and a column offset.", nameof(sections));
            _Mappings = String.Empty;
            _SectionLines.AddRange(lines);
            _SectionColumns.AddRange(columns);
            _Sections.AddRange(sections);
            Sources = sections.SelectMany(x => x.Sources).ToList();
            Names = sections.SelectMany(x => x.Names).ToList();
        }

        /// <summary>
        /// Parse source map JSON: a regular map with mappings, or an index map with sections.
        /// </summary>
        /// <param name="json">UTF-8 source map bytes.</param>
        /// <returns>Decoder.</returns>
        /// <exception cref="ArgumentNullException">The bytes are null.</exception>
        /// <exception cref="FormatException">The document is not a version 3 source map with mappings or sections.</exception>
        public static SourceMapDecoder Parse(byte[] json)
        {
            ArgumentNullException.ThrowIfNull(json);
            try
            {
                using (JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 }))
                {
                    return Parse(document.RootElement, 0);
                }
            }
            catch (JsonException e)
            {
                throw new FormatException("The source map is not valid JSON.", e);
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Map generated positions (0-based line and column) to original positions. Each result is the segment with the
        /// greatest generated column not after the asked column on that line, or null when the line has none.
        /// </summary>
        /// <param name="positions">Generated lines and columns, 0-based, as [line, column] pairs.</param>
        /// <returns>Original positions (1-based line and column) in the same order.</returns>
        /// <exception cref="ArgumentNullException">The positions are null.</exception>
        public List<OriginalPositionView?> Lookup(List<int[]> positions)
        {
            ArgumentNullException.ThrowIfNull(positions);
            if (_Sections.Count > 0) return LookupSections(positions);
            HashSet<int> wanted = new HashSet<int>(positions.Where(p => p != null && p.Length == 2 && p[0] >= 0).Select(p => p[0]));
            Dictionary<int, List<int[]>> lines = Decode(wanted);
            List<OriginalPositionView?> results = new List<OriginalPositionView?>();
            foreach (int[] p in positions)
            {
                if (p == null || p.Length != 2 || !lines.TryGetValue(p[0], out List<int[]>? segments) || segments.Count == 0)
                {
                    results.Add(null);
                    continue;
                }
                int[]? best = null;
                foreach (int[] segment in segments)
                {
                    if (segment[0] > p[1]) break;
                    if (segment.Length >= 4) best = segment;
                }
                if (best == null || best[1] < 0 || best[1] >= Sources.Count)
                {
                    results.Add(null);
                    continue;
                }
                results.Add(new OriginalPositionView
                {
                    Source = Sources[best[1]],
                    Line = best[2] + 1,
                    Column = best[3] + 1,
                    Name = best.Length >= 5 && best[4] >= 0 && best[4] < Names.Count ? Names[best[4]] : null
                });
            }
            return results;
        }

        #endregion

        #region Private-Methods

        private static SourceMapDecoder Parse(JsonElement root, int depth)
        {
            if (root.ValueKind != JsonValueKind.Object) throw new FormatException("Not a source map.");
            if (root.TryGetProperty("sections", out JsonElement sections))
            {
                if (sections.ValueKind != JsonValueKind.Array) throw new FormatException("sections must be an array.");
                if (depth >= _MaxSectionDepth) throw new FormatException("Index maps are nested too deeply.");
                if (sections.GetArrayLength() > _MaxSections) throw new FormatException("Too many sections.");
                List<int> lines = new List<int>();
                List<int> columns = new List<int>();
                List<SourceMapDecoder> maps = new List<SourceMapDecoder>();
                foreach (JsonElement section in sections.EnumerateArray())
                {
                    if (section.ValueKind != JsonValueKind.Object || !section.TryGetProperty("offset", out JsonElement offset) || offset.ValueKind != JsonValueKind.Object ||
                        !offset.TryGetProperty("line", out JsonElement l) || !l.TryGetInt32(out int line) || !offset.TryGetProperty("column", out JsonElement c) || !c.TryGetInt32(out int column) || line < 0 || column < 0)
                        throw new FormatException("Every section needs an offset with line and column.");
                    if (!section.TryGetProperty("map", out JsonElement map)) throw new FormatException("Sections with url are not supported; embed the map.");
                    if (lines.Count > 0 && (line < lines[lines.Count - 1] || (line == lines[lines.Count - 1] && column < columns[columns.Count - 1])))
                        throw new FormatException("Sections must be in generated order.");
                    lines.Add(line);
                    columns.Add(column);
                    maps.Add(Parse(map, depth + 1));
                }
                return new SourceMapDecoder(lines, columns, maps);
            }
            if (!root.TryGetProperty("mappings", out JsonElement m) || m.ValueKind != JsonValueKind.String) throw new FormatException("Not a source map with mappings or sections.");
            string sourceRoot = root.TryGetProperty("sourceRoot", out JsonElement r) && r.ValueKind == JsonValueKind.String ? r.GetString() ?? String.Empty : String.Empty;
            if (sourceRoot.Length > 0 && !sourceRoot.EndsWith("/", StringComparison.Ordinal)) sourceRoot += "/";
            List<string> sources = Strings(root, "sources").Select(s => sourceRoot + s).ToList();
            List<string> names = Strings(root, "names");
            return new SourceMapDecoder(sources, names, m.GetString() ?? String.Empty);
        }

        private List<OriginalPositionView?> LookupSections(List<int[]> positions)
        {
            // Each position belongs to the last section that starts at or before it; positions inside a section are made
            // relative to the section's offset (the column only on the section's first line).
            List<OriginalPositionView?> results = Enumerable.Repeat<OriginalPositionView?>(null, positions.Count).ToList();
            Dictionary<int, List<int>> bySection = new Dictionary<int, List<int>>();
            for (int i = 0; i < positions.Count; i++)
            {
                int[] p = positions[i];
                if (p == null || p.Length != 2 || p[0] < 0 || p[1] < 0) continue;
                int found = -1;
                int lo = 0;
                int hi = _Sections.Count - 1;
                while (lo <= hi)
                {
                    int mid = (lo + hi) / 2;
                    bool startsBefore = _SectionLines[mid] < p[0] || (_SectionLines[mid] == p[0] && _SectionColumns[mid] <= p[1]);
                    if (startsBefore)
                    {
                        found = mid;
                        lo = mid + 1;
                    }
                    else hi = mid - 1;
                }
                if (found < 0) continue;
                if (!bySection.TryGetValue(found, out List<int>? list))
                {
                    list = new List<int>();
                    bySection[found] = list;
                }
                list.Add(i);
            }
            foreach (KeyValuePair<int, List<int>> group in bySection)
            {
                int line = _SectionLines[group.Key];
                int column = _SectionColumns[group.Key];
                List<int[]> local = group.Value.Select(i => new[] { positions[i][0] - line, positions[i][0] == line ? positions[i][1] - column : positions[i][1] }).ToList();
                List<OriginalPositionView?> mapped = _Sections[group.Key].Lookup(local);
                for (int k = 0; k < group.Value.Count; k++) results[group.Value[k]] = mapped[k];
            }
            return results;
        }

        private Dictionary<int, List<int[]>> Decode(HashSet<int> wanted)
        {
            Dictionary<int, List<int[]>> lines = new Dictionary<int, List<int[]>>();
            if (wanted.Count == 0) return lines;
            int maxLine = wanted.Max();
            int line = 0;
            int column = 0;
            int source = 0;
            int originalLine = 0;
            int originalColumn = 0;
            int name = 0;
            int i = 0;
            int length = _Mappings.Length;
            List<int> fields = new List<int>(5);
            while (i < length && line <= maxLine)
            {
                char c = _Mappings[i];
                if (c == ';')
                {
                    line++;
                    column = 0;
                    i++;
                    continue;
                }
                if (c == ',')
                {
                    i++;
                    continue;
                }
                fields.Clear();
                while (i < length && _Mappings[i] != ',' && _Mappings[i] != ';')
                {
                    int value = 0;
                    int shift = 0;
                    bool more = true;
                    while (more)
                    {
                        if (i >= length) throw new FormatException("Truncated mappings.");
                        int digit = _Mappings[i] < 128 ? _Digits[_Mappings[i]] : -1;
                        if (digit < 0) throw new FormatException("Invalid mappings character.");
                        i++;
                        more = (digit & 32) != 0;
                        value += (digit & 31) << shift;
                        shift += 5;
                        if (shift > 30) throw new FormatException("Mappings value too large.");
                    }
                    fields.Add((value & 1) == 1 ? -(value >> 1) : value >> 1);
                }
                if (fields.Count == 0) continue;
                column += fields[0];
                int[] segment;
                if (fields.Count >= 4)
                {
                    source += fields[1];
                    originalLine += fields[2];
                    originalColumn += fields[3];
                    if (fields.Count >= 5)
                    {
                        name += fields[4];
                        segment = new[] { column, source, originalLine, originalColumn, name };
                    }
                    else segment = new[] { column, source, originalLine, originalColumn };
                }
                else segment = new[] { column };
                if (wanted.Contains(line))
                {
                    if (!lines.TryGetValue(line, out List<int[]>? list))
                    {
                        list = new List<int[]>();
                        lines[line] = list;
                    }
                    list.Add(segment);
                }
            }
            foreach (List<int[]> list in lines.Values) list.Sort((a, b) => a[0].CompareTo(b[0]));
            return lines;
        }

        private static List<string> Strings(JsonElement root, string property)
        {
            if (!root.TryGetProperty(property, out JsonElement list) || list.ValueKind != JsonValueKind.Array) return new List<string>();
            return list.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() ?? String.Empty : String.Empty).ToList();
        }

        private static int[] BuildDigits()
        {
            int[] digits = Enumerable.Repeat(-1, 128).ToArray();
            for (int i = 0; i < _Base64.Length; i++) digits[_Base64[i]] = i;
            return digits;
        }

        #endregion
    }
}
