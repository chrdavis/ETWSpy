using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Security;

namespace ETWSpyUI
{
    /// <summary>
    /// Helper class to register and unregister file associations for .etwconfig files.
    /// </summary>
    public static class FileAssociationHelper
    {
        private const string FileExtension = ".etwconfig";
        private const string ProgId = "ETWSpy.Configuration";
        private const string FileTypeDescription = "ETWSpy Configuration File";

        /// <summary>
        /// Checks if the .etwconfig file association is registered for this application.
        /// </summary>
        public static bool IsFileAssociationRegistered()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{FileExtension}");
                if (key == null)
                {
                    return false;
                }

                var progId = key.GetValue(null) as string;
                if (progId != ProgId)
                {
                    return false;
                }

                // Check if the ProgId exists and points to current executable
                using var progIdKey = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ProgId}\shell\open\command");
                if (progIdKey == null)
                {
                    return false;
                }

                var command = progIdKey.GetValue(null) as string;
                var currentExe = Process.GetCurrentProcess().MainModule?.FileName;
                
                return command != null && currentExe != null && command.Contains(currentExe, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Registers the .etwconfig file association with the current application.
        /// Uses HKEY_CURRENT_USER so no admin rights are required.
        /// </summary>
        public static bool RegisterFileAssociation()
        {
            try
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath))
                {
                    return false;
                }

                // Register the file extension
                using (var extKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{FileExtension}"))
                {
                    extKey?.SetValue(null, ProgId);
                }

                // Register the ProgId
                using (var progIdKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
                {
                    progIdKey?.SetValue(null, FileTypeDescription);

                    // Set the default icon (use the executable's icon)
                    using (var iconKey = progIdKey?.CreateSubKey("DefaultIcon"))
                    {
                        iconKey?.SetValue(null, $"\"{exePath}\",0");
                    }

                    // Set the open command
                    using (var commandKey = progIdKey?.CreateSubKey(@"shell\open\command"))
                    {
                        commandKey?.SetValue(null, $"\"{exePath}\" \"%1\"");
                    }
                }

                // Notify the shell that file associations have changed
                NativeMethods.SHChangeNotify(NativeMethods.SHCNE_ASSOCCHANGED, NativeMethods.SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);

                return true;
            }
            catch (SecurityException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Unregisters the .etwconfig file association.
        /// </summary>
        public static bool UnregisterFileAssociation()
        {
            try
            {
                // Delete the file extension key
                Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{FileExtension}", throwOnMissingSubKey: false);

                // Delete the ProgId key
                Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{ProgId}", throwOnMissingSubKey: false);

                // Notify the shell that file associations have changed
                NativeMethods.SHChangeNotify(NativeMethods.SHCNE_ASSOCCHANGED, NativeMethods.SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static class NativeMethods
        {
            public const int SHCNE_ASSOCCHANGED = 0x08000000;
            public const int SHCNF_IDLIST = 0x0000;

            [System.Runtime.InteropServices.DllImport("shell32.dll")]
            public static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);
        }
    }
}
