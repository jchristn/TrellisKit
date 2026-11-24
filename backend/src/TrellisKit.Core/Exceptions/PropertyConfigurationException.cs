namespace TrellisKit.Core.Exceptions
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Thrown when the property directory cannot be found, or property.json cannot be read or fails validation.
    /// The message names every problem so the server can refuse to start with a useful explanation.
    /// </summary>
    public class PropertyConfigurationException : Exception
    {
        #region Public-Members

        /// <summary>
        /// Individual problems, each naming the offending field or file. Never null.
        /// </summary>
        public IReadOnlyList<string> Problems { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the exception with one problem.
        /// </summary>
        /// <param name="problem">Problem description.</param>
        /// <param name="inner">Inner exception, or null.</param>
        public PropertyConfigurationException(string problem, Exception? inner = null)
            : base(problem, inner)
        {
            Problems = new List<string> { problem ?? String.Empty };
        }

        /// <summary>
        /// Instantiate the exception with several problems found in one file.
        /// </summary>
        /// <param name="source">File or directory the problems were found in.</param>
        /// <param name="problems">Problems.</param>
        public PropertyConfigurationException(string source, IReadOnlyList<string> problems)
            : base("Property configuration " + source + " is invalid: " + String.Join("; ", problems ?? new List<string>()))
        {
            Problems = problems ?? new List<string>();
        }

        #endregion
    }
}
