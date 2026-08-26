using System.Windows.Input;

namespace UpxSharp.Wpf.Commands
{
    public sealed class AsyncRelayCommand : ICommand
    {
        private readonly Func<CancellationToken, Task> _execute;
        private readonly Func<bool>?                   _canExecute;
        private CancellationTokenSource? _cts;
        private bool _isExecuting;

        public event EventHandler? CanExecuteChanged;

        public AsyncRelayCommand(Func<CancellationToken, Task> execute, Func<bool>? canExecute = null)
        {
            _execute    = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object? _) => !_isExecuting && (_canExecute?.Invoke() ?? true);

        public async void Execute(object? _)
        {
            if (!CanExecute(null)) return;
            _cts         = new CancellationTokenSource();
            _isExecuting = true;
            RaiseCanExecuteChanged();
            try     { await _execute(_cts.Token); }
            finally
            {
                _isExecuting = false;
                _cts.Dispose();
                _cts = null;
                RaiseCanExecuteChanged();
            }
        }

        public void Cancel() => _cts?.Cancel();
        private void RaiseCanExecuteChanged()
            => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}