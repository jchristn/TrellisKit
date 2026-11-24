namespace Test.Shared.Fakes
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Net;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Integrations.Interfaces;

    /// <summary>
    /// DNS resolver with scripted answers per host. Each host has a queue of answers; the last answer repeats.
    /// Unknown hosts resolve to nothing. Counts lookups per host.
    /// </summary>
    public sealed class ScriptedDnsResolver : IDnsResolver
    {
        private readonly ConcurrentDictionary<string, List<IPAddress[]>> _Answers = new ConcurrentDictionary<string, List<IPAddress[]>>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, int> _Lookups = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Script the answers for a host, in lookup order.
        /// </summary>
        /// <param name="host">Host.</param>
        /// <param name="answers">One answer per lookup, each a comma-separated address list.</param>
        public void Script(string host, params string[] answers)
        {
            List<IPAddress[]> list = new List<IPAddress[]>();
            foreach (string answer in answers)
            {
                List<IPAddress> addresses = new List<IPAddress>();
                foreach (string part in answer.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) addresses.Add(IPAddress.Parse(part));
                list.Add(addresses.ToArray());
            }
            _Answers[host] = list;
        }

        /// <summary>
        /// Lookups made for a host.
        /// </summary>
        /// <param name="host">Host.</param>
        /// <returns>Count.</returns>
        public int Lookups(string host)
        {
            return _Lookups.TryGetValue(host, out int count) ? count : 0;
        }

        /// <inheritdoc />
        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(host)) throw new ArgumentNullException(nameof(host));
            int index = _Lookups.AddOrUpdate(host, 1, (k, v) => v + 1) - 1;
            if (!_Answers.TryGetValue(host, out List<IPAddress[]>? answers) || answers.Count == 0) return Task.FromResult(Array.Empty<IPAddress>());
            return Task.FromResult(answers[Math.Min(index, answers.Count - 1)]);
        }
    }
}
