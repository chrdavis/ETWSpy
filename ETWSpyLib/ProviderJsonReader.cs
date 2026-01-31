using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ETWSpyLib
{
    /// <summary>
    /// Represents an ETW provider entry with name and GUID for JSON serialization.
    /// </summary>
    public class ProviderEntry
    {
        [JsonPropertyName("Name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("Guid")]
        public string GuidString { get; set; } = string.Empty;

        [JsonIgnore]
        public Guid Guid
        {
            get
            {
                var guidStr = GuidString.Trim();
                if (guidStr.StartsWith('{') && guidStr.EndsWith('}'))
                {
                    guidStr = guidStr[1..^1];
                }
                return Guid.TryParse(guidStr, out var guid) ? guid : Guid.Empty;
            }
            set => GuidString = $"{{{value}}}";
        }

        public ProviderEntry()
        {
        }

        public ProviderEntry(string name, Guid guid)
        {
            Name = name;
            Guid = guid;
        }

        public override string ToString() => $"{Name},{{{Guid}}}";
    }

    /// <summary>
    /// Provides read and write operations for ETW provider JSON files.
    /// Also provides access to the embedded default provider list resource.
    /// </summary>
    public static class ProviderJsonReader
    {
        private const string EmbeddedResourceName = "ETWSpyLib.ProviderNameGuid.json";
        private const string LocalFileName = "ProviderNameGuid.json";

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        /// <summary>
        /// Gets the path to the local user-editable JSON file.
        /// </summary>
        public static string GetLocalJsonPath()
        {
            var assemblyLocation = typeof(ProviderJsonReader).Assembly.Location;
            var directory = Path.GetDirectoryName(assemblyLocation) ?? string.Empty;
            return Path.Combine(directory, LocalFileName);
        }

        /// <summary>
        /// Reads ETW provider entries from a JSON file.
        /// </summary>
        /// <param name="filePath">The path to the JSON file.</param>
        /// <returns>A list of provider entries.</returns>
        /// <exception cref="FileNotFoundException">Thrown when the file does not exist.</exception>
        public static List<ProviderEntry> ReadFromFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("Provider JSON file not found.", filePath);
            }

            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<List<ProviderEntry>>(json, _jsonOptions) ?? [];
        }

        /// <summary>
        /// Reads ETW provider entries from a stream.
        /// </summary>
        /// <param name="stream">The stream to read from.</param>
        /// <returns>A list of provider entries.</returns>
        public static List<ProviderEntry> ReadFromStream(Stream stream)
        {
            return JsonSerializer.Deserialize<List<ProviderEntry>>(stream, _jsonOptions) ?? [];
        }

        /// <summary>
        /// Writes ETW provider entries to a JSON file.
        /// </summary>
        /// <param name="filePath">The path to the JSON file.</param>
        /// <param name="entries">The provider entries to write.</param>
        public static void WriteToFile(string filePath, IEnumerable<ProviderEntry> entries)
        {
            var json = JsonSerializer.Serialize(entries, _jsonOptions);
            File.WriteAllText(filePath, json);
        }

        /// <summary>
        /// Writes ETW provider entries to a stream.
        /// </summary>
        /// <param name="stream">The stream to write to.</param>
        /// <param name="entries">The provider entries to write.</param>
        public static void WriteToStream(Stream stream, IEnumerable<ProviderEntry> entries)
        {
            JsonSerializer.Serialize(stream, entries, _jsonOptions);
        }

        /// <summary>
        /// Reads the default provider list from the embedded resource.
        /// </summary>
        /// <returns>A list of default provider entries.</returns>
        public static List<ProviderEntry> ReadFromEmbeddedResource()
        {
            var assembly = typeof(ProviderJsonReader).Assembly;
            using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName);
            
            if (stream == null)
            {
                throw new InvalidOperationException($"Embedded resource '{EmbeddedResourceName}' not found.");
            }

            return ReadFromStream(stream);
        }

        /// <summary>
        /// Checks if the embedded default provider resource exists.
        /// </summary>
        /// <returns>True if the resource exists, false otherwise.</returns>
        public static bool HasEmbeddedResource()
        {
            var assembly = typeof(ProviderJsonReader).Assembly;
            using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName);
            return stream != null;
        }

        /// <summary>
        /// Resets the local provider file to the embedded default.
        /// </summary>
        /// <returns>True if reset was successful, false otherwise.</returns>
        public static bool ResetToDefault()
        {
            try
            {
                var defaultEntries = ReadFromEmbeddedResource();
                var localPath = GetLocalJsonPath();
                WriteToFile(localPath, defaultEntries);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Ensures the local JSON file exists. If it doesn't, creates it from the embedded resource.
        /// </summary>
        public static void EnsureLocalFileExists()
        {
            var localPath = GetLocalJsonPath();
            if (!File.Exists(localPath))
            {
                ResetToDefault();
            }
        }

        /// <summary>
        /// Reads providers from the local file, or from embedded resource if local file doesn't exist.
        /// </summary>
        /// <returns>A list of provider entries.</returns>
        public static List<ProviderEntry> ReadProviders()
        {
            var localPath = GetLocalJsonPath();
            
            if (File.Exists(localPath))
            {
                return ReadFromFile(localPath);
            }

            // Fall back to embedded resource
            return ReadFromEmbeddedResource();
        }
    }
}
