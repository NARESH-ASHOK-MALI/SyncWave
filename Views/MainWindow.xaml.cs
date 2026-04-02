using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using SyncWave.ViewModels;

namespace SyncWave.Views
{
    /// <summary>
    /// Minimal code-behind — handles waveform canvas rendering + custom title bar.
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly DispatcherTimer _waveformTimer;
        private MainViewModel? _vm;

        // Cached brushes — muted monochrome palette for OLED theme
        private static readonly Brush CenterLineBrush = new SolidColorBrush(Color.FromArgb(20, 180, 180, 180));
        private static readonly Brush WaveformBrush = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0));
        private static readonly Brush GlowBrush = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));

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

        // ── Custom Title Bar Handlers ─────────────────────────────

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                // Double-click toggles maximize
                ToggleMaximize();
            }
            else
            {
                DragMove();
            }
        }

        private void MinimizeBtn_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private void MaximizeBtn_Click(object sender, RoutedEventArgs e)
            => ToggleMaximize();

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
            => Close();

        private void ToggleMaximize()
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        // ── Waveform Rendering ────────────────────────────────────

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

            // Draw subtle glow (wide, blurred, very low opacity)
            var outerGlow = new Polyline
            {
                StrokeThickness = 6,
                StrokeLineJoin = PenLineJoin.Round,
                Opacity = 0.08,
                Stroke = GlowBrush,
                Points = points,
                Effect = new BlurEffect { Radius = 6 }
            };
            canvas.Children.Add(outerGlow);

            // Draw main waveform polyline
            var polyline = new Polyline
            {
                StrokeThickness = 1.5,
                StrokeLineJoin = PenLineJoin.Round,
                Stroke = WaveformBrush,
                Opacity = 0.7,
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

