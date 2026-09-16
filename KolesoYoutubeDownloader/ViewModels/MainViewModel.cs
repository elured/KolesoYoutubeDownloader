using KolesoYoutubeDownloader.Models;
using KolesoYoutubeDownloader.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace KolesoYoutubeDownloader.ViewModels
{
    public class MainViewModel : BaseViewModel
    {
        private readonly IYouTubeDownloaderService _downloaderService;
        private readonly IDialogService _dialogService;

        #region Свойства для привязки к интерфейсу (Bindings)

        private string _videoUrl;
        public string VideoUrl
        {
            get => _videoUrl;
            set => SetProperty(ref _videoUrl, value);
        }

        private bool _isAudioOnly = true; // По умолчанию качаем аудио (вариант 2)
        public bool IsAudioOnly
        {
            get => _isAudioOnly;
            set => SetProperty(ref _isAudioOnly, value);
        }

        public TimeInputViewModel StartTime { get; } = new TimeInputViewModel();

        public TimeInputViewModel EndTime { get; } = new TimeInputViewModel();

        private double _progressPercent;
        public double ProgressPercent
        {
            get => _progressPercent;
            set => SetProperty(ref _progressPercent, value);
        }

        private string _statusText = "Готов к работе";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        private bool _isDownloading;
        public bool IsDownloading
        {
            get => _isDownloading;
            set
            {
                if (SetProperty(ref _isDownloading, value))
                {
                    // Когда статус скачивания меняется, просим WPF перепроверить, активна ли кнопка
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        #endregion

        public ICommand DownloadCommand { get; }

        public MainViewModel(IYouTubeDownloaderService pDownloaderService, IDialogService pDialogService)
        {
            _downloaderService = pDownloaderService;
            _dialogService = pDialogService;

            DownloadCommand = new RelayCommand(ExecuteDownload, CanExecuteDownload);
        }

        private bool CanExecuteDownload(object pParameter)
        {
            // Кнопка активна только если мы не качаем прямо сейчас и URL не пустой
            return !IsDownloading && !string.IsNullOrWhiteSpace(VideoUrl);
        }

        private async void ExecuteDownload(object pParameter)
        {
            try
            {
                IsDownloading = true;
                ProgressPercent = 0;
                StatusText = "Подготовка...";

                // 1. Парсим время
                TimeSpan? lStartTime = StartTime.GetTimeSpan();
                TimeSpan? lEndTime = EndTime.GetTimeSpan();

                // 2. Формируем настройки
                var lOptions = new DownloadOptions
                {
                    VideoUrl = this.VideoUrl.Trim(),
                    IsAudioOnly = this.IsAudioOnly,
                    StartTime = lStartTime,
                    EndTime = lEndTime
                };

                // 3. Создаем обработчик прогресса, который будет обновлять UI
                var lProgressIndicator = new Progress<ProgressData>(pData =>
                {
                    ProgressPercent = pData.Percent;
                    StatusText = pData.StatusText;
                });

                // 4. Запускаем скачивание
                await _downloaderService.DownloadVideoAsync(lOptions, lProgressIndicator);

                _dialogService.ShowMessage("Успех", "Скачивание и обработка успешно завершены!");
            }
            catch (FormatException ex)
            {
                _dialogService.ShowError("Ошибка ввода", ex.Message);
            }
            catch (Exception ex)
            {
                _dialogService.ShowError("Ошибка", $"Произошла ошибка при скачивании:\n{ex.Message}");
            }
            finally
            {
                IsDownloading = false;
                StatusText = "Готов к работе";
                ProgressPercent = 0; 
                StartTime.Clear();
                EndTime.Clear();
            }
        }

        // Перенесенный метод парсинга времени из консольного проекта
        private TimeSpan? ParseTimeSpan(string pInput)
        {
            if (string.IsNullOrWhiteSpace(pInput)) return null;

            string[] lFormats =
            {
                "m\\:ss\\.f", "mm\\:ss\\.f",
                "m\\:ss\\.ff", "mm\\:ss\\.ff",
                "m\\:ss\\.fff", "mm\\:ss\\.fff",
                "h\\:mm\\:ss\\.f", "hh\\:mm\\:ss\\.f",
                "h\\:mm\\:ss\\.ff", "hh\\:mm\\:ss\\.ff",
                "h\\:mm\\:ss\\.fff", "hh\\:mm\\:ss\\.fff",
                "m\\:ss", "mm\\:ss",
                "h\\:mm\\:ss", "hh\\:mm\\:ss"
            };

            if (TimeSpan.TryParseExact(pInput.Trim(), lFormats, CultureInfo.InvariantCulture, TimeSpanStyles.None, out var lParsedTime))
            {
                return lParsedTime;
            }

            if (TimeSpan.TryParse(pInput.Trim(), out var lStandardParsedTime))
            {
                return lStandardParsedTime;
            }

            throw new FormatException($"Неверный формат времени: '{pInput}'. Используйте формат mm:ss.f или hh:mm:ss.fff");
        }
    }
}
