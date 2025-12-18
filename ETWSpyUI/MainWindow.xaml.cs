using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Navigation;
using System.Windows.Threading;
using ETWSpyLib;
using Microsoft.O365.Security.ETW;
using Microsoft.Win32;

namespace ETWSpyUI
{
    /// <summary>
    /// Represents a filter entry combining provider configuration and event filtering.
    /// </summary>
    public class FilterEntry
    {
        public string Provider { get; set; } = string.Empty;
        public string EventId { get; set; } = string.Empty;
        public string MatchText { get; set; } = string.Empty;
        public string FilterType { get; set; } = string.Empty;
        public string Keywords { get; set; } = string.Empty;
        public string TraceLevel { get; set; } = string.Empty;
        public string TraceFlags { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents an ETW event for display in the DataGrid.
    /// </summary>
    public class EventRecord
    {
        public DateTime Timestamp { get; set; }
        public string ProviderName { get; set; } = string.Empty;

        public string EventName { get; set; } = string.Empty;

        public string TaskName { get; set; } = string.Empty;
        public ushort EventId { get; set; }
        public uint ProcessId { get; set; }
        public uint ThreadId { get; set; }

        /// <summary>
        /// Event payload properties as name/value pairs (not displayed in grid).
        /// </summary>
        [System.ComponentModel.Browsable(false)]
        public Dictionary<string, string> Payload { get; set; } = [];

        /// <summary>
        /// Event payload properties with type information (not displayed in grid).
        /// Used by the EventDetailsWindow to show property types.
        /// </summary>
        [System.ComponentModel.Browsable(false)]
        public List<FormattedProperty> PayloadWithTypes { get; set; } = [];

        /// <summary>
        /// Pre-computed formatted payload string for display in the DataGrid.
        /// Set once when the record is created to avoid repeated computation.
        /// </summary>
        public string PayloadDisplay { get; set; } = string.Empty;
    }

    /// <summary>
    /// Container for saving/loading all configuration data.
    /// </summary>
    public class ConfigurationData
    {
        public List<FilterEntry> Filters { get; set; } = new();
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private const int DefaultMaxEventsToDisplay = 10000;
        private const int MaxPendingQueueSize = 10000; // Limit pending queue to prevent unbounded memory growth

        // Adaptive timer interval constants
        private const int MinBatchFlushIntervalMs = 500;
        private const int MaxBatchFlushIntervalMs = 4000;
        private const int BatchFlushIntervalStepMs = 500;
        private const int HighQueueThreshold = 2000;  // Queue size considered "high pressure"
        private const int LowQueueThreshold = 500;    // Queue size considered "low pressure"
        private const int AdaptiveWindowSize = 5;     // Number of samples in sliding window
        private const int AdaptiveThresholdCount = 3; // Consecutive samples needed to adjust

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        private bool _isDarkMode;
        private bool _showTimestampsInUTC;
        private int _maxEventsToShow = DefaultMaxEventsToDisplay;
        private EtwTraceSession? _traceSession;
        private CancellationTokenSource? _traceCancellation;

        // Batching support for high-volume ETW events
        private readonly ConcurrentQueue<EventRecord> _pendingEvents = new();
        private readonly DispatcherTimer _batchTimer;
        private int _currentBatchIntervalMs = MinBatchFlushIntervalMs;

        // Adaptive interval tracking - circular buffer of recent queue sizes
        private readonly int[] _queueSizeHistory = new int[AdaptiveWindowSize];
        private int _queueSizeHistoryIndex;

        // Tracks whether event processing is paused (trace session still runs, but events are not processed)
        private bool _isPaused;

        // Use a simple List as backing store to avoid ObservableCollection overhead
        private List<EventRecord> _eventRecordsList = new(DefaultMaxEventsToDisplay);

        // Keep provider wrappers alive to prevent GC from collecting them and their callbacks
        private readonly List<EtwProviderWrapper> _activeProviders = [];

        /// <summary>
        /// Collection of filter entries added by the user.
        /// </summary>
        public ObservableCollection<FilterEntry> FilterEntries { get; } = [];

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool IsDarkMode
        {
            get => _isDarkMode;
            set
            {
                if (_isDarkMode == value) return;
                _isDarkMode = value;
                OnPropertyChanged(nameof(IsDarkMode));
                SetDarkMode(_isDarkMode);
                RegistrySettings.SaveBool(RegistrySettings.DarkMode, _isDarkMode);
            }
        }

        public bool ShowTimestampsInUTC
        {
            get => _showTimestampsInUTC;
            set
            {
                if (_showTimestampsInUTC == value) return;
                _showTimestampsInUTC = value;
                OnPropertyChanged(nameof(ShowTimestampsInUTC));
                RegistrySettings.SaveBool(RegistrySettings.ShowTimestampsInUTC, _showTimestampsInUTC);
            }
        }

        public int MaxEventsToShow
        {
            get => _maxEventsToShow;
            set
            {
                if (_maxEventsToShow == value) return;
                _maxEventsToShow = value;
                OnPropertyChanged(nameof(MaxEventsToShow));
                RegistrySettings.SaveInt(RegistrySettings.MaxEventsToShow, _maxEventsToShow);
            }
        }

        /// <summary>
        /// Gets the application version string for display in the About tab.
        /// </summary>
        public string AppVersion
        {
            get
            {
                var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                return version != null ? $"Version: {version.Major}.{version.Minor}.{version.Build}" : "Version: Unknown";
            }
        }

        public MainWindow()
        {
            HandleAdminPrivileges();
            //Opacity = 0; // Start fully transparent to prevent white flash
            InitializeComponent();

            // Bind settings (checkbox) to this window
            DataContext = this;

            // Initialize batch timer for UI updates
            _batchTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(_currentBatchIntervalMs)
            };
            _batchTimer.Tick += BatchTimer_Tick;

            PopulateProviderComboBox();
            PopulateFilterTypeComboBox();
            PopulateTraceLevelComboBox();

            // Bind the FiltersListView to the FilterEntries collection
            FiltersListView.ItemsSource = FilterEntries;

            // Subscribe to filter changes to auto-start/restart trace session
            FilterEntries.CollectionChanged += OnFilterEntriesChanged;

            // EventsDataGrid uses a plain List<T> to avoid ObservableCollection overhead
            // ItemsSource is set/reset in FlushPendingEvents to trigger UI refresh
            EventsDataGrid.ItemsSource = _eventRecordsList;

            // Load settings from registry and apply theme after window is loaded
            Loaded += (_, _) =>
            {
                LoadSettingsFromRegistry();
            };

            //ContentRendered += (_, _) =>
           // {
            //    // Apply title bar theme after window is shown
            //    Opacity = 1;
            //};

            // Clean up on close
            Closing += MainWindow_Closing;
        }

        /// <summary>
        /// Formats a timestamp according to the current ShowTimestampsInUTC setting.
        /// </summary>
        public string FormatTimestamp(DateTime timestamp)
        {
            var displayTime = ShowTimestampsInUTC ? timestamp.ToUniversalTime() : timestamp.ToLocalTime();
            var suffix = ShowTimestampsInUTC ? " UTC" : "";
            return $"{displayTime:yyyy-MM-dd HH:mm:ss.fff}{suffix}";
        }

        private void BatchTimer_Tick(object? sender, EventArgs e)
        {
            FlushPendingEvents();
        }

        /// <summary>
        /// Adjusts the batch timer interval based on queue pressure history.
        /// Increases interval when queue is consistently high to reduce UI load.
        /// Decreases interval when queue is consistently low to improve responsiveness.
        /// </summary>
        private void AdjustBatchInterval(int currentQueueSize)
        {
            // Record current queue size in circular buffer
            _queueSizeHistory[_queueSizeHistoryIndex] = currentQueueSize;
            _queueSizeHistoryIndex = (_queueSizeHistoryIndex + 1) % AdaptiveWindowSize;

            // Count how many recent samples are above/below thresholds
            int highCount = 0;
            int lowCount = 0;
            for (int i = 0; i < AdaptiveWindowSize; i++)
            {
                if (_queueSizeHistory[i] >= HighQueueThreshold)
                    highCount++;
                else if (_queueSizeHistory[i] <= LowQueueThreshold)
                    lowCount++;
            }

            int newInterval = _currentBatchIntervalMs;

            // If consistently high queue pressure, increase interval
            if (highCount >= AdaptiveThresholdCount && _currentBatchIntervalMs < MaxBatchFlushIntervalMs)
            {
                newInterval = Math.Min(_currentBatchIntervalMs + BatchFlushIntervalStepMs, MaxBatchFlushIntervalMs);
            }
            // If consistently low queue pressure, decrease interval
            else if (lowCount >= AdaptiveThresholdCount && _currentBatchIntervalMs > MinBatchFlushIntervalMs)
            {
                newInterval = Math.Max(_currentBatchIntervalMs - BatchFlushIntervalStepMs, MinBatchFlushIntervalMs);
            }

            // Apply new interval if changed
            if (newInterval != _currentBatchIntervalMs)
            {
                _currentBatchIntervalMs = newInterval;
                _batchTimer.Interval = TimeSpan.FromMilliseconds(_currentBatchIntervalMs);
            }
        }

        /// <summary>
        /// Resets the adaptive interval tracking to initial state.
        /// </summary>
        private void ResetAdaptiveInterval()
        {
            _currentBatchIntervalMs = MinBatchFlushIntervalMs;
            _batchTimer.Interval = TimeSpan.FromMilliseconds(_currentBatchIntervalMs);
            _queueSizeHistoryIndex = 0;
            Array.Clear(_queueSizeHistory);
        }

        private void FlushPendingEvents()
        {
            // Capture current queue size for adaptive interval adjustment
            int pendingCount = _pendingEvents.Count;

            // Adjust the timer interval based on queue pressure
            AdjustBatchInterval(pendingCount);

            // Dynamically adjust batch size based on queue pressure
            // Process more aggressively when queue is large to prevent memory buildup
            int maxBatchSize = pendingCount > 10000 ? 2000 : 
                               pendingCount > 5000 ? 1000 : 500;
            
            int count = 0;
            var newRecords = new List<EventRecord>(maxBatchSize);

            // Collect new records
            while (count < maxBatchSize && _pendingEvents.TryDequeue(out var record))
            {
                newRecords.Add(record);
                count++;
            }

            if (newRecords.Count == 0)
            {
                return;
            }

            // Calculate how many old records to remove using the configurable max
            int totalAfterAdd = _eventRecordsList.Count + newRecords.Count;
            int toRemove = totalAfterAdd > MaxEventsToShow ? totalAfterAdd - MaxEventsToShow : 0;

            // Remove old records from the front using RemoveRange for efficiency
            if (toRemove > 0)
            {
                _eventRecordsList.RemoveRange(0, toRemove);
            }

            // Add new records
            _eventRecordsList.AddRange(newRecords);

            // Create a completely new list and assign it to ItemsSource
            // This replaces the entire binding rather than refreshing it,
            // which avoids the WeakEventManager listener accumulation
            var displayList = new List<EventRecord>(_eventRecordsList);
            EventsDataGrid.ItemsSource = displayList;

            // Update the event count display with current interval info
            EventCountText.Text = $"Events: {_eventRecordsList.Count:N0} (max {MaxEventsToShow:N0}) | Interval: {_currentBatchIntervalMs}ms";
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            _batchTimer.Stop();
            FilterEntries.CollectionChanged -= OnFilterEntriesChanged;
            StopTracing();
        }

        /// <summary>
        /// Handles changes to the FilterEntries collection.
        /// Automatically starts, restarts, or stops the trace session based on filter changes.
        /// </summary>
        private void OnFilterEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            // If filters were added or removed, restart the trace session with new providers
            if (FilterEntries.Count > 0)
            {
                RestartTraceSession();
            }
            else
            {
                // No filters remaining - stop the trace session
                StopTracing();
                PauseResumeButton.Content = "Pause";
                StatusTextBlock.Text = "Stopped";
            }
        }

        /// <summary>
        /// Stops the current trace session and starts a new one with the current filter configuration.
        /// </summary>
        private void RestartTraceSession()
        {
            try
            {
                // Stop any existing session
                StopTracing();

                // Clear any previous provider references
                _activeProviders.Clear();

                // Reset adaptive interval to start fresh
                ResetAdaptiveInterval();

                _traceSession = EtwTraceSession.CreateUserSession($"ETWSpySession_{Guid.NewGuid():N}");
                _traceCancellation = new CancellationTokenSource();

                // Group filters by provider to create one wrapper per provider
                var providerGroups = FilterEntries
                    .GroupBy(f => f.Provider, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var providerGroup in providerGroups)
                {
                    var filterList = providerGroup.ToList();
                    var firstFilter = filterList.First();

                    // Determine if we need OnAllEvents (when there are filters with no specific event IDs)
                    // or if all filters specify event IDs (use only event filters)
                    bool hasFilterWithAllEvents = filterList.Any(f => string.IsNullOrWhiteSpace(f.EventId));

                    // Collect exclude event IDs to filter out in OnAllEvents callback
                    var excludeEventIds = new HashSet<ushort>();
                    foreach (var f in filterList.Where(f => f.FilterType == "Exclude" && !string.IsNullOrWhiteSpace(f.EventId)))
                    {
                        if (TryParseEventIds(f.EventId, out var ids, out _))
                        {
                            foreach (var id in ids)
                            {
                                excludeEventIds.Add(id);
                            }
                        }
                    }

                    var wrapper = CreateProviderWrapper(firstFilter, hasFilterWithAllEvents, excludeEventIds);

                    // Apply all include filters with specific event IDs
                    foreach (var filterEntry in filterList.Where(f => f.FilterType == "Include" && !string.IsNullOrWhiteSpace(f.EventId)))
                    {
                        ApplyFilterToProvider(wrapper, filterEntry);
                    }

                    _activeProviders.Add(wrapper); // Keep reference alive
                    _traceSession.EnableProvider(wrapper);
                }

                // Subscribe to error events from the trace session
                _traceSession.ErrorOccurred += OnTraceSessionError;

                // Start the batch timer
                _batchTimer.Start();

                // Start the trace asynchronously
                _ = _traceSession.StartAsync(_traceCancellation.Token);

                // Update UI to reflect running state
                _isPaused = false;
                PauseResumeButton.Content = "Pause";
                StatusTextBlock.Text = "Running...";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start tracing: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StopTracing();
                PauseResumeButton.Content = "Pause";
                StatusTextBlock.Text = "Stopped";
            }
        }

        private void OnPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        /// <summary>
        /// Loads all settings from the Windows registry.
        /// </summary>
        private void LoadSettingsFromRegistry()
        {
            IsDarkMode = RegistrySettings.LoadBool(RegistrySettings.DarkMode);
            ShowTimestampsInUTC = RegistrySettings.LoadBool(RegistrySettings.ShowTimestampsInUTC);
            MaxEventsToShow = RegistrySettings.LoadInt(RegistrySettings.MaxEventsToShow, DefaultMaxEventsToDisplay);
        }

        private void PopulateProviderComboBox()
        {
            // Get all providers (defaults + user-added from registry), sorted alphabetically
            var providers = ProviderManager.GetAllProviders();

            ProviderComboBox.ItemsSource = providers;
            if (ProviderComboBox.Items.Count > 0)
            {
                ProviderComboBox.SelectedIndex = 0;
            }
        }

        /// <summary>
        /// Refreshes the provider combo box to include any newly added providers.
        /// </summary>
        private void RefreshProviderComboBox()
        {
            var currentText = ProviderComboBox.Text;
            var providers = ProviderManager.GetAllProviders();
            ProviderComboBox.ItemsSource = providers;

            // Try to restore the previous selection
            var matchingProvider = providers.FirstOrDefault(p => 
                string.Equals(p.Name, currentText, StringComparison.OrdinalIgnoreCase));
            
            if (matchingProvider != null)
            {
                ProviderComboBox.SelectedItem = matchingProvider;
            }
            else if (ProviderComboBox.Items.Count > 0)
            {
                ProviderComboBox.SelectedIndex = 0;
            }
        }

        private void PopulateFilterTypeComboBox()
        {
            var filterTypes = new List<string>
            {
                "Include",
                "Exclude"
            };
            FilterTypeComboBox.ItemsSource = filterTypes;
            if (FilterTypeComboBox.Items.Count > 0)
            {
                FilterTypeComboBox.SelectedIndex = 0;
            }
        }

        private void PopulateTraceLevelComboBox()
        {
            var traceLevels = new List<string>
            {
                "Critical",
                "Error",
                "Warning",
                "Information",
                "Verbose"
            };
            TraceLevelComboBox.ItemsSource = traceLevels;
            if (TraceLevelComboBox.Items.Count > 0)
            {
                // Default to verbose
                TraceLevelComboBox.SelectedIndex = 4;
            }
        }

        private void SetDarkMode(bool enable)
        {
            SwitchTheme(enable ? "Themes/DarkTheme.xaml" : "Themes/LightTheme.xaml");
            WindowHelper.ApplyTitleBarTheme(this, enable);
        }

        private void SwitchTheme(string themePath)
        {
            var uri = new Uri(themePath, UriKind.Relative);
            var resourceDict = new ResourceDictionary { Source = uri };
            Application.Current.Resources.MergedDictionaries.Clear();
            Application.Current.Resources.MergedDictionaries.Add(resourceDict);
        }

        private void PauseResumeTracing(object sender, RoutedEventArgs e)
        {
            // If no trace session exists, nothing to pause/resume
            if (_traceSession == null)
            {
                MessageBox.Show("No active trace session. Add a filter to start tracing.", "No Session", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Toggle pause state
            _isPaused = !_isPaused;

            if (_isPaused)
            {
                // Paused - stop the batch timer but keep the trace session running
                _batchTimer.Stop();
                if (sender is Button pauseResumeButton)
                {
                    pauseResumeButton.Content = "Resume";
                }
                StatusTextBlock.Text = "Paused";
            }
            else
            {
                // Resumed - restart the batch timer
                _batchTimer.Start();
                if (sender is Button pauseResumeButton)
                {
                    pauseResumeButton.Content = "Pause";
                }
                StatusTextBlock.Text = "Running...";
            }
        }

        private void ApplyFilterToProvider(EtwProviderWrapper wrapper, FilterEntry filterEntry)
        {
            // Parse event IDs from the filter entry
            if (!TryParseEventIds(filterEntry.EventId, out var eventIds, out _))
            {
                // Parsing already validated in AddFilter, but handle gracefully
                return;
            }

            // If no specific event IDs, the OnAllEvents callback handles all events
            if (eventIds.Count == 0)
            {
                return;
            }

            // Create a filter for each event ID
            foreach (var eventId in eventIds)
            {
                var filter = new EtwEventFilter(eventId);
                
                // If there's match text, add a predicate filter
                if (!string.IsNullOrWhiteSpace(filterEntry.MatchText))
                {
                    bool isInclude = filterEntry.FilterType == "Include";
                    string matchText = filterEntry.MatchText;
                    
                    // Register callback with text matching
                    filter.Where(
                        record => MatchesText(record, matchText) == isInclude,
                        OnEventReceived);
                }
                else
                {
                    // No text matching - just filter by event ID
                    // For Include: add callback, For Exclude: don't add (events will be dropped)
                    if (filterEntry.FilterType == "Include")
                    {
                        filter.OnEvent(OnEventReceived);
                    }
                }
                
                wrapper.AddFilter(filter);
            }
        }

        private static bool MatchesText(IEventRecord record, string matchText)
        {
            try
            {
                // Check if the match text is found in the event name or task name
                string? eventName = record.Name;
                string? taskName = record.TaskName;

                if (!string.IsNullOrEmpty(eventName) && eventName.Contains(matchText, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (!string.IsNullOrEmpty(taskName) && taskName.Contains(matchText, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private EtwProviderWrapper CreateProviderWrapper(FilterEntry entry, bool registerOnAllEvents, HashSet<ushort> excludeEventIds)
        {
            // Look up the provider to get its GUID if available
            var providerInfo = ProviderManager.FindByName(entry.Provider);
            
            EtwProviderWrapper wrapper;
            if (providerInfo?.Guid != null)
            {
                // Use the GUID from the provider info
                wrapper = new EtwProviderWrapper(providerInfo.Guid.Value);
            }
            else if (Guid.TryParse(entry.Provider, out var guid))
            {
                // The provider name itself is a GUID
                wrapper = new EtwProviderWrapper(guid);
            }
            else
            {
                // Use the provider name
                wrapper = new EtwProviderWrapper(entry.Provider);
            }

            // Set keywords
            if (!string.IsNullOrWhiteSpace(entry.Keywords) &&
                ulong.TryParse(entry.Keywords.Replace("0x", ""), System.Globalization.NumberStyles.HexNumber, null, out var keywords))
            {
                wrapper.SetKeywords(keywords);
            }
            else
            {
                wrapper.EnableAllEvents();
            }

            // Set trace level
            if (!string.IsNullOrWhiteSpace(entry.TraceLevel) &&
                Enum.TryParse<ETWSpyLib.TraceLevel>(entry.TraceLevel, ignoreCase: true, out var level))
            {
                wrapper.SetTraceLevel(level);
            }

            // Set trace flags
            if (!string.IsNullOrWhiteSpace(entry.TraceFlags) &&
                byte.TryParse(entry.TraceFlags, out var flags))
            {
                wrapper.SetTraceFlagsRaw(flags);
            }

            // Only register OnAllEvents if there are filters that want all events
            // (filters with no specific event IDs specified)
            if (registerOnAllEvents)
            {
                if (excludeEventIds.Count > 0)
                {
                    // Register callback that excludes specific event IDs
                    var capturedExcludeIds = excludeEventIds;
                    wrapper.OnAllEvents((IEventRecordDelegate)((IEventRecord record) =>
                    {
                        // Skip excluded event IDs
                        if (!capturedExcludeIds.Contains(record.Id))
                        {
                            OnEventReceived(record);
                        }
                    }));
                }
                else
                {
                    // Register callback to receive ALL events from this provider
                    wrapper.OnAllEvents((IEventRecordDelegate)OnEventReceived);
                }
            }

            return wrapper;
        }

        private void OnEventReceived(IEventRecord record)
        {
            // Don't enqueue events while paused
            if (_isPaused)
            {
                return;
            }

            // Drop events if queue is too large to prevent unbounded memory growth
            // This can happen when events arrive faster than the UI can process them
            if (_pendingEvents.Count >= MaxPendingQueueSize)
            {
                return;
            }

            var payloadWithTypes = EtwPropertyFormatter.GetFormattedPropertiesWithTypes(record);
            var payload = payloadWithTypes.ToDictionary(p => p.Name, p => p.Value);
            var eventRecord = new EventRecord
            {
                Timestamp = record.Timestamp,
                ProviderName = record.ProviderName ?? "Unknown",
                EventId = record.Id,
                ProcessId = record.ProcessId,
                ThreadId = record.ThreadId,
                EventName = record.Name ?? "Unknown",
                TaskName = record.TaskName ?? "Unknown",
                Payload = payload,
                PayloadWithTypes = payloadWithTypes,
                PayloadDisplay = FormatPayloadForCopy(payload)
            };

            // Queue for batch processing instead of immediate UI update
            _pendingEvents.Enqueue(eventRecord);
        }

        private void OnTraceSessionError(object? sender, TraceSessionErrorEventArgs e)
        {
            // Marshal to UI thread to show the message box and update UI
            Dispatcher.BeginInvoke(() =>
            {
                string title = e.IsNoSessionsRemaining
                    ? "No Trace Sessions Available"
                    : "Tracing Error";

                MessageBox.Show(e.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);

                // Stop tracing and reset UI
                StopTracing();

                // Reset the Pause/Resume button text
                PauseResumeButton.Content = "Resume";
                StatusTextBlock.Text = "Stopped";
            });
        }

        private static string FormatEventMessage(IEventRecord record)
        {
            try
            {
                // IEventRecord doesn't expose Task/Opcode directly, so we provide basic event info
                return $"Event ID: {record.Id}, PID: {record.ProcessId}, TID: {record.ThreadId}";
            }
            catch
            {
                return string.Empty;
            }
        }

        private void StopTracing()
        {
            _batchTimer.Stop();
            _isPaused = false;
            _traceCancellation?.Cancel();

            // Unsubscribe from error events before stopping
            if (_traceSession != null)
            {
                _traceSession.ErrorOccurred -= OnTraceSessionError;
            }

            _traceSession?.Stop();
            _traceSession?.Dispose();
            _traceSession = null;
            _traceCancellation?.Dispose();
            _traceCancellation = null;

            // Dispose each provider wrapper to unsubscribe callbacks
            foreach (var provider in _activeProviders)
            {
                provider.Dispose();
            }
            _activeProviders.Clear();

            // Flush any remaining events
            FlushPendingEvents();
        }

        private void ClearEvents(object sender, RoutedEventArgs e)
        {
            // Clear pending queue as well
            while (_pendingEvents.TryDequeue(out _)) { }

            // To properly release DataGrid internal caching (ItemContainerGenerator, 
            // row containers, dictionaries, etc.), we must:
            // 1. Unbind the ItemsSource to disconnect the DataGrid from the collection
            // 2. Clear/replace the collection
            // 3. Rebind to allow fresh virtualization state

            // Unbind ItemsSource - this forces the DataGrid to release its internal
            // ItemContainerGenerator cache, row containers, and hashtable entries
            EventsDataGrid.ItemsSource = null;

            // Replace the list with a new instance to ensure all references are released
            _eventRecordsList = new List<EventRecord>(MaxEventsToShow);

            // Rebind the ItemsSource to the new empty collection
            EventsDataGrid.ItemsSource = _eventRecordsList;

            // Update the event count display
            EventCountText.Text = "Events: 0";

            // Suggest garbage collection to reclaim memory from cleared items
            // Using Gen 2 collection to ensure large object heap is also collected
            GC.Collect(2, GCCollectionMode.Optimized, blocking: false);
        }

        private void AddFilter(object sender, RoutedEventArgs e)
        {
            // Validate provider is specified
            if (string.IsNullOrWhiteSpace(ProviderComboBox.Text))
            {
                MessageBox.Show("Please specify a provider name or GUID.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Validate and parse event IDs
            string eventIdInput = EventTextBox.Text.Trim();
            if (!TryParseEventIds(eventIdInput, out _, out string? errorMessage))
            {
                MessageBox.Show(errorMessage, "Invalid Event ID", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var filter = new FilterEntry
            {
                Provider = ProviderComboBox.Text,
                EventId = eventIdInput,
                MatchText = TextToMatchBox.Text,
                FilterType = FilterTypeComboBox.Text,
                Keywords = KeywordsTextBox.Text,
                TraceLevel = TraceLevelComboBox.Text,
                TraceFlags = TraceFlagsTextBox.Text
            };

            // Check if an exact duplicate filter already exists
            bool isDuplicate = FilterEntries.Any(existing =>
                string.Equals(existing.Provider, filter.Provider, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing.EventId, filter.EventId, StringComparison.Ordinal) &&
                string.Equals(existing.MatchText, filter.MatchText, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing.FilterType, filter.FilterType, StringComparison.Ordinal) &&
                string.Equals(existing.Keywords, filter.Keywords, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing.TraceLevel, filter.TraceLevel, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing.TraceFlags, filter.TraceFlags, StringComparison.Ordinal));

            if (isDuplicate)
            {
                MessageBox.Show("This filter has already been added.", "Duplicate Filter", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            FilterEntries.Add(filter);

            // Save the provider to registry if it's a new one (not in defaults or registry)
            if (ProviderManager.AddProvider(filter.Provider))
            {
                // Refresh the combo box to include the newly added provider
                RefreshProviderComboBox();
            }
        }

        private void RemoveFilter(object sender, RoutedEventArgs e)
        {
            if (FiltersListView.SelectedItem is FilterEntry selectedFilter)
            {
                FilterEntries.Remove(selectedFilter);
            }
        }

        private void ClearFilters(object sender, RoutedEventArgs e)
        {
            FilterEntries.Clear();
        }

        private void SaveToFile(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "ETW Configuration (*.etwconfig)|*.etwconfig|JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                DefaultExt = ".etwconfig",
                Title = "Save Configuration"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                var config = new ConfigurationData
                {
                    Filters = [.. FilterEntries]
                };

                string json = JsonSerializer.Serialize(config, JsonOptions);
                File.WriteAllText(dialog.FileName, json);

                MessageBox.Show("Configuration saved successfully.", "Save Configuration", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save configuration: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadFromFile(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "ETW Configuration (*.etwconfig)|*.etwconfig|JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                DefaultExt = ".etwconfig",
                Title = "Load Configuration"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                string json = File.ReadAllText(dialog.FileName);
                var config = JsonSerializer.Deserialize<ConfigurationData>(json);

                if (config is null)
                {
                    MessageBox.Show("Invalid configuration file.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Temporarily unsubscribe from CollectionChanged to avoid restarting the trace session
                // for each filter added during loading
                FilterEntries.CollectionChanged -= OnFilterEntriesChanged;

                try
                {
                    // Clear existing data and load from file
                    FilterEntries.Clear();
                    foreach (var filter in config.Filters)
                    {
                        FilterEntries.Add(filter);
                    }
                }
                finally
                {
                    // Re-subscribe to CollectionChanged
                    FilterEntries.CollectionChanged += OnFilterEntriesChanged;
                }

                // Now restart the trace session once with all filters loaded
                if (FilterEntries.Count > 0)
                {
                    RestartTraceSession();
                }
                else
                {
                    StopTracing();
                    PauseResumeButton.Content = "Pause";
                    StatusTextBlock.Text = "Stopped";
                }

                MessageBox.Show("Configuration loaded successfully.", "Load Configuration", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (JsonException)
            {
                MessageBox.Show("The file contains invalid JSON data.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load configuration: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var cb = (ComboBox)sender;
            // Keep Text synchronized with the selected item (ProviderInfo objects in ItemsSource)
            if (cb.SelectedItem is ProviderInfo provider)
            {
                cb.Text = provider.Name;
            }
        }

        private void EventsDataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Handle Ctrl+C for copy
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            {
                CopySelectedEvents(sender, e);
                e.Handled = true;
            }
            // Handle Enter to show event details
            else if (e.Key == Key.Enter)
            {
                ShowEventDetails();
                e.Handled = true;
            }
        }

        private void EventsDataGrid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            // Exclude the Payload dictionary property from display
            // PayloadDisplay provides the formatted string representation
            if (e.PropertyName == nameof(EventRecord.Payload))
            {
                e.Cancel = true;
                return;
            }

            // Exclude the PayloadWithTypes property from display
            // This is used internally for EventDetailsWindow
            if (e.PropertyName == nameof(EventRecord.PayloadWithTypes))
            {
                e.Cancel = true;
                return;
            }

            // Customize column headers with user-friendly names
            e.Column.Header = e.PropertyName switch
            {
                nameof(EventRecord.Timestamp) => "Timestamp",
                nameof(EventRecord.ProviderName) => "Provider Name",
                nameof(EventRecord.EventName) => "Event Name",
                nameof(EventRecord.TaskName) => "Task Name",
                nameof(EventRecord.EventId) => "Event Id",
                nameof(EventRecord.ProcessId) => "Process Id",
                nameof(EventRecord.ThreadId) => "Thread Id",
                nameof(EventRecord.PayloadDisplay) => "Payload",
                _ => e.Column.Header
            };

            // Replace the Timestamp column with a custom template that uses FormatTimestamp
            if (e.PropertyName == nameof(EventRecord.Timestamp))
            {
                e.Cancel = true;
                
                // Check if we already added the custom Timestamp column to avoid duplicates
                bool timestampColumnExists = EventsDataGrid.Columns.Any(c => c.Header?.ToString() == "Timestamp");
                if (timestampColumnExists)
                {
                    return;
                }
                
                var binding = new Binding(nameof(EventRecord.Timestamp))
                {
                    Converter = new TimestampConverter(),
                    ConverterParameter = this
                };
                
                var textColumn = new DataGridTextColumn
                {
                    Header = "Timestamp",
                    Binding = binding,
                    Width = new DataGridLength(180)
                };
                
                EventsDataGrid.Columns.Insert(0, textColumn);
                return;
            }

            // Set a fixed width for Payload to prevent auto-resizing
            // which causes annoying horizontal scrolling during live updates
            if (e.PropertyName == nameof(EventRecord.PayloadDisplay))
            {
                e.Column.Width = new DataGridLength(500);
            }
        }

        private void CopySelectedEvents(object sender, RoutedEventArgs e)
        {
            var selectedItems = EventsDataGrid.SelectedItems.Cast<EventRecord>().ToList();
            if (selectedItems.Count == 0)
            {
                return;
            }

            var sb = new StringBuilder();

            // Add header row
            sb.AppendLine("Timestamp\tProviderName\tEventName\tTaskName\tEventId\tProcessId\tThreadId\tPayload");

            // Add data rows
            foreach (var record in selectedItems)
            {
                string payloadStr = FormatPayloadForCopy(record.Payload);
                string timestampStr = FormatTimestamp(record.Timestamp);
                sb.AppendLine($"{timestampStr}\t{record.ProviderName}\t{record.EventName}\t{record.TaskName}\t{record.EventId}\t{record.ProcessId}\t{record.ThreadId}\t{payloadStr}");
            }

            try
            {
                Clipboard.SetText(sb.ToString());
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to copy to clipboard: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportToCsv(object sender, RoutedEventArgs e)
        {
            if (_eventRecordsList.Count == 0)
            {
                MessageBox.Show("No events to export.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                DefaultExt = ".csv",
                Title = "Export Events to CSV",
                FileName = $"ETWEvents_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                // Collect all unique payload property names across all events
                var allPayloadKeys = _eventRecordsList
                    .SelectMany(r => r.Payload.Keys)
                    .Distinct()
                    .OrderBy(k => k)
                    .ToList();

                using var writer = new StreamWriter(dialog.FileName, false, Encoding.UTF8);

                // Write header - base columns plus dynamic payload columns
                var headerParts = new List<string>
                {
                    "Timestamp",
                    "ProviderName",
                    "EventName",
                    "TaskName",
                    "EventId",
                    "ProcessId",
                    "ThreadId"
                };
                headerParts.AddRange(allPayloadKeys);
                writer.WriteLine(string.Join(",", headerParts.Select(EscapeCsvField)));

                // Write data rows
                foreach (var record in _eventRecordsList)
                {
                    var rowParts = new List<string>
                    {
                        FormatTimestamp(record.Timestamp),
                        record.ProviderName,
                        record.EventName,
                        record.TaskName,
                        record.EventId.ToString(),
                        record.ProcessId.ToString(),
                        record.ThreadId.ToString()
                    };

                    // Add payload values in the same order as headers
                    foreach (var key in allPayloadKeys)
                    {
                        rowParts.Add(record.Payload.TryGetValue(key, out var value) ? value : string.Empty);
                    }

                    writer.WriteLine(string.Join(",", rowParts.Select(EscapeCsvField)));
                }

                MessageBox.Show($"Successfully exported {_eventRecordsList.Count:N0} events to:\n{dialog.FileName}", 
                    "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to export events: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string EscapeCsvField(string field)
        {
            if (string.IsNullOrEmpty(field))
            {
                return string.Empty;
            }

            // If field contains comma, quote, or newline, wrap in quotes and escape existing quotes
            if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
            {
                return $"\"{field.Replace("\"", "\"\"")}\"";
            }

            return field;
        }

        private static string FormatPayloadForCopy(Dictionary<string, string> payload)
        {
            if (payload.Count == 0)
            {
                return string.Empty;
            }

            return string.Join("; ", payload.Select(kvp => $"{kvp.Key}={kvp.Value}"));
        }

        private void HandleAdminPrivileges()
        {
            if (!IsRunningAsAdministrator())
            {
                MessageBox.Show(
                    "ETWSpy requires administrator privileges to capture ETW events.\n\nPlease restart the application as Administrator.",
                    "Administrator Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private static bool IsRunningAsAdministrator()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        /// <summary>
        /// Parses an event ID input string into a list of individual event IDs.
        /// Supports: single ID ("5"), comma-separated ("1,2,3"), ranges ("1-5"), or combinations ("1,3-5,10").
        /// Empty or whitespace input returns an empty list (meaning "all events").
        /// </summary>
        /// <param name="input">The event ID input string</param>
        /// <param name="eventIds">The parsed list of event IDs</param>
        /// <param name="errorMessage">Error message if parsing fails</param>
        /// <returns>True if parsing succeeded, false otherwise</returns>
        private static bool TryParseEventIds(string input, out List<ushort> eventIds, out string? errorMessage)
        {
            eventIds = [];
            errorMessage = null;

            // Empty input means "all events" - valid with empty list
            if (string.IsNullOrWhiteSpace(input))
            {
                return true;
            }

            // Split by comma and process each part
            var parts = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var part in parts)
            {
                // Check if this part is a range (contains '-')
                if (part.Contains('-'))
                {
                    var rangeParts = part.Split('-', StringSplitOptions.TrimEntries);
                    
                    if (rangeParts.Length != 2)
                    {
                        errorMessage = $"Invalid range format: '{part}'. Use format 'start-end' (e.g., '1-5').";
                        return false;
                    }

                    if (!ushort.TryParse(rangeParts[0], out var rangeStart))
                    {
                        errorMessage = $"Invalid range start value: '{rangeParts[0]}'. Event IDs must be integers from 0 to 65535.";
                        return false;
                    }

                    if (!ushort.TryParse(rangeParts[1], out var rangeEnd))
                    {
                        errorMessage = $"Invalid range end value: '{rangeParts[1]}'. Event IDs must be integers from 0 to 65535.";
                        return false;
                    }

                    if (rangeStart > rangeEnd)
                    {
                        errorMessage = $"Invalid range: '{part}'. Start value ({rangeStart}) must be less than or equal to end value ({rangeEnd}).";
                        return false;
                    }

                    // Add all IDs in the range (inclusive)
                    for (ushort id = rangeStart; id <= rangeEnd; id++)
                    {
                        if (!eventIds.Contains(id))
                        {
                            eventIds.Add(id);
                        }
                    }
                }
                else
                {
                    // Single ID
                    if (!ushort.TryParse(part, out var eventId))
                    {
                        errorMessage = $"Invalid event ID: '{part}'. Event IDs must be integers from 0 to 65535.";
                        return false;
                    }

                    if (!eventIds.Contains(eventId))
                    {
                        eventIds.Add(eventId);
                    }
                }
            }

            return true;
        }

        private void EventsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            ShowEventDetails();
        }

        private void ShowEventDetails()
        {
            if (EventsDataGrid.SelectedItem is EventRecord selectedRecord)
            {
                var detailsWindow = new EventDetailsWindow(IsDarkMode);
                detailsWindow.SetEventRecord(selectedRecord, ShowTimestampsInUTC);
                detailsWindow.Owner = this;
                WindowHelper.ShowWithoutFlash(detailsWindow, centerOnScreen: false);
            }
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
    }

    /// <summary>
    /// Converts DateTime to formatted string based on UTC setting from MainWindow.
    /// </summary>
    public class TimestampConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is DateTime timestamp && parameter is MainWindow mainWindow)
            {
                return mainWindow.FormatTimestamp(timestamp);
            }
            return value?.ToString() ?? string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}