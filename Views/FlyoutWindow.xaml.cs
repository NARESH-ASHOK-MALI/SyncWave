using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using SyncWave.ViewModels;

namespace SyncWave.Views
{
    public partial class FlyoutWindow : Wpf.Ui.Controls.FluentWindow
    {
        private DispatcherTimer _closeTimer;

        public FlyoutWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _closeTimer.Tick += (s, e) =>
            {
                _closeTimer.Stop();
                this.Hide();
            };

            this.Deactivated += (s, e) => _closeTimer.Start();
            this.MouseLeave += (s, e) => _closeTimer.Start();
            
            this.MouseEnter += (s, e) => _closeTimer.Stop();
            this.Activated += (s, e) => _closeTimer.Stop();

            // Setup NoDevicesText visibility updates
            UpdateNoDevicesText();
            viewModel.Devices.CollectionChanged += (s, e) => UpdateNoDevicesText();
            // Also need to listen to IsSelected changes on each device, 
            // but for a lightweight flyout, we can just update on open
        }

        public void UpdateNoDevicesText()
        {
            if (DataContext is MainViewModel vm)
            {
                bool hasSynced = vm.Devices.Any(d => d.IsSelected);
                NoDevicesText.Visibility = hasSynced ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            UpdateNoDevicesText(); // Refresh when opened
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (App.IsShuttingDown)
            {
                base.OnClosing(e);
                return;
            }

            e.Cancel = true;
            this.Hide();
            base.OnClosing(e);
        }
    }
}
