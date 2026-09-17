using CliWrap;
using CliWrap.Buffered;
using KolesoYoutubeDownloader.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xabe.FFmpeg.Downloader;

namespace KolesoYoutubeDownloader.Services
{
    public class YouTubeDownloaderService : IYouTubeDownloaderService
    {
        public YouTubeDownloaderService()
        {
            // YoutubeClient больше не нужен, конструктор оставляем для DI
        }

        public async Task DownloadVideoAsync(DownloadOptions pOptions, IProgress<ProgressData> pProgress = null)
        {
            await EnsureYtDlpExistsAsync(pProgress);
            await EnsureFFmpegExistsAsync(pProgress);
            string lYtDlpPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "yt-dlp.exe");

            pProgress?.Report(new ProgressData { StatusText = "Получение информации о видео...", Percent = 0 });

            // Запрашиваем JSON с информацией о видео через yt-dlp
            var lInfoResult = await Cli.Wrap(lYtDlpPath)
                .WithArguments(pArgs => pArgs.Add("--dump-json").Add(pOptions.VideoUrl))
                .ExecuteBufferedAsync();

            using var lDoc = JsonDocument.Parse(lInfoResult.StandardOutput);
            var lRoot = lDoc.RootElement;

            string lTitle = lRoot.TryGetProperty("title", out var lTitleProp) && lTitleProp.ValueKind != JsonValueKind.Null ? lTitleProp.GetString() : "YouTube_Video";
            double lDurationSeconds = lRoot.TryGetProperty("duration", out var lDurProp) && lDurProp.ValueKind != JsonValueKind.Null ? lDurProp.GetDouble() : 0;
            double lAudioBitrate = lRoot.TryGetProperty("abr", out var lAbrProp) && lAbrProp.ValueKind != JsonValueKind.Null ? lAbrProp.GetDouble() : 128.0;

            string lSafeTitle = string.Join("_", lTitle.Split(Path.GetInvalidFileNameChars()));
            string lDownloadDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

            bool lNeedsTrimming = pOptions.StartTime.HasValue || pOptions.EndTime.HasValue;

            TimeSpan lEffectiveStart = pOptions.StartTime ?? TimeSpan.Zero;
            TimeSpan lEffectiveEnd = pOptions.EndTime ?? (lDurationSeconds > 0 ? TimeSpan.FromSeconds(lDurationSeconds) : TimeSpan.Zero);
            double lClipDurationSeconds = (lEffectiveEnd - lEffectiveStart).TotalSeconds;

            bool lApplyFadeOut = lNeedsTrimming && lClipDurationSeconds > 0.5;
            double lFadeStartTime = lClipDurationSeconds - 0.5;

            var lDownloadProgress = new Progress<double>(pPercent =>
            {
                pProgress?.Report(new ProgressData
                {
                    StatusText = "Скачивание потока...",
                    Percent = pPercent * 100
                });
            });

            if (pOptions.IsAudioOnly)
            {
                bool lExceedsAacThreshold = lAudioBitrate > 320;
                string lTargetExtension = lExceedsAacThreshold ? "flac" : "mp3";

                string lFullPath = GetUniqueFilePath(lDownloadDirectory, lSafeTitle, lTargetExtension);
                string lTempPath = Path.GetTempFileName() + ".tmp"; // FFmpeg сам поймет формат

                // Качаем только лучшее аудио (bestaudio)
                await DownloadWithYtDlpAsync(lYtDlpPath, pOptions.VideoUrl, "ba", lTempPath, lDownloadProgress);

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
                string lFullPath = GetUniqueFilePath(lDownloadDirectory, lSafeTitle, "mp4");

                string lTempVideoPath = Path.GetTempFileName() + ".tmp";
                string lTempAudioPath = Path.GetTempFileName() + ".tmp";

                try
                {
                    // Качаем лучшее видео (bestvideo)
                    pProgress?.Report(new ProgressData { StatusText = "Скачивание видеопотока...", Percent = 0 });
                    await DownloadWithYtDlpAsync(lYtDlpPath, pOptions.VideoUrl, "bv", lTempVideoPath, lDownloadProgress);

                    // Качаем лучшее аудио (bestaudio)
                    pProgress?.Report(new ProgressData { StatusText = "Скачивание аудиопотока...", Percent = 50 });
                    await DownloadWithYtDlpAsync(lYtDlpPath, pOptions.VideoUrl, "ba", lTempAudioPath, lDownloadProgress);

                    pProgress?.Report(new ProgressData { StatusText = "Мерж и обработка видео в FFmpeg...", Percent = 80 });

                    await Cli.Wrap("ffmpeg")
                        .WithArguments(pArgs =>
                        {
                            pArgs.Add("-i").Add(lTempVideoPath);
                            pArgs.Add("-i").Add(lTempAudioPath);

                            if (lNeedsTrimming)
                            {
                                pArgs.Add("-ss").Add(lEffectiveStart.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture));
                                pArgs.Add("-to").Add(lEffectiveEnd.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture));
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

                            pArgs.Add("-map").Add("0:v:0").Add("-map").Add("1:a:0");
                            pArgs.Add(lFullPath);
                        })
                        .WithStandardErrorPipe(PipeTarget.ToDelegate(pLine =>
                        {
                            ReportFFmpegProgress(pLine, lClipDurationSeconds, pProgress, "Обработка видео в FFmpeg...");
                        }))
                        .ExecuteAsync();
                }
                finally
                {
                    if (File.Exists(lTempVideoPath)) File.Delete(lTempVideoPath);
                    if (File.Exists(lTempAudioPath)) File.Delete(lTempAudioPath);
                }
            }

            pProgress?.Report(new ProgressData { StatusText = "Готово!", Percent = 100 });
        }

        private static async Task DownloadWithYtDlpAsync(string pYtDlpPath, string pUrl, string pFormat, string pOutputPath, IProgress<double> pProgress)
        {
            var lRegex = new Regex(@"\[download\]\s+(?<percent>\d+\.?\d*)%", RegexOptions.Compiled);
            var lErrorBuilder = new StringBuilder();
            string lFFmpegDir = AppDomain.CurrentDomain.BaseDirectory;

            var lResult = await Cli.Wrap(pYtDlpPath)
                .WithArguments(pArgs => pArgs
                    .Add("-f").Add(pFormat)
                    .Add("-o").Add(pOutputPath)
                    .Add("--ffmpeg-location").Add(lFFmpegDir) // Указываем, где лежит FFmpeg
                    .Add("--fixup").Add("never")              // Запрещаем менять файл после скачивания!
                    .Add(pUrl))
                .WithStandardOutputPipe(PipeTarget.ToDelegate(pLine =>
                {
                    var lMatch = lRegex.Match(pLine);
                    if (lMatch.Success && double.TryParse(lMatch.Groups["percent"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double lPercent))
                    {
                        pProgress?.Report(lPercent / 100.0);
                    }
                }))
                .WithStandardErrorPipe(PipeTarget.ToDelegate(pLine =>
                {
                    lErrorBuilder.AppendLine(pLine); // Собираем текст ошибки, если она будет
                }))
                .WithValidation(CommandResultValidation.None) // Отключаем автоматический эксепшен
                .ExecuteAsync();

            if (lResult.ExitCode != 0)
            {
                throw new Exception($"yt-dlp завершился с ошибкой (Exit Code {lResult.ExitCode}):\n{lErrorBuilder}");
            }
        }

        private static async Task EnsureYtDlpExistsAsync(IProgress<ProgressData> pProgress)
        {
            string lYtDlpPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "yt-dlp.exe");
            if (!File.Exists(lYtDlpPath))
            {
                pProgress?.Report(new ProgressData { StatusText = "Скачивание yt-dlp...", Percent = 0 });
                using var lClient = new HttpClient();
                var lBytes = await lClient.GetByteArrayAsync("https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe");
                await File.WriteAllBytesAsync(lYtDlpPath, lBytes);
            }
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

        public async Task<List<string>> GetAvailableVideoQualitiesAsync(string pUrl, CancellationToken pToken = default)
        {
            await EnsureYtDlpExistsAsync(null);
            string lYtDlpPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "yt-dlp.exe");

            BufferedCommandResult lResult = await Cli.Wrap(lYtDlpPath)
                .WithArguments(pArgs => pArgs.Add("--dump-json").Add(pUrl))
                .WithValidation(CommandResultValidation.None) // Не бросать Exception, если yt-dlp упал из-за кривой ссылки
                .ExecuteBufferedAsync(pToken);                // Передаем токен для мгновенной отмены

            // Если процесс завершился с ошибкой (например, битая ссылка), просто возвращаем пустой список
            if (lResult.ExitCode != 0 || string.IsNullOrWhiteSpace(lResult.StandardOutput))
            {
                return new List<string>();
            }

            using JsonDocument lDoc = JsonDocument.Parse(lResult.StandardOutput);
            JsonElement lFormats = lDoc.RootElement.GetProperty("formats");

            HashSet<int> lQualities = new HashSet<int>();

            foreach (JsonElement lFormat in lFormats.EnumerateArray())
            {
                if (lFormat.TryGetProperty("vcodec", out JsonElement lVcodec) && lVcodec.GetString() != "none")
                {
                    if (lFormat.TryGetProperty("height", out JsonElement lHeightProp) && lHeightProp.ValueKind == JsonValueKind.Number)
                    {
                        int lHeight = lHeightProp.GetInt32();
                        if (lHeight > 0)
                        {
                            lQualities.Add(lHeight);
                        }
                    }
                }
            }

            return lQualities.OrderByDescending(pHeight => pHeight).Select(pHeight => $"{pHeight}p").ToList();
        }
    }
}