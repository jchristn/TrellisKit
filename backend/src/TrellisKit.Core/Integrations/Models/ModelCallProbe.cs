namespace TrellisKit.Core.Integrations.Models
{
    using System;
    using System.Threading;

    /// <summary>
    /// Measures the HTTP traffic of one model call (docs/AI_TELEMETRY.md): the caller opens a probe around the call, and
    /// the endpoint's HTTP handler adds the size of every request body it sends and of every response body whose length
    /// the provider declares. Streamed responses declare no length, so their size stays unknown. The probe follows the
    /// asynchronous flow of the call, so concurrent calls never share one.
    /// Thread safety: safe for concurrent use.
    /// </summary>
    public sealed class ModelCallProbe : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// The probe of the call running on this asynchronous flow, or null when none is open.
        /// </summary>
        public static ModelCallProbe? Current
        {
            get { return _Current.Value; }
        }

        /// <summary>
        /// HTTP requests sent.
        /// </summary>
        public int Requests
        {
            get { return Volatile.Read(ref _Requests); }
        }

        /// <summary>
        /// Bytes of the request bodies sent, or null when no request declared a length.
        /// </summary>
        public long? RequestBytes
        {
            get { return Volatile.Read(ref _RequestsMeasured) == 0 ? null : Interlocked.Read(ref _RequestBytes); }
        }

        /// <summary>
        /// Bytes of the response bodies, or null when no response declared a length.
        /// </summary>
        public long? ResponseBytes
        {
            get { return Volatile.Read(ref _ResponsesMeasured) == 0 ? null : Interlocked.Read(ref _ResponseBytes); }
        }

        #endregion

        #region Private-Members

        private static readonly AsyncLocal<ModelCallProbe?> _Current = new AsyncLocal<ModelCallProbe?>();
        private readonly ModelCallProbe? _Parent;
        private long _RequestBytes = 0;
        private long _ResponseBytes = 0;
        private int _Requests = 0;
        private int _RequestsMeasured = 0;
        private int _ResponsesMeasured = 0;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        private ModelCallProbe(ModelCallProbe? parent)
        {
            _Parent = parent;
        }

        /// <summary>
        /// Open a probe for the call that follows on this asynchronous flow. Dispose it when the call ends.
        /// </summary>
        /// <returns>Probe.</returns>
        public static ModelCallProbe Begin()
        {
            ModelCallProbe probe = new ModelCallProbe(_Current.Value);
            _Current.Value = probe;
            return probe;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Count one request.
        /// </summary>
        /// <param name="bytes">Length of its body, or null when unknown.</param>
        public void AddRequest(long? bytes)
        {
            Interlocked.Increment(ref _Requests);
            if (bytes == null || bytes.Value < 0) return;
            Interlocked.Add(ref _RequestBytes, bytes.Value);
            Interlocked.Increment(ref _RequestsMeasured);
        }

        /// <summary>
        /// Count one response.
        /// </summary>
        /// <param name="bytes">Declared length of its body, or null when the provider declared none.</param>
        public void AddResponse(long? bytes)
        {
            if (bytes == null || bytes.Value < 0) return;
            Interlocked.Add(ref _ResponseBytes, bytes.Value);
            Interlocked.Increment(ref _ResponsesMeasured);
        }

        /// <summary>
        /// Close the probe (the enclosing probe, if any, becomes current again).
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            if (_Current.Value == this) _Current.Value = _Parent;
        }

        #endregion
    }
}
