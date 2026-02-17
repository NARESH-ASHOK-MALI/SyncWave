using System.Windows;
using SyncWave.Utils;

namespace SyncWave
{
    /// <summary>
    /// Application entry point. Logs startup and handles unhandled exceptions.
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            Logger.Info("═══════════════════════════════════════");
            Logger.Info("SyncWave v1.0.0 starting up.");
            Logger.Info("═══════════════════════════════════════");

            // Global unhandled exception handler
            DispatcherUnhandledException += (sender, args) =>
            {
                Logger.Error("Unhandled exception", args.Exception);
                MessageBox.Show(
                    $"An unexpected error occurred:\n\n{args.Exception.Message}\n\nThe application will try to continue.",
                    "SyncWave — Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                args.Handled = true; // Prevent crash
            };
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Logger.Info("SyncWave shutting down.");
            base.OnExit(e);
        }
    }
}
