using Microsoft.O365.Security.ETW;

namespace ETWSpyLib
{
    /// <summary>
    /// Wrapper for ETW event filtering with enhanced capabilities
    /// </summary>
    public class EtwEventFilter
    {
        private readonly List<Action<IEventRecord>> _callbacks = new();
        private readonly List<Predicate<IEventRecord>> _predicates = new();

        public EventFilter Filter { get; }
        public ushort EventId { get; }
        public IReadOnlyList<Action<IEventRecord>> Callbacks => _callbacks.AsReadOnly();

        /// <summary>
        /// Creates an event filter for a specific event ID
        /// </summary>
        public EtwEventFilter(ushort eventId)
        {
            EventId = eventId;
            Filter = new EventFilter(eventId);
        }

        /// <summary>
        /// Adds a callback when events matching this filter are received
        /// </summary>
        public void OnEvent(Action<IEventRecord> callback)
        {
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            _callbacks.Add(callback);
            Filter.OnEvent += (record) => callback(record);
        }

        /// <summary>
        /// Adds a predicate-based filter that only processes events matching the condition
        /// </summary>
        public void Where(Predicate<IEventRecord> predicate, Action<IEventRecord> callback)
        {
            if (predicate == null)
                throw new ArgumentNullException(nameof(predicate));
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            _predicates.Add(predicate);
            Filter.OnEvent += (record) =>
            {
                if (predicate(record))
                {
                    callback(record);
                }
            };
        }

        /// <summary>
        /// Filters events by process ID
        /// </summary>
        public void WhereProcessId(uint processId, Action<IEventRecord> callback)
        {
            Where(record => 
            {
                try
                {
                    return record.ProcessId == processId;
                }
                catch
                {
                    return false;
                }
            }, callback);
        }

        /// <summary>
        /// Filters events by thread ID
        /// </summary>
        public void WhereThreadId(uint threadId, Action<IEventRecord> callback)
        {
            Where(record =>
            {
                try
                {
                    return record.ThreadId == threadId;
                }
                catch
                {
                    return false;
                }
            }, callback);
        }

        /// <summary>
        /// Filters events by a property value
        /// </summary>
        public void WhereProperty<T>(string propertyName, T expectedValue, Action<IEventRecord> callback)
            where T : IEquatable<T>
        {
            Where(record =>
            {
                try
                {
                    var value = GetPropertyValue<T>(record, propertyName);
                    return value != null && value.Equals(expectedValue);
                }
                catch
                {
                    return false;
                }
            }, callback);
        }

        /// <summary>
        /// Filters events where a string property contains specific text
        /// </summary>
        public void WherePropertyContains(string propertyName, string searchText, Action<IEventRecord> callback)
        {
            Where(record =>
            {
                try
                {
                    var value = record.GetUnicodeString(propertyName, null) 
                             ?? record.GetAnsiString(propertyName, null);
                    return value != null && value.Contains(searchText, StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    return false;
                }
            }, callback);
        }

        /// <summary>
        /// Gets the count of registered callbacks
        /// </summary>
        public int CallbackCount => _callbacks.Count;

        /// <summary>
        /// Gets the count of registered predicates
        /// </summary>
        public int PredicateCount => _predicates.Count;

        /// <summary>
        /// Helper method to get property values with proper type handling
        /// </summary>
        private T? GetPropertyValue<T>(IEventRecord record, string propertyName)
        {
            var type = typeof(T);

            if (type == typeof(string))
            {
                return (T?)(object?)(record.GetUnicodeString(propertyName, null) 
                                  ?? record.GetAnsiString(propertyName, null));
            }
            else if (type == typeof(uint))
            {
                return (T)(object)record.GetUInt32(propertyName, 0);
            }
            else if (type == typeof(int))
            {
                return (T)(object)record.GetInt32(propertyName, 0);
            }
            else if (type == typeof(ulong))
            {
                return (T)(object)record.GetUInt64(propertyName, 0);
            }
            else if (type == typeof(long))
            {
                return (T)(object)record.GetInt64(propertyName, 0);
            }
            else if (type == typeof(ushort))
            {
                return (T)(object)record.GetUInt16(propertyName, 0);
            }
            else if (type == typeof(short))
            {
                return (T)(object)record.GetInt16(propertyName, 0);
            }
            else if (type == typeof(byte))
            {
                return (T)(object)record.GetUInt8(propertyName, 0);
            }
            else if (type == typeof(bool))
            {
                return (T)(object)(record.GetUInt8(propertyName, 0) != 0);
            }
            else if (type == typeof(Guid))
            {
                // GUIDs in ETW are typically stored as binary data
                try
                {
                    var guidBytes = record.GetBinary(propertyName);
                    if (guidBytes != null && guidBytes.Length == 16)
                    {
                        return (T)(object)new Guid(guidBytes);
                    }
                }
                catch
                {
                    // Property may not exist or not be binary
                }
                return (T)(object)Guid.Empty;
            }

            throw new NotSupportedException($"Type {type.Name} is not supported for property extraction.");
        }
    }
}