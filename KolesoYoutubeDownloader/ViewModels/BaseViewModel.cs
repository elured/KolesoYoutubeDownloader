using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace KolesoYoutubeDownloader.ViewModels
{
    public abstract class BaseViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string pPropertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(pPropertyName));
        }

        protected bool SetProperty<T>(ref T pBackingField, T pValue, [CallerMemberName] string pPropertyName = null)
        {
            if (Equals(pBackingField, pValue))
            {
                return false;
            }

            pBackingField = pValue;
            OnPropertyChanged(pPropertyName);
            return true;
        }
    }
}
