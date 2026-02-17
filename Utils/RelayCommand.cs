using System;
using System.Windows.Input;

namespace SyncWave.Utils
{
    /// <summary>
    /// Robust ICommand relay implementation for MVVM data binding.
    /// Uses its own CanExecuteChanged event (not CommandManager.RequerySuggested)
    /// for reliable command state updates.
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        // Track all instances so we can raise CanExecuteChanged globally
        private static readonly System.Collections.Generic.List<WeakReference<RelayCommand>> _instances = new();

        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
            _instances.Add(new WeakReference<RelayCommand>(this));
        }

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
            : this(_ => execute(), canExecute != null ? _ => canExecute() : null)
        {
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

        public void Execute(object? parameter) => _execute(parameter);

        /// <summary>
        /// Raises CanExecuteChanged on this specific command instance.
        /// </summary>
        public void NotifyCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Forces all RelayCommand instances to re-evaluate CanExecute.
        /// This is more reliable than CommandManager.InvalidateRequerySuggested().
        /// </summary>
        public static void RaiseCanExecuteChanged()
        {
            // Also poke WPF's CommandManager as backup
            CommandManager.InvalidateRequerySuggested();

            // Directly raise on all live instances
            for (int i = _instances.Count - 1; i >= 0; i--)
            {
                if (_instances[i].TryGetTarget(out var cmd))
                {
                    cmd.CanExecuteChanged?.Invoke(cmd, EventArgs.Empty);
                }
                else
                {
                    _instances.RemoveAt(i); // Clean up dead references
                }
            }
        }
    }
}
