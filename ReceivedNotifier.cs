using System.Media;
using System.Windows;

namespace BlueDrop
{
    public sealed class ReceivedNotifier
    {
        private readonly TransferStore _store;
        private readonly AppSettings _settings;
        private ReceivedPopup? _popup;

        public ReceivedNotifier(TransferStore store, AppSettings settings)
        {
            _store = store;
            _settings = settings;
            _store.IncomingReceived += item => Application.Current.Dispatcher.BeginInvoke(() => Show(item));
        }

        private void Show(TransferItem item)
        {
            _popup?.Close();

            if (_settings.AutoCopy && ClipboardService.Write(item))
            {
                _store.MarkIncomingTaken();
            }

            if (QuietState.IsActive)
            {
                return;
            }

            if (_settings.SoundEnabled)
            {
                SystemSounds.Asterisk.Play();
            }

            if (!_settings.PopupEnabled)
            {
                return;
            }

            var lifetime = TimeSpan.FromSeconds(Math.Clamp(_settings.PopupSeconds, AppSettings.MinPopupSeconds, AppSettings.MaxPopupSeconds));
            var popup = new ReceivedPopup(_store, item, _settings.HidePopupContent, lifetime);
            popup.Closed += (_, _) =>
            {
                if (ReferenceEquals(_popup, popup))
                {
                    _popup = null;
                }
            };

            _popup = popup;
            popup.Show();
        }
    }
}
