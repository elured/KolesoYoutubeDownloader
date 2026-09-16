using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KolesoYoutubeDownloader.ViewModels
{
    public class TimeInputViewModel : BaseViewModel
    {
        private string _minutes;
        public string Minutes
        {
            get => _minutes;
            set => SetProperty(ref _minutes, FilterNumbers(value));
        }

        private string _seconds;
        public string Seconds
        {
            get => _seconds;
            set => SetProperty(ref _seconds, FilterNumbers(value));
        }

        private string _milliseconds;
        public string Milliseconds
        {
            get => _milliseconds;
            set => SetProperty(ref _milliseconds, FilterNumbers(value));
        }

        // Простая защита от ввода букв
        private string FilterNumbers(string pInput)
        {
            if (string.IsNullOrEmpty(pInput)) return pInput;
            return new string(Array.FindAll(pInput.ToCharArray(), char.IsDigit));
        }

        public TimeSpan? GetTimeSpan()
        {
            bool lHasMin = int.TryParse(Minutes, out int lMin);
            bool lHasSec = int.TryParse(Seconds, out int lSec);
            bool lHasMs = int.TryParse(Milliseconds, out int lMs);

            // Если все поля пустые, возвращаем null (без обрезки)
            if (!lHasMin && !lHasSec && !lHasMs)
            {
                return null;
            }

            return new TimeSpan(0, 0, lMin, lSec, lMs);
        }

        public void Clear()
        {
            Minutes = string.Empty;
            Seconds = string.Empty;
            Milliseconds = string.Empty;
        }
    }
}
