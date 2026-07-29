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
    public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
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
            Wpf.Ui.Appearance.ApplicationThemeManager.ApplySystemTheme();
            Wpf.Ui.Appearance.SystemThemeWatcher.Watch(this);

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

        protected override void OnStateChanged(EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                this.Hide();
            }
            base.OnStateChanged(e);
        }

        private static string PrefFile => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SyncWave", "close_pref.txt");
        private static string GetClosePreference() => System.IO.File.Exists(PrefFile) ? System.IO.File.ReadAllText(PrefFile) : "Ask";
        private static void SetClosePreference(string pref) => System.IO.File.WriteAllText(PrefFile, pref);

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj == null) yield break;
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(depObj, i);
                if (child != null && child is T t) yield return t;
                foreach (T childOfChild in FindVisualChildren<T>(child)) yield return childOfChild;
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        private FlyoutWindow _flyout;

        private void PositionFlyout()
        {
            if (GetCursorPos(out POINT pt))
            {
                var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice;
                Point logicalPos = transform.HasValue ? transform.Value.Transform(new Point(pt.X, pt.Y)) : new Point(pt.X, pt.Y);

                double w = _flyout.ActualWidth > 0 ? _flyout.ActualWidth : (double.IsNaN(_flyout.Width) ? 300 : _flyout.Width);
                double h = _flyout.ActualHeight > 0 ? _flyout.ActualHeight : 200; // Height is Auto (NaN) initially

                _flyout.Left = logicalPos.X - (w / 2);
                _flyout.Top = logicalPos.Y - h - 10;
            }
        }

        private void TrayIcon_TrayLeftMouseUp(object sender, RoutedEventArgs e)
        {
            if (_flyout == null)
            {
                _flyout = new FlyoutWindow((MainViewModel)this.DataContext);
                _flyout.WindowStartupLocation = WindowStartupLocation.Manual;
                _flyout.SizeChanged += (s, ev) => PositionFlyout();
            }
            
            _flyout.UpdateNoDevicesText();
            PositionFlyout();
            
            _flyout.Show();
            _flyout.Activate();
        }

        protected override async void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (App.IsShuttingDown)
            {
                base.OnClosing(e);
                return;
            }

            e.Cancel = true;

            string pref = GetClosePreference();
            if (pref == "Tray")
            {
                this.Hide();
                return;
            }
            else if (pref == "Exit")
            {
                App.IsShuttingDown = true;
                Application.Current.Shutdown();
                return;
            }

            var checkBox = new System.Windows.Controls.CheckBox { Content = "Don't ask me again", Margin = new Thickness(0, 12, 0, 0) };
            var panel = new System.Windows.Controls.StackPanel();
            panel.Children.Add(new System.Windows.Controls.TextBlock 
            { 
                Text = "Do you want to keep SyncWave running in the background (system tray) or exit completely?", 
                TextWrapping = TextWrapping.Wrap 
            });
            panel.Children.Add(checkBox);

            var msgBox = new Wpf.Ui.Controls.MessageBox
            {
                Title = "Closing SyncWave",
                Content = panel,
                PrimaryButtonText = "Keep in background",
                SecondaryButtonText = "Exit SyncWave",
                CloseButtonText = "HIDEME_CLOSE"
            };

            msgBox.Loaded += (s, ev) =>
            {
                var buttons = FindVisualChildren<System.Windows.Controls.Button>(msgBox).ToList();
                foreach (var btn in buttons)
                {
                    if (btn.Content?.ToString() == "Keep in background" || btn.Content?.ToString() == "Exit SyncWave")
                    {
                        btn.MaxWidth = double.PositiveInfinity;
                        
                        if (btn.Content.ToString() == "Keep in background")
                        {
                            btn.Margin = new Thickness(0, 0, 12, 0);
                        }

                        if (System.Windows.Media.VisualTreeHelper.GetParent(btn) is System.Windows.Controls.Grid parentGrid)
                        {
                            parentGrid.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                            var colIndex = System.Windows.Controls.Grid.GetColumn(btn);
                            if (colIndex >= 0 && colIndex < parentGrid.ColumnDefinitions.Count)
                            {
                                parentGrid.ColumnDefinitions[colIndex].Width = new GridLength(1, GridUnitType.Auto);
                            }
                        }
                    }

                    if (btn.Content?.ToString() == "HIDEME_CLOSE")
                    {
                        btn.Visibility = Visibility.Collapsed;
                        btn.Margin = new Thickness(0);
                        btn.Width = 0;

                        if (System.Windows.Media.VisualTreeHelper.GetParent(btn) is System.Windows.Controls.Grid parentGrid)
                        {
                            var colIndex = System.Windows.Controls.Grid.GetColumn(btn);
                            if (colIndex >= 0 && colIndex < parentGrid.ColumnDefinitions.Count)
                            {
                                parentGrid.ColumnDefinitions[colIndex].Width = new GridLength(0);
                            }
                        }
                    }
                }
            };

            var result = await msgBox.ShowDialogAsync();

            if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
            {
                if (checkBox.IsChecked == true) SetClosePreference("Tray");
                this.Hide();
            }
            else if (result == Wpf.Ui.Controls.MessageBoxResult.Secondary)
            {
                if (checkBox.IsChecked == true) SetClosePreference("Exit");
                App.IsShuttingDown = true;
                Application.Current.Shutdown();
            }
        }

        private void TrayIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e)
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
            this.Focus();
        }

        private void TrayIcon_Open_Click(object sender, RoutedEventArgs e)
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
            this.Focus();
        }

        private void TrayIcon_Exit_Click(object sender, RoutedEventArgs e)
        {
            App.IsShuttingDown = true;
            TrayIcon?.Dispose();
            Application.Current.Shutdown();
        }
    }
}

