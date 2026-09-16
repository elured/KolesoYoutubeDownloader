using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KolesoYoutubeDownloader.Models
{
    public class ProgressData
    {
        // Процент от 0 до 100
        public double Percent { get; set; }

        // Текущий этап: "Скачивание аудио...", "Обрезка в FFmpeg...", "Готово!"
        public string StatusText { get; set; }
    }
}
