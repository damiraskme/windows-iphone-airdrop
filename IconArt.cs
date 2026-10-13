using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClipboardPhonePc
{
    public static class IconArt
    {
        private const double ViewSize = 512;
        private const double Dpi = 96;
        private const double FrameThickness = 16;
        private const double FrameRadius = 104;
        private const double FramedScale = 0.78;
        private const double LineThickness = 30;
        private const double TrayLineThickness = 56;
        private const double PhoneRadius = 30;
        private const double ButtonRadius = 16;
        private const double BadgeRadius = 124;
        private const double MarkThickness = 34;
        private const double MarkTop = -62;
        private const double MarkBottom = 6;
        private const double MarkDotOffset = 64;
        private const double MarkDotRadius = 20;

        private static readonly Rect Frame = new(8, 8, 496, 496);
        private static readonly Rect Phone = new(94, 76, 160, 360);
        private static readonly Point Button = new(174, 388);
        private static readonly Point PendingCenter = new(388, 124);
        private static readonly Point ProblemCenter = new(388, 388);
        private static readonly Color PendingColor = Color.FromRgb(232, 17, 35);
        private static readonly Color ProblemColor = Color.FromRgb(255, 200, 0);

        private static readonly (Point Start, Point End)[] Rays =
        [
            (new Point(303, 222), new Point(377, 170)),
            (new Point(314, 256), new Point(404, 256)),
            (new Point(303, 290), new Point(377, 342))
        ];

        public static BitmapSource Render(int size, Color color, bool pending, bool problem, bool framed = false)
        {
            var brush = new SolidColorBrush(color);
            var line = new Pen(brush, framed ? LineThickness : TrayLineThickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };

            var visual = new DrawingVisual();

            using (var context = visual.RenderOpen())
            {
                context.PushTransform(new ScaleTransform(size / ViewSize, size / ViewSize));

                if (framed)
                {
                    context.DrawRoundedRectangle(Brushes.White, new Pen(brush, FrameThickness), Frame, FrameRadius, FrameRadius);
                    context.PushTransform(new ScaleTransform(FramedScale, FramedScale, ViewSize / 2, ViewSize / 2));
                }

                context.DrawRoundedRectangle(null, line, Phone, PhoneRadius, PhoneRadius);
                context.DrawEllipse(brush, null, Button, ButtonRadius, ButtonRadius);

                foreach (var (start, end) in Rays)
                {
                    context.DrawLine(line, start, end);
                }

                if (framed)
                {
                    context.Pop();
                }

                if (pending)
                {
                    DrawBadge(context, PendingCenter, PendingColor, Colors.White);
                }

                if (problem)
                {
                    DrawBadge(context, ProblemCenter, ProblemColor, Colors.Black);
                }

                context.Pop();
            }

            var bitmap = new RenderTargetBitmap(size, size, Dpi, Dpi, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        private static void DrawBadge(DrawingContext context, Point center, Color background, Color mark)
        {
            var markBrush = new SolidColorBrush(mark);
            var markPen = new Pen(markBrush, MarkThickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };

            context.DrawEllipse(new SolidColorBrush(background), null, center, BadgeRadius, BadgeRadius);
            context.DrawLine(markPen, new Point(center.X, center.Y + MarkTop), new Point(center.X, center.Y + MarkBottom));
            context.DrawEllipse(markBrush, null, new Point(center.X, center.Y + MarkDotOffset), MarkDotRadius, MarkDotRadius);
        }
    }
}
