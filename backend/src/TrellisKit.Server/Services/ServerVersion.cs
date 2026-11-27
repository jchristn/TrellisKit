namespace TrellisKit.Server.Services
{
    using System;
    using System.Reflection;

    /// <summary>
    /// The running server's version (the assembly informational version without build metadata), shown by the health
    /// route and on system-originated staff emails. Thread safety: stateless; the value is computed once.
    /// </summary>
    public static class ServerVersion
    {
        #region Public-Members

        /// <summary>
        /// Version, for example 0.1.0-alpha; "0.0.0" when the assembly carries none.
        /// </summary>
        public static string Current
        {
            get { return _Current.Value; }
        }

        #endregion

        #region Private-Members

        private static readonly Lazy<string> _Current = new Lazy<string>(Resolve);

        #endregion

        #region Private-Methods

        private static string Resolve()
        {
            Assembly assembly = typeof(ServerVersion).Assembly;
            AssemblyInformationalVersionAttribute? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (informational != null && !String.IsNullOrWhiteSpace(informational.InformationalVersion))
            {
                string value = informational.InformationalVersion;
                int metadata = value.IndexOf('+');
                return metadata > 0 ? value.Substring(0, metadata) : value;
            }
            Version? version = assembly.GetName().Version;
            return version == null ? "0.0.0" : version.ToString(3);
        }

        #endregion
    }
}
