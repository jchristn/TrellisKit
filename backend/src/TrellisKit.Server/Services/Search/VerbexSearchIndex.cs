namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Settings;
    using Verbex;
    using Verbex.Models;

    /// <summary>
    /// <see cref="ISearchIndex"/> on Verbex (NuGet Verbex 0.2.1; https://github.com/jchristn/verbex) with its PostgreSQL
    /// backend: one inverted index per property, named after the property id, in a dedicated schema of the engine
    /// database (search.schema). Lemmatization and stop-word removal follow search.lemmatize and search.removeStopWords.
    /// The index opens lazily on first use. Thread safety: searches run concurrently; writes are serialized.
    /// </summary>
    public sealed class VerbexSearchIndex : ISearchIndex
    {
        #region Public-Members

        /// <inheritdoc />
        public string Provider
        {
            get { return "verbex"; }
        }

        /// <summary>
        /// Most prefix expansions of the last search term.
        /// </summary>
        public int PrefixExpansions
        {
            get { return _PrefixExpansions; }
            set { _PrefixExpansions = Math.Clamp(value, 0, 20); }
        }

        #endregion

        #region Private-Members

        private static readonly Regex _Token = new Regex("[\\p{L}\\p{N}]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private readonly TrellisKit.Core.Database.DatabaseSettings _Database;
        private readonly SearchSettings _Settings;
        private readonly string _IndexName;
        private readonly Func<string, CancellationToken, Task> _EnsureSchema;
        private readonly SemaphoreSlim _OpenLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _WriteLock = new SemaphoreSlim(1, 1);
        private Verbex.Database.DatabaseDriverBase? _Driver = null;
        private InvertedIndex? _Index = null;
        private int _PrefixExpansions = 5;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate (nothing opens until first use).
        /// </summary>
        /// <param name="database">Engine database settings (host, credentials, schema).</param>
        /// <param name="settings">Search settings.</param>
        /// <param name="propertyId">Property identifier (the index name).</param>
        /// <param name="ensureSchema">Creates the search schema when missing.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public VerbexSearchIndex(TrellisKit.Core.Database.DatabaseSettings database, SearchSettings settings, string propertyId, Func<string, CancellationToken, Task> ensureSchema)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            if (String.IsNullOrWhiteSpace(propertyId)) throw new ArgumentNullException(nameof(propertyId));
            _EnsureSchema = ensureSchema ?? throw new ArgumentNullException(nameof(ensureSchema));
            _IndexName = "p_" + Regex.Replace(propertyId, "[^A-Za-z0-9_]", "_");
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The schema of the search tables for engine database settings and search settings.
        /// </summary>
        /// <param name="database">Engine database settings.</param>
        /// <param name="settings">Search settings.</param>
        /// <returns>Schema name.</returns>
        public static string SchemaFor(TrellisKit.Core.Database.DatabaseSettings database, SearchSettings settings)
        {
            ArgumentNullException.ThrowIfNull(database);
            ArgumentNullException.ThrowIfNull(settings);
            if (!String.IsNullOrWhiteSpace(settings.Schema)) return settings.Schema.Trim();
            return String.IsNullOrWhiteSpace(database.Schema) ? "search" : database.Schema.Trim() + "_search";
        }

        /// <summary>
        /// The host value handed to Verbex so its PostgreSQL connections use the search schema. Verbex 0.2.1 builds its
        /// connection string from the host and ignores its own schema setting, so the search path is appended to the host
        /// (its tables would otherwise land in the default schema and collide with engine tables such as users).
        /// </summary>
        /// <param name="host">Database host.</param>
        /// <param name="schema">Search schema (a simple identifier).</param>
        /// <returns>Host value with the search path.</returns>
        /// <exception cref="ArgumentNullException">The host is null or empty.</exception>
        /// <exception cref="ArgumentException">The schema is not a simple identifier.</exception>
        public static string HostWithSearchPath(string host, string schema)
        {
            if (String.IsNullOrWhiteSpace(host)) throw new ArgumentNullException(nameof(host));
            if (String.IsNullOrWhiteSpace(schema) || !Regex.IsMatch(schema, "^[A-Za-z_][A-Za-z0-9_]{0,62}$")) throw new ArgumentException("The search schema must be a simple identifier.", nameof(schema));
            return host.Trim() + ";Search Path=" + schema;
        }

        /// <inheritdoc />
        public async Task UpsertAsync(SearchDocument document, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(document);
            InvertedIndex index = await OpenAsync(token).ConfigureAwait(false);
            await _WriteLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (await index.DocumentExistsAsync(document.Id, token).ConfigureAwait(false)) await index.RemoveDocumentAsync(document.Id, token).ConfigureAwait(false);
                if (String.IsNullOrWhiteSpace(document.Content) || !_Token.IsMatch(document.Content)) return;
                // Index the same word tokens the query side produces: Verbex splits on whitespace and keeps inner
                // punctuation ("follow-up", "[tag]", "@handle"), which a query (tokenized below) could never match.
                string content = String.Join(" ", _Token.Matches(document.Content).Select(m => m.Value));
                await index.AddDocumentAsync(document.Id, document.Id, content, token).ConfigureAwait(false);
                if (document.Labels.Count > 0) await index.ReplaceLabelsAsync(document.Id, document.Labels.Select(l => l.ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToList(), token).ConfigureAwait(false);
                if (document.Tags.Count > 0) await index.ReplaceTagsAsync(document.Id, new Dictionary<string, string>(document.Tags, StringComparer.Ordinal), token).ConfigureAwait(false);
            }
            finally
            {
                _WriteLock.Release();
            }
        }

        /// <inheritdoc />
        public async Task RemoveAsync(string documentId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(documentId)) return;
            InvertedIndex index = await OpenAsync(token).ConfigureAwait(false);
            await _WriteLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (await index.DocumentExistsAsync(documentId, token).ConfigureAwait(false)) await index.RemoveDocumentAsync(documentId, token).ConfigureAwait(false);
            }
            finally
            {
                _WriteLock.Release();
            }
        }

        /// <inheritdoc />
        public async Task<List<SearchHit>> SearchAsync(string text, IReadOnlyList<string> labels, IReadOnlyDictionary<string, string> tags, int maxResults, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(labels);
            ArgumentNullException.ThrowIfNull(tags);
            List<string> words = _Token.Matches(text ?? String.Empty).Select(m => m.Value.ToLowerInvariant()).ToList();
            if (words.Count == 0) return new List<SearchHit>();
            InvertedIndex index = await OpenAsync(token).ConfigureAwait(false);
            List<string>? labelFilter = labels.Count == 0 ? null : labels.Select(l => l.ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToList();
            Dictionary<string, string>? tagFilter = tags.Count == 0 ? null : new Dictionary<string, string>(tags, StringComparer.Ordinal);
            int max = Math.Max(1, maxResults);
            Dictionary<string, double> scores = new Dictionary<string, double>(StringComparer.Ordinal);
            await CollectAsync(index, String.Join(" ", words), max, labelFilter, tagFilter, scores, 1.0, token).ConfigureAwait(false);
            string last = words[words.Count - 1];
            if (_PrefixExpansions > 0 && last.Length >= 2)
            {
                List<TermRecord> terms = await index.GetTermsByPrefixAsync(last, _PrefixExpansions, token).ConfigureAwait(false);
                string head = String.Join(" ", words.Take(words.Count - 1));
                foreach (TermRecord term in terms)
                {
                    if (String.IsNullOrWhiteSpace(term.Term) || String.Equals(term.Term, last, StringComparison.Ordinal)) continue;
                    await CollectAsync(index, (head + " " + term.Term).Trim(), max, labelFilter, tagFilter, scores, 0.9, token).ConfigureAwait(false);
                }
            }
            return scores.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).Take(max).Select(p => new SearchHit { Id = p.Key, Score = p.Value }).ToList();
        }

        /// <inheritdoc />
        public async Task<long> CountAsync(CancellationToken token = default)
        {
            InvertedIndex index = await OpenAsync(token).ConfigureAwait(false);
            return await index.GetDocumentCountAsync(token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task ClearAsync(CancellationToken token = default)
        {
            InvertedIndex index = await OpenAsync(token).ConfigureAwait(false);
            await _WriteLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await index.ClearAsync(token).ConfigureAwait(false);
            }
            finally
            {
                _WriteLock.Release();
            }
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            if (_Disposed) return;
            _Disposed = true;
            if (_Index != null)
            {
                try
                {
                    await _Index.DisposeAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    // Already closed.
                }
            }
            if (_Driver != null)
            {
                try
                {
                    await _Driver.DisposeAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    // Already closed.
                }
            }
            _OpenLock.Dispose();
            _WriteLock.Dispose();
        }

        #endregion

        #region Private-Methods

        private static async Task CollectAsync(InvertedIndex index, string query, int max, List<string>? labels, Dictionary<string, string>? tags, Dictionary<string, double> scores, double weight, CancellationToken token)
        {
            SearchResults results = await index.SearchAsync(query, max, true, labels, tags, token).ConfigureAwait(false);
            foreach (SearchResult result in results.Results)
            {
                double score = result.Score * weight;
                if (!scores.TryGetValue(result.DocumentId, out double existing) || score > existing) scores[result.DocumentId] = score;
            }
        }

        private static async Task DisposeQuietlyAsync(InvertedIndex index, Verbex.Database.DatabaseDriverBase driver)
        {
            try
            {
                await index.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Best effort: the open already failed.
            }
            try
            {
                await driver.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Best effort: the open already failed.
            }
        }

        private async Task<InvertedIndex> OpenAsync(CancellationToken token)
        {
            ObjectDisposedException.ThrowIf(_Disposed, this);
            if (_Index != null) return _Index;
            await _OpenLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_Index != null) return _Index;
                string schema = SchemaFor(_Database, _Settings);
                await _EnsureSchema(schema, token).ConfigureAwait(false);
                Verbex.Database.DatabaseSettings settings = new Verbex.Database.DatabaseSettings
                {
                    Type = Verbex.Database.DatabaseTypeEnum.Postgresql,
                    Hostname = HostWithSearchPath(_Database.Hostname, schema),
                    Port = _Database.Port,
                    DatabaseName = _Database.DatabaseName,
                    Username = _Database.Username,
                    Password = _Database.Password,
                    Schema = schema,
                    MinPoolSize = 0,
                    MaxPoolSize = 10
                };
                Verbex.Database.DatabaseDriverBase driver = Verbex.Database.DatabaseDriverFactory.Create(settings);
                VerbexConfiguration configuration = new VerbexConfiguration
                {
                    Lemmatizer = _Settings.Lemmatize ? new BasicLemmatizer() : null,
                    StopWordRemover = _Settings.RemoveStopWords ? new BasicStopWordRemover() : null
                };
                InvertedIndex index = new InvertedIndex(_IndexName, driver, configuration);
                try
                {
                    await index.OpenAsync(token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // A failed open must release the driver's connection pool; otherwise every retry leaks one.
                    await DisposeQuietlyAsync(index, driver).ConfigureAwait(false);
                    throw;
                }
                _Driver = driver;
                _Index = index;
                return index;
            }
            finally
            {
                _OpenLock.Release();
            }
        }

        #endregion
    }
}
