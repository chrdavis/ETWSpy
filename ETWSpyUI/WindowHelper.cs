using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

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
