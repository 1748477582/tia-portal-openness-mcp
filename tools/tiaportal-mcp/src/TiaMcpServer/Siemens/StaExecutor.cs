using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace TiaMcpServer.Siemens
{
    /// <summary>
    /// Owns a single STA thread running a real Windows message pump
    /// (<see cref="Dispatcher.Run"/>) so that every Siemens.Engineering
    /// (TIA Portal Openness) COM call executes on one apartment-consistent
    /// thread that can ALSO pump messages while it waits for the cross-process
    /// callbacks TIA Portal sends back during creation-type operations.
    ///
    /// Why this matters: Openness requires the calling thread to belong to a
    /// Single-Threaded Apartment (STA). Beyond that, operations such as
    /// <c>Project.Devices.CreateWithItem</c> resolve the hardware catalog /
    /// HSP and internally wait for a callback from the TIA Portal process.
    /// That callback is delivered through the STA thread's Windows message
    /// queue, so the STA thread MUST pump messages while it blocks — otherwise
    /// Openness aborts with "Cross-thread operation is not valid in Openness
    /// within STA".
    ///
    /// The previous implementation used a <c>BlockingCollection</c> queue on an
    /// STA thread but never pumped messages. Read operations and simple local
    /// creates (e.g. <c>TiaPortal.Projects.Create</c>) happened to work, but
    /// <c>CreateWithItem</c> failed consistently with the cross-thread error.
    /// <see cref="Dispatcher.Run"/> provides exactly the message pump Openness
    /// needs, and <see cref="Dispatcher.Invoke"/> executes the work
    /// synchronously on that STA thread.
    ///
    /// Usage: serialize all Openness access through <see cref="Run{T}"/> /
    /// <see cref="RunAsync{T}"/>. The TiaPortal instance is created and touched
    /// exclusively on this thread, so the apartment stays consistent.
    /// </summary>
    public sealed class StaExecutor : IDisposable
    {
        private readonly Thread _thread;
        private readonly Dispatcher _dispatcher;
        private readonly ManualResetEventSlim _ready = new ManualResetEventSlim(false);
        private bool _disposed;

        public StaExecutor()
        {
            Dispatcher? captured = null;
            _thread = new Thread(() =>
            {
                // Create the Dispatcher bound to THIS STA thread, then run the
                // message pump. Dispatcher.CurrentDispatcher returns the
                // dispatcher for the calling thread and creates one if absent.
                captured = Dispatcher.CurrentDispatcher;
                _ready.Set();
                Dispatcher.Run();
            })
            {
                IsBackground = true,
                Name = "PortalSta"
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            // Wait until the STA thread has actually created its Dispatcher,
            // otherwise the first Run() could dereference a null dispatcher.
            _ready.Wait();
            _dispatcher = captured!;
        }

        /// <summary>Runs <paramref name="func"/> on the STA thread, blocking the caller until done.</summary>
        public T Run<T>(Func<T> func)
        {
            if (Thread.CurrentThread == _thread)
            {
                try { return func(); }
                catch { throw; }
            }
            // Dispatcher.Invoke executes synchronously on the STA thread and
            // keeps pumping messages while it waits, so Openness callbacks are
            // handled on the same apartment-consistent thread.
            try
            {
                return (T)_dispatcher.Invoke((Delegate)(() =>
                {
                    return func();
                }))!;
            }
            catch
            {
                throw;
            }
        }

        /// <summary>Runs <paramref name="action"/> on the STA thread, blocking the caller until done.</summary>
        public void Run(Action action)
        {
            if (Thread.CurrentThread == _thread) { action(); return; }
            _dispatcher.Invoke((Delegate)(() =>
            {
                action();
            }));
        }

        public Task<T> RunAsync<T>(Func<T> func) => Task.Run(() => Run(func));
        public Task RunAsync(Action action) => Task.Run(() => Run(action));

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _dispatcher.InvokeShutdown(); } catch { /* best effort */ }
            try { _thread.Join(2000); } catch { /* best effort */ }
        }
    }
}
