namespace TrellisKit.Core.Enumeration
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Describes one enumerable collection for the shared enumeration helper: its id and created columns (with the
    /// table alias the query uses), allowed orderings (CreatedDescending and CreatedAscending are always present),
    /// searchable expressions, collection-specific filter names, the metadata target type for label and tag filters,
    /// and whether it is an admin collection (larger pages, skip, exact totals). Define each collection once as a
    /// static instance and reuse it. Thread safety: immutable after construction by convention.
    /// </summary>
    public class EnumerationCollection
    {
        #region Public-Members

        /// <summary>
        /// Collection name, used in continuation token scopes, for example "admin-members".
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Target type whose labels and tags filter this collection, or null when it carries none.
        /// </summary>
        public TargetTypeEnum? TargetType { get; }

        /// <summary>
        /// Whether a bounded catalog supplies metadata directly rather than through a database target type.
        /// Default false. Such catalogs still apply the standard label and tag validation and filtering.
        /// </summary>
        public bool HasInlineMetadata { get; set; } = false;

        /// <summary>
        /// SQL id column (unique, used as the tie breaker), for example "u.id".
        /// </summary>
        public string IdColumn { get; }

        /// <summary>
        /// SQL created timestamp column, for example "u.createdutc".
        /// </summary>
        public string CreatedColumn { get; }

        /// <summary>
        /// Whether this is an admin collection.
        /// </summary>
        public bool IsAdmin { get; }

        /// <summary>
        /// Allowed orderings keyed by name (case-insensitive).
        /// </summary>
        public IReadOnlyDictionary<string, EnumerationOrdering> Orderings
        {
            get { return _Orderings; }
        }

        /// <summary>
        /// SQL expressions matched by search (case-insensitive contains).
        /// </summary>
        public IReadOnlyList<string> SearchExpressions
        {
            get { return _Search; }
        }

        /// <summary>
        /// Allowed collection-specific filter names (case-insensitive).
        /// </summary>
        public IReadOnlyCollection<string> FilterNames
        {
            get { return _Filters; }
        }

        /// <summary>
        /// Default ordering name.
        /// </summary>
        public string DefaultOrdering { get; private set; } = "CreatedDescending";

        #endregion

        #region Private-Members

        private readonly Dictionary<string, EnumerationOrdering> _Orderings = new Dictionary<string, EnumerationOrdering>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _Search = new List<string>();
        private readonly HashSet<string> _Filters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a collection with the CreatedDescending and CreatedAscending orderings.
        /// </summary>
        /// <param name="name">Collection name.</param>
        /// <param name="targetType">Metadata target type, or null.</param>
        /// <param name="idColumn">SQL id column.</param>
        /// <param name="createdColumn">SQL created column.</param>
        /// <param name="isAdmin">Whether this is an admin collection.</param>
        /// <exception cref="ArgumentNullException">A name or column is null or empty.</exception>
        public EnumerationCollection(string name, TargetTypeEnum? targetType, string idColumn, string createdColumn, bool isAdmin)
        {
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (String.IsNullOrEmpty(idColumn)) throw new ArgumentNullException(nameof(idColumn));
            if (String.IsNullOrEmpty(createdColumn)) throw new ArgumentNullException(nameof(createdColumn));
            Name = name;
            TargetType = targetType;
            IdColumn = idColumn;
            CreatedColumn = createdColumn;
            IsAdmin = isAdmin;
            Order("CreatedDescending", createdColumn, true, EnumerationKeyKindEnum.Time);
            Order("CreatedAscending", createdColumn, false, EnumerationKeyKindEnum.Time);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Add (or replace) an ordering.
        /// </summary>
        /// <param name="name">Name, for example UpdatedDescending.</param>
        /// <param name="expression">SQL expression (never null for a row).</param>
        /// <param name="descending">Direction.</param>
        /// <param name="kind">Key type.</param>
        /// <returns>This collection.</returns>
        public EnumerationCollection Order(string name, string expression, bool descending, EnumerationKeyKindEnum kind)
        {
            _Orderings[name] = new EnumerationOrdering { Name = name, Expression = expression, Descending = descending, KeyKind = kind };
            return this;
        }

        /// <summary>
        /// Make an ordering the default.
        /// </summary>
        /// <param name="name">Existing ordering name.</param>
        /// <returns>This collection.</returns>
        /// <exception cref="ArgumentException">The ordering does not exist.</exception>
        public EnumerationCollection DefaultTo(string name)
        {
            if (!_Orderings.ContainsKey(name)) throw new ArgumentException("Unknown ordering " + name + ".", nameof(name));
            DefaultOrdering = _Orderings[name].Name;
            return this;
        }

        /// <summary>
        /// Add searchable SQL expressions.
        /// </summary>
        /// <param name="expressions">Expressions.</param>
        /// <returns>This collection.</returns>
        public EnumerationCollection Search(params string[] expressions)
        {
            _Search.AddRange(expressions.Where(e => !String.IsNullOrEmpty(e)));
            return this;
        }

        /// <summary>
        /// Allow collection-specific filter names.
        /// </summary>
        /// <param name="names">Names.</param>
        /// <returns>This collection.</returns>
        public EnumerationCollection Filters(params string[] names)
        {
            foreach (string n in names) _Filters.Add(n);
            return this;
        }

        /// <summary>
        /// Ordering names, for documentation and error messages.
        /// </summary>
        /// <returns>Names.</returns>
        public string OrderingNames()
        {
            return String.Join(", ", _Orderings.Values.Select(o => o.Name));
        }

        #endregion
    }
}
