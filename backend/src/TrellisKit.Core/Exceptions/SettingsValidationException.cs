namespace TrellisKit.Core.Exceptions
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Thrown when a settings update fails validation. Carries per-field messages keyed by setting key.
    /// </summary>
    public class SettingsValidationException : Exception
    {
        #region Public-Members

        /// <summary>
        /// Per-field validation messages keyed by setting key. Never null.
        /// </summary>
        public Dictionary<string, string> FieldErrors { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the exception.
        /// </summary>
        /// <param name="fieldErrors">Per-field validation messages.</param>
        public SettingsValidationException(Dictionary<string, string> fieldErrors)
            : base("One or more settings are invalid.")
        {
            FieldErrors = fieldErrors ?? new Dictionary<string, string>();
        }

        #endregion
    }
}
