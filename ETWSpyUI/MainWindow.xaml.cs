using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
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
        public string? ProviderGuid { get; set; }
        /// <summary>
        /// The type of filter: "Event Id" or "Match Text". Defaults to "Event Id".
        /// </summary>
        public string FilterCategory { get; set; } = "Event Id";
        /// <summary>
        /// The filter value - either event IDs or match text depending on FilterCategory.
        /// Empty value means "all" for the given category.
        /// </summary>
        public string Value { get; set; } = string.Empty;
        /// <summary>
        /// The filter logic: "Include" or "Exclude". Defaults to "Include".
        /// </summary>
        public string FilterLogic { get; set; } = "Include";
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
        public List<ProviderConfigEntry> Providers { get; set; } = new();
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
        private bool _useSystemTheme;
        private bool _showTimestampsInUTC;
        private int _maxEventsToShow = DefaultMaxEventsToDisplay;
        private bool _autoscroll = true;
        private bool _restoreSessionOnLaunch = true;
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

        // Tracks whether the user wants event capture to be active (user explicitly started/stopped)
        // This is separate from _isPaused - when true, we should be capturing when filters exist
        private bool _wantsCaptureActive;

        // Use a simple List as backing store to avoid ObservableCollection overhead
        private List<EventRecord> _eventRecordsList = new(DefaultMaxEventsToDisplay);

        // Keep provider wrappers alive to prevent GC from collecting them and their callbacks
        private readonly List<EtwProviderWrapper> _activeProviders = [];

        // String interner for deduplicating repeated strings in EventRecord objects
        // Provider names, event names, task names, and property names are highly repetitive
        private readonly StringInterner _stringInterner = new();

        // Track open windows to avoid duplicates
        private FiltersWindow? _filtersWindow;
        private ProviderConfigWindow? _providerConfigWindow;

        /// <summary>
        /// Collection of filter entries added by the user.
        /// </summary>
        public ObservableCollection<FilterEntry> FilterEntries { get; } = [];

        /// <summary>
        /// Collection of provider configuration entries added by the user.
        /// </summary>
        public ObservableCollection<ProviderConfigEntry> ProviderConfigEntries { get; } = [];

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool IsDarkMode
        {
            get => _isDarkMode;
            set
            {
                if (_isDarkMode == value) return;
                _isDarkMode = value;
                OnPropertyChanged(nameof(IsDarkMode));
                ApplyTheme();
                RegistrySettings.SaveBool(RegistrySettings.DarkMode, _isDarkMode);
            }
        }

        public bool UseSystemTheme
        {
            get => _useSystemTheme;
            set
            {
                if (_useSystemTheme == value) return;
                _useSystemTheme = value;
                OnPropertyChanged(nameof(UseSystemTheme));
                ApplyTheme();
                RegistrySettings.SaveBool(RegistrySettings.UseSystemTheme, _useSystemTheme);
                UpdateThemeMenuCheckmarks();
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

        public bool Autoscroll
        {
            get => _autoscroll;
            set
            {
                if (_autoscroll == value) return;
                _autoscroll = value;
                OnPropertyChanged(nameof(Autoscroll));
                RegistrySettings.SaveBool(RegistrySettings.Autoscroll, _autoscroll);
                UpdateAutoscrollMenuCheckmark();
            }
        }

        public bool RestoreSessionOnLaunch
        {
            get => _restoreSessionOnLaunch;
            set
            {
                if (_restoreSessionOnLaunch == value) return;
                _restoreSessionOnLaunch = value;
                OnPropertyChanged(nameof(RestoreSessionOnLaunch));
                RegistrySettings.SaveBool(RegistrySettings.RestoreSessionOnLaunch, _restoreSessionOnLaunch);
                UpdateRestoreSessionMenuCheckmark();
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

        /// <summary>
        /// Gets the effective dark mode state, considering UseSystemTheme setting.
        /// </summary>
        public bool EffectiveDarkMode => _useSystemTheme ? IsSystemInDarkMode() : _isDarkMode;

        public MainWindow()
        {
            HandleAdminPrivileges();
            InitializeComponent();

            // Apply title bar theme immediately after window handle is available
            SourceInitialized += (_, _) =>
            {
                bool useDarkMode = _useSystemTheme ? IsSystemInDarkMode() : _isDarkMode;
                WindowHelper.ApplyTitleBarTheme(this, useDarkMode);
            };

            // Bind settings to this window
            DataContext = this;

            // Initialize batch timer for UI updates
            _batchTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(_currentBatchIntervalMs)
            };
            _batchTimer.Tick += BatchTimer_Tick;

            // Subscribe to filter changes to auto-start/restart trace session
            FilterEntries.CollectionChanged += OnFilterEntriesChanged;

            // Subscribe to provider config changes to enable/disable Filters menu
            ProviderConfigEntries.CollectionChanged += OnProviderConfigEntriesChanged;

            // EventsDataGrid uses a plain List<T> to avoid ObservableCollection overhead
            EventsDataGrid.ItemsSource = _eventRecordsList;

            // Load settings from registry and apply theme after window is loaded
            Loaded += (_, _) =>
            {
                LoadSettingsFromRegistry();
            };

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
            EventCountText.Text = $"Events: {_eventRecordsList.Count:N0} (max {MaxEventsToShow:N0})";

            // Enable Clear and Export buttons when there are events
            UpdateEventButtonsEnabled();

            // Autoscroll to the last item if enabled
            if (_autoscroll && _eventRecordsList.Count > 0)
            {
                EventsDataGrid.ScrollIntoView(_eventRecordsList[^1]);
            }
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            _batchTimer.Stop();
            FilterEntries.CollectionChanged -= OnFilterEntriesChanged;
            ProviderConfigEntries.CollectionChanged -= OnProviderConfigEntriesChanged;
            StopTracing();

            // Save session state if enabled
            if (_restoreSessionOnLaunch)
            {
                SaveLastSession();
            }
        }

        /// <summary>
        /// Handles changes to the ProviderConfigEntries collection.
        /// Enables or disables the Filters menu based on whether providers exist.
        /// </summary>
        private void OnProviderConfigEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateFiltersEnabled();
        }

        /// <summary>
        /// Updates the enabled state of the Filters menu and button based on whether providers exist.
        /// Also enables/disables the Start/Pause button and Save button/menu.
        /// </summary>
        private void UpdateFiltersEnabled()
        {
            bool hasProviders = ProviderConfigEntries.Count > 0;
            FiltersMenuItem.IsEnabled = hasProviders;
            FiltersToolbarButton.IsEnabled = hasProviders;
            StartPauseToolbarButton.IsEnabled = hasProviders;
            StartPauseMenuItem.IsEnabled = hasProviders;
            SaveMenuItem.IsEnabled = hasProviders;
            SaveToolbarButton.IsEnabled = hasProviders;
            
            // Update placeholder visibility when provider state changes
            UpdateEmptyPlaceholderVisibility();
        }

        /// <summary>
        /// Updates the enabled state of the Clear and Export buttons based on whether events exist.
        /// Also updates the empty placeholder visibility.
        /// </summary>
        private void UpdateEventButtonsEnabled()
        {
            bool hasEvents = _eventRecordsList.Count > 0;
            ClearEventsToolbarButton.IsEnabled = hasEvents;
            ClearMenuItem.IsEnabled = hasEvents;
            ContextMenuClearMenuItem.IsEnabled = hasEvents;
            ExportToolbarButton.IsEnabled = hasEvents;
            ExportMenuItem.IsEnabled = hasEvents;
            
            // Show/hide empty placeholder text based on whether there are events or providers
            UpdateEmptyPlaceholderVisibility();
        }

        /// <summary>
        /// Updates the visibility of the empty placeholder text in the main event grid.
        /// </summary>
        private void UpdateEmptyPlaceholderVisibility()
        {
            bool hasEvents = _eventRecordsList.Count > 0;
            bool hasProviders = ProviderConfigEntries.Count > 0;
            
            // Hide placeholder when there are events, or show appropriate message
            if (hasEvents)
            {
                EmptyPlaceholderText.Visibility = Visibility.Collapsed;
            }
            else
            {
                EmptyPlaceholderText.Visibility = Visibility.Visible;
                EmptyPlaceholderText.Text = hasProviders 
                    ? "No events captured yet.\n\nEvents will appear here as they are received from the configured providers."
                    : "No events captured.\n\nClick the Providers button in the toolbar to add an ETW provider and start capturing events.";
            }
        }

        /// <summary>
        /// Handles changes to the FilterEntries collection.
        /// Reconfigures the trace session based on filter changes, respecting the current capture state.
        /// </summary>
        private void OnFilterEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (FilterEntries.Count > 0)
            {
                // Only restart if user wants capture active, otherwise just update the session config
                if (_wantsCaptureActive)
                {
                    RestartTraceSession();
                }
                else
                {
                    // Stop any existing session but don't start a new one
                    // The session will be started when user clicks Start
                    StopTracing();
                    // Keep UI showing paused state
                    StartPauseToolbarIcon.Text = "\uE768"; // Play icon
                    StartPauseToolbarButton.ToolTip = "Start event capture";
                    StartPauseMenuItem.Header = "_Start event capture";
                    StartPauseMenuIcon.Text = "\uE768"; // Play icon
                }
            }
            else
            {
                // No filters remaining - stop the trace session
                _wantsCaptureActive = false;
                StopTracing();
                StartPauseToolbarIcon.Text = "\uE768"; // Play icon
                StartPauseToolbarButton.ToolTip = "Start event capture";
                StartPauseMenuItem.Header = "_Start event capture";
                StartPauseMenuIcon.Text = "\uE768"; // Play icon
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

                    // Determine if we need OnAllEvents:
                    // - When there are no Event Id filters with specific values (only Match Text filters or "all events" filters)
                    // - Or when there's an Event Id filter with empty value (all events)
                    bool hasEventIdFilters = filterList.Any(f => f.FilterCategory == "Event Id" && !string.IsNullOrWhiteSpace(f.Value));
                    bool hasMatchTextFilters = filterList.Any(f => f.FilterCategory == "Match Text");
                    bool hasAllEventsFilter = filterList.Any(f => f.FilterCategory == "Event Id" && string.IsNullOrWhiteSpace(f.Value));
                    bool needsOnAllEvents = !hasEventIdFilters || hasMatchTextFilters || hasAllEventsFilter;

                    // Collect exclude event IDs to filter out in OnAllEvents callback
                    var excludeEventIds = new HashSet<ushort>();
                    foreach (var f in filterList.Where(f => f.FilterLogic == "Exclude" && f.FilterCategory == "Event Id" && !string.IsNullOrWhiteSpace(f.Value)))
                    {
                        if (TryParseEventIds(f.Value, out var ids, out _))
                        {
                            foreach (var id in ids)
                            {
                                excludeEventIds.Add(id);
                            }
                        }
                    }

                    // Collect match text filters for OnAllEvents callback
                    var includeMatchTexts = filterList
                        .Where(f => f.FilterLogic == "Include" && f.FilterCategory == "Match Text" && !string.IsNullOrWhiteSpace(f.Value))
                        .Select(f => f.Value)
                        .ToList();
                    var excludeMatchTexts = filterList
                        .Where(f => f.FilterLogic == "Exclude" && f.FilterCategory == "Match Text" && !string.IsNullOrWhiteSpace(f.Value))
                        .Select(f => f.Value)
                        .ToList();

                    var wrapper = CreateProviderWrapper(firstFilter, needsOnAllEvents, excludeEventIds, includeMatchTexts, excludeMatchTexts);

                    // Apply all include filters with specific event IDs
                    foreach (var filterEntry in filterList.Where(f => f.FilterLogic == "Include" && f.FilterCategory == "Event Id" && !string.IsNullOrWhiteSpace(f.Value)))
                    {
                        ApplyEventIdFilterToProvider(wrapper, filterEntry);
                    }

                    _activeProviders.Add(wrapper); // Keep reference alive
                    try
                    {
                        _traceSession.EnableProvider(wrapper);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to add provider: {wrapper.Name} {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }

                // Subscribe to error events from the trace session
                _traceSession.ErrorOccurred += OnTraceSessionError;

                // Start the batch timer
                _batchTimer.Start();

                // Start the trace asynchronously
                _ = _traceSession.StartAsync(_traceCancellation.Token);

                // Update UI to reflect running state
                _isPaused = false;
                _wantsCaptureActive = true;
                StartPauseToolbarIcon.Text = "\uE769"; // Pause icon
                StartPauseToolbarButton.ToolTip = "Pause event capture";
                StartPauseMenuItem.Header = "_Pause event capture";
                StartPauseMenuIcon.Text = "\uE769"; // Pause icon
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start tracing: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StopTracing();
                StartPauseToolbarIcon.Text = "\uE768"; // Play icon
                StartPauseToolbarButton.ToolTip = "Start event capture";
                StartPauseMenuItem.Header = "_Start event capture";
                StartPauseMenuIcon.Text = "\uE768"; // Play icon
            }
        }

        private void OnPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        /// <summary>
        /// Loads all settings from the Windows registry.
        /// </summary>
        private void LoadSettingsFromRegistry()
        {
            _useSystemTheme = RegistrySettings.LoadBool(RegistrySettings.UseSystemTheme);
            _isDarkMode = RegistrySettings.LoadBool(RegistrySettings.DarkMode);
            _showTimestampsInUTC = RegistrySettings.LoadBool(RegistrySettings.ShowTimestampsInUTC);
            _maxEventsToShow = RegistrySettings.LoadInt(RegistrySettings.MaxEventsToShow, DefaultMaxEventsToDisplay);
            _autoscroll = RegistrySettings.LoadBool(RegistrySettings.Autoscroll, true);
            _restoreSessionOnLaunch = RegistrySettings.LoadBool(RegistrySettings.RestoreSessionOnLaunch, true);
            
            // Apply the theme based on settings
            ApplyTheme();
            UpdateThemeMenuCheckmarks();
            UpdateTimeFormatMenuCheckmarks();
            UpdateMaxEventsMenuCheckmarks();
            UpdateAutoscrollMenuCheckmark();
            UpdateRestoreSessionMenuCheckmark();
            UpdateFileAssociationMenuCheckmark();

            // Check if a file was passed via command line - this takes priority over session restore
            if (!string.IsNullOrEmpty(App.StartupFilePath) && File.Exists(App.StartupFilePath))
            {
                LoadConfigurationFile(App.StartupFilePath);
            }
            // Restore last session if enabled and no file was passed
            else if (_restoreSessionOnLaunch)
            {
                TryRestoreLastSession();
            }
        }

        /// <summary>
        /// Applies the current theme based on UseSystemTheme and IsDarkMode settings.
        /// </summary>
        private void ApplyTheme()
        {
            bool useDarkMode = _useSystemTheme ? IsSystemInDarkMode() : _isDarkMode;
            SwitchTheme(useDarkMode ? "Themes/DarkColors.xaml" : "Themes/LightColors.xaml");
            WindowHelper.ApplyTitleBarTheme(this, useDarkMode);
            
            // Update title bar theme for any open child windows
            if (_filtersWindow != null && _filtersWindow.IsLoaded)
            {
                WindowHelper.ApplyTitleBarTheme(_filtersWindow, useDarkMode);
            }
            if (_providerConfigWindow != null && _providerConfigWindow.IsLoaded)
            {
                WindowHelper.ApplyTitleBarTheme(_providerConfigWindow, useDarkMode);
            }
        }

        /// <summary>
        /// Detects if the Windows system is using dark mode.
        /// </summary>
        private static bool IsSystemInDarkMode()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (key?.GetValue("AppsUseLightTheme") is int value)
                {
                    return value == 0; // 0 = dark mode, 1 = light mode
                }
            }
            catch
            {
                // Ignore registry access errors
            }
            return false; // Default to light mode
        }

        /// <summary>
        /// Updates the checkmarks on the theme menu items.
        /// </summary>
        private void UpdateThemeMenuCheckmarks()
        {
            LightThemeMenuItem.IsChecked = !_useSystemTheme && !_isDarkMode;
            DarkThemeMenuItem.IsChecked = !_useSystemTheme && _isDarkMode;
            SystemThemeMenuItem.IsChecked = _useSystemTheme;
        }

        private void SwitchTheme(string colorsPath)
        {
            var colorsUri = new Uri(colorsPath, UriKind.Relative);
            var colorsDict = new ResourceDictionary { Source = colorsUri };
            
            var stylesUri = new Uri("Themes/BaseStyles.xaml", UriKind.Relative);
            var stylesDict = new ResourceDictionary { Source = stylesUri };
            
            Application.Current.Resources.MergedDictionaries.Clear();
            Application.Current.Resources.MergedDictionaries.Add(colorsDict);
            Application.Current.Resources.MergedDictionaries.Add(stylesDict);
        }

        private void PauseResumeTracing(object sender, RoutedEventArgs e)
        {
            // If no filters configured, can't start
            if (FilterEntries.Count == 0)
            {
                MessageBox.Show("No providers configured. Add a provider to start capturing events.", "No Providers", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // If no trace session exists, start one
            if (_traceSession == null)
            {
                _wantsCaptureActive = true;
                RestartTraceSession();
                return;
            }

            // Toggle pause state
            _isPaused = !_isPaused;
            _wantsCaptureActive = !_isPaused;

            if (_isPaused)
            {
                // Paused - stop the batch timer but keep the trace session running
                _batchTimer.Stop();
                StartPauseToolbarIcon.Text = "\uE768"; // Play icon
                StartPauseToolbarButton.ToolTip = "Start event capture";
                StartPauseMenuItem.Header = "_Start event capture";
                StartPauseMenuIcon.Text = "\uE768"; // Play icon
            }
            else
            {
                // Resumed - restart the batch timer
                _batchTimer.Start();
                StartPauseToolbarIcon.Text = "\uE769"; // Pause icon
                StartPauseToolbarButton.ToolTip = "Pause event capture";
                StartPauseMenuItem.Header = "_Pause event capture";
                StartPauseMenuIcon.Text = "\uE769"; // Pause icon
            }
        }

        private void ApplyEventIdFilterToProvider(EtwProviderWrapper wrapper, FilterEntry filterEntry)
        {
            // Parse event IDs from the filter entry Value
            if (!TryParseEventIds(filterEntry.Value, out var eventIds, out _))
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
                
                // For Include: add callback, For Exclude: don't add (events will be dropped)
                if (filterEntry.FilterLogic == "Include")
                {
                    filter.OnEvent(OnEventReceived);
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

        private EtwProviderWrapper CreateProviderWrapper(FilterEntry entry, bool registerOnAllEvents, HashSet<ushort> excludeEventIds, List<string> includeMatchTexts, List<string> excludeMatchTexts)
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

            // Set trace level (default to Verbose if not specified to capture all events)
            if (!string.IsNullOrWhiteSpace(entry.TraceLevel) &&
                Enum.TryParse<ETWSpyLib.TraceLevel>(entry.TraceLevel, ignoreCase: true, out var level))
            {
                wrapper.SetTraceLevel(level);
            }
            else
            {
                wrapper.SetTraceLevel(ETWSpyLib.TraceLevel.Verbose);
            }

            // Set trace flags
            if (!string.IsNullOrWhiteSpace(entry.TraceFlags) &&
                byte.TryParse(entry.TraceFlags, out var flags))
            {
                wrapper.SetTraceFlagsRaw(flags);
            }

            // Only register OnAllEvents if needed
            if (registerOnAllEvents)
            {
                // Capture variables for closure
                var capturedExcludeIds = excludeEventIds;
                var capturedIncludeTexts = includeMatchTexts;
                var capturedExcludeTexts = excludeMatchTexts;

                wrapper.OnAllEvents((IEventRecordDelegate)((IEventRecord record) =>
                {
                    // Skip excluded event IDs
                    if (capturedExcludeIds.Contains(record.Id))
                    {
                        return;
                    }

                    // Check exclude match texts first - if event matches any exclude text, skip it
                    if (capturedExcludeTexts.Count > 0)
                    {
                        foreach (var excludeText in capturedExcludeTexts)
                        {
                            if (MatchesText(record, excludeText))
                            {
                                return; // Excluded by text match
                            }
                        }
                    }

                    // If there are include match text filters, event must match at least one (OR logic)
                    // If there are NO include match text filters, allow all events through
                    if (capturedIncludeTexts.Count > 0)
                    {
                        bool matchesAny = false;
                        foreach (var includeText in capturedIncludeTexts)
                        {
                            if (MatchesText(record, includeText))
                            {
                                matchesAny = true;
                                break;
                            }
                        }
                        if (!matchesAny)
                        {
                            return; // Doesn't match any include text filter (OR logic)
                        }
                    }

                    OnEventReceived(record);
                }));
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

            // Use string interner to deduplicate repeated strings (provider names, event names, etc.)
            // Property names and type names are also interned inside GetFormattedPropertiesWithTypes
            var payloadWithTypes = EtwPropertyFormatter.GetFormattedPropertiesWithTypes(record, _stringInterner);
            
            // Create payload dictionary with quotes trimmed from values
            var payload = payloadWithTypes.ToDictionary(p => p.Name, p => TrimQuotes(p.Value));
            
            // Also update the PayloadWithTypes values to have trimmed quotes for consistency
            foreach (var prop in payloadWithTypes)
            {
                prop.Value = TrimQuotes(prop.Value);
            }
            
            var eventRecord = new EventRecord
            {
                Timestamp = record.Timestamp,
                // Intern frequently repeated strings to reduce memory allocations
                ProviderName = _stringInterner.Intern(record.ProviderName) ?? "Unknown",
                EventId = record.Id,
                ProcessId = record.ProcessId,
                ThreadId = record.ThreadId,
                EventName = _stringInterner.Intern(record.Name) ?? "Unknown",
                TaskName = _stringInterner.Intern(record.TaskName) ?? "Unknown",
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

                string message = e.IsNoSessionsRemaining
                    ? "No ETW trace sessions are available. Windows has a limited number of trace sessions. Would you like to open Performance Monitor and stop some currently running trace sessions?"
                    : e.Message;

                if (e.IsNoSessionsRemaining)
                {
                    if (MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Error) == MessageBoxResult.Yes)
                    {
                        // Open Performance Monitor to the Trace Sessions view
                        Process.Start(new ProcessStartInfo("perfmon.exe") { UseShellExecute = true });
                    }
                }
                else
                {
                    MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
                }
                // Stop tracing and reset UI
                StopTracing();

                // Reset the toolbar icon
                StartPauseToolbarIcon.Text = "\uE768"; // Play icon
                StartPauseToolbarButton.ToolTip = "Start event capture";
                StartPauseMenuItem.Header = "_Start event capture";
                StartPauseMenuIcon.Text = "\uE768"; // Play icon
            });
        }

        private void StopTracing()
        {
            _batchTimer.Stop();
            _isPaused = false;
            // Note: Don't reset _wantsCaptureActive here - let the caller decide
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

            // Clear interned strings to release memory from old events
            _stringInterner.Clear();

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

            // Disable Clear and Export buttons when there are no events
            UpdateEventButtonsEnabled();

            // Suggest garbage collection to reclaim memory from cleared items
            // Using Gen 2 collection to ensure large object heap is also collected
            GC.Collect(2, GCCollectionMode.Optimized, blocking: false);
        }

        private void SaveToFile(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "ETW Configuration (*.etwspy)|*.etwspy|JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                DefaultExt = ".etwspy",
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
                    Filters = [.. FilterEntries],
                    Providers = [.. ProviderConfigEntries]
                };

                string json = JsonSerializer.Serialize(config, JsonOptions);
                File.WriteAllText(dialog.FileName, json);
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
                Filter = "ETW Configuration (*.etwspy)|*.etwspy|JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                DefaultExt = ".etwspy",
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
                ProviderConfigEntries.CollectionChanged -= OnProviderConfigEntriesChanged;

                try
                {
                    // Clear existing data and load from file
                    FilterEntries.Clear();
                    ProviderConfigEntries.Clear();

                    // Load providers first
                    foreach (var provider in config.Providers)
                    {
                        ProviderConfigEntries.Add(provider);
                    }

                    // Load filters
                    foreach (var filter in config.Filters)
                    {
                        FilterEntries.Add(filter);
                    }
                }
                finally
                {
                    // Re-subscribe to CollectionChanged
                    FilterEntries.CollectionChanged += OnFilterEntriesChanged;
                    ProviderConfigEntries.CollectionChanged += OnProviderConfigEntriesChanged;
                }

                // Update UI state based on loaded data
                UpdateFiltersEnabled();

                // After loading, don't automatically start capturing - let user decide
                // Stop any existing session and reset to paused state
                _wantsCaptureActive = false;
                StopTracing();
                StartPauseToolbarIcon.Text = "\uE768"; // Play icon
                StartPauseToolbarButton.ToolTip = "Start event capture";
                StartPauseMenuItem.Header = "_Start event capture";
                StartPauseMenuIcon.Text = "\uE768"; // Play icon
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

        /// <summary>
        /// Removes leading and trailing quote characters from a string.
        /// Handles both double quotes (") and single quotes (').
        /// </summary>
        private static string TrimQuotes(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value ?? string.Empty;
            }

            // Check for double quotes
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            {
                return value[1..^1];
            }

            // Check for single quotes
            if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
            {
                return value[1..^1];
            }

            return value;
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

        private void EventsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            bool hasSelection = EventsDataGrid.SelectedItems.Count > 0;
            EventDetailsToolbarButton.IsEnabled = hasSelection;
            CopyToolbarButton.IsEnabled = hasSelection;
            CopyMenuItem.IsEnabled = hasSelection;
            ContextMenuCopyMenuItem.IsEnabled = hasSelection;
        }

        private void ShowEventDetailsButton_Click(object sender, RoutedEventArgs e)
        {
            ShowEventDetails();
        }

        private void ShowEventDetails()
        {
            if (EventsDataGrid.SelectedItem is EventRecord selectedRecord)
            {
                var detailsWindow = new EventDetailsWindow(EffectiveDarkMode);
                detailsWindow.SetEventRecord(selectedRecord, ShowTimestampsInUTC);
                detailsWindow.Owner = this;
                detailsWindow.Show();
            }
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void CheckForUpdates_Click(object sender, RoutedEventArgs e)
        {
            // Open the GitHub releases page
            Process.Start(new ProcessStartInfo("https://github.com/chrdavis/ETWSpy/releases") { UseShellExecute = true });
        }

        private void Help_Click(object sender, RoutedEventArgs e)
        {
            // Open the GitHub repository page
            Process.Start(new ProcessStartInfo("https://github.com/chrdavis/ETWSpy") { UseShellExecute = true });
        }

        private void ShowFilters_Click(object sender, RoutedEventArgs e)
        {
            // If the window is already open, just activate it
            if (_filtersWindow != null && _filtersWindow.IsLoaded)
            {
                _filtersWindow.Activate();
                return;
            }

            _filtersWindow = new FiltersWindow(FilterEntries, ProviderConfigEntries, OnFiltersChangedFromWindow, EffectiveDarkMode);
            _filtersWindow.Owner = this;
            _filtersWindow.Closed += (_, _) => _filtersWindow = null;
            _filtersWindow.Show();
        }

        private void OnFiltersChangedFromWindow()
        {
            // This is called when filters are added/removed from the FiltersWindow
            // The CollectionChanged event will handle restarting the trace session
        }

        private void ShowProviders_Click(object sender, RoutedEventArgs e)
        {
            // If the window is already open, just activate it
            if (_providerConfigWindow != null && _providerConfigWindow.IsLoaded)
            {
                _providerConfigWindow.Activate();
                return;
            }

            _providerConfigWindow = new ProviderConfigWindow(ProviderConfigEntries, FilterEntries, OnProvidersChangedFromWindow, EffectiveDarkMode);
            _providerConfigWindow.Owner = this;
            _providerConfigWindow.Closed += (_, _) => _providerConfigWindow = null;
            _providerConfigWindow.Show();
        }

        private void OnProvidersChangedFromWindow()
        {
            // This is called when provider configurations are added/removed from the ProviderConfigWindow
            // Enable or disable the Filters menu/button based on whether providers exist
            UpdateFiltersEnabled();
        }

        private void EditProviderList_Click(object sender, RoutedEventArgs e)
        {
            var editorWindow = new ProviderListEditorWindow(EffectiveDarkMode);
            editorWindow.Owner = this;
            editorWindow.ShowDialog();
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            var aboutWindow = new AboutWindow(EffectiveDarkMode, AppVersion);
            aboutWindow.Owner = this;
            aboutWindow.ShowDialog();
        }

        private void LightTheme_Click(object sender, RoutedEventArgs e)
        {
            _useSystemTheme = false;
            _isDarkMode = false;
            RegistrySettings.SaveBool(RegistrySettings.UseSystemTheme, false);
            RegistrySettings.SaveBool(RegistrySettings.DarkMode, false);
            ApplyTheme();
            UpdateThemeMenuCheckmarks();
        }

        private void DarkTheme_Click(object sender, RoutedEventArgs e)
        {
            _useSystemTheme = false;
            _isDarkMode = true;
            RegistrySettings.SaveBool(RegistrySettings.UseSystemTheme, false);
            RegistrySettings.SaveBool(RegistrySettings.DarkMode, true);
            ApplyTheme();
            UpdateThemeMenuCheckmarks();
        }

        private void SystemTheme_Click(object sender, RoutedEventArgs e)
        {
            _useSystemTheme = true;
            RegistrySettings.SaveBool(RegistrySettings.UseSystemTheme, true);
            ApplyTheme();
            UpdateThemeMenuCheckmarks();
        }

        private void TimeFormatLocal_Click(object sender, RoutedEventArgs e)
        {
            ShowTimestampsInUTC = false;
            UpdateTimeFormatMenuCheckmarks();
        }

        private void TimeFormatUTC_Click(object sender, RoutedEventArgs e)
        {
            ShowTimestampsInUTC = true;
            UpdateTimeFormatMenuCheckmarks();
        }

        /// <summary>
        /// Updates the checkmarks on the time format menu items.
        /// </summary>
        private void UpdateTimeFormatMenuCheckmarks()
        {
            TimeFormatLocalMenuItem.IsChecked = !_showTimestampsInUTC;
            TimeFormatUTCMenuItem.IsChecked = _showTimestampsInUTC;
        }

        private void MaxEvents1000_Click(object sender, RoutedEventArgs e)
        {
            MaxEventsToShow = 1000;
            UpdateMaxEventsMenuCheckmarks();
        }

        private void MaxEvents10000_Click(object sender, RoutedEventArgs e)
        {
            MaxEventsToShow = 10000;
            UpdateMaxEventsMenuCheckmarks();
        }

        private void MaxEvents100000_Click(object sender, RoutedEventArgs e)
        {
            MaxEventsToShow = 100000;
            UpdateMaxEventsMenuCheckmarks();
        }

        /// <summary>
        /// Updates the checkmarks on the max events menu items.
        /// </summary>
        private void UpdateMaxEventsMenuCheckmarks()
        {
            MaxEvents1000MenuItem.IsChecked = _maxEventsToShow == 1000;
            MaxEvents10000MenuItem.IsChecked = _maxEventsToShow == 10000;
            MaxEvents100000MenuItem.IsChecked = _maxEventsToShow == 100000;
        }

        private void Autoscroll_Click(object sender, RoutedEventArgs e)
        {
            Autoscroll = !Autoscroll;
        }

        /// <summary>
        /// Updates the checkmark on the autoscroll menu item.
        /// </summary>
        private void UpdateAutoscrollMenuCheckmark()
        {
            AutoscrollMenuItem.IsChecked = _autoscroll;
        }

        private void RestoreSession_Click(object sender, RoutedEventArgs e)
        {
            RestoreSessionOnLaunch = !RestoreSessionOnLaunch;
        }

        /// <summary>
        /// Updates the checkmark on the restore session menu item.
        /// </summary>
        private void UpdateRestoreSessionMenuCheckmark()
        {
            RestoreSessionMenuItem.IsChecked = _restoreSessionOnLaunch;
        }

        /// <summary>
        /// Gets the path to the last session configuration file.
        /// </summary>
        private static string GetLastSessionFilePath()
        {
            var exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(exeDirectory, "lastsession.etwspy");
        }

        /// <summary>
        /// Saves the current provider and filter configuration to the last session file.
        /// </summary>
        private void SaveLastSession()
        {
            // Only save if there's something to save
            if (ProviderConfigEntries.Count == 0 && FilterEntries.Count == 0)
            {
                // Delete the last session file if it exists since there's nothing to restore
                try
                {
                    var filePath = GetLastSessionFilePath();
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                    }
                }
                catch
                {
                    // Silently ignore file deletion errors
                }
                return;
            }

            try
            {
                var config = new ConfigurationData
                {
                    Filters = [.. FilterEntries],
                    Providers = [.. ProviderConfigEntries]
                };

                string json = JsonSerializer.Serialize(config, JsonOptions);
                File.WriteAllText(GetLastSessionFilePath(), json);
            }
            catch
            {
                // Silently ignore save errors - don't interrupt app closing
            }
        }

        /// <summary>
        /// Tries to restore the last session configuration from the last session file.
        /// </summary>
        private void TryRestoreLastSession()
        {
            var filePath = GetLastSessionFilePath();
            if (!File.Exists(filePath))
            {
                return;
            }

            try
            {
                string json = File.ReadAllText(filePath);
                var config = JsonSerializer.Deserialize<ConfigurationData>(json);

                if (config is null)
                {
                    return;
                }

                // Temporarily unsubscribe from CollectionChanged to avoid restarting the trace session
                // for each filter added during loading
                FilterEntries.CollectionChanged -= OnFilterEntriesChanged;
                ProviderConfigEntries.CollectionChanged -= OnProviderConfigEntriesChanged;

                try
                {
                    // Load providers first
                    foreach (var provider in config.Providers)
                    {
                        ProviderConfigEntries.Add(provider);
                    }

                    // Load filters
                    foreach (var filter in config.Filters)
                    {
                        FilterEntries.Add(filter);
                    }
                }
                finally
                {
                    // Re-subscribe to CollectionChanged
                    FilterEntries.CollectionChanged += OnFilterEntriesChanged;
                    ProviderConfigEntries.CollectionChanged += OnProviderConfigEntriesChanged;
                }

                // Update UI state based on loaded data
                UpdateFiltersEnabled();

                // Don't automatically start capturing - let user decide
                _wantsCaptureActive = false;
                StartPauseToolbarIcon.Text = "\uE768"; // Play icon
                StartPauseToolbarButton.ToolTip = "Start event capture";
                StartPauseMenuItem.Header = "_Start event capture";
                StartPauseMenuIcon.Text = "\uE768"; // Play icon
            }
            catch
            {
                // Silently ignore restore errors - don't prevent app from starting
            }
        }

        private void FileAssociation_Click(object sender, RoutedEventArgs e)
        {
            if (FileAssociationHelper.IsFileAssociationRegistered())
            {
                // Unregister
                if (FileAssociationHelper.UnregisterFileAssociation())
                {
                    RegistrySettings.SaveBool(RegistrySettings.FileAssociationRegistered, false);
                }
            }
            else
            {
                // Register
                if (FileAssociationHelper.RegisterFileAssociation())
                {
                    RegistrySettings.SaveBool(RegistrySettings.FileAssociationRegistered, true);
                }
            }
            UpdateFileAssociationMenuCheckmark();
        }

        /// <summary>
        /// Updates the checkmark on the file association menu item based on actual registry state.
        /// </summary>
        private void UpdateFileAssociationMenuCheckmark()
        {
            FileAssociationMenuItem.IsChecked = FileAssociationHelper.IsFileAssociationRegistered();
        }

        /// <summary>
        /// Loads a configuration file from the specified path.
        /// </summary>
        private void LoadConfigurationFile(string filePath)
        {
            try
            {
                string json = File.ReadAllText(filePath);
                var config = JsonSerializer.Deserialize<ConfigurationData>(json);

                if (config is null)
                {
                    MessageBox.Show("Invalid configuration file.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Temporarily unsubscribe from CollectionChanged to avoid restarting the trace session
                // for each filter added during loading
                FilterEntries.CollectionChanged -= OnFilterEntriesChanged;
                ProviderConfigEntries.CollectionChanged -= OnProviderConfigEntriesChanged;

                try
                {
                    // Clear existing data and load from file
                    FilterEntries.Clear();
                    ProviderConfigEntries.Clear();

                    // Load providers first
                    foreach (var provider in config.Providers)
                    {
                        ProviderConfigEntries.Add(provider);
                    }

                    // Load filters
                    foreach (var filter in config.Filters)
                    {
                        FilterEntries.Add(filter);
                    }
                }
                finally
                {
                    // Re-subscribe to CollectionChanged
                    FilterEntries.CollectionChanged += OnFilterEntriesChanged;
                    ProviderConfigEntries.CollectionChanged += OnProviderConfigEntriesChanged;
                }

                // Update UI state based on loaded data
                UpdateFiltersEnabled();

                // After loading, don't automatically start capturing - let user decide
                _wantsCaptureActive = false;
                StopTracing();
                StartPauseToolbarIcon.Text = "\uE768"; // Play icon
                StartPauseToolbarButton.ToolTip = "Start event capture";
                StartPauseMenuItem.Header = "_Start event capture";
                StartPauseMenuIcon.Text = "\uE768"; // Play icon
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