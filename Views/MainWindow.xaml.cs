using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
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
        /// Renders the waveform as a smooth polyline on the canvas.
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
                Stroke = new SolidColorBrush(Color.FromArgb(40, 0, 229, 255)),
                StrokeThickness = 1
            };
            canvas.Children.Add(centerLine);

            // Draw waveform polyline
            var polyline = new Polyline
            {
                StrokeThickness = 1.5,
                StrokeLineJoin = PenLineJoin.Round,
                Stroke = new LinearGradientBrush(
                    Color.FromRgb(0, 229, 255),
                    Color.FromRgb(124, 77, 255),
                    0)
            };

            for (int i = 0; i < count; i++)
            {
                double x = i * stepX;
                double y = midY - (data[i] * midY * 0.9); // Scale to 90% of half-height
                polyline.Points.Add(new Point(x, y));
            }

            canvas.Children.Add(polyline);

            // Draw glow effect (thicker, semi-transparent copy behind)
            var glow = new Polyline
            {
                StrokeThickness = 4,
                StrokeLineJoin = PenLineJoin.Round,
                Opacity = 0.3,
                Stroke = new LinearGradientBrush(
                    Color.FromRgb(0, 229, 255),
                    Color.FromRgb(124, 77, 255),
                    0),
                Points = polyline.Points
            };
            canvas.Children.Insert(1, glow);
        }

        private void WaveformCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Force redraw on resize
            RenderWaveform(null, EventArgs.Empty);
        }
    }
}
