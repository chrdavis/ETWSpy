using Microsoft.O365.Security.ETW;

namespace ETWSpyLib
{
    /// <summary>
    /// Wrapper for ETW Provider configuration (user-mode)
    /// </summary>
    public class EtwProviderWrapper : IDisposable
    {
        private readonly List<EtwEventFilter> _filters = new();
        private IEventRecordDelegate? _onEventCallback;
        private bool _disposed;

        public Provider Provider { get; }
        public string? Name { get; }
        public Guid? Guid { get; }
        public IReadOnlyList<EtwEventFilter> Filters => _filters.AsReadOnly();

        /// <summary>
        /// Creates a provider wrapper by provider name
        /// </summary>
        public EtwProviderWrapper(string providerName)
        {
            Name = providerName;
            Provider = new Provider(providerName);
        }

        /// <summary>
        /// Creates a provider wrapper by provider GUID
        /// </summary>
        public EtwProviderWrapper(Guid providerGuid)
        {
            Guid = providerGuid;
            Provider = new Provider(providerGuid);
        }

        /// <summary>
        /// Registers a callback to receive ALL events from this provider.
        /// Use this instead of AddEventFilter when you want to capture all events.
        /// </summary>
        public void OnAllEvents(IEventRecordDelegate callback)
        {
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            // Store reference so we can unsubscribe later
            _onEventCallback = callback;
            Provider.OnEvent += _onEventCallback;
        }

        /// <summary>
        /// Adds an event filter to this provider
        /// </summary>
        public void AddFilter(EtwEventFilter filter)
        {
            if (filter == null)
                throw new ArgumentNullException(nameof(filter));

            _filters.Add(filter);
            Provider.AddFilter(filter.Filter);
        }

        /// <summary>
        /// Removes an event filter from this provider
        /// </summary>
        public bool RemoveFilter(EtwEventFilter filter)
        {
            if (filter == null)
                throw new ArgumentNullException(nameof(filter));

            return _filters.Remove(filter);
        }

        /// <summary>
        /// Removes all event filters from this provider
        /// </summary>
        public void ClearFilters()
        {
            _filters.Clear();
        }

        /// <summary>
        /// Gets the count of registered filters
        /// </summary>
        public int FilterCount => _filters.Count;

        /// <summary>
        /// Creates and adds a filter for a specific event ID
        /// </summary>
        public EtwEventFilter AddEventFilter(ushort eventId, Action<IEventRecord>? callback = null)
        {
            var filter = new EtwEventFilter(eventId);
            if (callback != null)
            {
                filter.OnEvent(callback);
            }
            AddFilter(filter);
            return filter;
        }

        /// <summary>
        /// Sets the trace level for this provider
        /// </summary>
        public void SetTraceLevel(TraceLevel level)
        {
            Provider.Level = (byte)level;
        }

        /// <summary>
        /// Sets keywords to filter events
        /// </summary>
        public void SetKeywords(ulong keywords)
        {
            Provider.Any = keywords;
        }

        /// <summary>
        /// Enables all events from this provider
        /// </summary>
        public void EnableAllEvents()
        {
            Provider.Any = 0xFFFFFFFFFFFFFFFF;
        }

        /// <summary>
        /// Sets the provider trace flags
        /// </summary>
        public void SetTraceFlags(TraceFlags flags)
        {
            Provider.TraceFlags = flags;
        }

        /// <summary>
        /// Sets the provider trace flags using raw value
        /// </summary>
        public void SetTraceFlagsRaw(byte flags)
        {
            Provider.TraceFlags = (TraceFlags)flags;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            // Unsubscribe from events to break callback reference
            if (_onEventCallback != null)
            {
                Provider.OnEvent -= _onEventCallback;
                _onEventCallback = null;
            }

            _filters.Clear();
        }
    }

    public enum TraceLevel : byte
    {
        Critical = 1,
        Error = 2,
        Warning = 3,
        Information = 4,
        Verbose = 5
    }
}