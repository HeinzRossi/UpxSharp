using System.Windows.Input;

namespace UpxSharp.Wpf.Commands
{
    // ─────────────────────────────────────────────────────────────────────────────
    // RelayCommand  (síncrono)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Implementação de <see cref="ICommand"/> para uso em ViewModels (MVVM).
    /// Encapsula um delegate de execução e outro de habilitação opcional.
    /// Compatível com qualquer binding WPF.
    /// </summary>
    public sealed class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        /// <inheritdoc/>
        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        /// <summary>
        /// Cria um <see cref="RelayCommand"/> com execute e canExecute opcionais.
        /// </summary>
        /// <param name="execute">Ação executada ao invocar o comando.</param>
        /// <param name="canExecute">Predicado que determina se o comando está habilitado.</param>
        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        /// <summary>Versão sem parâmetro para conveniência.</summary>
        public RelayCommand(Action execute, Func<bool>? canExecute = null)
            : this(_ => execute(), canExecute is null ? null : _ => canExecute()) { }

        /// <inheritdoc/>
        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

        /// <inheritdoc/>
        public void Execute(object? parameter) => _execute(parameter);

        /// <summary>
        /// Força a reavaliação de <see cref="CanExecute"/> em todos os bindings.
        /// Útil quando o estado muda por fatores externos ao WPF.
        /// </summary>
        public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
    }
}
