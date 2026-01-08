using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace ETWSpyUI
{
    /// <summary>
    /// Represents a provider configuration entry.
    /// </summary>
    public class ProviderConfigEntry
    {
        public string Provider { get; set; } = string.Empty;
        public string Keywords { get; set; } = string.Empty;
        public string TraceLevel { get; set; } = string.Empty;
        public string TraceFlags { get; set; } = string.Empty;
    }

    /// <summary>
    /// Interaction logic for ProviderConfigWindow.xaml
    /// </summary>
    public partial class ProviderConfigWindow : Window
    {
        private readonly ObservableCollection<ProviderConfigEntry> _providerEntries;
        private readonly Action _onProvidersChanged;
        private readonly bool _isDarkMode;

        public ProviderConfigWindow(ObservableCollection<ProviderConfigEntry> providerEntries, Action onProvidersChanged, bool isDarkMode)
        {
            _providerEntries = providerEntries;
            _onProvidersChanged = onProvidersChanged;
            _isDarkMode = isDarkMode;

            InitializeComponent();

            // Apply title bar theme immediately after window handle is available
            SourceInitialized += (_, _) => WindowHelper.ApplyTitleBarTheme(this, _isDarkMode);

            PopulateProviderComboBox();
            PopulateTraceLevelComboBox();

            // Bind the ListView to the provider entries collection
            ProvidersListView.ItemsSource = _providerEntries;
        }

        private void PopulateProviderComboBox()
        {
            var providers = ProviderManager.GetAllProviders();
            ProviderComboBox.ItemsSource = providers;
            if (ProviderComboBox.Items.Count > 0)
            {
                ProviderComboBox.SelectedIndex = 0;
            }
        }

        private void RefreshProviderComboBox()
        {
            var currentText = ProviderComboBox.Text;
            var providers = ProviderManager.GetAllProviders();
            ProviderComboBox.ItemsSource = providers;

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

            var entry = new ProviderConfigEntry
            {
                Provider = ProviderComboBox.Text,
                Keywords = KeywordsTextBox.Text.Trim(),
                TraceLevel = TraceLevelComboBox.SelectedItem?.ToString() ?? "Verbose",
                TraceFlags = TraceFlagsTextBox.Text.Trim()
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
            _onProvidersChanged?.Invoke();

            // Save the provider to registry if it's a new one
            if (ProviderManager.AddProvider(entry.Provider))
            {
                RefreshProviderComboBox();
            }
        }

        private void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            if (ProvidersListView.SelectedItem is ProviderConfigEntry selectedEntry)
            {
                _providerEntries.Remove(selectedEntry);
                _onProvidersChanged?.Invoke();
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
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
