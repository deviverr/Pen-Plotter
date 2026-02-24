using System;
using System.Threading.Tasks;
using System.Windows.Input; // For ICommand

namespace PlotterControl.ViewModels
{
    public class AsyncRelayCommand : ICommand
    {
        private readonly Func<Task> _execute;
        private readonly Func<bool> _canExecute;
        private bool _isExecuting;

        public event EventHandler CanExecuteChanged;

        public AsyncRelayCommand(Func<Task> execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter)
        {
            return !_isExecuting && (_canExecute?.Invoke() ?? true);
        }

        public async void Execute(object parameter)
        {
            if (CanExecute(parameter))
            {
                try
                {
                    _isExecuting = true;
                    RaiseCanExecuteChanged();
                    await _execute();
                }
                finally
                {
                    _isExecuting = false;
                    RaiseCanExecuteChanged();
                }
            }
        }

        public void RaiseCanExecuteChanged()
        {
            // Dispatch CanExecuteChanged to UI thread — WPF ignores it from background threads
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(() =>
                {
                    CanExecuteChanged?.Invoke(this, EventArgs.Empty);
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                });
            }
            else
            {
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
        }
    }
    
    public class AsyncRelayCommand<T> : ICommand
    {
        private readonly Func<T, Task> _execute;
        private readonly Func<T, bool> _canExecute;
        private bool _isExecuting;

        public event EventHandler CanExecuteChanged;

        public AsyncRelayCommand(Func<T, Task> execute, Func<T, bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter)
        {
            return !_isExecuting && (_canExecute?.Invoke((T)parameter) ?? true);
        }

        public async void Execute(object parameter)
        {
            if (CanExecute(parameter))
            {
                try
                {
                    _isExecuting = true;
                    RaiseCanExecuteChanged();
                    await _execute((T)parameter);
                }
                finally
                {
                    _isExecuting = false;
                    RaiseCanExecuteChanged();
                }
            }
        }

        public void RaiseCanExecuteChanged()
        {
            // Dispatch CanExecuteChanged to UI thread — WPF ignores it from background threads
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(() =>
                {
                    CanExecuteChanged?.Invoke(this, EventArgs.Empty);
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                });
            }
            else
            {
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
        }
    }
}
