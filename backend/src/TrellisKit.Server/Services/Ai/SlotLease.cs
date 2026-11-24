namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Threading;

    /// <summary>
    /// A held endpoint slot; disposing it releases the slot once.
    /// </summary>
    internal sealed class SlotLease : IDisposable
    {
        private readonly EndpointSlot _Slot;
        private int _Released = 0;

        internal SlotLease(EndpointSlot slot)
        {
            _Slot = slot;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _Released, 1) == 0) _Slot.Semaphore.Release();
        }
    }
}
