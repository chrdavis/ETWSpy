using System.Collections.Concurrent;

namespace ETWSpyLib
{
    /// <summary>
    /// Provides string interning (flyweight pattern) to deduplicate repeated strings in memory.
    /// Uses a thread-safe concurrent dictionary to store unique string instances.
    /// This is particularly useful for ETW events where provider names, event names, 
    /// task names, and property names are frequently repeated across thousands of events.
    /// </summary>
    public class StringInterner
    {
        private readonly ConcurrentDictionary<string, string> _internedStrings = new(StringComparer.Ordinal);

        /// <summary>
        /// Maximum number of interned strings before automatic cleanup.
        /// Prevents unbounded memory growth from highly variable strings.
        /// </summary>
        private const int MaxInternedStrings = 50000;

        /// <summary>
        /// Interns a string, returning a shared instance if one already exists.
        /// If the string is null or empty, returns the original value without interning.
        /// </summary>
        /// <param name="value">The string to intern.</param>
        /// <returns>A shared instance of the string, or the original if null/empty.</returns>
        public string Intern(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value ?? string.Empty;
            }

            // Try to get existing interned string
            if (_internedStrings.TryGetValue(value, out var interned))
            {
                return interned;
            }

            // Check if we need to clear (simple size-based eviction)
            // This prevents unbounded memory growth from highly variable payload values
            if (_internedStrings.Count >= MaxInternedStrings)
            {
                // Clear and start fresh - this is a simple approach
                // More sophisticated LRU eviction could be implemented if needed
                _internedStrings.Clear();
            }

            // Add and return the new string
            // GetOrAdd handles race conditions where another thread may have added it
            return _internedStrings.GetOrAdd(value, value);
        }

        /// <summary>
        /// Gets the current count of interned strings.
        /// </summary>
        public int Count => _internedStrings.Count;

        /// <summary>
        /// Clears all interned strings, releasing memory.
        /// </summary>
        public void Clear()
        {
            _internedStrings.Clear();
        }

        /// <summary>
        /// Shared instance for application-wide string interning.
        /// Thread-safe for concurrent access from ETW event callbacks.
        /// </summary>
        public static StringInterner Shared { get; } = new();
    }
}
