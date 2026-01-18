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
    /// Manages the list of ETW providers loaded from CSV file.
    /// </summary>
    public static class ProviderManager
    {
        private static List<ProviderInfo>? _cachedProviders;
        private static readonly object _cacheLock = new();
        private static Task? _preloadTask;

        /// <summary>
        /// Gets the providers loaded from the CSV file.
        /// Uses cached value if available.
        /// </summary>
        private static List<ProviderInfo> Providers
        {
            get
            {
                if (_cachedProviders != null)
                {
                    return _cachedProviders;
                }

                lock (_cacheLock)
                {
                    _cachedProviders ??= LoadProvidersFromCsv();
                    return _cachedProviders;
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
                // Force loading of providers
                _ = Providers;
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
        /// </summary>
        public static void InvalidateCache()
        {
            lock (_cacheLock)
            {
                _cachedProviders = null;
            }
        }

        /// <summary>
        /// Loads providers from the ProviderNameGuid.csv file.
        /// </summary>
        private static List<ProviderInfo> LoadProvidersFromCsv()
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
        /// Gets all providers sorted alphabetically by name.
        /// Results are cached for performance.
        /// </summary>
        /// <returns>List of providers sorted by name.</returns>
        public static List<ProviderInfo> GetAllProviders()
        {
            return Providers
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Finds a provider by name.
        /// </summary>
        /// <param name="name">The provider name to search for.</param>
        /// <returns>The ProviderInfo if found, null otherwise.</returns>
        public static ProviderInfo? FindByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            return Providers
                .FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
