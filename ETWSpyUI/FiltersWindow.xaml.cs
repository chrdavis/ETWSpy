using System;
using System.Collections.ObjectModel;
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
            PopulateFilterTypeComboBox();

            // Bind the FiltersListView to the FilterEntries collection
            FiltersListView.ItemsSource = _filterEntries;
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

        private void PopulateFilterTypeComboBox()
        {
            FilterTypeComboBox.ItemsSource = new[] { "Include", "Exclude" };
            FilterTypeComboBox.SelectedIndex = 0;
        }

        private void AddFilter_Click(object sender, RoutedEventArgs e)
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
                FilterType = FilterTypeComboBox.Text
            };

            // Check if an exact duplicate filter already exists
            bool isDuplicate = _filterEntries.Any(existing =>
                string.Equals(existing.Provider, filter.Provider, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing.EventId, filter.EventId, StringComparison.Ordinal) &&
                string.Equals(existing.MatchText, filter.MatchText, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing.FilterType, filter.FilterType, StringComparison.Ordinal));

            if (isDuplicate)
            {
                MessageBox.Show("This filter has already been added.", "Duplicate Filter", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _filterEntries.Add(filter);
            _onFiltersChanged?.Invoke();
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
        private static bool TryParseEventIds(string input, out System.Collections.Generic.List<ushort> eventIds, out string? errorMessage)
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
