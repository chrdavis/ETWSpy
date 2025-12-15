using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ETWSpyUI
{
    /// <summary>
    /// Helper class for window operations.
    /// </summary>
    public static class WindowHelper
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_CAPTION_COLOR = 35;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        /// <summary>
        /// Shows a window without the white flash that can occur in dark mode.
        /// This works by performing the first layout and render pass off-screen,
        /// then moving the window to its final position before showing it.
        /// </summary>
        /// <param name="window">The window to show.</param>
        /// <param name="centerOnScreen">If true, centers the window on screen. If false, centers on owner if available, otherwise centers on screen.</param>
        public static void ShowWithoutFlash(Window window, bool centerOnScreen = true)
        {
            window.Opacity = 0;
            window.ContentRendered += (_, _) =>
            {
                window.Opacity = 1;
            };
            // Save original settings
            var originalShowInTaskbar = window.ShowInTaskbar;

            // Force one layout+render pass off-screen
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -10000;
            window.Top = -10000;
            window.ShowInTaskbar = false;

            // Show creates HWND + composition, but it's off-screen
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
            window.Hide();

            // Restore taskbar visibility
            window.ShowInTaskbar = originalShowInTaskbar;

            // Position the window
            if (centerOnScreen || window.Owner == null)
            {
                // Center on screen
                window.Left = (SystemParameters.WorkArea.Width - window.Width) / 2;
                window.Top = (SystemParameters.WorkArea.Height - window.Height) / 2;
            }
            else
            {
                // Center on owner
                window.Left = window.Owner.Left + (window.Owner.Width - window.Width) / 2;
                window.Top = window.Owner.Top + (window.Owner.Height - window.Height) / 2;
            }

            // Show for real
            window.Show();
            window.Activate();
        }

        /// <summary>
        /// Applies the dark or light mode theme to the window's title bar.
        /// This uses Windows DWM APIs to customize the title bar appearance.
        /// </summary>
        /// <param name="window">The window to apply the theme to.</param>
        /// <param name="isDarkMode">True for dark mode, false for light mode.</param>
        public static void ApplyTitleBarTheme(Window window, bool isDarkMode)
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
            {
                return;
            }

            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            int darkMode = isDarkMode ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            {
                int titleBarColor = isDarkMode ? 0x001E1E1E : 0x00FFFFFF;
                DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref titleBarColor, sizeof(int));
            }
        }
    }
}
