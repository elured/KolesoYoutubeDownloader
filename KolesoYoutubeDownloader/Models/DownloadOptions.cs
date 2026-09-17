using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KolesoYoutubeDownloader.Models
{
    public class DownloadOptions
    {
        public string VideoUrl { get; set; }

        public bool IsAudioOnly { get; set; }

        public TimeSpan? StartTime { get; set; }

        public TimeSpan? EndTime { get; set; }
        public string SelectedQuality { get; set; }

    }
}
