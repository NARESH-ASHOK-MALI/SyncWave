using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using SyncWave.ViewModels;

namespace SyncWave.Views
{
    /// <summary>
    /// Minimal code-behind — handles waveform canvas rendering
    /// which requires direct access to UIElement tree.
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly DispatcherTimer _waveformTimer;
        private MainViewModel? _vm;

        // Cached brushes for performance (avoid creating per-frame)
        private static readonly Brush CenterLineBrush = new SolidColorBrush(Color.FromArgb(30, 0, 229, 255));
        private static readonly Brush WaveformBrush = new LinearGradientBrush(
            Color.FromRgb(0, 229, 255),
            Color.FromRgb(124, 77, 255), 0);
        private static readonly Brush GlowBrush = new LinearGradientBrush(
            Color.FromRgb(0, 229, 255),
            Color.FromRgb(124, 77, 255), 0);

        static MainWindow()
        {
            CenterLineBrush.Freeze();
            WaveformBrush.Freeze();
            GlowBrush.Freeze();
        }

        public MainWindow()
        {
            InitializeComponent();

            // Waveform render timer at ~30 FPS
            _waveformTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(33)
            };
            _waveformTimer.Tick += RenderWaveform;
            _waveformTimer.Start();

            Loaded += (_, _) => _vm = DataContext as MainViewModel;

            Closed += (_, _) =>
            {
                _waveformTimer.Stop();
                (_vm as IDisposable)?.Dispose();
            };
        }

        /// <summary>
        /// Renders the waveform as a smooth polyline on the canvas with glow/bloom effect.
        /// </summary>
        private void RenderWaveform(object? sender, EventArgs e)
        {
            if (_vm?.WaveformData == null) return;

            var canvas = WaveformCanvas;
            double w = canvas.ActualWidth;
            double h = canvas.ActualHeight;
            if (w <= 0 || h <= 0) return;

            canvas.Children.Clear();

            var data = _vm.WaveformData;
            int count = data.Length;
            if (count == 0) return;

            double midY = h / 2;
            double stepX = w / count;

            // Draw center line
            var centerLine = new Line
            {
                X1 = 0, Y1 = midY,
                X2 = w, Y2 = midY,
                Stroke = CenterLineBrush,
                StrokeThickness = 1
            };
            canvas.Children.Add(centerLine);

            // Build shared point collection
            var points = new PointCollection(count);
            for (int i = 0; i < count; i++)
            {
                double x = i * stepX;
                double y = midY - (data[i] * midY * 0.9);
                points.Add(new Point(x, y));
            }

            // Draw outer glow (wide, blurred, low opacity)
            var outerGlow = new Polyline
            {
                StrokeThickness = 8,
                StrokeLineJoin = PenLineJoin.Round,
                Opacity = 0.12,
                Stroke = GlowBrush,
                Points = points,
                Effect = new BlurEffect { Radius = 8 }
            };
            canvas.Children.Add(outerGlow);

            // Draw inner glow (medium width, semi-transparent)
            var innerGlow = new Polyline
            {
                StrokeThickness = 4,
                StrokeLineJoin = PenLineJoin.Round,
                Opacity = 0.3,
                Stroke = GlowBrush,
                Points = points
            };
            canvas.Children.Add(innerGlow);

            // Draw main waveform polyline (sharp, on top)
            var polyline = new Polyline
            {
                StrokeThickness = 1.8,
                StrokeLineJoin = PenLineJoin.Round,
                Stroke = WaveformBrush,
                Points = points
            };
            canvas.Children.Add(polyline);
        }

        private void WaveformCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Force redraw on resize
            RenderWaveform(null, EventArgs.Empty);
        }
    }
}
