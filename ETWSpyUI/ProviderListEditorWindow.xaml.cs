using ETWSpyLib;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ETWSpyUI
{
    /// <summary>
    /// Represents a provider entry for display in the editor list.
    /// </summary>
    public class ProviderListEntry : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private Guid _guid;

        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged(nameof(Name));
                }
            }
        }

        public Guid Guid
        {
            get => _guid;
            set
            {
                if (_guid != value)
                {
                    _guid = value;
                    OnPropertyChanged(nameof(Guid));
                    OnPropertyChanged(nameof(GuidDisplay));
                }
            }
        }

        public string GuidDisplay => $"{{{Guid}}}";

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public ProviderListEntry()
        {
        }

        public ProviderListEntry(string name, Guid guid)
        {
            Name = name;
            Guid = guid;
        }

        public ProviderListEntry(ProviderEntry jsonEntry)
        {
            Name = jsonEntry.Name;
            Guid = jsonEntry.Guid;
        }
    }

    /// <summary>
    /// Interaction logic for ProviderListEditorWindow.xaml
    /// </summary>
    public partial class ProviderListEditorWindow : Window
    {
        private readonly ObservableCollection<ProviderListEntry> _providerEntries;
        private readonly bool _isDarkMode;
        private bool _hasUnsavedChanges;
        private ProviderListEntry? _editingEntry;

        public ProviderListEditorWindow(bool isDarkMode)
        {
            _isDarkMode = isDarkMode;
            _providerEntries = new ObservableCollection<ProviderListEntry>();
            _hasUnsavedChanges = false;

            InitializeComponent();

            // Apply title bar theme immediately after window handle is available
            SourceInitialized += (_, _) => WindowHelper.ApplyTitleBarTheme(this, _isDarkMode);

            // Bind the ListView to the provider entries collection
            ProvidersListView.ItemsSource = _providerEntries;

            // Load existing providers
            LoadProviders();

            // Update button states
            UpdateButtonStates();
        }

        /// <summary>
        /// Loads providers from the JSON file.
        /// </summary>
        private void LoadProviders()
        {
            try
            {
                var entries = ProviderJsonReader.ReadProviders();

                _providerEntries.Clear();
                foreach (var entry in entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
                {
                    _providerEntries.Add(new ProviderListEntry(entry));
                }

                _hasUnsavedChanges = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to load providers: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Updates the enabled state of buttons based on current state.
        /// </summary>
        private void UpdateButtonStates()
        {
            bool hasValidInput = !string.IsNullOrWhiteSpace(ProviderNameTextBox.Text) &&
                                !string.IsNullOrWhiteSpace(ProviderGuidTextBox.Text);
            bool hasSelection = ProvidersListView.SelectedItems.Count > 0;
            bool hasSingleSelection = ProvidersListView.SelectedItems.Count == 1;
            bool hasItems = _providerEntries.Count > 0;
            bool isEditing = _editingEntry != null;

            // Add button - enabled when there's valid input and not editing
            AddButton.IsEnabled = hasValidInput && !isEditing;

            // Edit button - enabled when single selection and valid input during edit mode, or single selection to start editing
            EditButton.IsEnabled = (isEditing && hasValidInput) || (!isEditing && hasSingleSelection);
            EditButton.Content = isEditing ? "Update" : "Edit";

            // Remove button - enabled when items are selected
            RemoveButton.IsEnabled = hasSelection;

            // Context menu items
            ContextMenuEditMenuItem.IsEnabled = hasSingleSelection;
            ContextMenuRemoveMenuItem.IsEnabled = hasSelection;

            // Empty placeholder
            EmptyPlaceholderText.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>
        /// Validates the GUID input.
        /// </summary>
        /// <param name="guidText">The GUID text to validate.</param>
        /// <param name="guid">The parsed GUID if valid.</param>
        /// <returns>True if valid, false otherwise.</returns>
        private static bool TryParseGuid(string guidText, out Guid guid)
        {
            // Remove curly braces if present
            var cleanGuid = guidText.Trim();
            if (cleanGuid.StartsWith('{') && cleanGuid.EndsWith('}'))
            {
                cleanGuid = cleanGuid[1..^1];
            }

            return Guid.TryParse(cleanGuid, out guid);
        }

        /// <summary>
        /// Checks if a provider with the given name or GUID already exists.
        /// </summary>
        /// <param name="name">The provider name to check.</param>
        /// <param name="guid">The provider GUID to check.</param>
        /// <param name="excludeEntry">Entry to exclude from the check (for edit scenarios).</param>
        /// <returns>True if a duplicate exists, false otherwise.</returns>
        private bool IsDuplicate(string name, Guid guid, ProviderListEntry? excludeEntry = null)
        {
            return _providerEntries.Any(e =>
                e != excludeEntry &&
                (string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase) || e.Guid == guid));
        }

        private void InputFields_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateButtonStates();
        }

        private void ProvidersListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // If we're in edit mode and selection changes, cancel edit
            if (_editingEntry != null && ProvidersListView.SelectedItem != _editingEntry)
            {
                CancelEdit();
            }

            UpdateButtonStates();
        }

        private void ProvidersListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ProvidersListView.SelectedItem is ProviderListEntry entry)
            {
                StartEdit(entry);
            }
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            var name = ProviderNameTextBox.Text.Trim();
            var guidText = ProviderGuidTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Please enter a provider name.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                ProviderNameTextBox.Focus();
                return;
            }

            if (!TryParseGuid(guidText, out var guid))
            {
                MessageBox.Show("Please enter a valid GUID.\n\nExample: {22fb2cd6-0e7b-422b-a0c7-2fad1fd0e716}", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                ProviderGuidTextBox.Focus();
                return;
            }

            if (IsDuplicate(name, guid))
            {
                MessageBox.Show("A provider with this name or GUID already exists.", "Duplicate Entry", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Add the new entry and sort
            var newEntry = new ProviderListEntry(name, guid);
            
            // Find the correct insertion point to maintain sorted order
            var insertIndex = _providerEntries.TakeWhile(e =>
                string.Compare(e.Name, name, StringComparison.OrdinalIgnoreCase) < 0).Count();
            
            _providerEntries.Insert(insertIndex, newEntry);
            
            _hasUnsavedChanges = true;

            // Clear input fields
            ProviderNameTextBox.Clear();
            ProviderGuidTextBox.Clear();
            ProviderNameTextBox.Focus();

            // Select the new entry
            ProvidersListView.SelectedItem = newEntry;
            ProvidersListView.ScrollIntoView(newEntry);

            UpdateButtonStates();
        }

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            if (_editingEntry != null)
            {
                // We're in edit mode - update the entry
                ApplyEdit();
            }
            else if (ProvidersListView.SelectedItem is ProviderListEntry entry)
            {
                // Start editing the selected entry
                StartEdit(entry);
            }
        }

        private void StartEdit(ProviderListEntry entry)
        {
            _editingEntry = entry;
            ProviderNameTextBox.Text = entry.Name;
            ProviderGuidTextBox.Text = entry.GuidDisplay;
            ProviderNameTextBox.Focus();
            ProviderNameTextBox.SelectAll();
            UpdateButtonStates();
        }

        private void ApplyEdit()
        {
            if (_editingEntry == null)
            {
                return;
            }

            var name = ProviderNameTextBox.Text.Trim();
            var guidText = ProviderGuidTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Please enter a provider name.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                ProviderNameTextBox.Focus();
                return;
            }

            if (!TryParseGuid(guidText, out var guid))
            {
                MessageBox.Show("Please enter a valid GUID.\n\nExample: {22fb2cd6-0e7b-422b-a0c7-2fad1fd0e716}", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                ProviderGuidTextBox.Focus();
                return;
            }

            if (IsDuplicate(name, guid, _editingEntry))
            {
                MessageBox.Show("A provider with this name or GUID already exists.", "Duplicate Entry", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Update the entry
            _editingEntry.Name = name;
            _editingEntry.Guid = guid;
            _hasUnsavedChanges = true;

            // Re-sort the list
            var entries = _providerEntries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
            _providerEntries.Clear();
            foreach (var entry in entries)
            {
                _providerEntries.Add(entry);
            }

            // Select the edited entry
            var editedEntry = _providerEntries.FirstOrDefault(e => e.Name == name && e.Guid == guid);
            if (editedEntry != null)
            {
                ProvidersListView.SelectedItem = editedEntry;
                ProvidersListView.ScrollIntoView(editedEntry);
            }

            // Clear edit mode
            CancelEdit();
        }

        private void CancelEdit()
        {
            _editingEntry = null;
            ProviderNameTextBox.Clear();
            ProviderGuidTextBox.Clear();
            UpdateButtonStates();
        }

        private void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = ProvidersListView.SelectedItems.Cast<ProviderListEntry>().ToList();

            if (selectedItems.Count == 0)
            {
                return;
            }

            var message = selectedItems.Count == 1
                ? $"Are you sure you want to remove '{selectedItems[0].Name}'?"
                : $"Are you sure you want to remove {selectedItems.Count} providers?";

            var result = MessageBox.Show(
                message,
                "Confirm Remove",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                foreach (var item in selectedItems)
                {
                    _providerEntries.Remove(item);
                }

                _hasUnsavedChanges = true;

                // Cancel any editing
                CancelEdit();
                UpdateButtonStates();
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var jsonPath = ProviderJsonReader.GetLocalJsonPath();
                var jsonEntries = _providerEntries
                    .Select(e => new ProviderEntry(e.Name, e.Guid))
                    .ToList();

                ProviderJsonReader.WriteToFile(jsonPath, jsonEntries);

                // Invalidate the provider manager cache so it reloads
                ProviderManager.InvalidateCache();

                _hasUnsavedChanges = false;

                MessageBox.Show(
                    "Provider list saved successfully.",
                    "Save Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to save providers: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (_hasUnsavedChanges)
            {
                var result = MessageBox.Show(
                    "You have unsaved changes. Are you sure you want to close without saving?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.No)
                {
                    e.Cancel = true;
                }
            }
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "This will reset the provider list to the built-in defaults.\n\n" +
                "All your custom changes will be lost. This action cannot be undone.\n\n" +
                "Are you sure you want to continue?",
                "Reset to Default",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    var defaultEntries = ProviderJsonReader.ReadFromEmbeddedResource();

                    _providerEntries.Clear();
                    foreach (var entry in defaultEntries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        _providerEntries.Add(new ProviderListEntry(entry));
                    }

                    _hasUnsavedChanges = true;
                    CancelEdit();
                    UpdateButtonStates();

                    MessageBox.Show(
                        "Provider list has been reset to defaults.\n\nClick Save to apply the changes.",
                        "Reset Complete",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Failed to reset providers: {ex.Message}",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }
    }
}
