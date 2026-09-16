using KolesoYoutubeDownloader.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;
using CliWrap.Buffered;
using CliWrap;
using Xabe.FFmpeg.Downloader;

namespace KolesoYoutubeDownloader.Services
{
    public class YouTubeDownloaderService : IYouTubeDownloaderService
    {
        private readonly YoutubeClient _youtubeClient;

        public YouTubeDownloaderService()
        {
            var lHandler = new HttpClientHandler { UseDefaultCredentials = true };
            var lHttpClient = new HttpClient(lHandler);

            _youtubeClient = new YoutubeClient(lHttpClient);
        }

        public async Task DownloadVideoAsync(DownloadOptions pOptions, IProgress<ProgressData> pProgress = null)
        {
            pProgress?.Report(new ProgressData { StatusText = "Получение информации о видео...", Percent = 0 });

            var lVideo = await _youtubeClient.Videos.GetAsync(pOptions.VideoUrl);
            var lStreamManifest = await _youtubeClient.Videos.Streams.GetManifestAsync(lVideo.Id);

            string lSafeTitle = string.Join("_", lVideo.Title.Split(Path.GetInvalidFileNameChars()));
            string lDownloadDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

            bool lNeedsTrimming = pOptions.StartTime.HasValue || pOptions.EndTime.HasValue;

            TimeSpan lEffectiveStart = pOptions.StartTime ?? TimeSpan.Zero;
            TimeSpan lEffectiveEnd = pOptions.EndTime ?? lVideo.Duration ?? TimeSpan.Zero;
            double lClipDurationSeconds = (lEffectiveEnd - lEffectiveStart).TotalSeconds;

            bool lApplyFadeOut = lNeedsTrimming && lClipDurationSeconds > 0.5;
            double lFadeStartTime = lClipDurationSeconds - 0.5;

            var lDownloadProgress = new Progress<double>(pPercent =>
            {
                pProgress?.Report(new ProgressData
                {
                    StatusText = "Скачивание потока с YouTube...",
                    Percent = pPercent * 100
                });
            });

            if (pOptions.IsAudioOnly)
            {
                var lAudioStreamInfo = lStreamManifest.GetAudioOnlyStreams().GetWithHighestBitrate();
                if (lAudioStreamInfo == null)
                {
                    throw new InvalidOperationException("Подходящий аудиопоток не найден.");
                }

                bool lExceedsAacThreshold = lAudioStreamInfo.Bitrate.KiloBitsPerSecond > 320;
                string lTargetExtension = lExceedsAacThreshold ? "flac" : "mp3";

                string lFullPath = GetUniqueFilePath(lDownloadDirectory, lSafeTitle, lTargetExtension);
                string lTempPath = Path.GetTempFileName() + $".{lAudioStreamInfo.Container.Name}";

                await _youtubeClient.Videos.Streams.DownloadAsync(lAudioStreamInfo, lTempPath, lDownloadProgress);

                await EnsureFFmpegExistsAsync(pProgress);

                pProgress?.Report(new ProgressData { StatusText = "Подготовка к обработке аудио...", Percent = 0 });

                await Cli.Wrap("ffmpeg")
                    .WithArguments(pArgs =>
                    {
                        pArgs.Add("-i").Add(lTempPath);

                        if (pOptions.StartTime.HasValue)
                        {
                            pArgs.Add("-ss").Add(pOptions.StartTime.Value.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture));
                        }

                        if (pOptions.EndTime.HasValue)
                        {
                            pArgs.Add("-to").Add(pOptions.EndTime.Value.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture));
                        }

                        if (lExceedsAacThreshold)
                        {
                            pArgs.Add("-c:a").Add("flac");
                        }
                        else
                        {
                            pArgs.Add("-c:a").Add("libmp3lame").Add("-q:a").Add("0");
                        }

                        var lAudioFilters = new List<string>();

                        if (pOptions.StartTime.HasValue)
                        {
                            lAudioFilters.Add("afade=t=in:st=0:d=1.5");
                        }
                        if (lApplyFadeOut)
                        {
                            string lFadeTimeStr = lFadeStartTime.ToString("F3", CultureInfo.InvariantCulture);
                            lAudioFilters.Add($"afade=t=out:st={lFadeTimeStr}:d=0.5");
                        }

                        if (lAudioFilters.Count > 0)
                        {
                            pArgs.Add("-af").Add(string.Join(",", lAudioFilters));
                        }

                        pArgs.Add(lFullPath);
                    })
                    .WithStandardErrorPipe(PipeTarget.ToDelegate(pLine =>
                    {
                        ReportFFmpegProgress(pLine, lClipDurationSeconds, pProgress, "Обработка аудио в FFmpeg...");
                    }))
                    .ExecuteAsync();

                if (File.Exists(lTempPath))
                {
                    File.Delete(lTempPath);
                }
            }
            else
            {
                var lVideoStreamInfo = lStreamManifest.GetMuxedStreams().GetWithHighestVideoQuality();
                if (lVideoStreamInfo == null)
                {
                    throw new InvalidOperationException("Подходящий видеопоток не найден.");
                }

                string lFullPath = GetUniqueFilePath(lDownloadDirectory, lSafeTitle, lVideoStreamInfo.Container.Name);

                if (lNeedsTrimming)
                {
                    string lTempPath = Path.GetTempFileName() + $".{lVideoStreamInfo.Container.Name}";

                    await _youtubeClient.Videos.Streams.DownloadAsync(lVideoStreamInfo, lTempPath, lDownloadProgress);

                    await EnsureFFmpegExistsAsync(pProgress);

                    pProgress?.Report(new ProgressData { StatusText = "Подготовка к обработке видео...", Percent = 0 });

                    await Cli.Wrap("ffmpeg")
                        .WithArguments(pArgs =>
                        {
                            pArgs.Add("-i").Add(lTempPath);

                            if (pOptions.StartTime.HasValue)
                            {
                                pArgs.Add("-ss").Add(pOptions.StartTime.Value.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture));
                            }

                            if (pOptions.EndTime.HasValue)
                            {
                                pArgs.Add("-to").Add(pOptions.EndTime.Value.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture));
                            }

                            pArgs.Add("-c:v").Add("libx264").Add("-c:a").Add("aac");

                            var lVideoFilters = new List<string>();
                            var lAudioFilters = new List<string>();

                            if (pOptions.StartTime.HasValue)
                            {
                                lVideoFilters.Add("fade=t=in:st=0:d=1.5");
                                lAudioFilters.Add("afade=t=in:st=0:d=1.5");
                            }
                            if (lApplyFadeOut)
                            {
                                string lFadeTimeStr = lFadeStartTime.ToString("F3", CultureInfo.InvariantCulture);
                                lVideoFilters.Add($"fade=t=out:st={lFadeTimeStr}:d=0.5");
                                lAudioFilters.Add($"afade=t=out:st={lFadeTimeStr}:d=0.5");
                            }

                            if (lVideoFilters.Count > 0)
                            {
                                pArgs.Add("-vf").Add(string.Join(",", lVideoFilters));
                            }
                            if (lAudioFilters.Count > 0)
                            {
                                pArgs.Add("-af").Add(string.Join(",", lAudioFilters));
                            }

                            pArgs.Add(lFullPath);
                        })
                        .WithStandardErrorPipe(PipeTarget.ToDelegate(pLine =>
                        {
                            ReportFFmpegProgress(pLine, lClipDurationSeconds, pProgress, "Обработка видео в FFmpeg...");
                        }))
                        .ExecuteAsync();

                    File.Delete(lTempPath);
                }
                else
                {
                    await _youtubeClient.Videos.Streams.DownloadAsync(lVideoStreamInfo, lFullPath, lDownloadProgress);
                }
            }

            pProgress?.Report(new ProgressData { StatusText = "Готово!", Percent = 100 });
        }

        private static string GetUniqueFilePath(string pDirectory, string pFileName, string pExtension)
        {
            string lFullPath = Path.Combine(pDirectory, $"{pFileName}.{pExtension}");
            int lFileIndex = 1;

            while (File.Exists(lFullPath))
            {
                lFullPath = Path.Combine(pDirectory, $"{pFileName} ({lFileIndex}).{pExtension}");
                lFileIndex++;
            }

            return lFullPath;
        }

        private static async Task EnsureFFmpegExistsAsync(IProgress<ProgressData> pProgress)
        {
            string lFFmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");

            if (!File.Exists(lFFmpegPath))
            {
                var lProgress = new Progress<Xabe.FFmpeg.ProgressInfo>(pInfo =>
                {
                    double lPercent = pInfo.TotalBytes > 0 ? (double)pInfo.DownloadedBytes / pInfo.TotalBytes * 100 : 0;
                    pProgress?.Report(new ProgressData
                    {
                        StatusText = "Скачивание FFmpeg...",
                        Percent = lPercent
                    });
                });

                await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official, AppDomain.CurrentDomain.BaseDirectory, lProgress);
            }
        }

        private static void ReportFFmpegProgress(string pLine, double pClipDurationSeconds, IProgress<ProgressData> pProgress, string pStatusText)
        {
            int lTimeIndex = pLine.IndexOf("time=");
            if (lTimeIndex != -1)
            {
                int lStartIndex = lTimeIndex + 5;
                int lEndIndex = pLine.IndexOf(' ', lStartIndex);
                if (lEndIndex == -1) lEndIndex = pLine.Length;

                string lTimeString = pLine.Substring(lStartIndex, lEndIndex - lStartIndex).Trim();

                if (TimeSpan.TryParse(lTimeString, CultureInfo.InvariantCulture, out TimeSpan lCurrentTime))
                {
                    if (pClipDurationSeconds > 0)
                    {
                        double lProgressValue = lCurrentTime.TotalSeconds / pClipDurationSeconds;
                        double lPercent = Math.Clamp(lProgressValue * 100, 0, 100);

                        pProgress?.Report(new ProgressData
                        {
                            StatusText = pStatusText,
                            Percent = lPercent
                        });
                    }
                }
            }
        }
    }
}
