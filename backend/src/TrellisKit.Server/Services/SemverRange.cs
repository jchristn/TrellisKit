namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;

    /// <summary>
    /// npm-style semver ranges for the appVersion targeting condition: comparators (&gt;=, &lt;=, &gt;, &lt;, =),
    /// caret and tilde ranges, x-ranges (1.x, *), hyphen ranges (1.2.0 - 2.0.0), space-separated intersections, and
    /// || unions. Versions compare numerically on major.minor.patch; pre-release suffixes are ignored.
    /// Thread safety: immutable after parsing.
    /// </summary>
    public class SemverRange
    {
        #region Private-Members

        private readonly List<List<KeyValuePair<string, int[]>>> _Sets;

        #endregion

        #region Constructors-and-Factories

        private SemverRange(List<List<KeyValuePair<string, int[]>>> sets)
        {
            _Sets = sets;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Parse a range.
        /// </summary>
        /// <param name="text">Range text, for example &gt;=1.2.0 &lt;2.0.0.</param>
        /// <param name="range">Parsed range, or null.</param>
        /// <returns>True when valid.</returns>
        public static bool TryParse(string? text, out SemverRange? range)
        {
            range = null;
            if (String.IsNullOrWhiteSpace(text) || text.Length > 200) return false;
            List<List<KeyValuePair<string, int[]>>> sets = new List<List<KeyValuePair<string, int[]>>>();
            foreach (string alternative in text.Split("||"))
            {
                string a = alternative.Trim();
                List<KeyValuePair<string, int[]>> set = new List<KeyValuePair<string, int[]>>();
                if (a.Length == 0 || a == "*" || a.Equals("x", StringComparison.OrdinalIgnoreCase))
                {
                    sets.Add(set);
                    continue;
                }
                string[] hyphen = a.Split(" - ");
                if (hyphen.Length == 2)
                {
                    int[]? low = Version(hyphen[0].Trim(), out int _);
                    int[]? high = Version(hyphen[1].Trim(), out int highParts);
                    if (low == null || high == null) return false;
                    set.Add(new KeyValuePair<string, int[]>(">=", low));
                    set.Add(highParts >= 3 ? new KeyValuePair<string, int[]>("<=", high) : new KeyValuePair<string, int[]>("<", Bump(high, highParts)));
                    sets.Add(set);
                    continue;
                }
                foreach (string token in a.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!AddComparator(token, set)) return false;
                }
                sets.Add(set);
            }
            range = new SemverRange(sets);
            return true;
        }

        /// <summary>
        /// Whether a version satisfies the range.
        /// </summary>
        /// <param name="version">Version, for example 1.3.0.</param>
        /// <returns>True when satisfied; false for a malformed version.</returns>
        public bool Satisfies(string? version)
        {
            int[]? v = Version(version, out int _);
            if (v == null) return false;
            foreach (List<KeyValuePair<string, int[]>> set in _Sets)
            {
                bool ok = true;
                foreach (KeyValuePair<string, int[]> c in set)
                {
                    int cmp = Compare(v, c.Value);
                    bool pass = c.Key switch { ">=" => cmp >= 0, "<=" => cmp <= 0, ">" => cmp > 0, "<" => cmp < 0, _ => cmp == 0 };
                    if (!pass)
                    {
                        ok = false;
                        break;
                    }
                }
                if (ok) return true;
            }
            return false;
        }

        /// <summary>
        /// Compare two version strings numerically.
        /// </summary>
        /// <param name="a">First version.</param>
        /// <param name="b">Second version.</param>
        /// <returns>Negative, 0, or positive; null when either is not a version.</returns>
        public static int? CompareVersions(string? a, string? b)
        {
            int[]? x = Version(a, out int _);
            int[]? y = Version(b, out int _);
            if (x == null || y == null) return null;
            return Compare(x, y);
        }

        #endregion

        #region Private-Methods

        private static bool AddComparator(string token, List<KeyValuePair<string, int[]>> set)
        {
            string op = "=";
            string rest = token;
            foreach (string candidate in new[] { ">=", "<=", ">", "<", "=", "^", "~" })
            {
                if (token.StartsWith(candidate, StringComparison.Ordinal))
                {
                    op = candidate;
                    rest = token.Substring(candidate.Length);
                    break;
                }
            }
            if (rest == "*" || rest.Equals("x", StringComparison.OrdinalIgnoreCase)) return true;
            int[]? v = Version(rest, out int parts);
            if (v == null) return false;
            switch (op)
            {
                case "^":
                    set.Add(new KeyValuePair<string, int[]>(">=", v));
                    set.Add(new KeyValuePair<string, int[]>("<", v[0] > 0 || parts == 1 ? new[] { v[0] + 1, 0, 0 } : v[1] > 0 || parts == 2 ? new[] { 0, v[1] + 1, 0 } : new[] { 0, 0, v[2] + 1 }));
                    return true;
                case "~":
                    set.Add(new KeyValuePair<string, int[]>(">=", v));
                    set.Add(new KeyValuePair<string, int[]>("<", parts == 1 ? new[] { v[0] + 1, 0, 0 } : new[] { v[0], v[1] + 1, 0 }));
                    return true;
                case "=":
                    if (parts < 3)
                    {
                        set.Add(new KeyValuePair<string, int[]>(">=", v));
                        set.Add(new KeyValuePair<string, int[]>("<", Bump(v, parts)));
                    }
                    else set.Add(new KeyValuePair<string, int[]>("=", v));
                    return true;
                default:
                    set.Add(new KeyValuePair<string, int[]>(op, v));
                    return true;
            }
        }

        private static int[] Bump(int[] v, int parts)
        {
            if (parts <= 1) return new[] { v[0] + 1, 0, 0 };
            if (parts == 2) return new[] { v[0], v[1] + 1, 0 };
            return new[] { v[0], v[1], v[2] + 1 };
        }

        private static int[]? Version(string? text, out int parts)
        {
            parts = 0;
            if (String.IsNullOrWhiteSpace(text)) return null;
            string t = text.Trim().TrimStart('v', 'V');
            int dash = t.IndexOfAny(new[] { '-', '+' });
            if (dash >= 0) t = t.Substring(0, dash);
            string[] pieces = t.Split('.');
            if (pieces.Length == 0 || pieces.Length > 3) return null;
            int[] v = new int[3];
            for (int i = 0; i < pieces.Length; i++)
            {
                if (pieces[i] == "x" || pieces[i] == "X" || pieces[i] == "*") break;
                if (!Int32.TryParse(pieces[i], NumberStyles.None, CultureInfo.InvariantCulture, out int n)) return null;
                v[i] = n;
                parts = i + 1;
            }
            return parts == 0 ? null : v;
        }

        private static int Compare(int[] a, int[] b)
        {
            for (int i = 0; i < 3; i++)
            {
                int d = a[i].CompareTo(b[i]);
                if (d != 0) return d;
            }
            return 0;
        }

        #endregion
    }
}
