using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace ETWSpyUI
{
    /// <summary>
    /// Interaction logic for EventDetailsWindow.xaml
    /// </summary>
    public partial class EventDetailsWindow : Window
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_CAPTION_COLOR = 35;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

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
            Opacity = 0;
            _isDarkMode = isDarkMode;
            InitializeComponent();

            // Apply title bar theme immediately after window handle is available
            SourceInitialized += EventDetailsWindow_SourceInitialized;

            ContentRendered += (s, e) =>
            {
                Opacity = 1.0; // Ensure window is fully opaque after rendering
            };
        }

        private void EventDetailsWindow_SourceInitialized(object? sender, EventArgs e)
        {
            // Apply title bar theme as early as possible (when HWND is available but before window is shown)
            ApplyTitleBarTheme(_isDarkMode);
        }

        private void ApplyTitleBarTheme(bool isDarkMode)
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
            {
                return;
            }

            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            int darkMode = isDarkMode ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            {
                int titleBarColor = isDarkMode ? 0x001E1E1E : 0x00FFFFFF;
                DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref titleBarColor, sizeof(int));
            }
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
