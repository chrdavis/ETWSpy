using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ETWSpyLib
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
    /// Combines providers from CSV file with user-added providers stored in the registry.
    /// </summary>
    public static class ProviderManager
    {
        private const string RegistryKeyPath = @"SOFTWARE\ETWSpy\Providers";

        private static List<ProviderInfo>? _cachedDefaultProviders;
        private static List<ProviderInfo>? _cachedAllProviders;
        private static readonly object _cacheLock = new();
        private static Task? _preloadTask;

        /// <summary>
        /// Gets the default providers loaded from the CSV file.
        /// Uses cached value if available.
        /// </summary>
        private static List<ProviderInfo> DefaultProviders
        {
            get
            {
                if (_cachedDefaultProviders != null)
                {
                    return _cachedDefaultProviders;
                }

                lock (_cacheLock)
                {
                    _cachedDefaultProviders ??= LoadDefaultProvidersFromCsv();
                    return _cachedDefaultProviders;
                }
            }
        }

        /// <summary>
        /// Starts pre-loading providers in the background.
        /// Call this early in app startup to minimize delay when opening provider window.
        /// </summary>
        public static void PreloadProvidersAsync()
        {
            if (_preloadTask != null)
            {
                return;
            }

            _preloadTask = Task.Run(() =>
            {
                // Force loading of default providers
                _ = DefaultProviders;
                // Also pre-compute the full provider list
                _ = GetAllProviders();
            });
        }

        /// <summary>
        /// Waits for the preload task to complete if it's running.
        /// </summary>
        public static void WaitForPreload()
        {
            _preloadTask?.Wait();
        }

        /// <summary>
        /// Invalidates the cached provider list, forcing a reload on next access.
        /// Call this after adding new providers.
        /// </summary>
        public static void InvalidateCache()
        {
            lock (_cacheLock)
            {
                _cachedAllProviders = null;
            }
        }

        /// <summary>
        /// Loads default providers from the ProviderNameGuid.csv file.
        /// </summary>
        private static List<ProviderInfo> LoadDefaultProvidersFromCsv()
        {
            try
            {
                var csvPath = ProviderCsvReader.GetDefaultCsvPath();
                var csvEntries = ProviderCsvReader.ReadFromFile(csvPath);
                
                return csvEntries
                    .Select(e => new ProviderInfo(e.Name, e.Guid))
                    .ToList();
            }
            catch
            {
                // If CSV file is not found or cannot be read, return empty list
                return [];
            }
        }

        /// <summary>
        /// Gets all providers (default + user-added from registry), sorted alphabetically by name.
        /// Duplicates are automatically excluded. Results are cached for performance.
        /// </summary>
        /// <returns>List of unique providers sorted by name.</returns>
        public static List<ProviderInfo> GetAllProviders()
        {
            // Return cached list if available
            if (_cachedAllProviders != null)
            {
                return _cachedAllProviders;
            }

            lock (_cacheLock)
            {
                // Double-check after acquiring lock
                if (_cachedAllProviders != null)
                {
                    return _cachedAllProviders;
                }

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

                // Cache and return sorted by name
                _cachedAllProviders = providers.Values
                    .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return _cachedAllProviders;
            }
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

            // Add to registry and invalidate cache
            SaveProviderToRegistry(name, guid, description);
            InvalidateCache();
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
