using System;

namespace KolesoYoutubeDownloader.ViewModels
{
    public class TimeInputViewModel : BaseViewModel
    {
        private string _hours;
        public string Hours
        {
            get => _hours;
            set => SetProperty(ref _hours, FilterNumbers(value));
        }

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

        private string FilterNumbers(string pInput)
        {
            if (string.IsNullOrEmpty(pInput)) return pInput;
            return new string(Array.FindAll(pInput.ToCharArray(), char.IsDigit));
        }

        public TimeSpan? GetTimeSpan()
        {
            bool lHasHour = int.TryParse(Hours, out int lHour);
            bool lHasMin = int.TryParse(Minutes, out int lMin);
            bool lHasSec = int.TryParse(Seconds, out int lSec);
            bool lHasMs = int.TryParse(Milliseconds, out int lMs);

            // Если все поля пустые, возвращаем null (без обрезки)
            if (!lHasHour && !lHasMin && !lHasSec && !lHasMs)
            {
                return null;
            }

            // Передаем: дни (0), часы, минуты, секунды и миллисекунды
            return new TimeSpan(0, lHour, lMin, lSec, lMs);
        }

        public void Clear()
        {
            Hours = string.Empty;
            Minutes = string.Empty;
            Seconds = string.Empty;
            Milliseconds = string.Empty;
        }
    }
}