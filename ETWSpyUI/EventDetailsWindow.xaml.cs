using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;

namespace ETWSpyUI
{
    /// <summary>
    /// Interaction logic for EventDetailsWindow.xaml
    /// </summary>
    public partial class EventDetailsWindow : Window
    {
        /// <summary>
        /// Represents a payload item for display in the ListView.
        /// </summary>
        public class PayloadItem
        {
            public string Name { get; set; } = string.Empty;
            public string Type { get; set; } = string.Empty;
            public string Value { get; set; } = string.Empty;
        }

        private readonly bool _isDarkMode;
        private EventRecord? _eventRecord;
        private bool _showTimestampsInUTC;

        public EventDetailsWindow()
            : this(false)
        {
        }

        public EventDetailsWindow(bool isDarkMode)
        {
            _isDarkMode = isDarkMode;
            InitializeComponent();

            // Apply title bar theme immediately after window handle is available
            SourceInitialized += (_, _) => WindowHelper.ApplyTitleBarTheme(this, _isDarkMode);
        }

        /// <summary>
        /// Initializes the window with the specified event record data.
        /// </summary>
        /// <param name="eventRecord">The event record to display.</param>
        /// <param name="showTimestampsInUTC">Whether to display timestamps in UTC format.</param>
        public void SetEventRecord(EventRecord eventRecord, bool showTimestampsInUTC = false)
        {
            // Store for later use (e.g., copying)
            _eventRecord = eventRecord;
            _showTimestampsInUTC = showTimestampsInUTC;

            // Populate the labels
            ProviderLabel.Content = eventRecord.ProviderName;
            EventIdLabel.Content = eventRecord.EventId.ToString();
            
            // Format timestamp based on UTC setting
            var displayTime = showTimestampsInUTC 
                ? eventRecord.Timestamp.ToUniversalTime() 
                : eventRecord.Timestamp.ToLocalTime();
            var suffix = showTimestampsInUTC ? " UTC" : "";
            TimestampdLabel.Content = $"{displayTime:yyyy-MM-dd HH:mm:ss.fff}{suffix}";
            
            EventNameLabel.Content = eventRecord.EventName;
            TaskNameLabel.Content = eventRecord.TaskName;

            // Populate the payload list view using type information from PayloadWithTypes
            var payloadItems = new List<PayloadItem>();
            foreach (var prop in eventRecord.PayloadWithTypes)
            {
                payloadItems.Add(new PayloadItem
                {
                    Name = prop.Name,
                    Type = prop.TypeName,
                    Value = prop.Value
                });
            }

            EventPayloadListView.ItemsSource = payloadItems;
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_eventRecord == null)
            {
                return;
            }

            var sb = new StringBuilder();

            // Add header row
            sb.AppendLine("Timestamp\tProviderName\tEventName\tTaskName\tEventId\tProcessId\tThreadId\tPayload");

            // Format timestamp
            var displayTime = _showTimestampsInUTC
                ? _eventRecord.Timestamp.ToUniversalTime()
                : _eventRecord.Timestamp.ToLocalTime();
            var suffix = _showTimestampsInUTC ? " UTC" : "";
            string timestampStr = $"{displayTime:yyyy-MM-dd HH:mm:ss.fff}{suffix}";

            // Format payload
            string payloadStr = _eventRecord.Payload.Count == 0
                ? string.Empty
                : string.Join("; ", _eventRecord.Payload.Select(kvp => $"{kvp.Key}={kvp.Value}"));

            // Add data row
            sb.AppendLine($"{timestampStr}\t{_eventRecord.ProviderName}\t{_eventRecord.EventName}\t{_eventRecord.TaskName}\t{_eventRecord.EventId}\t{_eventRecord.ProcessId}\t{_eventRecord.ThreadId}\t{payloadStr}");

            try
            {
                Clipboard.SetText(sb.ToString());
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to copy to clipboard: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
