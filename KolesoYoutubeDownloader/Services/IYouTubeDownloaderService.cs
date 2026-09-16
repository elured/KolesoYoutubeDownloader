using KolesoYoutubeDownloader.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KolesoYoutubeDownloader.Services
{
    public interface IYouTubeDownloaderService
    {
        // Обратите внимание: вместо 4 параметров мы передаем один объект DownloadOptions,
        // а для прогресса используем наш новый ProgressData
        Task DownloadVideoAsync(DownloadOptions pOptions, IProgress<ProgressData> pProgress = null);
    }
}
