using System;
using System.Windows;
using System.Windows.Controls;

namespace ETWSpyUI
{
    /// <summary>
    /// Interaction logic for SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private readonly MainWindow _mainWindow;
        private readonly bool _isDarkMode;
        private bool _isInitializing = true;

        public SettingsWindow(MainWindow mainWindow, bool isDarkMode)
        {
            _mainWindow = mainWindow;
            _isDarkMode = isDarkMode;

            InitializeComponent();

            // Apply title bar theme immediately after window handle is available
            SourceInitialized += (_, _) => WindowHelper.ApplyTitleBarTheme(this, _isDarkMode);

            // Initialize controls with current values
            DarkModeCheckBox.IsChecked = _mainWindow.IsDarkMode;
            UTCCheckBox.IsChecked = _mainWindow.ShowTimestampsInUTC;
            MaxEventsSlider.Value = _mainWindow.MaxEventsToShow;
            UpdateMaxEventsText();

            _isInitializing = false;
        }

        private void UpdateMaxEventsText()
        {
            MaxEventsText.Text = $"{MaxEventsSlider.Value:N0}";
        }

        private void DarkModeCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            _mainWindow.IsDarkMode = DarkModeCheckBox.IsChecked == true;
            // Update this window's title bar theme
            WindowHelper.ApplyTitleBarTheme(this, _mainWindow.IsDarkMode);
        }

        private void UTCCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            _mainWindow.ShowTimestampsInUTC = UTCCheckBox.IsChecked == true;
        }

        private void MaxEventsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing) return;
            _mainWindow.MaxEventsToShow = (int)MaxEventsSlider.Value;
            UpdateMaxEventsText();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
