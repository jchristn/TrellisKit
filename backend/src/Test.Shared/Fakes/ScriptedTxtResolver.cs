namespace Test.Shared.Fakes
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Integrations.Interfaces;

    /// <summary>
    /// DNS TXT resolver with scripted answers (domain verification tests). Unknown names have no records; a name scripted
    /// with null fails the lookup.
    /// </summary>
    public sealed class ScriptedTxtResolver : IDnsTxtResolver
    {
        #region Private-Members

        private readonly ConcurrentDictionary<string, List<string>?> _Records = new ConcurrentDictionary<string, List<string>?>(StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Script the TXT values of a name (null makes the lookup fail).
        /// </summary>
        /// <param name="name">Name.</param>
        /// <param name="values">Values, or null.</param>
        public void Set(string name, params string[]? values)
        {
            _Records[name] = values?.ToList();
        }

        /// <inheritdoc />
        public Task<List<string>> ResolveTxtAsync(string name, CancellationToken token = default)
        {
            if (!_Records.TryGetValue(name, out List<string>? values)) return Task.FromResult(new List<string>());
            if (values == null) throw new InvalidOperationException("Scripted DNS failure.");
            return Task.FromResult(new List<string>(values));
        }

        #endregion
    }
}
