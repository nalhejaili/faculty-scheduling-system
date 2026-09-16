
using System;
using System.Windows.Input;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed class RelayCommand : ICommand
    {
        private readonly Action<object?> _exec;
        private readonly Predicate<object?>? _can;
        public RelayCommand(Action<object?> exec, Predicate<object?>? can = null){ _exec = exec; _can = can; }
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? p) => _can?.Invoke(p) ?? true;
        public void Execute(object? p) => _exec(p);
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
