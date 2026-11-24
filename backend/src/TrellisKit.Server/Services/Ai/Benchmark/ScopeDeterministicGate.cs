namespace TrellisKit.Server.Services.Ai.Benchmark
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;

    /// <summary>
    /// The deterministic half of the scope benchmark: every case through <see cref="OffScopeDetector.IsCodeRequest"/>
    /// and every reply probe through <see cref="ScopeOutputCheck.RejectReason"/>, scored against the set's
    /// deterministic thresholds. No model, judge, or clock is involved, so the result is reproducible and gates CI and
    /// every prompt publish. Thread safety: stateless.
    /// </summary>
    public static class ScopeDeterministicGate
    {
        #region Public-Members

        /// <summary>
        /// Layer name of the input detector.
        /// </summary>
        public const string DetectorLayer = "detector";

        /// <summary>
        /// Layer name of the post-call check.
        /// </summary>
        public const string OutputLayer = "outputCheck";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run the gate.
        /// </summary>
        /// <param name="set">Benchmark set.</param>
        /// <param name="allowCode">Evaluate the property policy that permits code.</param>
        /// <returns>Report.</returns>
        /// <exception cref="ArgumentNullException">set is null.</exception>
        public static ScopeGateReport Evaluate(ScopeBenchmarkSet set, bool allowCode = false)
        {
            ArgumentNullException.ThrowIfNull(set);
            ScopeGateReport report = new ScopeGateReport { Dataset = set.Name, DatasetVersion = set.Version, DatasetSha256 = set.Sha256, DatasetOverlay = set.Overlay };
            Dictionary<string, ScopeLayerScore> scores = new Dictionary<string, ScopeLayerScore>(StringComparer.Ordinal);
            int codeCases = 0;
            int codeRefused = 0;
            foreach (ScopeBenchmarkCase c in set.Cases)
            {
                bool flagged = !allowCode && OffScopeDetector.IsCodeRequest(c.Text);
                string expected = c.Expect.Detector;
                bool correct = expected == ScopeBenchmark.Any || (expected == ScopeBenchmark.Block) == flagged;
                Tally(scores, c.Category, DetectorLayer, expected, c.Split, flagged, correct);
                if (expected == ScopeBenchmark.Allow && flagged)
                {
                    report.DetectorFalsePositives++;
                    report.Misses.Add(c.Id + " (" + c.Split + "): refused but in scope");
                }
                if (c.Category == ScopeBenchmark.CodeRequest)
                {
                    codeCases++;
                    if (flagged) codeRefused++;
                    else report.Misses.Add(c.Id + " (" + c.Split + "): code request not refused");
                }
            }
            int rejectProbes = 0;
            int rejected = 0;
            foreach (ScopeBenchmarkReply r in set.Replies)
            {
                string? reason = ScopeOutputCheck.RejectReason(r.Text, allowCode);
                bool flagged = reason != null;
                bool mustReject = r.Expect.OutputCheck == ScopeBenchmark.Reject;
                Tally(scores, r.Category, OutputLayer, r.Expect.OutputCheck, r.Split, flagged, mustReject == flagged);
                if (mustReject)
                {
                    rejectProbes++;
                    if (flagged) rejected++;
                    else report.Misses.Add(r.Id + " (" + r.Split + "): reply not rejected");
                }
                else if (flagged)
                {
                    report.OutputFalsePositives++;
                    report.Misses.Add(r.Id + " (" + r.Split + "): clean reply rejected (" + reason + ")");
                }
            }

            report.CodeRequestRecall = codeCases == 0 ? 0 : Math.Round((double)codeRefused / codeCases, 4);
            report.OutputRejectRecall = rejectProbes == 0 ? 0 : Math.Round((double)rejected / rejectProbes, 4);
            List<string> order = ScopeBenchmark.CaseCategories.Concat(ScopeBenchmark.ReplyCategories).ToList();
            report.Scores = scores.Values.OrderBy(s => order.IndexOf(s.Category)).ThenBy(s => s.Layer, StringComparer.Ordinal).ToList();

            ScopeDeterministicThresholds t = set.Thresholds.Deterministic;
            if (report.DetectorFalsePositives > t.DetectorFalsePositivesMax)
                report.Failures.Add("Detector false positives " + report.DetectorFalsePositives + " exceed " + t.DetectorFalsePositivesMax + ".");
            if (report.CodeRequestRecall < t.CodeRequestRecallMin)
                report.Failures.Add("Code request recall " + Percent(report.CodeRequestRecall) + " is below " + Percent(t.CodeRequestRecallMin) + ".");
            if (report.OutputRejectRecall < t.OutputRejectRecallMin)
                report.Failures.Add("Post-call reject recall " + Percent(report.OutputRejectRecall) + " is below " + Percent(t.OutputRejectRecallMin) + ".");
            if (report.OutputFalsePositives > t.OutputFalsePositivesMax)
                report.Failures.Add("Post-call false positives " + report.OutputFalsePositives + " exceed " + t.OutputFalsePositivesMax + ".");
            report.Passed = report.Failures.Count == 0;
            return report;
        }

        /// <summary>
        /// The report as Markdown: provenance, the thresholds that failed, the per-category table, and the misses.
        /// </summary>
        /// <param name="report">Report.</param>
        /// <returns>Markdown.</returns>
        /// <exception cref="ArgumentNullException">report is null.</exception>
        public static string ToMarkdown(ScopeGateReport report)
        {
            ArgumentNullException.ThrowIfNull(report);
            StringBuilder sb = new StringBuilder();
            sb.Append("# Scope gate: ").Append(report.Passed ? "passed" : "failed").Append('\n').Append('\n');
            sb.Append("| Dataset | Version | Overlay | SHA-256 |\n|---|---|---|---|\n");
            sb.Append("| ").Append(report.Dataset).Append(" | ").Append(report.DatasetVersion).Append(" | ").Append(report.DatasetOverlay ?? "none").Append(" | ").Append(report.DatasetSha256).Append(" |\n\n");
            sb.Append("Detector false positives: ").Append(report.DetectorFalsePositives).Append(". Code request recall: ").Append(Percent(report.CodeRequestRecall));
            sb.Append(". Post-call reject recall: ").Append(Percent(report.OutputRejectRecall)).Append(". Post-call false positives: ").Append(report.OutputFalsePositives).Append(".\n\n");
            foreach (string failure in report.Failures) sb.Append("- FAILED: ").Append(failure).Append('\n');
            if (report.Failures.Count > 0) sb.Append('\n');
            sb.Append("| Category | Layer | Expected | Cases | Flagged | Correct | Tune correct | Holdout correct |\n|---|---|---|---|---|---|---|---|\n");
            foreach (ScopeLayerScore s in report.Scores)
            {
                sb.Append("| ").Append(s.Category).Append(" | ").Append(s.Layer).Append(" | ").Append(s.Expected).Append(" | ").Append(s.Cases).Append(" | ").Append(s.Flagged);
                sb.Append(" | ").Append(s.Correct).Append(" | ").Append(s.TuneCorrect).Append('/').Append(s.TuneCases).Append(" | ").Append(s.HoldoutCorrect).Append('/').Append(s.HoldoutCases).Append(" |\n");
            }
            if (report.Misses.Count > 0)
            {
                sb.Append("\nMisses:\n\n");
                foreach (string miss in report.Misses) sb.Append("- ").Append(miss).Append('\n');
            }

            return sb.ToString();
        }

        /// <summary>
        /// Percent text with one decimal (for example 97.5%).
        /// </summary>
        /// <param name="rate">Rate (0 to 1).</param>
        /// <returns>Text.</returns>
        public static string Percent(double rate)
        {
            return (rate * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";
        }

        #endregion

        #region Private-Methods

        private static void Tally(Dictionary<string, ScopeLayerScore> scores, string category, string layer, string expected, string split, bool flagged, bool correct)
        {
            string key = category + "|" + layer;
            if (!scores.TryGetValue(key, out ScopeLayerScore? score))
            {
                score = new ScopeLayerScore { Category = category, Layer = layer, Expected = expected };
                scores[key] = score;
            }
            if (score.Expected != expected) score.Expected = "Mixed";
            score.Cases++;
            if (flagged) score.Flagged++;
            if (correct) score.Correct++;
            if (split == ScopeBenchmark.Holdout)
            {
                score.HoldoutCases++;
                if (correct) score.HoldoutCorrect++;
            }
            else
            {
                score.TuneCases++;
                if (correct) score.TuneCorrect++;
            }
        }

        #endregion
    }
}
