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
        Task DownloadVideoAsync(DownloadOptions pOptions, IProgress<ProgressData> pProgress = null);
        Task<List<string>> GetAvailableVideoQualitiesAsync(string pUrl, CancellationToken pToken = default);
    }
}
