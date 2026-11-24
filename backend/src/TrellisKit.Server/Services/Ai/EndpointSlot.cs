namespace TrellisKit.Server.Services.Ai
{
    using System.Threading;

    /// <summary>
    /// Concurrency state of one endpoint in <see cref="EndpointConcurrencyGate"/>.
    /// </summary>
    internal sealed class EndpointSlot
    {
        internal int Concurrency { get; }

        internal SemaphoreSlim Semaphore { get; }

        private int _Waiting = 0;

        internal EndpointSlot(int concurrency)
        {
            Concurrency = concurrency;
            Semaphore = new SemaphoreSlim(concurrency, concurrency);
        }

        internal int EnterQueue()
        {
            return Interlocked.Increment(ref _Waiting);
        }

        internal void LeaveQueue()
        {
            Interlocked.Decrement(ref _Waiting);
        }
    }
}
