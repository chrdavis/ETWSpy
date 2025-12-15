using Microsoft.O365.Security.ETW;

namespace ETWSpyLib
{
    /// <summary>
    /// Wrapper for ETW Kernel Provider configuration
    /// </summary>
    public class EtwKernelProviderWrapper
    {
        public KernelProvider KernelProvider { get; }

        /// <summary>
        /// Creates a kernel provider wrapper with specified GUID and flags
        /// </summary>
        public EtwKernelProviderWrapper(Guid guid, uint flags)
        {
            KernelProvider = new KernelProvider(guid, flags);
        }

        /// <summary>
        /// Creates a kernel provider wrapper with specified flags (uses default kernel GUID)
        /// </summary>
        public EtwKernelProviderWrapper(KernelProviderFlags flags)
            : this(KernelGuids.SystemTraceControlGuid, (uint)flags)
        {
        }

        /// <summary>
        /// Adds additional flags to the kernel provider
        /// </summary>
        public void AddFlags(KernelProviderFlags flags)
        {
            // Note: This may need to be set before enabling the provider
            // Kernel providers typically need to be recreated with new flags
        }

        /// <summary>
        /// Creates a kernel provider for process events
        /// </summary>
        public static EtwKernelProviderWrapper ForProcessEvents()
        {
            return new EtwKernelProviderWrapper(KernelProviderFlags.Process);
        }

        /// <summary>
        /// Creates a kernel provider for thread events
        /// </summary>
        public static EtwKernelProviderWrapper ForThreadEvents()
        {
            return new EtwKernelProviderWrapper(KernelProviderFlags.Thread);
        }

        /// <summary>
        /// Creates a kernel provider for image (DLL) load events
        /// </summary>
        public static EtwKernelProviderWrapper ForImageLoadEvents()
        {
            return new EtwKernelProviderWrapper(KernelProviderFlags.ImageLoad);
        }

        /// <summary>
        /// Creates a kernel provider for network events
        /// </summary>
        public static EtwKernelProviderWrapper ForNetworkEvents()
        {
            return new EtwKernelProviderWrapper(KernelProviderFlags.NetworkTCPIP);
        }

        /// <summary>
        /// Creates a kernel provider for registry events
        /// </summary>
        public static EtwKernelProviderWrapper ForRegistryEvents()
        {
            return new EtwKernelProviderWrapper(KernelProviderFlags.Registry);
        }

        /// <summary>
        /// Creates a kernel provider for file I/O events
        /// </summary>
        public static EtwKernelProviderWrapper ForFileIOEvents()
        {
            return new EtwKernelProviderWrapper(KernelProviderFlags.FileIO | KernelProviderFlags.FileIOInit);
        }

        /// <summary>
        /// Adds an event filter to this kernel provider
        /// </summary>
        public void AddFilter(EtwEventFilter filter)
        {
            KernelProvider.AddFilter(filter.Filter);
        }
    }

    /// <summary>
    /// Well-known kernel trace control GUIDs
    /// </summary>
    public static class KernelGuids
    {
        /// <summary>
        /// System Trace Control GUID for kernel providers
        /// </summary>
        public static readonly Guid SystemTraceControlGuid = new Guid("9e814aad-3204-11d2-9a82-006008a86939");
    }

    /// <summary>
    /// Kernel provider flags for different event types
    /// </summary>
    [Flags]
    public enum KernelProviderFlags : uint
    {
        Process = 0x00000001,
        Thread = 0x00000002,
        ImageLoad = 0x00000004,
        ProcessCounters = 0x00000008,
        ContextSwitch = 0x00000010,
        DeferedProcedureCalls = 0x00000020,
        Interrupts = 0x00000040,
        SystemCall = 0x00000080,
        DiskIO = 0x00000100,
        DiskFileIO = 0x00000200,
        DiskIOInit = 0x00000400,
        Dispatcher = 0x00000800,
        MemoryPageFaults = 0x00001000,
        MemoryHardFaults = 0x00002000,
        VirtualAlloc = 0x00004000,
        NetworkTCPIP = 0x00010000,
        Registry = 0x00020000,
        Alpc = 0x00100000,
        SplitIO = 0x00200000,
        Driver = 0x00800000,
        Profile = 0x01000000,
        FileIO = 0x02000000,
        FileIOInit = 0x04000000
    }
}