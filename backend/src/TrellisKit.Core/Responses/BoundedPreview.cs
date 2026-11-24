namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A bounded preview of a nested list inside a detail payload (docs/ENUMERATION_AND_METADATA.md section 2.4): at
    /// most <see cref="MaxObjects"/> objects, the exact total, and the path of the full enumeration.
    /// </summary>
    /// <typeparam name="T">Object type.</typeparam>
    public class BoundedPreview<T>
    {
        #region Public-Members

        /// <summary>
        /// Most objects a preview carries.
        /// </summary>
        public const int MaxObjects = 5;

        /// <summary>
        /// At most <see cref="MaxObjects"/> objects, in the collection's default ordering. Never null.
        /// </summary>
        public List<T> Objects
        {
            get { return _Objects; }
            set { _Objects = value ?? new List<T>(); }
        }

        /// <summary>
        /// Exact number of objects in the full list.
        /// </summary>
        public long TotalRecords { get; set; } = 0;

        /// <summary>
        /// Path of the POST enumeration for the full list, for example /api/v1/admin/members/usr_x/sessions/enumerate.
        /// </summary>
        public string Enumerate { get; set; } = String.Empty;

        #endregion

        #region Private-Members

        private List<T> _Objects = new List<T>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build a preview from the first page of an enumeration.
        /// </summary>
        /// <param name="page">First page (maxResults at most <see cref="MaxObjects"/>).</param>
        /// <param name="enumerate">Enumeration path.</param>
        /// <returns>Preview.</returns>
        /// <exception cref="ArgumentNullException">The page is null.</exception>
        public static BoundedPreview<T> From(EnumerationResult<T> page, string enumerate)
        {
            ArgumentNullException.ThrowIfNull(page);
            List<T> objects = page.Objects.Count > MaxObjects ? page.Objects.GetRange(0, MaxObjects) : page.Objects;
            return new BoundedPreview<T> { Objects = objects, TotalRecords = page.TotalRecords ?? objects.Count, Enumerate = enumerate ?? String.Empty };
        }

        #endregion
    }
}
