using Microsoft.O365.Security.ETW;
using System.Runtime.InteropServices;

namespace ETWSpyLib
{
    /// <summary>
    /// Validates ETW provider names and GUIDs before use
    /// </summary>
    public static class EtwProviderValidator
    {
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

            try
            {
                using var provider = new Provider(providerName);
                // If we get here without exception, the name was valid
                // Note: krabsetw doesn't expose the resolved GUID directly,
                // so we can only confirm the name is valid
                return false; // Can't get the GUID, but name is valid
            }
            catch
            {
                return false;
            }
        }
    }
}
