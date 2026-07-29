using System.Windows;
using SyncWave.Utils;

namespace SyncWave
{
    /// <summary>
    /// Application entry point. Logs startup and handles unhandled exceptions.
    /// </summary>
    public partial class App : Application
    {
        public static bool IsShuttingDown = false;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            Logger.Info("═══════════════════════════════════════");
            Logger.Info("SyncWave v1.0.0 starting up.");
            Logger.Info("═══════════════════════════════════════");

            // Global unhandled exception handler
            DispatcherUnhandledException += (sender, args) =>
            {
                Logger.Error("Unhandled Dispatcher exception", args.Exception);
                MessageBox.Show(
                    $"An unexpected error occurred:\n\n{args.Exception.Message}\n\nThe application will try to continue.",
                    "SyncWave — Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                args.Handled = true; // Prevent crash
            };

            System.AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                var ex = args.ExceptionObject as System.Exception;
                Logger.Error("Unhandled AppDomain exception", ex);
                MessageBox.Show(
                    $"A fatal error occurred:\n\n{ex?.Message}\n\nThe application will terminate.",
                    "SyncWave — Fatal Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            };
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Logger.Info("SyncWave shutting down.");
            base.OnExit(e);
        }
    }
}
