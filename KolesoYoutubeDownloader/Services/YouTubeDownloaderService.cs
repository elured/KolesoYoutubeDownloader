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
using System.Threading;
using System.Threading.Tasks;
using Xabe.FFmpeg.Downloader;

namespace KolesoYoutubeDownloader.Services
{
    public class YouTubeDownloaderService : IYouTubeDownloaderService
    {
        public YouTubeDownloaderService()
        {
            // Конструктор оставляем для DI
        }

        public async Task DownloadVideoAsync(DownloadOptions pOptions, IProgress<ProgressData> pProgress = null)
        {
            await EnsureYtDlpExistsAsync(pProgress);
            await EnsureFFmpegExistsAsync(pProgress);

            string lYtDlpPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "yt-dlp.exe");
            string lFFmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");

            pProgress?.Report(new ProgressData { StatusText = "Получение информации о видео...", Percent = 0 });

            BufferedCommandResult lInfoResult = await Cli.Wrap(lYtDlpPath)
                .WithArguments(pArgs => pArgs.Add("--dump-json").Add(pOptions.VideoUrl))
                .ExecuteBufferedAsync();

            using JsonDocument lDoc = JsonDocument.Parse(lInfoResult.StandardOutput);
            JsonElement lRoot = lDoc.RootElement;

            string lTitle = lRoot.TryGetProperty("title", out JsonElement lTitleProp) && lTitleProp.ValueKind != JsonValueKind.Null ? lTitleProp.GetString() : "YouTube_Video";
            double lDurationSeconds = lRoot.TryGetProperty("duration", out JsonElement lDurProp) && lDurProp.ValueKind != JsonValueKind.Null ? lDurProp.GetDouble() : 0;
            double lAudioBitrate = lRoot.TryGetProperty("abr", out JsonElement lAbrProp) && lAbrProp.ValueKind != JsonValueKind.Null ? lAbrProp.GetDouble() : 128.0;

            string lSafeTitle = string.Join("_", lTitle.Split(Path.GetInvalidFileNameChars()));
            string lDownloadDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

            bool lNeedsTrimming = pOptions.StartTime.HasValue || pOptions.EndTime.HasValue;

            TimeSpan lEffectiveStart = pOptions.StartTime ?? TimeSpan.Zero;
            TimeSpan lEffectiveEnd = pOptions.EndTime ?? (lDurationSeconds > 0 ? TimeSpan.FromSeconds(lDurationSeconds) : TimeSpan.Zero);
            double lClipDurationSeconds = (lEffectiveEnd - lEffectiveStart).TotalSeconds;

            bool lApplyFadeOut = lNeedsTrimming && lClipDurationSeconds > 0.5;
            double lFadeStartTime = lClipDurationSeconds - 0.5;

            string lTempDir = Path.Combine(Path.GetTempPath(), "KolesoDownloader", Guid.NewGuid().ToString());
            Directory.CreateDirectory(lTempDir);

            // Состояние для ограничения частоты обновлений прогресса FFmpeg
            int[] lFfmpegThrottle = new int[] { -1 };

            try
            {
                if (pOptions.IsAudioOnly)
                {
                    bool lExceedsAacThreshold = lAudioBitrate > 320;
                    string lTargetExtension = lExceedsAacThreshold ? "flac" : "mp3";
                    string lFullPath = GetUniqueFilePath(lDownloadDirectory, lSafeTitle, lTargetExtension);

                    string lAudioOutTemplate = Path.Combine(lTempDir, "audio.%(ext)s");

                    Progress<double> lAudioProgress = new Progress<double>(pPercent =>
                    {
                        pProgress?.Report(new ProgressData { StatusText = "Скачивание аудио...", Percent = pPercent * 80 });
                    });

                    await DownloadWithYtDlpAsync(lYtDlpPath, pOptions.VideoUrl, "ba", lAudioOutTemplate, lAudioProgress);

                    string lTempAudioPath = Directory.GetFiles(lTempDir, "audio.*").FirstOrDefault();
                    if (string.IsNullOrEmpty(lTempAudioPath)) throw new Exception("yt-dlp не смог сохранить аудиофайл.");

                    pProgress?.Report(new ProgressData { StatusText = "Подготовка к обработке аудио...", Percent = 80 });

                    await Cli.Wrap(lFFmpegPath)
                        .WithArguments(pArgs =>
                        {
                            pArgs.Add("-y");

                            if (pOptions.StartTime.HasValue)
                                pArgs.Add("-ss").Add(pOptions.StartTime.Value.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture));
                            if (pOptions.EndTime.HasValue)
                                pArgs.Add("-to").Add(pOptions.EndTime.Value.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture));

                            pArgs.Add("-i").Add(lTempAudioPath);

                            if (lExceedsAacThreshold)
                                pArgs.Add("-c:a").Add("flac");
                            else
                                pArgs.Add("-c:a").Add("libmp3lame").Add("-q:a").Add("0");

                            List<string> lAudioFilters = new List<string>();

                            if (pOptions.StartTime.HasValue)
                                lAudioFilters.Add("afade=t=in:st=0:d=1.5");
                            if (lApplyFadeOut)
                            {
                                string lFadeTimeStr = lFadeStartTime.ToString("F3", CultureInfo.InvariantCulture);
                                lAudioFilters.Add($"afade=t=out:st={lFadeTimeStr}:d=0.5");
                            }

                            if (lAudioFilters.Count > 0)
                                pArgs.Add("-af").Add(string.Join(",", lAudioFilters));

                            pArgs.Add(lFullPath);
                        })
                        .WithStandardErrorPipe(PipeTarget.ToDelegate(pLine =>
                            ReportFFmpegProgress(pLine, lClipDurationSeconds, pProgress, "Обработка аудио...", 80, 20, lFfmpegThrottle)))
                        .ExecuteAsync();
                }
                else
                {
                    string lFullPath = GetUniqueFilePath(lDownloadDirectory, lSafeTitle, "mp4");

                    string lVideoOutTemplate = Path.Combine(lTempDir, "video.%(ext)s");
                    string lAudioOutTemplate = Path.Combine(lTempDir, "audio.%(ext)s");

                    string lVideoFormat = "bv";
                    if (!string.IsNullOrEmpty(pOptions.SelectedQuality))
                    {
                        string lHeight = pOptions.SelectedQuality.Replace("p", "");
                        lVideoFormat = $"bv*[height<={lHeight}]";
                    }

                    Progress<double> lVideoProgress = new Progress<double>(pPercent =>
                    {
                        pProgress?.Report(new ProgressData { StatusText = "Скачивание видео...", Percent = pPercent * 50 });
                    });

                    await DownloadWithYtDlpAsync(lYtDlpPath, pOptions.VideoUrl, lVideoFormat, lVideoOutTemplate, lVideoProgress);

                    pProgress?.Report(new ProgressData { StatusText = "Скачивание аудио...", Percent = 50 });

                    Progress<double> lAudioProgress = new Progress<double>(pPercent =>
                    {
                        pProgress?.Report(new ProgressData { StatusText = "Скачивание аудио...", Percent = 50 + (pPercent * 25) });
                    });

                    await DownloadWithYtDlpAsync(lYtDlpPath, pOptions.VideoUrl, "ba", lAudioOutTemplate, lAudioProgress);

                    string lTempVideoPath = Directory.GetFiles(lTempDir, "video.*").FirstOrDefault();
                    string lTempAudioPath = Directory.GetFiles(lTempDir, "audio.*").FirstOrDefault();

                    if (string.IsNullOrEmpty(lTempVideoPath) || string.IsNullOrEmpty(lTempAudioPath))
                    {
                        throw new Exception("yt-dlp не смог сохранить временные потоки.");
                    }

                    pProgress?.Report(new ProgressData { StatusText = "Мерж и обработка видео в FFmpeg...", Percent = 75 });

                    await Cli.Wrap(lFFmpegPath)
                        .WithArguments(pArgs =>
                        {
                            pArgs.Add("-y");

                            if (lNeedsTrimming)
                            {
                                pArgs.Add("-ss").Add(lEffectiveStart.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture));
                                pArgs.Add("-to").Add(lEffectiveEnd.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture));
                            }

                            pArgs.Add("-i").Add(lTempVideoPath);

                            if (lNeedsTrimming)
                            {
                                pArgs.Add("-ss").Add(lEffectiveStart.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture));
                                pArgs.Add("-to").Add(lEffectiveEnd.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture));
                            }

                            pArgs.Add("-i").Add(lTempAudioPath);

                            pArgs.Add("-c:v").Add("libx264").Add("-c:a").Add("aac");

                            List<string> lVideoFilters = new List<string>();
                            List<string> lAudioFilters = new List<string>();

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
                            ReportFFmpegProgress(pLine, lClipDurationSeconds, pProgress, "Мерж и обрезка...", 75, 25, lFfmpegThrottle)))
                        .ExecuteAsync();
                }
            }
            finally
            {
                if (Directory.Exists(lTempDir))
                {
                    Directory.Delete(lTempDir, true);
                }
            }

            pProgress?.Report(new ProgressData { StatusText = "Готово!", Percent = 100 });
        }

        private static async Task DownloadWithYtDlpAsync(string pYtDlpPath, string pUrl, string pFormat, string pOutputPath, IProgress<double> pProgress)
        {
            Regex lRegex = new Regex(@"\[download\]\s+(?<percent>\d+\.?\d*)%", RegexOptions.Compiled);
            StringBuilder lErrorBuilder = new StringBuilder();
            string lFFmpegDir = AppDomain.CurrentDomain.BaseDirectory;

            int lLastReportedPercent = -1;

            CommandResult lResult = await Cli.Wrap(pYtDlpPath)
                .WithArguments(pArgs => pArgs
                    .Add("-f").Add(pFormat)
                    .Add("-o").Add(pOutputPath)
                    .Add("--ffmpeg-location").Add(lFFmpegDir)
                    .Add(pUrl))
                .WithStandardOutputPipe(PipeTarget.ToDelegate(pLine =>
                {
                    Match lMatch = lRegex.Match(pLine);
                    if (lMatch.Success && double.TryParse(lMatch.Groups["percent"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double lPercent))
                    {
                        // Жесткий дроссель: обновляем UI только если изменился целый процент
                        int lCurrentIntPercent = (int)lPercent;
                        if (lCurrentIntPercent != lLastReportedPercent || lPercent >= 100.0)
                        {
                            lLastReportedPercent = lCurrentIntPercent;
                            pProgress?.Report(lPercent / 100.0);
                        }
                    }
                }))
                .WithStandardErrorPipe(PipeTarget.ToDelegate(pLine =>
                {
                    lErrorBuilder.AppendLine(pLine);
                }))
                .WithValidation(CommandResultValidation.None)
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
                using HttpClient lClient = new HttpClient();
                byte[] lBytes = await lClient.GetByteArrayAsync("https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe");
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
                Progress<Xabe.FFmpeg.ProgressInfo> lProgress = new Progress<Xabe.FFmpeg.ProgressInfo>(pInfo =>
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

        private static void ReportFFmpegProgress(string pLine, double pClipDurationSeconds, IProgress<ProgressData> pProgress, string pStatusText, double pBasePercent, double pScale, int[] pThrottleState)
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
                        double lPercent = Math.Clamp(lProgressValue, 0, 1) * pScale;

                        int lTotalIntPercent = (int)(pBasePercent + lPercent);

                        // Дроссель для FFmpeg
                        if (lTotalIntPercent != pThrottleState[0] || lTotalIntPercent >= 100)
                        {
                            pThrottleState[0] = lTotalIntPercent;
                            pProgress?.Report(new ProgressData
                            {
                                StatusText = pStatusText,
                                Percent = pBasePercent + lPercent
                            });
                        }
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
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync(pToken);

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

            return lQualities.OrderBy(pHeight => pHeight).Select(pHeight => $"{pHeight}p").ToList();
        }
    }
}