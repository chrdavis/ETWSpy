using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ETWSpyLib
{
    /// <summary>
    /// Represents an ETW provider entry from the CSV file with name and GUID.
    /// </summary>
    public class CsvProviderEntry
    {
        public string Name { get; set; } = string.Empty;
        public Guid Guid { get; set; }

        public CsvProviderEntry()
        {
        }

        public CsvProviderEntry(string name, Guid guid)
        {
            Name = name;
            Guid = guid;
        }

        public override string ToString() => $"{Name},{{{Guid}}}";
    }

    /// <summary>
    /// Provides read and write operations for ETW provider CSV files.
    /// The CSV format is: ProviderName,{GUID}
    /// </summary>
    public static class ProviderCsvReader
    {
        /// <summary>
        /// Reads ETW provider entries from a CSV file.
        /// </summary>
        /// <param name="filePath">The path to the CSV file.</param>
        /// <returns>A list of provider entries.</returns>
        /// <exception cref="FileNotFoundException">Thrown when the file does not exist.</exception>
        /// <exception cref="FormatException">Thrown when a line has an invalid format.</exception>
        public static List<CsvProviderEntry> ReadFromFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("Provider CSV file not found.", filePath);
            }

            var entries = new List<CsvProviderEntry>();
            var lines = File.ReadAllLines(filePath);

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                
                // Skip empty lines
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var entry = ParseLine(line, i + 1);
                if (entry != null)
                {
                    entries.Add(entry);
                }
            }

            return entries;
        }

        /// <summary>
        /// Reads ETW provider entries from a stream.
        /// </summary>
        /// <param name="stream">The stream to read from.</param>
        /// <returns>A list of provider entries.</returns>
        public static List<CsvProviderEntry> ReadFromStream(Stream stream)
        {
            var entries = new List<CsvProviderEntry>();
            
            using var reader = new StreamReader(stream);
            int lineNumber = 0;
            
            while (!reader.EndOfStream)
            {
                lineNumber++;
                var line = reader.ReadLine()?.Trim();
                
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var entry = ParseLine(line, lineNumber);
                if (entry != null)
                {
                    entries.Add(entry);
                }
            }

            return entries;
        }

        /// <summary>
        /// Writes ETW provider entries to a CSV file.
        /// </summary>
        /// <param name="filePath">The path to the CSV file.</param>
        /// <param name="entries">The provider entries to write.</param>
        public static void WriteToFile(string filePath, IEnumerable<CsvProviderEntry> entries)
        {
            var lines = entries.Select(e => $"{e.Name},{{{e.Guid}}}");
            File.WriteAllLines(filePath, lines);
        }

        /// <summary>
        /// Writes ETW provider entries to a stream.
        /// </summary>
        /// <param name="stream">The stream to write to.</param>
        /// <param name="entries">The provider entries to write.</param>
        public static void WriteToStream(Stream stream, IEnumerable<CsvProviderEntry> entries)
        {
            using var writer = new StreamWriter(stream, leaveOpen: true);
            
            foreach (var entry in entries)
            {
                writer.WriteLine($"{entry.Name},{{{entry.Guid}}}");
            }
        }

        /// <summary>
        /// Parses a single CSV line into a provider entry.
        /// </summary>
        /// <param name="line">The line to parse.</param>
        /// <param name="lineNumber">The line number for error reporting.</param>
        /// <returns>The parsed entry, or null if the line is invalid.</returns>
        private static CsvProviderEntry? ParseLine(string line, int lineNumber)
        {
            // Find the last comma that precedes a GUID (format: Name,{GUID})
            var lastCommaIndex = line.LastIndexOf(',');
            
            if (lastCommaIndex <= 0 || lastCommaIndex >= line.Length - 1)
            {
                return null;
            }

            var name = line[..lastCommaIndex].Trim();
            var guidPart = line[(lastCommaIndex + 1)..].Trim();

            // Remove curly braces if present
            if (guidPart.StartsWith('{') && guidPart.EndsWith('}'))
            {
                guidPart = guidPart[1..^1];
            }

            if (!Guid.TryParse(guidPart, out var guid))
            {
                return null;
            }

            return new CsvProviderEntry(name, guid);
        }

        /// <summary>
        /// Gets the default path to the embedded ProviderNameGuid.csv file.
        /// </summary>
        /// <returns>The path to the CSV file relative to the assembly location.</returns>
        public static string GetDefaultCsvPath()
        {
            var assemblyLocation = typeof(ProviderCsvReader).Assembly.Location;
            var directory = Path.GetDirectoryName(assemblyLocation) ?? string.Empty;
            return Path.Combine(directory, "ProviderNameGuid.csv");
        }
    }
}
