using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ClipboardPhonePc
{
    public partial class ReceivedPopup : Window
    {
        private const string ReceivedText = "Received";
        private const double EdgeMargin = 12;
        private const int ThumbnailPixels = 96;

        private readonly TransferStore? _store;
        private readonly TransferItem? _item;
        private readonly DispatcherTimer _timer;

        public ReceivedPopup(string title, TimeSpan lifetime)
        {
            InitializeComponent();

            TitleLabel.Text = title;
            BodyLabel.Visibility = Visibility.Collapsed;
            _timer = StartTimer(lifetime);
        }

        public ReceivedPopup(TransferStore store, TransferItem item, bool hideContent, TimeSpan lifetime)
        {
            InitializeComponent();

            _store = store;
            _item = item;

            if (hideContent)
            {
                TitleLabel.Text = ReceivedText;
                BodyLabel.Visibility = Visibility.Collapsed;
            }
            else if (item.Kind == TransferKind.Text)
            {
                TitleLabel.Text = ReceivedText;
                BodyLabel.Text = item.Preview.ReplaceLineEndings(" ");
            }
            else
            {
                TitleLabel.Text = $"{ReceivedText} \"{item.Name}\"";
                BodyLabel.Visibility = Visibility.Collapsed;
            }

            if (!hideContent && item.Kind == TransferKind.Image && LoadThumbnail(item.Data!) is { } thumbnail)
            {
                Thumbnail.Source = thumbnail;
                Thumbnail.Visibility = Visibility.Visible;
            }

            _timer = StartTimer(lifetime);
        }

        private DispatcherTimer StartTimer(TimeSpan lifetime)
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - Width - EdgeMargin;
            Top = workArea.Bottom - Height - EdgeMargin;

            var timer = new DispatcherTimer { Interval = lifetime };
            timer.Tick += (_, _) => Close();
            timer.Start();
            return timer;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            WindowInterop.AddExtendedStyle(this, WindowInterop.ToolWindowStyle | WindowInterop.NoActivateStyle);
        }

        protected override void OnClosed(EventArgs e)
        {
            _timer.Stop();
            base.OnClosed(e);
        }

        private void OnClick(object sender, MouseButtonEventArgs e)
        {
            if (_item is not null && ClipboardService.Write(_item))
            {
                _store?.MarkIncomingTaken();
            }

            Close();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private static BitmapSource? LoadThumbnail(byte[] data)
        {
            try
            {
                using var stream = new MemoryStream(data);

                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelHeight = ThumbnailPixels;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (Exception e) when (e is NotSupportedException or FileFormatException)
            {
                return null;
            }
        }
    }
}
