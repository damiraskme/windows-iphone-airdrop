using System.Media;
using System.Windows;

namespace ClipboardPhonePc
{
    public sealed class ReceivedNotifier
    {
        private const string PairedText = "Paired with \"{0}\"";
        private const string PairedUnnamedText = "New device paired";
        private const string ConnectedText = "\"{0}\" is connected";
        private const string ConnectedUnnamedText = "New device is connected";

        private readonly TransferStore _store;
        private readonly AppSettings _settings;
        private ReceivedPopup? _popup;

        public ReceivedNotifier(TransferStore store, AppSettings settings, PairingService pairing)
        {
            _store = store;
            _settings = settings;
            pairing.Paired += device => Application.Current.Dispatcher.BeginInvoke(() => ShowPaired(device));
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

            Show(new ReceivedPopup(_store, item, _settings.HidePopupContent, Lifetime));
        }

        public void ShowMessage(string message)
        {
            _popup?.Close();
            Show(new ReceivedPopup(message, Lifetime));
        }

        public void ShowConnected(PairedDevice device)
        {
            ShowMessage(device.Name.Length == 0 ? ConnectedUnnamedText : string.Format(ConnectedText, device.Name));
        }

        private void ShowPaired(PairedDevice device)
        {
            ShowMessage(device.Name.Length == 0 ? PairedUnnamedText : string.Format(PairedText, device.Name));
        }

        private TimeSpan Lifetime => TimeSpan.FromSeconds(Math.Clamp(_settings.PopupSeconds, AppSettings.MinPopupSeconds, AppSettings.MaxPopupSeconds));

        private void Show(ReceivedPopup popup)
        {
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
