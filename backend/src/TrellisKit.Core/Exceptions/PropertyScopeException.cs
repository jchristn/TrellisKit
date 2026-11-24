namespace TrellisKit.Core.Exceptions
{
    using System;

    /// <summary>
    /// A data access would read or write property-owned rows without the property scope (a missing propertyid
    /// predicate, a missing property binding, or a malformed or reserved property id). Always a programming error;
    /// the statement is never executed (docs/MULTIPROPERTY_CONTRACT.md section 4.3).
    /// </summary>
    public class PropertyScopeException : InvalidOperationException
    {
        #region Public-Members

        /// <summary>
        /// Violation kind for telemetry: missing_scope, reserved_id, or invalid_id.
        /// </summary>
        public string Kind { get; } = "missing_scope";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the exception.
        /// </summary>
        /// <param name="kind">Violation kind: missing_scope, reserved_id, or invalid_id.</param>
        /// <param name="message">Message. Never include row data or secrets.</param>
        public PropertyScopeException(string kind, string message) : base(message)
        {
            Kind = String.IsNullOrWhiteSpace(kind) ? "missing_scope" : kind;
        }

        #endregion
    }
}
