using System.Runtime.CompilerServices;

namespace ETWSpyLib
{
    /// <summary>
    /// Verifies that the native dependencies required for ETW tracing can be loaded.
    /// </summary>
    /// <remarks>
    /// The krabsetw assembly (Microsoft.O365.Security.Native.ETW.dll) is a mixed-mode
    /// C++/CLI assembly. Loading it pulls in the Visual C++ runtime (VCRUNTIME140.dll and
    /// MSVCP140.dll), which is not present on a clean Windows installation. Without it the
    /// CLR throws a FileNotFoundException reporting that the krabsetw assembly itself could
    /// not be found, which is misleading - the assembly is present, but one of its native
    /// dependencies is not.
    ///
    /// Probing up front lets the application report the real problem and the fix instead of
    /// crashing the first time a provider is used.
    /// </remarks>
    public static class NativeRuntimeChecker
    {
        /// <summary>
        /// Command that installs the required Visual C++ runtime.
        /// </summary>
        public const string InstallCommand = "winget install Microsoft.VCRedist.2015+.x64";

        /// <summary>
        /// Message describing the missing dependency and how to resolve it.
        /// </summary>
        public const string MissingRuntimeMessage =
            "ETWSpy requires the Microsoft Visual C++ Redistributable (2015-2022, x64), " +
            "which does not appear to be installed.\n\n" +
            "Install it by running:\n\n" +
            "    " + InstallCommand + "\n\n" +
            "or download it from:\n\n" +
            "    https://aka.ms/vs/17/release/vc_redist.x64.exe\n\n" +
            "ETWSpy cannot capture events until it is installed.";

        private static bool? _isAvailable;
        private static readonly object _lock = new();

        /// <summary>
        /// Determines whether the native ETW dependencies can be loaded.
        /// </summary>
        /// <remarks>The result is cached; the runtime cannot appear mid-process.</remarks>
        /// <returns><c>true</c> when tracing is possible; otherwise <c>false</c>.</returns>
        public static bool IsEtwRuntimeAvailable()
        {
            lock (_lock)
            {
                _isAvailable ??= ProbeEtwRuntime();
                return _isAvailable.Value;
            }
        }

        /// <summary>
        /// Attempts to construct a krabsetw object, forcing the mixed-mode assembly and its
        /// native dependencies to load.
        /// </summary>
        /// <remarks>
        /// Kept in its own non-inlined method so the assembly reference is not resolved until
        /// the call is actually made, allowing the load failure to be caught here.
        /// </remarks>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool ProbeEtwRuntime()
        {
            try
            {
                // Constructing a Provider executes C++/CLI code, which requires the native
                // runtime to be resolvable. The instance itself is discarded.
                _ = new Microsoft.O365.Security.ETW.Provider(Guid.Empty);
                return true;
            }
            catch (Exception ex) when (
                ex is FileNotFoundException ||
                ex is DllNotFoundException ||
                ex is BadImageFormatException ||
                ex is TypeInitializationException ||
                ex is TypeLoadException)
            {
                return false;
            }
            catch
            {
                // Any other exception means the assembly and its native dependencies loaded
                // successfully and managed to run - the runtime is present, this particular
                // probe value was simply rejected. Treat that as available.
                return true;
            }
        }
    }
}
