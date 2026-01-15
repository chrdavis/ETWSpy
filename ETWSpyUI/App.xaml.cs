using System.Windows;
using ETWSpyLib;

namespace ETWSpyUI
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private void App_Startup(object sender, StartupEventArgs e)
        {
            // Start pre-loading providers in the background immediately
            // This reduces delay when opening the provider configuration window
            ProviderManager.PreloadProvidersAsync();

            // Load theme from registry BEFORE the main window is created
            // This prevents the white flash on startup
            bool isDarkMode = RegistrySettings.LoadBool(RegistrySettings.DarkMode);
            if (isDarkMode)
            {
                SwitchTheme("Themes/DarkTheme.xaml");
            }

            // Create and show the main window using flash-free display
            var mainWindow = new MainWindow();
            WindowHelper.ShowWithoutFlash(mainWindow, centerOnScreen: true);
        }

        private void SwitchTheme(string themePath)
        {
            var uri = new Uri(themePath, UriKind.Relative);
            var resourceDict = new ResourceDictionary { Source = uri };
            Resources.MergedDictionaries.Clear();
            Resources.MergedDictionaries.Add(resourceDict);
        }
    }
}
