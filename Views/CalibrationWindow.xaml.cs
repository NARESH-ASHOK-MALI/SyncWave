using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SyncWave.Core;
using SyncWave.Models;
using SyncWave.Utils;

namespace SyncWave.Views
{
    /// <summary>
    /// Code-behind for CalibrationWindow.
    /// Manages the calibration UI flow and delegates audio work to CalibrationSession.
    /// </summary>
    public partial class CalibrationWindow : Wpf.Ui.Controls.FluentWindow
    {
        private readonly CalibrationSession _session = new();
        private readonly List<AudioDeviceModel> _allDevices;

        // Melody intervals matching SyncMelodyGenerator (ms to NEXT note)
        private static readonly int[] NoteIntervals = { 600, 600, 600, 1200 };

        // Cached brushes for alignment preview
        private static readonly Brush RefTickBrush = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0));
        private static readonly Brush TargetTickBrush = new SolidColorBrush(Color.FromRgb(0x5A, 0x9A, 0x5A));
        private static readonly Brush MisalignedTickBrush = new SolidColorBrush(Color.FromRgb(0xB0, 0x50, 0x50));

        static CalibrationWindow()
        {
            RefTickBrush.Freeze();
            TargetTickBrush.Freeze();
            MisalignedTickBrush.Freeze();
        }

        public CalibrationWindow(List<AudioDeviceModel> devices)
        {
            InitializeComponent();
            Wpf.Ui.Appearance.ApplicationThemeManager.ApplySystemTheme();

            _allDevices = devices;

            // Auto-select anchor device based on highest estimated latency
            var sortedDevices = devices
                .Where(d => !d.IsDefaultDevice)
                .OrderByDescending(d => CodecLatencyEstimator.Estimate(d.DeviceType).EstimatedDelayMs)
                .ToList();

            AnchorDeviceCombo.ItemsSource = sortedDevices;
            if (sortedDevices.Count > 0)
                AnchorDeviceCombo.SelectedIndex = 0;

            UpdateDeviceQueue();
            UpdateStatus("Anchor auto-selected. Click Start Calibration to begin.");

            Closed += (_, _) =>
            {
                _session.StopPlayback();
                _session.Dispose();
            };
        }

        private void ChangeAnchorButton_Click(object sender, RoutedEventArgs e)
        {
            AnchorDeviceCombo.Visibility = AnchorDeviceCombo.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        }

        private void AnchorDeviceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateDeviceQueue();
        }

        private void UpdateDeviceQueue()
        {
            var anchorDevice = AnchorDeviceCombo.SelectedItem as AudioDeviceModel;
            if (anchorDevice == null) return;

            var queue = _allDevices
                .Where(d => !d.IsDefaultDevice && d.DeviceId != anchorDevice.DeviceId)
                .ToList();

            DeviceQueueList.ItemsSource = queue;
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            var anchorDevice = AnchorDeviceCombo.SelectedItem as AudioDeviceModel;
            if (anchorDevice == null)
            {
                UpdateStatus("⚠ Please select an anchor device first.");
                return;
            }

            var devicesToCalibrate = _allDevices
                .Where(d => !d.IsDefaultDevice && d.DeviceId != anchorDevice.DeviceId)
                .ToList();

            if (devicesToCalibrate.Count == 0)
            {
                UpdateStatus("⚠ No devices available to calibrate.");
                return;
            }

            _session.Initialize(anchorDevice, devicesToCalibrate);

            if (_session.AdvanceToNext())
            {
                ShowCalibrationPanel();
            }
            else
            {
                UpdateStatus("No devices to calibrate.");
            }
        }

        private void ShowCalibrationPanel()
        {
            CalibrationPanel.Visibility = Visibility.Visible;
            StartButton.Visibility = Visibility.Collapsed;

            var device = _session.CurrentDevice;
            if (device == null) return;

            CurrentDeviceName.Text = device.FriendlyName;
            DeviceCountText.Text = $"{_session.CurrentDeviceNumber} / {_session.TotalDevices}";

            // Set slider to codec estimate
            var estimate = _session.CurrentEstimate;
            if (estimate != null)
            {
                DelaySlider.Value = estimate.EstimatedDelayMs;
                EstimateText.Text = $"Starting estimate: {estimate.Description}";
                EstimateBadge.Visibility = Visibility.Visible;
            }
            else
            {
                DelaySlider.Value = 0;
                EstimateBadge.Visibility = Visibility.Collapsed;
            }

            UpdateDelayDisplay();
            RenderAlignmentPreview();
            UpdateReAnchorPrompt();
            UpdateStatus($"Adjust the delay slider until the melody notes from both devices align.");
        }

        private void DelaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_session != null)
            {
                _session.CurrentDelay = DelaySlider.Value;
            }
            UpdateDelayDisplay();
            RenderAlignmentPreview();
            UpdateReAnchorPrompt();
        }

        private void UpdateReAnchorPrompt()
        {
            if (ReAnchorPrompt != null)
            {
                ReAnchorPrompt.Visibility = DelaySlider.Value == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void ReAnchorButton_Click(object sender, RoutedEventArgs e)
        {
            var currentDevice = _session.CurrentDevice;
            if (currentDevice != null)
            {
                _session.ReAnchorToCurrentDevice();
                
                // Update combo selection to match
                AnchorDeviceCombo.SelectedItem = currentDevice;
                
                // Refresh queue UI
                UpdateDeviceQueue();
                
                // Restart calibration sequence from the beginning
                var devicesToCalibrate = _allDevices
                    .Where(d => !d.IsDefaultDevice && d.DeviceId != currentDevice.DeviceId)
                    .ToList();
                _session.Initialize(currentDevice, devicesToCalibrate);
                
                if (_session.AdvanceToNext())
                {
                    ShowCalibrationPanel();
                }
            }
        }

        private void UpdateDelayDisplay()
        {
            if (DelayValueText != null)
            {
                DelayValueText.Text = $"{DelaySlider.Value:F0}";
            }
        }

        private void PlayStopButton_Click(object sender, RoutedEventArgs e)
        {
            if (_session.IsPlaying)
            {
                _session.StopPlayback();
                PlayStopButton.Content = "▶ Play Melody";
                PlayStopButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
            }
            else
            {
                _session.StartPlayback();
                if (_session.IsPlaying)
                {
                    PlayStopButton.Content = "■ Stop";
                    PlayStopButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Danger;
                }
            }
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            _session.ConfirmCurrentDevice();

            // Update the queue display
            DeviceQueueList.ItemsSource = null;
            DeviceQueueList.ItemsSource = _session.DeviceQueue;

            // Advance to next
            if (_session.AdvanceToNext())
            {
                ShowCalibrationPanel();
            }
            else
            {
                CalibrationPanel.Visibility = Visibility.Collapsed;
                StartButton.Visibility = Visibility.Visible;
                StartButton.Content = "Recalibrate";
                UpdateStatus("✓ All devices calibrated! You can close this window or recalibrate.");
            }
        }

        private void SkipButton_Click(object sender, RoutedEventArgs e)
        {
            _session.SkipCurrentDevice();

            if (_session.AdvanceToNext())
            {
                ShowCalibrationPanel();
            }
            else
            {
                CalibrationPanel.Visibility = Visibility.Collapsed;
                StartButton.Visibility = Visibility.Visible;
                StartButton.Content = "Recalibrate";
                UpdateStatus("Calibration complete. Skipped devices keep their previous delay values.");
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _session.StopPlayback();
            Close();
        }

        private void UpdateStatus(string text)
        {
            if (StatusText != null)
                StatusText.Text = text;
        }

        /// <summary>
        /// Renders the alignment preview: two rows of tick marks showing where
        /// notes fire for the anchor device vs. the target device.
        /// The target row is shifted by the current delay value.
        /// When aligned, ticks overlap — when misaligned, they visually diverge.
        /// </summary>
        private void RenderAlignmentPreview()
        {
            if (AnchorTickCanvas == null || TargetTickCanvas == null) return;

            double canvasWidth = AnchorTickCanvas.ActualWidth;
            if (canvasWidth <= 0) canvasWidth = 400; // fallback before layout

            AnchorTickCanvas.Children.Clear();
            TargetTickCanvas.Children.Clear();

            // Calculate total pattern duration
            int totalMs = 0;
            foreach (var ms in NoteIntervals) totalMs += ms;

            // We'll show 2 full loops worth of pattern
            double totalDisplayMs = totalMs * 2;
            double pixelsPerMs = canvasWidth / totalDisplayMs;

            // Draw anchor ticks (at their natural positions)
            DrawTicks(AnchorTickCanvas, 0, totalMs, pixelsPerMs, totalDisplayMs, RefTickBrush);

            // Draw target ticks (shifted by current delay)
            double delay = DelaySlider?.Value ?? 0;
            DrawTicks(TargetTickCanvas, delay, totalMs, pixelsPerMs, totalDisplayMs, TargetTickBrush);
        }

        private void DrawTicks(Canvas canvas, double offsetMs, int loopMs, double pxPerMs, double totalMs, Brush brush)
        {
            // Draw ticks for 2 loops
            for (int loop = 0; loop < 2; loop++)
            {
                int posMs = loop * loopMs;
                foreach (var interval in NoteIntervals)
                {
                    double tickMs = posMs + offsetMs;
                    double x = tickMs * pxPerMs;

                    if (x >= 0 && x < canvas.ActualWidth)
                    {
                        var tick = new Rectangle
                        {
                            Width = 3,
                            Height = 14,
                            Fill = brush,
                            RadiusX = 1,
                            RadiusY = 1,
                            Opacity = 0.9
                        };
                        Canvas.SetLeft(tick, x);
                        Canvas.SetTop(tick, 1);
                        canvas.Children.Add(tick);
                    }

                    posMs += interval;
                }
            }
        }

        // Redraw alignment preview when canvases resize
        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            RenderAlignmentPreview();
        }
    }
}
