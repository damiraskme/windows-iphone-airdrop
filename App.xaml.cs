using System.Reflection;
using System.Windows;

namespace BlueDrop
{
    public partial class App : Application
    {
        public static string AppName { get; } = Assembly.GetExecutingAssembly().GetName().Name!;

        private Mutex? _mutex;
        private TrayIcon? _trayIcon;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _mutex = new Mutex(true, AppName, out var isFirstInstance);
            if (!isFirstInstance)
            {
                Shutdown();
                return;
            }

            _trayIcon = new TrayIcon(new MainWindow());
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _trayIcon?.Dispose();
            _mutex?.Dispose();
            base.OnExit(e);
        }
    }

}
