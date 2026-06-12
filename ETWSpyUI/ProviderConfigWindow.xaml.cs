using ETWSpyLib;
using Microsoft.O365.Security.ETW;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;


namespace ETWSpyUI
{
    /// <summary>
    /// Represents a provider configuration entry.
    /// </summary>
    public class ProviderConfigEntry : ObservableObject
    {
        private string _provider = string.Empty;
        public string Provider { get => _provider; set => SetProperty(ref _provider, value); }

        private string? _providerGuid;
        public string? ProviderGuid { get => _providerGuid; set => SetProperty(ref _providerGuid, value); }

        private string _keywords = string.Empty;
        public string Keywords { get => _keywords; set => SetProperty(ref _keywords, value); }

        private string _traceLevel = string.Empty;
        public string TraceLevel { get => _traceLevel; set => SetProperty(ref _traceLevel, value); }

        private string _traceFlags = string.Empty;
        public string TraceFlags { get => _traceFlags; set => SetProperty(ref _traceFlags, value); }
    }

    /// <summary>
    /// Interaction logic for ProviderConfigWindow.xaml
    /// </summary>
    public partial class ProviderConfigWindow : Window
    {
        private readonly ObservableCollection<ProviderConfigEntry> _providerEntries;
        private readonly ObservableCollection<FilterEntry> _filterEntries;
        private readonly Action _onProvidersChanged;
        private readonly bool _isDarkMode;
        private List<ProviderInfo> _allProviders = [];
        private ICollectionView? _providerView;
        private ProviderConfigEntry? _editingProvider;

        public ProviderConfigWindow(ObservableCollection<ProviderConfigEntry> providerEntries, ObservableCollection<FilterEntry> filterEntries, Action onProvidersChanged, bool isDarkMode)
        {
            _providerEntries = providerEntries;
            _filterEntries = filterEntries;
            _onProvidersChanged = onProvidersChanged;
            _isDarkMode = isDarkMode;

            InitializeComponent();

            // Apply title bar theme immediately after window handle is available
            SourceInitialized += (_, _) => WindowHelper.ApplyTitleBarTheme(this, _isDarkMode);

            PopulateProviderComboBox();
            PopulateTraceLevelComboBox();

            // Bind the ListView to the provider entries collection
            ProvidersListView.ItemsSource = _providerEntries;

            // Subscribe to collection changes to update button states
            _providerEntries.CollectionChanged += ProviderEntries_CollectionChanged;

            // Initialize button states
            UpdateButtonStates();
        }

        private void ProviderEntries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateButtonStates();
        }

        private void ProvidersListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProvidersListView.SelectedItem is ProviderConfigEntry entry)
            {
                EnterProviderEditMode(entry);
            }
            else
            {
                ExitProviderEditMode();
            }

            UpdateButtonStates();
        }

        /// <summary>
        /// Loads the selected provider entry into the input fields and switches the Add button to Update.
        /// </summary>
        private void EnterProviderEditMode(ProviderConfigEntry entry)
        {
            _editingProvider = entry;

            // Suppress the ComboBox text-change handler so populating the field doesn't
            // open the dropdown or start filtering.
            _suppressTextChanged = true;
            try
            {
                ProviderComboBox.Text = entry.Provider;
            }
            finally
            {
                _suppressTextChanged = false;
            }

            KeywordsTextBox.Text = entry.Keywords;
            TraceLevelComboBox.SelectedItem = entry.TraceLevel;
            TraceFlagsTextBox.Text = entry.TraceFlags;

            AddButton.Content = "Update";
        }

        /// <summary>
        /// Leaves edit mode and restores the Add button.
        /// </summary>
        private void ExitProviderEditMode()
        {
            _editingProvider = null;
            AddButton.Content = "Add";
        }

        private void UpdateButtonStates()
        {
            bool hasSelection = ProvidersListView.SelectedItem != null;
            bool hasItems = _providerEntries.Count > 0;
            
            RemoveButton.IsEnabled = hasSelection;
            ClearButton.IsEnabled = hasItems;
            
            // Update context menu items
            ContextMenuRemoveMenuItem.IsEnabled = hasSelection;
            ContextMenuClearMenuItem.IsEnabled = hasItems;
            
            // Show/hide empty placeholder text
            EmptyPlaceholderText.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
        }

        private void PopulateProviderComboBox()
        {
            _allProviders = ProviderManager.GetAllProviders();
            _providerView = CollectionViewSource.GetDefaultView(_allProviders);
            _providerView.Filter = ProviderFilter;
            ProviderComboBox.ItemsSource = _providerView;
            
            // Subscribe to dropdown events to manage filtering state
            ProviderComboBox.DropDownOpened += ProviderComboBox_DropDownOpened;
            ProviderComboBox.DropDownClosed += ProviderComboBox_DropDownClosed;
            
            if (ProviderComboBox.Items.Count > 0)
            {
                ProviderComboBox.SelectedIndex = 0;
            }
            
            // Subscribe to text changes AFTER initial selection to avoid opening dropdown on launch
            ProviderComboBox.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, 
                new TextChangedEventHandler(ProviderComboBox_TextChanged));
            
            // Mark initialization complete after layout is done
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => _isInitialized = true);
        }

        private string _filterText = string.Empty;
        private bool _isUserTyping;
        private bool _isInitialized;
        private bool _suppressTextChanged;
        private CancellationTokenSource? _filterCts;
        private const int FilterDebounceMs = 150;

        private bool ProviderFilter(object item)
        {
            if (string.IsNullOrEmpty(_filterText))
            {
                return true;
            }

            if (item is ProviderInfo provider)
            {
                return provider.Name.Contains(_filterText, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private void ProviderComboBox_DropDownOpened(object? sender, EventArgs e)
        {
            _isUserTyping = true;
        }

        private void ProviderComboBox_DropDownClosed(object? sender, EventArgs e)
        {
            _isUserTyping = false;
            // Clear filter when dropdown closes so all items are available next time
            _filterText = string.Empty;
            _suppressTextChanged = true;
            try
            {
                _providerView?.Refresh();
            }
            finally
            {
                _suppressTextChanged = false;
            }
        }

        private void ProviderComboBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Don't process text changes until initialization is complete
            // or when changes are triggered programmatically (re-entrancy guard)
            if (!_isInitialized || _suppressTextChanged)
            {
                return;
            }

            // Only filter when the user is actively typing in the dropdown
            if (!_isUserTyping)
            {
                // If dropdown is closed and user starts typing, open it and start filtering
                if (!ProviderComboBox.IsDropDownOpen && !string.IsNullOrEmpty(ProviderComboBox.Text))
                {
                    _isUserTyping = true;
                    _suppressTextChanged = true;
                    try
                    {
                        // Clear selection so WPF doesn't try to sync Text with SelectedItem during typing
                        // This prevents the ComboBox from overwriting user input when Refresh() is called
                        ProviderComboBox.SelectedItem = null;
                        ProviderComboBox.IsDropDownOpen = true;
                    }
                    finally
                    {
                        _suppressTextChanged = false;
                    }
                }
                else
                {
                    return;
                }
            }

            // Cancel any pending filter operation
            _filterCts?.Cancel();
            _filterCts = new CancellationTokenSource();
            var token = _filterCts.Token;
            var filterText = ProviderComboBox.Text ?? string.Empty;

            // Debounce the filter to avoid filtering on every keystroke
            Task.Delay(FilterDebounceMs, token).ContinueWith(_ =>
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                Dispatcher.Invoke(() =>
                {
                    _suppressTextChanged = true;
                    try
                    {
                        // Preserve text - Refresh() can cause WPF to sync Text with SelectedItem
                        var textToRestore = filterText;

                        _filterText = filterText;
                        _providerView?.Refresh();

                        // Restore the user's typed text if it was changed by Refresh()
                        if (ProviderComboBox.Text != textToRestore)
                        {
                            ProviderComboBox.Text = textToRestore;
                            // Move caret to end after restoring text
                            if (ProviderComboBox.Template.FindName("PART_EditableTextBox", ProviderComboBox) is TextBox textBox)
                            {
                                textBox.CaretIndex = textBox.Text?.Length ?? 0;
                            }
                        }
                    }
                    finally
                    {
                        _suppressTextChanged = false;
                    }
                });
            }, token, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
        }

        private void RefreshProviderComboBox()
        {
            var currentText = ProviderComboBox.Text;
            _allProviders = ProviderManager.GetAllProviders();
            _providerView = CollectionViewSource.GetDefaultView(_allProviders);
            _providerView.Filter = ProviderFilter;
            ProviderComboBox.ItemsSource = _providerView;

            var matchingProvider = _allProviders.FirstOrDefault(p =>
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

        private void PopulateTraceLevelComboBox()
        {
            TraceLevelComboBox.ItemsSource = new[] { "Critical", "Error", "Warning", "Information", "Verbose" };
            TraceLevelComboBox.SelectedIndex = 4; // Default to Verbose
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            // Validate provider is specified
            if (string.IsNullOrWhiteSpace(ProviderComboBox.Text))
            {
                MessageBox.Show("Please specify a provider name or GUID.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Validate keywords if specified
            if (!string.IsNullOrWhiteSpace(KeywordsTextBox.Text))
            {
                if (!TryParseKeywords(KeywordsTextBox.Text.Trim(), out _))
                {
                    MessageBox.Show("Invalid keywords value. Use hex (e.g., 0xFFFFFFFF) or decimal.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            // Validate trace flags if specified
            if (!string.IsNullOrWhiteSpace(TraceFlagsTextBox.Text))
            {
                if (!byte.TryParse(TraceFlagsTextBox.Text.Trim(), out _))
                {
                    MessageBox.Show("Invalid trace flags value. Must be an integer from 0 to 255.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            string providerName = ProviderComboBox.Text;
            string keywords = KeywordsTextBox.Text.Trim();
            string traceLevel = TraceLevelComboBox.SelectedItem?.ToString() ?? "Verbose";
            string traceFlags = TraceFlagsTextBox.Text.Trim();

            // Validate the provider (known name or parseable GUID)
            string providerInput = providerName.Trim();
            var knownProvider = ProviderManager.FindByName(providerInput);

            if (!EtwProviderValidator.IsValidProvider(providerInput, knownProvider?.Guid, out var errorMessage))
            {
                MessageBox.Show(errorMessage, "Invalid Provider", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Get the GUID - either from the known provider or try to parse from input
            string? providerGuid = null;
            if (knownProvider?.Guid != null)
            {
                providerGuid = knownProvider.Guid.Value.ToString();
            }
            else if (Guid.TryParse(providerInput, out var parsedGuid))
            {
                providerGuid = parsedGuid.ToString();
            }

            if (_editingProvider != null)
            {
                UpdateProviderEntry(providerName, providerGuid, keywords, traceLevel, traceFlags);
            }
            else
            {
                AddProviderEntry(providerName, providerGuid, keywords, traceLevel, traceFlags);
            }
        }

        private void AddProviderEntry(string providerName, string? providerGuid, string keywords, string traceLevel, string traceFlags)
        {
            var entry = new ProviderConfigEntry
            {
                Provider = providerName,
                ProviderGuid = providerGuid,
                Keywords = keywords,
                TraceLevel = traceLevel,
                TraceFlags = traceFlags
            };

            // Check if an exact duplicate entry already exists
            bool isDuplicate = _providerEntries.Any(existing =>
                string.Equals(existing.Provider, entry.Provider, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing.Keywords, entry.Keywords, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing.TraceLevel, entry.TraceLevel, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing.TraceFlags, entry.TraceFlags, StringComparison.Ordinal));

            if (isDuplicate)
            {
                MessageBox.Show("This provider configuration has already been added.", "Duplicate Entry", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _providerEntries.Add(entry);

            // Add a default "Include all events" filter for this provider
            var defaultFilter = new FilterEntry
            {
                Provider = entry.Provider,
                ProviderGuid = entry.ProviderGuid,
                FilterCategory = "Event Id", // Default to Event Id filter type
                Value = string.Empty, // Empty value = All event IDs
                FilterLogic = "Include",
                Keywords = entry.Keywords,
                TraceLevel = entry.TraceLevel,
                TraceFlags = entry.TraceFlags
            };
            _filterEntries.Add(defaultFilter);

            _onProvidersChanged?.Invoke();
        }

        private void UpdateProviderEntry(string providerName, string? providerGuid, string keywords, string traceLevel, string traceFlags)
        {
            var entry = _editingProvider;
            if (entry == null)
            {
                return;
            }

            int index = _providerEntries.IndexOf(entry);
            if (index < 0)
            {
                ExitProviderEditMode();
                return;
            }

            // Check for an exact duplicate, excluding the entry currently being edited
            bool isDuplicate = _providerEntries.Any(existing =>
                !ReferenceEquals(existing, entry) &&
                string.Equals(existing.Provider, providerName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing.Keywords, keywords, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing.TraceLevel, traceLevel, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing.TraceFlags, traceFlags, StringComparison.Ordinal));

            if (isDuplicate)
            {
                MessageBox.Show("This provider configuration has already been added.", "Duplicate Entry", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string oldProvider = entry.Provider;
            bool providerNameChanged = !string.Equals(oldProvider, providerName, StringComparison.OrdinalIgnoreCase);

            // Leave edit mode first so clearing the selection doesn't re-populate the fields.
            ProvidersListView.SelectedItem = null;

            // Update the provider entry in place; INotifyPropertyChanged refreshes the row.
            entry.Provider = providerName;
            entry.ProviderGuid = providerGuid;
            entry.Keywords = keywords;
            entry.TraceLevel = traceLevel;
            entry.TraceFlags = traceFlags;

            // Cascade the change to filters associated with this provider so the trace session
            // (which groups filters by provider name) and the Filters window stay consistent.
            var associatedFilters = _filterEntries
                .Where(f => string.Equals(f.Provider, oldProvider, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var filter in associatedFilters)
            {
                filter.Provider = providerName;
                filter.ProviderGuid = providerGuid;
                filter.Keywords = keywords;
                filter.TraceLevel = traceLevel;
                filter.TraceFlags = traceFlags;
            }

            // If the provider name changed, raise a collection-changed notification so dependents
            // that snapshot provider names (e.g., the Filters window dropdown) refresh.
            if (providerNameChanged)
            {
                _providerEntries[index] = entry;
            }

            // Poke the filter collection once so the trace session restarts a single time with
            // the updated configuration (in-place edits alone don't raise CollectionChanged).
            if (associatedFilters.Count > 0)
            {
                int filterIndex = _filterEntries.IndexOf(associatedFilters[0]);
                if (filterIndex >= 0)
                {
                    _filterEntries[filterIndex] = associatedFilters[0];
                }
            }

            _onProvidersChanged?.Invoke();
        }

        private void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            if (ProvidersListView.SelectedItem is ProviderConfigEntry selectedEntry)
            {
                // Remove all filter entries associated with this provider
                var filtersToRemove = _filterEntries
                    .Where(f => string.Equals(f.Provider, selectedEntry.Provider, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                foreach (var filter in filtersToRemove)
                {
                    _filterEntries.Remove(filter);
                }

                _providerEntries.Remove(selectedEntry);
                _onProvidersChanged?.Invoke();
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            // Clear all filter entries as well since they're associated with providers
            _filterEntries.Clear();
            _providerEntries.Clear();
            _onProvidersChanged?.Invoke();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private static bool TryParseKeywords(string input, out ulong keywords)
        {
            keywords = 0;

            if (string.IsNullOrWhiteSpace(input))
            {
                return true;
            }

            // Try parsing as hex (with or without 0x prefix)
            var trimmed = input.Trim();
            if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return ulong.TryParse(trimmed[2..], System.Globalization.NumberStyles.HexNumber, null, out keywords);
            }

            // Try parsing as decimal
            return ulong.TryParse(trimmed, out keywords);
        }
    }
}
