using System;
using System.Runtime.ExceptionServices;
using System.Security;
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
        [HandleProcessCorruptedStateExceptions]
        [SecurityCritical]
        public T Run<T>(Func<T> func)
        {
            if (Thread.CurrentThread == _thread)
            {
                try { return func(); }
                catch { throw; }
            }

            // CRITICAL: the delegate executed via Dispatcher.Invoke must NEVER throw.
            // During cross-process COM calls (e.g. ImportFromDocuments) TIA pumps NESTED
            // messages on this STA thread, and an exception escaping the delegate can
            // break out of the Invoke return-path entirely (observed: TargetInvocationException
            // surfacing at the pump-thread root via the WndProc hook), terminating the process
            // even though Run's own catch could handle that exception type. Capturing the
            // exception into a holder keeps the Dispatcher machinery exception-free; the
            // caller thread then rethrows from the holder below.
            var holder = new RunHolder<T>();
            _dispatcher.Invoke((Action)(() => RunSafe(func, holder)));
            return RethrowCaptured(holder);
        }

        /// <summary>Runs <paramref name="action"/> on the STA thread, blocking the caller until done.</summary>
        [HandleProcessCorruptedStateExceptions]
        [SecurityCritical]
        public void Run(Action action)
        {
            if (Thread.CurrentThread == _thread) { action(); return; }
            var holder = new RunHolder<object>();
            _dispatcher.Invoke((Action)(() => RunSafe<object>(() => { action(); return null; }, holder)));
            RethrowCaptured(holder);
        }

        private sealed class RunHolder<T>
        {
            public T? Value;
            public Exception? Error;
        }

        /// <summary>
        /// Executes func ON THE STA THREAD and captures every exception (including
        /// corrupted-state exceptions, thanks to HPCSE on this method) into the holder.
        /// Never throws, so nothing ever escapes through the Dispatcher message machinery.
        /// Note: this must be a non-lambda method because [HandleProcessCorruptedStateExceptions]
        /// cannot be applied to compiler-generated lambda methods.
        /// </summary>
        [HandleProcessCorruptedStateExceptions]
        [SecurityCritical]
        private static void RunSafe<T>(Func<T> func, RunHolder<T> holder)
        {
            try { holder.Value = func(); }
            catch (Exception ex) { holder.Error = ex; }
        }

        /// <summary>
        /// Rethrows a captured STA-thread exception on the caller thread WITHOUT touching
        /// a possibly-dead RCW: our own PortalException / McpException have safe ToString()
        /// and are rethrown as-is (stack preserved via ExceptionDispatchInfo); foreign
        /// exceptions (including corrupted-state ones from dead RCWs) are wrapped in a safe
        /// PortalException carrying only the type name.
        /// </summary>
        [HandleProcessCorruptedStateExceptions]
        [SecurityCritical]
        private static T RethrowCaptured<T>(RunHolder<T> holder)
        {
            var ex = holder.Error;
            if (ex == null) return holder.Value!;
            if (ex is PortalException || ex is global::ModelContextProtocol.McpException)
            {
                ExceptionDispatchInfo.Capture(ex).Throw();
            }
            throw new PortalException(PortalErrorCode.OpennessError,
                $"Openness operation failed on the STA thread: {ex.GetType().FullName}");
        }

        /// <summary>True when the caller is already executing on the PortalSta thread.</summary>
        public bool IsStaThread => Thread.CurrentThread == _thread;

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
