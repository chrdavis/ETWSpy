using Microsoft.O365.Security.ETW;

namespace ETWSpyLib
{
    /// <summary>
    /// Event arguments for trace session errors
    /// </summary>
    public class TraceSessionErrorEventArgs : EventArgs
    {
        public Exception Exception { get; }
        public string Message { get; }
        public bool IsNoSessionsRemaining { get; }

        public TraceSessionErrorEventArgs(Exception exception)
        {
            Exception = exception;
            IsNoSessionsRemaining = exception is NoTraceSessionsRemaining;
            Message = IsNoSessionsRemaining
                ? "No ETW trace sessions are available. Windows has a limited number of trace sessions. Please close other tracing tools (e.g., Performance Monitor, other ETW tools) and try again."
                : exception.Message;
        }
    }

    /// <summary>
    /// Wrapper for ETW trace sessions (user and kernel mode)
    /// </summary>
    public class EtwTraceSession : IDisposable
    {
        private readonly UserTrace? _userTrace;
        private readonly KernelTrace? _kernelTrace;
        private readonly string _sessionName;
        private readonly object _lock = new();
        private bool _isRunning;
        private bool _disposed;
        private bool _stopRequested;
        private Task? _traceTask;

        /// <summary>
        /// Event raised when an error occurs during tracing
        /// </summary>
        public event EventHandler<TraceSessionErrorEventArgs>? ErrorOccurred;

        public bool IsRunning => _isRunning;
        public string SessionName => _sessionName;

        /// <summary>
        /// Creates a user-mode trace session
        /// </summary>
        public static EtwTraceSession CreateUserSession(string sessionName)
        {
            // Try to stop any existing session with this name first
            TryStopExistingSession(sessionName);
            return new EtwTraceSession(sessionName, isKernel: false);
        }

        /// <summary>
        /// Creates a kernel-mode trace session
        /// </summary>
        public static EtwTraceSession CreateKernelSession(string sessionName)
        {
            TryStopExistingSession(sessionName);
            return new EtwTraceSession(sessionName, isKernel: true);
        }

        /// <summary>
        /// Attempts to stop an existing session with the given name.
        /// This helps clean up orphaned sessions from previous runs.
        /// </summary>
        private static void TryStopExistingSession(string sessionName)
        {
            try
            {
                // Create a temporary trace just to stop the existing session
                using var tempTrace = new UserTrace(sessionName);
                tempTrace.Stop();
            }
            catch
            {
                // Ignore errors - session may not exist
            }
        }

        private EtwTraceSession(string sessionName, bool isKernel)
        {
            _sessionName = sessionName;
            
            if (isKernel)
            {
                _kernelTrace = new KernelTrace(sessionName);
            }
            else
            {
                _userTrace = new UserTrace(sessionName);
            }
        }

        /// <summary>
        /// Enables a provider for this trace session (user-mode only)
        /// </summary>
        public void EnableProvider(EtwProviderWrapper provider)
        {
            if (_userTrace != null)
            {
                _userTrace.Enable(provider.Provider);
            }
            else
            {
                throw new InvalidOperationException("Cannot enable user-mode providers on kernel trace. Use EnableKernelProvider instead.");
            }
        }

        /// <summary>
        /// Enables a kernel provider for this trace session (kernel-mode only)
        /// </summary>
        public void EnableKernelProvider(EtwKernelProviderWrapper provider)
        {
            if (_kernelTrace != null)
            {
                _kernelTrace.Enable(provider.KernelProvider);
            }
            else
            {
                throw new InvalidOperationException("Cannot enable kernel providers on user-mode trace. Use EnableProvider instead.");
            }
        }

        /// <summary>
        /// Starts the trace session (blocking call)
        /// </summary>
        /// <exception cref="NoTraceSessionsRemaining">Thrown when no ETW trace sessions are available</exception>
        public void Start()
        {
            lock (_lock)
            {
                if (_isRunning || _stopRequested)
                    return;
                _isRunning = true;
            }
            
            try
            {
                if (_userTrace != null)
                {
                    _userTrace.Start();
                }
                else if (_kernelTrace != null)
                {
                    _kernelTrace.Start();
                }
            }
            catch (NoTraceSessionsRemaining)
            {
                // Re-throw this specific exception so callers can handle it
                throw;
            }
            catch (Exception) when (_stopRequested)
            {
                // Expected when Stop() is called - ignore
            }
            finally
            {
                lock (_lock)
                {
                    _isRunning = false;
                }
            }
        }

        /// <summary>
        /// Starts the trace session asynchronously
        /// </summary>
        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            _traceTask = Task.Run(() =>
            {
                try
                {
                    Start();
                }
                catch (Exception) when (cancellationToken.IsCancellationRequested || _stopRequested)
                {
                    // Expected when cancelled - ignore
                }
                catch (Exception ex)
                {
                    // Raise error event on a thread-safe manner
                    OnErrorOccurred(new TraceSessionErrorEventArgs(ex));
                }
            }, cancellationToken);

            return _traceTask;
        }

        /// <summary>
        /// Raises the ErrorOccurred event
        /// </summary>
        protected virtual void OnErrorOccurred(TraceSessionErrorEventArgs e)
        {
            ErrorOccurred?.Invoke(this, e);
        }

        /// <summary>
        /// Stops the trace session
        /// </summary>
        public void Stop()
        {
            lock (_lock)
            {
                if (_stopRequested)
                    return;
                _stopRequested = true;
            }

            try
            {
                _userTrace?.Stop();
            }
            catch
            {
                // Ignore stop errors
            }

            try
            {
                _kernelTrace?.Stop();
            }
            catch
            {
                // Ignore stop errors
            }

            // Wait for the trace task to complete (with timeout)
            try
            {
                _traceTask?.Wait(TimeSpan.FromSeconds(5));
            }
            catch
            {
                // Ignore wait errors
            }

            lock (_lock)
            {
                _isRunning = false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Stop();

            // Give the trace time to fully stop before disposing
            Thread.Sleep(100);

            try
            {
                _userTrace?.Dispose();
            }
            catch
            {
                // Ignore dispose errors
            }

            try
            {
                _kernelTrace?.Dispose();
            }
            catch
            {
                // Ignore dispose errors
            }
        }
    }
}