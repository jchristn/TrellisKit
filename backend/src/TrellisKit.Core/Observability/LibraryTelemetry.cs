namespace TrellisKit.Core.Observability
{
    using System;
    using System.Diagnostics.Metrics;

    /// <summary>
    /// Library instruments on the TrellisKit meter (docs/PHASE4_CONTRACT.md section 13). Labels are low-cardinality only
    /// (never ids, slugs, or text); every value carries the trelliskit.property label through
    /// <see cref="TrellisKitTelemetry.Add"/>. Thread safety: instruments are thread-safe.
    /// </summary>
    public static class LibraryTelemetry
    {
        #region Public-Members

        /// <summary>
        /// Playback sessions started, labeled platform (web, ios, android) and kind (video, article).
        /// </summary>
        public static readonly Counter<long> Plays = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.library.plays", "{play}", "Lesson playback sessions started.");

        /// <summary>
        /// Progress updates, labeled outcome (accepted, stale, clamped).
        /// </summary>
        public static readonly Counter<long> Progress = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.library.progress", "{update}", "Lesson progress updates.");

        /// <summary>
        /// Lesson completions, labeled source (threshold, manual).
        /// </summary>
        public static readonly Counter<long> Completions = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.library.completions", "{completion}", "Lesson completions.");

        /// <summary>
        /// Download license events, labeled action (issue, renew, revoke, expire) and platform.
        /// </summary>
        public static readonly Counter<long> Downloads = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.library.downloads", "{license}", "Lesson download license events.");

        /// <summary>
        /// Lessons published, labeled kind (video, article).
        /// </summary>
        public static readonly Counter<long> LessonsPublished = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.library.lessons.published", "{lesson}", "Lessons published.");

        /// <summary>
        /// Document text extractions, labeled content_type (pdf, vtt), chunker (partio, local), and outcome (ready, failed, skipped, retry).
        /// </summary>
        public static readonly Counter<long> DocumentExtractions = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.library.documents.extractions", "{document}", "Document text extractions.");

        /// <summary>
        /// Staff reads of member viewing history, labeled scope (member, lesson).
        /// </summary>
        public static readonly Counter<long> ViewingHistoryReads = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.library.viewing_history.reads", "{read}", "Audited staff reads of viewing history.");

        #endregion

        #region Public-Methods

        /// <summary>
        /// Add one to a counter with label pairs (name, value, name, value, ...).
        /// </summary>
        /// <param name="counter">Counter.</param>
        /// <param name="labels">Label names and values.</param>
        public static void Count(Counter<long> counter, params string[] labels)
        {
            ArgumentNullException.ThrowIfNull(counter);
            TrellisKitTelemetry.Add(counter, 1, labels);
        }

        #endregion
    }
}
