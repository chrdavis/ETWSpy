using Microsoft.Win32;

namespace ETWSpyUI
{
    /// <summary>
    /// Provides centralized registry settings access for the application.
    /// </summary>
    internal static class RegistrySettings
    {
        private const string RegistryKeyPath = @"SOFTWARE\ETWSpy";

        // Registry value names
        public const string DarkMode = "DarkMode";
        public const string UseSystemTheme = "UseSystemTheme";
        public const string ShowTimestampsInUTC = "ShowTimestampsInUTC";
        public const string MaxEventsToShow = "MaxEventsToShow";
        public const string Autoscroll = "Autoscroll";

        /// <summary>
        /// Loads a boolean setting from the Windows registry.
        /// </summary>
        public static bool LoadBool(string valueName, bool defaultValue = false)
        {
            return LoadInt(valueName, defaultValue ? 1 : 0) != 0;
        }

        /// <summary>
        /// Loads an integer setting from the Windows registry.
        /// </summary>
        public static int LoadInt(string valueName, int defaultValue = 0)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
                return key?.GetValue(valueName) is int value ? value : defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// Saves an integer setting to the Windows registry.
        /// </summary>
        public static void SaveInt(string valueName, int value)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
                key?.SetValue(valueName, value, RegistryValueKind.DWord);
            }
            catch
            {
                // Silently ignore registry write failures
            }
        }

        /// <summary>
        /// Saves a boolean setting to the Windows registry.
        /// </summary>
        public static void SaveBool(string valueName, bool value)
        {
            SaveInt(valueName, value ? 1 : 0);
        }
    }
}
