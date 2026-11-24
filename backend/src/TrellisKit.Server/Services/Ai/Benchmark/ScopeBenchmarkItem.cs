namespace TrellisKit.Server.Services.Ai.Benchmark
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The payload of one ai.scopeBenchmark job item: the case and the prompt versions resolved when the run was planned.
    /// </summary>
    public class ScopeBenchmarkItem
    {
        #region Public-Members

        /// <summary>
        /// Case identifier.
        /// </summary>
        public string CaseId { get; set; } = String.Empty;

        /// <summary>
        /// Prompt versions of the run. Never null.
        /// </summary>
        public List<ScopePromptVersion> Prompts { get; set; } = new List<ScopePromptVersion>();

        #endregion
    }
}
