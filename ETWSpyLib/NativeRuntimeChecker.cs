using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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
        /// Checks that the Visual C++ runtime krabsetw depends on can be loaded.
        /// </summary>
        /// <remarks>
        /// This deliberately probes the CRT directly rather than constructing a krabsetw
        /// object. Creating a real Provider would allocate a native object that is then
        /// abandoned to the finalizer, which is needless risk for a startup check - and
        /// LoadLibrary answers the actual question (is the VC runtime present?) without
        /// touching any tracing state.
        ///
        /// The modules are already loaded if the process got this far with them present, so
        /// LoadLibrary simply increments a refcount; the handle is intentionally not freed.
        /// </remarks>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool ProbeEtwRuntime()
        {
            try
            {
                foreach (var module in RequiredModules)
                {
                    if (LoadLibraryW(module) == IntPtr.Zero)
                    {
                        return false;
                    }
                }

                return true;
            }
            catch
            {
                // If the probe itself cannot run, assume the runtime is present rather than
                // blocking startup on a diagnostic.
                return true;
            }
        }

        /// <summary>
        /// Native modules imported by the krabsetw assembly.
        /// </summary>
        private static readonly string[] RequiredModules = ["vcruntime140.dll", "msvcp140.dll"];

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string lpLibFileName);
    }
}
