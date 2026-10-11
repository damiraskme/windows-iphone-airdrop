namespace BlueDrop
{
    public sealed class SettingsLauncher
    {
        private readonly Func<SettingsWindow> _createWindow;
        private SettingsWindow? _window;

        public SettingsLauncher(Func<SettingsWindow> createWindow)
        {
            _createWindow = createWindow;
        }

        public void Open()
        {
            if (_window is null)
            {
                _window = _createWindow();
                _window.Closed += (_, _) => _window = null;
                _window.Show();
            }

            _window.Activate();
        }
    }
}
