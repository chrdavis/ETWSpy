using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace ETWSpyUI
{
    /// <summary>
    /// Interaction logic for FiltersWindow.xaml
    /// </summary>
    public partial class FiltersWindow : Window
    {
        private readonly ObservableCollection<FilterEntry> _filterEntries;
        private readonly ObservableCollection<ProviderConfigEntry> _providerEntries;
        private readonly Action _onFiltersChanged;
        private readonly bool _isDarkMode;

        private const string EventIdCategory = "Event Id";
        private const string MatchTextCategory = "Match Text";

        public FiltersWindow(ObservableCollection<FilterEntry> filterEntries, ObservableCollection<ProviderConfigEntry> providerEntries, Action onFiltersChanged, bool isDarkMode)
        {
            _filterEntries = filterEntries;
            _providerEntries = providerEntries;
            _onFiltersChanged = onFiltersChanged;
            _isDarkMode = isDarkMode;

            InitializeComponent();

            // Apply title bar theme immediately after window handle is available
            SourceInitialized += (_, _) => WindowHelper.ApplyTitleBarTheme(this, _isDarkMode);

            PopulateProviderComboBox();
            PopulateFilterCategoryComboBox();
            PopulateActionComboBox();

            // Bind the FiltersListView to the FilterEntries collection
            FiltersListView.ItemsSource = _filterEntries;

            // Subscribe to collection changes to update button states
            _filterEntries.CollectionChanged += FilterEntries_CollectionChanged;

            // Subscribe to provider collection changes to update the provider combo box
            _providerEntries.CollectionChanged += ProviderEntries_CollectionChanged;

            // Initialize button states
            UpdateButtonStates();

            // Set initial instruction label
            UpdateValueInstructionLabel();

            // Unsubscribe when window is closed
            Closed += FiltersWindow_Closed;
        }

        private void FiltersWindow_Closed(object? sender, EventArgs e)
        {
            _filterEntries.CollectionChanged -= FilterEntries_CollectionChanged;
            _providerEntries.CollectionChanged -= ProviderEntries_CollectionChanged;
        }

        private void ProviderEntries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            // Refresh the provider combo box when providers are added or removed
            var currentSelection = ProviderComboBox.SelectedItem?.ToString();
            PopulateProviderComboBox();

            // Try to restore the previous selection if it still exists
            if (!string.IsNullOrEmpty(currentSelection))
            {
                var providers = ProviderComboBox.ItemsSource as IList<string>;
                if (providers != null && providers.Contains(currentSelection))
                {
                    ProviderComboBox.SelectedItem = currentSelection;
                }
            }
        }

        private void FilterEntries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateButtonStates();
        }

        private void FiltersListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateButtonStates();
        }

        private void UpdateButtonStates()
        {
            bool hasSelection = FiltersListView.SelectedItem != null;
            bool hasItems = _filterEntries.Count > 0;
            
            RemoveFilterButton.IsEnabled = hasSelection;
            ClearFiltersButton.IsEnabled = hasItems;
            
            // Update context menu items
            ContextMenuRemoveMenuItem.IsEnabled = hasSelection;
            ContextMenuClearMenuItem.IsEnabled = hasItems;
            
            // Show/hide empty placeholder text
            EmptyPlaceholderText.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
        }

        private void PopulateProviderComboBox()
        {
            // Only show providers that have been added through the Providers window
            var providers = _providerEntries.Select(p => p.Provider).Distinct().ToList();
            ProviderComboBox.ItemsSource = providers;
            if (ProviderComboBox.Items.Count > 0)
            {
                ProviderComboBox.SelectedIndex = 0;
            }
        }

        private void PopulateFilterCategoryComboBox()
        {
            FilterCategoryComboBox.ItemsSource = new[] { EventIdCategory, MatchTextCategory };
            FilterCategoryComboBox.SelectedIndex = 0;
        }

        private void PopulateActionComboBox()
        {
            ActionComboBox.ItemsSource = new[] { "Include", "Exclude" };
            ActionComboBox.SelectedIndex = 0;
        }

        private void FilterCategoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateValueInstructionLabel();
            ValueTextBox.Clear();
        }

        private void UpdateValueInstructionLabel()
        {
            if (FilterCategoryComboBox.SelectedItem is string category)
            {
                ValueTextBox.ToolTip = category == EventIdCategory
                    ? "Separate IDs with ','; Specify range with '-'; Empty matches all"
                    : "Case-insensitive sub-string match in event/task name; Empty matches all";
            }
        }

        private void AddFilter_Click(object sender, RoutedEventArgs e)
        {
            // Validate provider is specified
            if (ProviderComboBox.SelectedItem == null)
            {
                MessageBox.Show("Please select a provider.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string filterCategory = FilterCategoryComboBox.SelectedItem?.ToString() ?? EventIdCategory;
            string value = ValueTextBox.Text.Trim();

            // Validate based on filter category
            if (filterCategory == EventIdCategory && !string.IsNullOrWhiteSpace(value))
            {
                if (!TryParseEventIds(value, out _, out string? errorMessage))
                {
                    MessageBox.Show(errorMessage, "Invalid Event ID", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            var filter = new FilterEntry
            {
                Provider = ProviderComboBox.SelectedItem.ToString() ?? string.Empty,
                FilterCategory = filterCategory,
                Value = value,
                FilterLogic = ActionComboBox.SelectedItem?.ToString() ?? "Include"
            };

            // Check if an exact duplicate filter already exists
            bool isDuplicate = _filterEntries.Any(existing =>
                string.Equals(existing.Provider, filter.Provider, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing.FilterCategory, filter.FilterCategory, StringComparison.Ordinal) &&
                string.Equals(existing.Value, filter.Value, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing.FilterLogic, filter.FilterLogic, StringComparison.Ordinal));

            if (isDuplicate)
            {
                MessageBox.Show("This filter has already been added.", "Duplicate Filter", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _filterEntries.Add(filter);
            _onFiltersChanged?.Invoke();

            // Clear the value field for the next entry
            ValueTextBox.Clear();
        }

        private void RemoveFilter_Click(object sender, RoutedEventArgs e)
        {
            if (FiltersListView.SelectedItem is FilterEntry selectedFilter)
            {
                _filterEntries.Remove(selectedFilter);
                _onFiltersChanged?.Invoke();
            }
        }

        private void ClearFilters_Click(object sender, RoutedEventArgs e)
        {
            _filterEntries.Clear();
            _onFiltersChanged?.Invoke();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>
        /// Parses an event ID input string into a list of individual event IDs.
        /// </summary>
        private static bool TryParseEventIds(string input, out List<ushort> eventIds, out string? errorMessage)
        {
            eventIds = [];
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(input))
            {
                return true;
            }

            var parts = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var part in parts)
            {
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
    }
}
