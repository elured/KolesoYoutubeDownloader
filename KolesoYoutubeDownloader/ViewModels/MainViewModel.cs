using KolesoYoutubeDownloader.Models;
using KolesoYoutubeDownloader.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace KolesoYoutubeDownloader.ViewModels
{
    public class MainViewModel : BaseViewModel
    {
        private readonly IYouTubeDownloaderService _downloaderService;
        private readonly IDialogService _dialogService;
        private CancellationTokenSource _analysisCts;

        #region Свойства для привязки к интерфейсу (Bindings)

        private bool _isAnalyzing;
        public bool IsAnalyzing
        {
            get => _isAnalyzing;
            set => SetProperty(ref _isAnalyzing, value);
        }

        private string _videoUrl;
        public string VideoUrl
        {
            get => _videoUrl;
            set
            {
                if (_videoUrl == value) return;

                SetProperty(ref _videoUrl, value);

                // Очистка привязана строго к удалению ссылки
                if (string.IsNullOrWhiteSpace(value))
                {
                    _analysisCts?.Cancel();
                    AvailableQualities.Clear();
                    SelectedQuality = string.Empty;
                    HasQualities = false;

                    StartTime.Clear();
                    EndTime.Clear();
                    return;
                }

                _ = CheckAndAnalyzeVideoAsync();
            }
        }

        private bool _isAudioOnly;
        public bool IsAudioOnly
        {
            get => _isAudioOnly;
            set
            {
                if (_isAudioOnly == value) return;

                SetProperty(ref _isAudioOnly, value);
                _ = CheckAndAnalyzeVideoAsync();
            }
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
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public ObservableCollection<string> AvailableQualities { get; } = new ObservableCollection<string>();

        private string _selectedQuality;
        public string SelectedQuality
        {
            get => _selectedQuality;
            set => SetProperty(ref _selectedQuality, value);
        }

        private bool _hasQualities;
        public bool HasQualities
        {
            get => _hasQualities;
            set => SetProperty(ref _hasQualities, value);
        }

        #endregion

        public ICommand DownloadCommand { get; }

        public MainViewModel(IYouTubeDownloaderService pDownloaderService, IDialogService pDialogService)
        {
            _downloaderService = pDownloaderService;
            _dialogService = pDialogService;
            _isAudioOnly = true;
            DownloadCommand = new RelayCommand(ExecuteDownload, CanExecuteDownload);
        }

        private bool CanExecuteDownload(object pParameter)
        {
            return !IsDownloading && !string.IsNullOrWhiteSpace(VideoUrl);
        }

        private async void ExecuteDownload(object pParameter)
        {
            try
            {
                IsDownloading = true;
                ProgressPercent = 0;
                StatusText = "Подготовка...";

                TimeSpan? lStartTime = StartTime.GetTimeSpan();
                TimeSpan? lEndTime = EndTime.GetTimeSpan();

                DownloadOptions lOptions = new DownloadOptions
                {
                    VideoUrl = this.VideoUrl.Trim(),
                    IsAudioOnly = this.IsAudioOnly,
                    StartTime = lStartTime,
                    EndTime = lEndTime,
                    SelectedQuality = this.SelectedQuality
                };

                Progress<ProgressData> lProgressIndicator = new Progress<ProgressData>(pData =>
                {
                    ProgressPercent = pData.Percent;
                    StatusText = pData.StatusText;
                });

                await _downloaderService.DownloadVideoAsync(lOptions, lProgressIndicator);

                // Даем UI-потоку дорисовать полоску без зависаний перед вызовом диалога
                await Task.Delay(800);

                _dialogService.ShowMessage("Успех", "Скачивание и обработка успешно завершены!");
            }
            catch (FormatException lEx)
            {
                _dialogService.ShowError("Ошибка ввода", lEx.Message);
            }
            catch (Exception lEx)
            {
                _dialogService.ShowError("Ошибка", $"Произошла ошибка при скачивании:\n{lEx.Message}");
            }
            finally
            {
                IsDownloading = false;
                StatusText = "Готов к работе";
                ProgressPercent = 0;
            }
        }

        private async Task CheckAndAnalyzeVideoAsync()
        {
            _analysisCts?.Cancel();
            _analysisCts = new CancellationTokenSource();
            CancellationToken lToken = _analysisCts.Token;

            AvailableQualities.Clear();
            SelectedQuality = string.Empty;
            HasQualities = false;

            if (IsAudioOnly || string.IsNullOrWhiteSpace(VideoUrl)) return;

            string lLowerUrl = VideoUrl.ToLower();
            if (!lLowerUrl.Contains("youtube.com") && !lLowerUrl.Contains("youtu.be")) return;

            try
            {
                IsAnalyzing = true;

                List<string> lQualities = await _downloaderService.GetAvailableVideoQualitiesAsync(VideoUrl, lToken);

                if (lToken.IsCancellationRequested) return;

                if (lQualities.Count > 0)
                {
                    foreach (string lQuality in lQualities)
                    {
                        AvailableQualities.Add(lQuality);
                    }

                    string lTargetQuality = null;

                    for (int lIndex = lQualities.Count - 1; lIndex >= 0; lIndex--)
                    {
                        string lCleanString = lQualities[lIndex].Replace("p", "");
                        if (int.TryParse(lCleanString, out int lHeight) && lHeight <= 720)
                        {
                            lTargetQuality = lQualities[lIndex];
                            break;
                        }
                    }

                    SelectedQuality = lTargetQuality != null ? lTargetQuality : AvailableQualities[0];
                    HasQualities = true;
                }
            }
            catch (TaskCanceledException)
            {
                // Отменено пользователем
            }
            catch (Exception lEx)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка анализа видео: {lEx.Message}");
            }
            finally
            {
                if (!lToken.IsCancellationRequested)
                {
                    IsAnalyzing = false;
                }
            }
        }
    }
}