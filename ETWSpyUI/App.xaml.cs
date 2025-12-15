using System.Windows;

namespace ETWSpyUI
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private void App_Startup(object sender, StartupEventArgs e)
        {
            // 1) Apply theme synchronously first
            // Load theme from registry BEFORE the main window is created
            // This prevents the white flash on startup
            bool isDarkMode = RegistrySettings.LoadBool(RegistrySettings.DarkMode);
            if (isDarkMode)
            {
                SwitchTheme("Themes/DarkTheme.xaml");
            }

            // 2) Create window but DO NOT show yet
            var w = new MainWindow();

            // 3) Force one layout+render pass off-screen
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Left = -10000;
            w.Top = -10000;
            w.ShowInTaskbar = false;

            w.Show();                 // creates HWND + comp, but it's off-screen
            w.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
            w.Hide();

            // 4) Put it back and show for real
            w.Left = (SystemParameters.WorkArea.Width - w.Width) / 2;
            w.Top = (SystemParameters.WorkArea.Height - w.Height) / 2;
            w.ShowInTaskbar = true;
            w.Show();
            w.Activate();
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
