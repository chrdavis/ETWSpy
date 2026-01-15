using Microsoft.O365.Security.ETW;
using System.Runtime.InteropServices;

namespace ETWSpyLib
{
    /// <summary>
    /// Validates ETW provider names and GUIDs before use
    /// </summary>
    public static class EtwProviderValidator
    {
        private const int ERROR_SUCCESS = 0;
        private const int ERROR_INSUFFICIENT_BUFFER = 122;

        /// <summary>
        /// Enumerates all registered ETW providers on the system.
        /// </summary>
        [DllImport("tdh.dll", CharSet = CharSet.Unicode)]
        private static extern int TdhEnumerateProviders(
            IntPtr pBuffer,
            ref int pBufferSize);

        // Cached set of registered provider GUIDs for performance
        private static HashSet<Guid>? _registeredProviders;
        private static readonly object _cacheLock = new();

        /// <summary>
        /// Validates that a provider name or GUID is valid and can be used for tracing
        /// </summary>
        /// <param name="providerNameOrGuid">The provider name or GUID string to validate</param>
        /// <param name="knownGuid">Optional known GUID for the provider (e.g., from ProviderManager)</param>
        /// <param name="errorMessage">Error message if validation fails</param>
        /// <returns>True if valid, false otherwise</returns>
        public static bool IsValidProvider(string providerNameOrGuid, Guid? knownGuid, out string? errorMessage)
        {
            if (string.IsNullOrWhiteSpace(providerNameOrGuid))
            {
                errorMessage = "Provider name cannot be empty.";
                return false;
            }

            // If we have a known GUID from the provider list, it's valid
            if (knownGuid.HasValue)
            {
                errorMessage = null;
                return true;
            }

            // If it's a valid GUID format, it's always acceptable (even if not registered)
            if (Guid.TryParse(providerNameOrGuid, out _))
            {
                errorMessage = null;
                return true;
            }

            // For provider names, we need to verify they can be resolved
            try
            {
                // Attempt to create the provider - this will throw if the name can't be resolved
                using var testProvider = new Provider(providerNameOrGuid);
                errorMessage = null;
                return true;
            }
            catch (SEHException)
            {
                errorMessage = $"'{providerNameOrGuid}' is not a registered ETW provider name. Use a provider GUID instead, or select from the list of known providers.";
                return false;
            }
            catch (Exception ex)
            {
                errorMessage = $"Failed to validate provider: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Validates that a provider name or GUID is valid and can be used for tracing
        /// </summary>
        /// <param name="providerNameOrGuid">The provider name or GUID string to validate</param>
        /// <param name="errorMessage">Error message if validation fails</param>
        /// <returns>True if valid, false otherwise</returns>
        public static bool IsValidProvider(string providerNameOrGuid, out string? errorMessage)
        {
            return IsValidProvider(providerNameOrGuid, knownGuid: null, out errorMessage);
        }

        /// <summary>
        /// Checks if a provider GUID is registered on the machine.
        /// </summary>
        /// <param name="providerGuid">The GUID to check.</param>
        /// <returns>True if the provider is registered, false otherwise.</returns>
        public static bool IsProviderRegistered(Guid providerGuid)
        {
            var registeredProviders = GetRegisteredProviders();
            return registeredProviders.Contains(providerGuid);
        }

        /// <summary>
        /// Checks if a provider (by name or GUID string) is registered on the machine.
        /// </summary>
        /// <param name="providerNameOrGuid">The provider name or GUID string to check.</param>
        /// <returns>True if the provider is registered, false otherwise.</returns>
        public static bool IsProviderRegistered(string providerNameOrGuid)
        {
            if (string.IsNullOrWhiteSpace(providerNameOrGuid))
                return false;

            // If it's a GUID, check directly
            if (Guid.TryParse(providerNameOrGuid, out var guid))
            {
                return IsProviderRegistered(guid);
            }

            // For names, try to find a matching registered provider
            // This requires enumerating and checking names
            return GetRegisteredProviderInfo()
                .Any(p => string.Equals(p.Name, providerNameOrGuid, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gets all registered ETW provider GUIDs on the machine.
        /// Results are cached for performance.
        /// </summary>
        /// <returns>A set of registered provider GUIDs.</returns>
        public static HashSet<Guid> GetRegisteredProviders()
        {
            lock (_cacheLock)
            {
                if (_registeredProviders != null)
                    return _registeredProviders;

                _registeredProviders = [];
                var providerInfos = GetRegisteredProviderInfo();
                
                foreach (var info in providerInfos)
                {
                    _registeredProviders.Add(info.Guid);
                }

                return _registeredProviders;
            }
        }

        /// <summary>
        /// Gets detailed information about all registered ETW providers.
        /// </summary>
        /// <returns>A list of registered provider information.</returns>
        public static List<RegisteredProviderInfo> GetRegisteredProviderInfo()
        {
            var providers = new List<RegisteredProviderInfo>();
            int bufferSize = 0;

            // First call to get required buffer size
            int result = TdhEnumerateProviders(IntPtr.Zero, ref bufferSize);
            if (result != ERROR_INSUFFICIENT_BUFFER)
                return providers;

            IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
            try
            {
                result = TdhEnumerateProviders(buffer, ref bufferSize);
                if (result != ERROR_SUCCESS)
                    return providers;

                // Parse PROVIDER_ENUMERATION_INFO structure
                int providerCount = Marshal.ReadInt32(buffer, 0);
                int offset = 8; // Skip NumberOfProviders (4 bytes) + Reserved (4 bytes)

                for (int i = 0; i < providerCount; i++)
                {
                    // Read TRACE_PROVIDER_INFO structure
                    var providerGuid = Marshal.PtrToStructure<Guid>(buffer + offset);
                    offset += 16; // Size of GUID

                    int schemaSource = Marshal.ReadInt32(buffer, offset);
                    offset += 4;

                    int providerNameOffset = Marshal.ReadInt32(buffer, offset);
                    offset += 4;

                    string providerName = string.Empty;
                    if (providerNameOffset > 0)
                    {
                        providerName = Marshal.PtrToStringUni(buffer + providerNameOffset) ?? string.Empty;
                    }

                    providers.Add(new RegisteredProviderInfo(providerGuid, providerName, schemaSource));
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            return providers;
        }

        /// <summary>
        /// Clears the cached list of registered providers.
        /// Call this if providers may have been registered/unregistered.
        /// </summary>
        public static void ClearCache()
        {
            lock (_cacheLock)
            {
                _registeredProviders = null;
            }
        }

        /// <summary>
        /// Attempts to resolve a provider name to its GUID
        /// </summary>
        /// <param name="providerName">The provider name to resolve</param>
        /// <param name="guid">The resolved GUID if successful</param>
        /// <returns>True if resolved, false otherwise</returns>
        public static bool TryResolveProviderGuid(string providerName, out Guid guid)
        {
            guid = Guid.Empty;

            if (string.IsNullOrWhiteSpace(providerName))
                return false;

            if (Guid.TryParse(providerName, out guid))
                return true;

            // Try to find the GUID from registered providers
            var registeredProvider = GetRegisteredProviderInfo()
                .FirstOrDefault(p => string.Equals(p.Name, providerName, StringComparison.OrdinalIgnoreCase));
            
            if (registeredProvider != null)
            {
                guid = registeredProvider.Guid;
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Information about a registered ETW provider.
    /// </summary>
    public class RegisteredProviderInfo
    {
        public Guid Guid { get; }
        public string Name { get; }
        public int SchemaSource { get; }

        public RegisteredProviderInfo(Guid guid, string name, int schemaSource)
        {
            Guid = guid;
            Name = name;
            SchemaSource = schemaSource;
        }

        public override string ToString() => $"{Name} ({Guid})";
    }
}
