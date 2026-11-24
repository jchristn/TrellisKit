namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Collections.Generic;
    using System.Threading;

    /// <summary>
    /// Restores the previous prompt overlay of the async flow when disposed (see <see cref="PromptService.Overlay"/>).
    /// </summary>
    internal sealed class PromptOverlayScope : IDisposable
    {
        private readonly AsyncLocal<Dictionary<string, string>?> _Slot;
        private readonly Dictionary<string, string>? _Previous;
        private bool _Disposed = false;

        internal PromptOverlayScope(AsyncLocal<Dictionary<string, string>?> slot, Dictionary<string, string>? previous)
        {
            _Slot = slot;
            _Previous = previous;
        }

        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            _Slot.Value = _Previous;
        }
    }
}
