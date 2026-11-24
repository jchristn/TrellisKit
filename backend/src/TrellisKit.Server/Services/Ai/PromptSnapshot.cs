namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Models;

    /// <summary>
    /// The saved prompts of a property as loaded at one time (immutable; replaced as a whole).
    /// </summary>
    internal sealed class PromptSnapshot
    {
        internal PromptSnapshot(Dictionary<string, AiPromptTemplate> saved, DateTime loadedUtc)
        {
            Saved = saved ?? throw new ArgumentNullException(nameof(saved));
            LoadedUtc = loadedUtc;
        }

        internal Dictionary<string, AiPromptTemplate> Saved { get; }

        internal DateTime LoadedUtc { get; }
    }
}
