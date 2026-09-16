using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace KolesoYoutubeDownloader.Services
{
    public class DialogService : IDialogService
    {
        public void ShowMessage(string pTitle, string pMessage)
        {
            MessageBox.Show(pMessage, pTitle, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public void ShowError(string pTitle, string pMessage)
        {
            MessageBox.Show(pMessage, pTitle, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
