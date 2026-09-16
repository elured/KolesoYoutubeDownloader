using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace KolesoYoutubeDownloader.ViewModels
{
    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Predicate<object> _canExecute;

        public RelayCommand(Action<object> pExecute, Predicate<object> pCanExecute = null)
        {
            _execute = pExecute ?? throw new ArgumentNullException(nameof(pExecute));
            _canExecute = pCanExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object pParameter)
        {
            return _canExecute == null || _canExecute(pParameter);
        }

        public void Execute(object pParameter)
        {
            _execute(pParameter);
        }
    }
}
