using System.Threading;
using System.Windows;

namespace MiviaDesktop
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private readonly MainWindow _mainWindow;
        private static Mutex _mutex = null; 
        public App()
        {
            _mainWindow = new MainWindow();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            const string appName = "MyAppName";

            _mutex = new Mutex(true, appName, out var createdNew);
            
            if (!createdNew)
            {
                MessageBox.Show("Instance already running");
                Application.Current.Shutdown();
            }

            _mainWindow.Show();
            base.OnStartup(e);
        }
        

        protected override void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);
        }
    }
}
