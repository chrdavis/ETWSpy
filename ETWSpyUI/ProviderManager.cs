using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace ETWSpyUI
{
    /// <summary>
    /// Represents an ETW provider with its name, optional GUID, and optional description.
    /// </summary>
    public class ProviderInfo
    {
        public string Name { get; set; } = string.Empty;
        public Guid? Guid { get; set; }
        public string Description { get; set; } = string.Empty;

        public ProviderInfo()
        {
        }

        public ProviderInfo(string name)
        {
            Name = name;
        }

        public ProviderInfo(string name, Guid guid, string description = "")
        {
            Name = name;
            Guid = guid;
            Description = description;
        }

        public ProviderInfo(string name, string guidString, string description = "")
        {
            Name = name;
            Description = description;
            
            if (!string.IsNullOrEmpty(guidString) && System.Guid.TryParse(guidString, out var parsedGuid))
            {
                Guid = parsedGuid;
            }
        }

        /// <summary>
        /// Gets the identifier to use when creating the ETW provider.
        /// Returns the GUID if available, otherwise returns the name.
        /// </summary>
        public string GetProviderIdentifier() => Guid?.ToString() ?? Name;

        public override string ToString() => Name;
    }

    /// <summary>
    /// Manages the list of ETW providers with support for persistence to the Windows registry.
    /// Combines hardcoded default providers with user-added providers stored in the registry.
    /// </summary>
    public static class ProviderManager
    {
        private const string RegistryKeyPath = @"SOFTWARE\ETWSpy\Providers";

        /// <summary>
        /// Hardcoded list of common ETW providers.
        /// </summary>
        private static readonly List<ProviderInfo> DefaultProviders =
        [
            new("Google.Chrome", "d2d578d9-2936-45b6-a09f-30e32715f42d"),
            new("Microsoft-Antimalware-AMFilter"),
            new("Microsoft-Antimalware-Engine"),
            new("Microsoft-Antimalware-Protection"),
            new("Microsoft-Antimalware-RTP"),
            new("Microsoft-Antimalware-Service"),
            new("Microsoft-Windows-AppLifeCycle-UI", "ee97cdc4-b095-5c70-6e37-a541eb74c2b5"),
            new("Microsoft-Windows-COMRuntime"),
            new("Microsoft-Windows-D3D10"),
            new("Microsoft-Windows-D3D10_1"),
            new("Microsoft-Windows-D3D10Level9"),
            new("Microsoft-Windows-D3D11"),
            new("Microsoft-Windows-DNS-Client"),
            new("Microsoft-Windows-DotNETRuntime"),
            new("Microsoft-Windows-Dwm", "d29d56ea-4867-4221-b02e-cfd998834075"),
            new("Microsoft-Windows-Dwm-Api", "292a52c4-fa27-4461-b526-54a46430bd54"),
            new("Microsoft-Windows-Dwm-Core", "9e9bba3c-2e38-40cb-99f4-9e8281425164"),
            new("Microsoft-Windows-Dwm-Redir", "7d99f6a4-1bec-4c09-9703-3aaa8148347f"),
            new("Microsoft-Windows-DXGI"),
            new("Microsoft-Windows-DxgKrnl"),
            new("Microsoft-Windows-DXVA2"),
            new("Microsoft-Windows-Energy-Estimation-Engine"),
            new("Microsoft-Windows-FaultReporting"),
            new("Microsoft-Windows-HangReporting"),
            new("Microsoft-Windows-ImageLoad"),
            new("Microsoft-Windows-Kernel-Acpi"),
            new("Microsoft-Windows-Kernel-File"),
            new("Microsoft-Windows-Kernel-Memory"),
            new("Microsoft-Windows-Kernel-Network"),
            new("Microsoft-Windows-Kernel-Pep"),
            new("Microsoft-Windows-Kernel-Power"),
            new("Microsoft-Windows-Kernel-Process"),
            new("Microsoft-Windows-Kernel-Processor-Power"),
            new("Microsoft-Windows-Kernel-Registry"),
            new("Microsoft-Windows-LDAP-Client"),
            new("Microsoft-Windows-LimitsManagement"),
            new("Microsoft-Windows-Networking-Correlation"),
            new("Microsoft-Windows-PDC"),
            new("Microsoft-Windows-PowerShell"),
            new("Microsoft-Windows-RPC"),
            new("Microsoft.Windows.Launcher.Desktop", "e3185da8-ecf4-4051-8bf1-8b6602e3577d"),
            new("Microsoft-Windows-Shell-DefaultAssoc", "e305fb0f-da8e-52b5-a918-7a4f17a2531a"),
            new("Microsoft-Windows-Shell-Taskbar", "df8dab3f-b1c9-58d3-2ea1-4c08592bb71b"),
            new("Microsoft-Windows-Shell-Launcher", "3d6120a6-0986-51c4-213a-e2975903051d"),
            new("Microsoft-Windows-ShellExecute", "382b5e24-181e-417f-a8d6-2155f749e724"),
            new("Microsoft-Windows-Security-Auditing"),
            new("Microsoft-Windows-TCPIP"),
            new("Microsoft-Windows-Thermal-Polling"),
            new("Microsoft-Windows-UMD"),
            new("Microsoft-Windows-Warp"),
            new("Microsoft-Windows-Win32k"),
            new("Microsoft-Windows-WindowsErrorReporting"),
            new("Microsoft-Windows-WinHttp"),
            new("Microsoft-Windows-WinINet"),
            new("MSEdge.Beta", "BD089BAA-4E52-4794-A887-9E96868570D2"),
            new("MSEdge.Canary", "C56B8664-45C5-4E65-B3C7-A8D6BD3F2E67"),
            new("MSEdge.Dev", "D30B5C9F-B58F-4DC9-AFAF-134405D72107"),
            new("MSEdge.Internal", "49C85E08-E8A5-49D6-81EA-7270531EC8AF"),
            new("MSEdge.Stable", "3A5F2396-5C8F-4F1F-9B67-6CCA6C990E61"),
            new("MSEdge.WebView", "E16EC3D2-BB0F-4E8F-BDB8-DE0BEA82DC3D"),
        ];

        /// <summary>
        /// Gets all providers (default + user-added from registry), sorted alphabetically by name.
        /// Duplicates are automatically excluded.
        /// </summary>
        /// <returns>List of unique providers sorted by name.</returns>
        public static List<ProviderInfo> GetAllProviders()
        {
            var providers = new Dictionary<string, ProviderInfo>(StringComparer.OrdinalIgnoreCase);

            // Add default providers first
            foreach (var provider in DefaultProviders)
            {
                providers[provider.Name] = provider;
            }

            // Add user providers from registry (won't overwrite existing defaults)
            foreach (var provider in LoadProvidersFromRegistry())
            {
                if (!providers.ContainsKey(provider.Name))
                {
                    providers[provider.Name] = provider;
                }
            }

            // Return sorted by name
            return providers.Values
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Adds a new provider to the registry if it doesn't already exist.
        /// </summary>
        /// <param name="name">The provider name.</param>
        /// <param name="guid">Optional provider GUID.</param>
        /// <param name="description">Optional description of the provider.</param>
        /// <returns>True if the provider was added, false if it already exists.</returns>
        public static bool AddProvider(string name, Guid? guid = null, string description = "")
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            // Check if already exists in defaults
            if (DefaultProviders.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            // Check if already exists in registry
            var registryProviders = LoadProvidersFromRegistry();
            if (registryProviders.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            // Add to registry
            SaveProviderToRegistry(name, guid, description);
            return true;
        }

        /// <summary>
        /// Finds a provider by name from all providers (defaults + registry).
        /// </summary>
        /// <param name="name">The provider name to search for.</param>
        /// <returns>The ProviderInfo if found, null otherwise.</returns>
        public static ProviderInfo? FindByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            return GetAllProviders()
                .FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Loads user-added providers from the Windows registry.
        /// Each provider is stored as a subkey with Name, Guid (optional), and Description (optional) values.
        /// </summary>
        private static List<ProviderInfo> LoadProvidersFromRegistry()
        {
            var providers = new List<ProviderInfo>();

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
                if (key == null)
                {
                    return providers;
                }

                // Each provider is stored as a subkey
                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    using var providerKey = key.OpenSubKey(subKeyName);
                    if (providerKey == null) continue;

                    var name = providerKey.GetValue("Name") as string ?? subKeyName;
                    var guidString = providerKey.GetValue("Guid") as string;
                    var description = providerKey.GetValue("Description") as string ?? string.Empty;

                    Guid? guid = null;
                    if (!string.IsNullOrEmpty(guidString) && Guid.TryParse(guidString, out var parsedGuid))
                    {
                        guid = parsedGuid;
                    }

                    providers.Add(new ProviderInfo(name, description) { Guid = guid });
                }
            }
            catch
            {
                // Silently ignore registry read failures
            }

            return providers;
        }

        /// <summary>
        /// Saves a provider to the Windows registry as a subkey with Name, Guid, and Description values.
        /// </summary>
        private static void SaveProviderToRegistry(string name, Guid? guid, string description)
        {
            try
            {
                // Create a sanitized subkey name (replace invalid characters)
                var subKeyName = SanitizeRegistryKeyName(name);
                
                using var key = Registry.CurrentUser.CreateSubKey($@"{RegistryKeyPath}\{subKeyName}");
                if (key == null) return;

                key.SetValue("Name", name, RegistryValueKind.String);
                
                if (guid.HasValue)
                {
                    key.SetValue("Guid", guid.Value.ToString(), RegistryValueKind.String);
                }

                if (!string.IsNullOrEmpty(description))
                {
                    key.SetValue("Description", description, RegistryValueKind.String);
                }
            }
            catch
            {
                // Silently ignore registry write failures
            }
        }

        /// <summary>
        /// Sanitizes a string for use as a registry key name by replacing invalid characters.
        /// </summary>
        private static string SanitizeRegistryKeyName(string name)
        {
            // Registry key names cannot contain backslashes
            return name.Replace('\\', '_').Replace('/', '_');
        }
    }
}
