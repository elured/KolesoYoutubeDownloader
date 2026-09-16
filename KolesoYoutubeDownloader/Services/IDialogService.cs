using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KolesoYoutubeDownloader.Services
{
    public interface IDialogService
    {
        void ShowMessage(string pTitle, string pMessage);
        void ShowError(string pTitle, string pMessage);
        // В будущем сюда можно добавить: string SelectFolderDialog();
    }
}
