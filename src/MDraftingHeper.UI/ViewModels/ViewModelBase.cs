using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace MDraftingHeper.UI.ViewModels
{
    public abstract class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void Raise([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            Raise(name);
            return true;
        }

        protected static string Mm(double v, string format = "0.0") =>
            double.IsNaN(v) ? "—" : v.ToString(format, CultureInfo.InvariantCulture) + " mm";

        protected static string Mm2(double v) =>
            double.IsNaN(v) ? "—" : v.ToString("#,##0", CultureInfo.InvariantCulture) + " mm²";

        protected static bool TryParse(string text, out double value) =>
            double.TryParse((text ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
            !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public sealed class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute; _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter) => _canExecute == null || _canExecute();
        public void Execute(object parameter) => _execute();
    }

    /// <summary>What the dialog asks the CAD command to do after it closes.</summary>
    public enum DialogAction { None, Select, Commit, Close }

    /// <summary>
    /// A view model shown repeatedly by a command loop (dialog → model-space pick → dialog).
    /// <see cref="Action"/> is reset every time the dialog opens, so closing it with the window's
    /// X button (no command button) always reads as <see cref="DialogAction.None"/>, never as a stale
    /// Commit/Select left over from the previous round.
    /// </summary>
    public abstract class DialogViewModel : ViewModelBase
    {
        public event Action RequestClose;
        public DialogAction Action { get; private set; }

        /// <summary>Called by the window when it opens.</summary>
        public void BeginDialog() => Action = DialogAction.None;

        protected void Finish(DialogAction a)
        {
            Action = a;
            RequestClose?.Invoke();
        }
    }
}
