using System.Windows;
using ETWSpyLib;

namespace ETWSpyUI
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// Gets the file path passed as a command-line argument, if any.
        /// </summary>
        public static string? StartupFilePath { get; private set; }

        private void App_Startup(object sender, StartupEventArgs e)
        {
            // Check for command-line arguments (file path to open)
            if (e.Args.Length > 0 && !string.IsNullOrWhiteSpace(e.Args[0]))
            {
                StartupFilePath = e.Args[0];
            }

            // Start pre-loading providers in the background immediately
            // This reduces delay when opening the provider configuration window
            ProviderManager.PreloadProvidersAsync();

            // Load theme from registry BEFORE the main window is created
            // This prevents the white flash on startup
            bool isDarkMode = RegistrySettings.LoadBool(RegistrySettings.DarkMode);
            if (isDarkMode)
            {
                SwitchTheme("Themes/DarkColors.xaml");
            }

            // Create and show the main window
            var mainWindow = new MainWindow();
            mainWindow.Show();
        }

        private void SwitchTheme(string colorsPath)
        {
            var colorsUri = new Uri(colorsPath, UriKind.Relative);
            var colorsDict = new ResourceDictionary { Source = colorsUri };
            
            var stylesUri = new Uri("Themes/BaseStyles.xaml", UriKind.Relative);
            var stylesDict = new ResourceDictionary { Source = stylesUri };
            
            Resources.MergedDictionaries.Clear();
            Resources.MergedDictionaries.Add(colorsDict);
            Resources.MergedDictionaries.Add(stylesDict);
        }
    }
}
