using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace ClipboardPhonePc
{
    public sealed class PairPrompt : Window
    {
        private const string AllowText = "Yes";
        private const string DenyText = "No ({0})";
        private const double ContentWidth = 300;

        private readonly TextBlock _denyLabel = new() { HorizontalAlignment = HorizontalAlignment.Center };
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
        private int _secondsLeft;

        public PairPrompt(string question, TimeSpan timeout)
        {
            _secondsLeft = (int)timeout.TotalSeconds;

            Title = App.AppName;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Topmost = true;
            Background = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20));

            var allow = CreateButton(new TextBlock { Text = AllowText, HorizontalAlignment = HorizontalAlignment.Center }, new Thickness(0, 0, 4, 0));
            allow.Click += (_, _) => DialogResult = true;

            var deny = CreateButton(_denyLabel, new Thickness(4, 0, 0, 0));
            deny.IsCancel = true;
            deny.Click += (_, _) => DialogResult = false;

            var buttons = new UniformGrid { Columns = 2, Margin = new Thickness(0, 16, 0, 0) };
            buttons.Children.Add(allow);
            buttons.Children.Add(deny);

            var panel = new StackPanel { Margin = new Thickness(16), Width = ContentWidth };
            panel.Children.Add(new TextBlock { Text = question, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(buttons);
            Content = panel;

            _timer.Tick += OnTick;
            UpdateCountdown();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            WindowInterop.UseDarkTitleBar(this);
            _timer.Start();
        }

        protected override void OnClosed(EventArgs e)
        {
            _timer.Stop();
            base.OnClosed(e);
        }

        private Button CreateButton(TextBlock label, Thickness margin)
        {
            return new Button
            {
                Style = (Style)FindResource("TileButton"),
                Padding = new Thickness(0, 8, 0, 8),
                Margin = margin,
                Content = label
            };
        }

        private void OnTick(object? sender, EventArgs e)
        {
            _secondsLeft--;

            if (_secondsLeft <= 0)
            {
                DialogResult = false;
                return;
            }

            UpdateCountdown();
        }

        private void UpdateCountdown()
        {
            _denyLabel.Text = string.Format(DenyText, _secondsLeft);
        }
    }
}
