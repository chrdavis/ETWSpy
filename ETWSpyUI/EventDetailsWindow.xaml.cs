using System.Collections.Generic;
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

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
