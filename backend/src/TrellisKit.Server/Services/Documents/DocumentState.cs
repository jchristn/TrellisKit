namespace TrellisKit.Server.Services.Documents
{
    using System;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.PropertyConfig;

    /// <summary>
    /// One property document as the service holds it: its property.json declaration, its current stored version, and
    /// the lowest sequence a member must have accepted. Immutable once published in a snapshot.
    /// </summary>
    public class DocumentState
    {
        #region Public-Members

        /// <summary>
        /// The property.json declaration.
        /// </summary>
        public PropertyDocument Config { get; }

        /// <summary>
        /// The current version (with content).
        /// </summary>
        public PropertyDocumentVersion Current { get; }

        /// <summary>
        /// The lowest sequence a member must have accepted: 0 for documents that are not accepted, 1 for gating documents
        /// without reacceptOnChange, and otherwise the latest version that requires re-acceptance.
        /// </summary>
        public int RequiredSequence { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the state.
        /// </summary>
        /// <param name="config">Declaration.</param>
        /// <param name="current">Current version.</param>
        /// <param name="requiredSequence">Required sequence (see <see cref="RequiredSequence"/>).</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public DocumentState(PropertyDocument config, PropertyDocumentVersion current, int requiredSequence)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            Current = current ?? throw new ArgumentNullException(nameof(current));
            RequiredSequence = Math.Max(0, requiredSequence);
        }

        #endregion
    }
}
