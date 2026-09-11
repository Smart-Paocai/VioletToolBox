using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SmartTool
{
    internal sealed class Aria2DownloadProgress
    {
        public double Percentage { get; init; }
        public double SpeedMegabytesPerSecond { get; init; }
        public string SpeedText { get; init; } = string.Empty;
        public string EtaText { get; init; } = string.Empty;
        public int ConnectionCount { get; init; }
    }

    internal sealed class Aria2DownloadResult
    {
        public bool Succeeded { get; init; }
        public int? ExitCode { get; init; }
        public string ErrorMessage { get; init; } = string.Empty;
    }

    internal static class Aria2DownloadService
    {
        private static readonly Regex PercentRegex =
            new(@"\((?<value>\d{1,3})%\)", RegexOptions.Compiled);

        private static readonly Regex SpeedRegex =
            new(@"DL:(?<value>[^\s\]]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex EtaRegex =
            new(@"ETA:(?<value>[^\s\]]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex ConnectionRegex =
            new(@"CN:(?<value>\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex SizeValueRegex =
            new(@"^(?<number>\d+(?:\.\d+)?)(?<unit>[KMGTP]?i?B)$",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static string? ResolveExecutablePath()
        {
            var candidates = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "exe", "aria2c.exe"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "aria2c.exe"),
                Path.GetFullPath(Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "..", "..", "..", "..", "exe", "aria2c.exe")),
                Path.GetFullPath(Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "..", "..", "..", "..", "..", "断点续传", "aria2c.exe"))
            };

            return candidates.FirstOrDefault(File.Exists);
        }

        public static string BuildArguments(
            string url,
            string outputDirectory,
            string outputFileName,
            int splitCount = 16,
            IEnumerable<string>? headers = null)
        {
            int split = Math.Clamp(splitCount, 1, 32);
            int maxConnectionPerServer = Math.Min(split, 16);
            var arguments = new List<string>
            {
                "--continue=true",
                "--allow-overwrite=false",
                "--auto-file-renaming=false",
                "--file-allocation=none",
                "--check-certificate=false",
                "--summary-interval=1",
                "--console-log-level=notice",
                "--max-tries=5",
                "--retry-wait=2",
                "--connect-timeout=15",
                "--timeout=30",
                $"--split={split}",
                $"--max-connection-per-server={maxConnectionPerServer}",
                "--min-split-size=1M",
                $"--dir={QuoteArgument(outputDirectory)}"
            };

            if (!string.IsNullOrWhiteSpace(outputFileName))
            {
                arguments.Add($"--out={QuoteArgument(outputFileName)}");
            }

            if (headers != null)
            {
                foreach (string header in headers.Where(value => !string.IsNullOrWhiteSpace(value)))
                {
                    arguments.Add($"--header={QuoteArgument(header.Trim())}");
                }
            }

            arguments.Add(QuoteArgument(url));
            return string.Join(" ", arguments);
        }

        public static async Task<Aria2DownloadResult> DownloadAsync(
            string url,
            string outputPath,
            int splitCount,
            Action<Aria2DownloadProgress>? progressCallback = null,
            CancellationToken cancellationToken = default)
        {
            string? aria2Path = ResolveExecutablePath();
            if (aria2Path == null)
            {
                return new Aria2DownloadResult
                {
                    ErrorMessage = "未找到 aria2c.exe"
                };
            }

            string fullOutputPath = Path.GetFullPath(outputPath);
            string outputDirectory = Path.GetDirectoryName(fullOutputPath)
                ?? throw new InvalidOperationException("下载文件的保存目录无效。");
            string outputFileName = Path.GetFileName(fullOutputPath);
            Directory.CreateDirectory(outputDirectory);

            var startInfo = new ProcessStartInfo
            {
                FileName = aria2Path,
                Arguments = BuildArguments(url, outputDirectory, outputFileName, splitCount),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = outputDirectory
            };

            using var process = new Process
            {
                StartInfo = startInfo
            };

            var recentOutput = new Queue<string>();
            object outputLock = new();

            void HandleOutput(string line)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    return;
                }

                lock (outputLock)
                {
                    recentOutput.Enqueue(line.Trim());
                    while (recentOutput.Count > 12)
                    {
                        recentOutput.Dequeue();
                    }
                }

                if (TryParseProgress(line, out Aria2DownloadProgress progress))
                {
                    progressCallback?.Invoke(progress);
                }
            }

            try
            {
                if (!process.Start())
                {
                    return new Aria2DownloadResult
                    {
                        ErrorMessage = "aria2c 进程启动失败"
                    };
                }

                Task stdoutTask = ConsumeOutputAsync(
                    process.StandardOutput,
                    HandleOutput,
                    cancellationToken);
                Task stderrTask = ConsumeOutputAsync(
                    process.StandardError,
                    HandleOutput,
                    cancellationToken);

                using CancellationTokenRegistration registration = cancellationToken.Register(() =>
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill(entireProcessTree: true);
                        }
                    }
                    catch
                    {
                    }
                });

                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);

                int exitCode = process.ExitCode;
                bool succeeded = exitCode == 0 && File.Exists(fullOutputPath);
                string errorMessage = string.Empty;
                if (!succeeded)
                {
                    lock (outputLock)
                    {
                        errorMessage = recentOutput.Count > 0
                            ? string.Join(" | ", recentOutput)
                            : $"aria2c 退出码：{exitCode}";
                    }
                }

                return new Aria2DownloadResult
                {
                    Succeeded = succeeded,
                    ExitCode = exitCode,
                    ErrorMessage = errorMessage
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new Aria2DownloadResult
                {
                    ErrorMessage = ex.Message
                };
            }
        }

        private static async Task ConsumeOutputAsync(
            StreamReader reader,
            Action<string> outputCallback,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                string? line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line == null)
                {
                    return;
                }

                outputCallback(line);
            }
        }

        private static bool TryParseProgress(
            string line,
            out Aria2DownloadProgress progress)
        {
            Match percentMatch = PercentRegex.Match(line);
            Match speedMatch = SpeedRegex.Match(line);
            Match etaMatch = EtaRegex.Match(line);
            Match connectionMatch = ConnectionRegex.Match(line);

            if (!percentMatch.Success && !speedMatch.Success &&
                !etaMatch.Success && !connectionMatch.Success)
            {
                progress = null!;
                return false;
            }

            double percentage = 0;
            if (percentMatch.Success)
            {
                double.TryParse(
                    percentMatch.Groups["value"].Value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out percentage);
            }

            string speedText = speedMatch.Success
                ? speedMatch.Groups["value"].Value
                : string.Empty;
            string etaText = etaMatch.Success
                ? etaMatch.Groups["value"].Value
                : string.Empty;
            int connectionCount = 0;
            if (connectionMatch.Success)
            {
                int.TryParse(
                    connectionMatch.Groups["value"].Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out connectionCount);
            }

            progress = new Aria2DownloadProgress
            {
                Percentage = Math.Clamp(percentage, 0, 100),
                SpeedMegabytesPerSecond = ConvertSizeTextToMegabytes(speedText),
                SpeedText = speedText,
                EtaText = etaText,
                ConnectionCount = connectionCount
            };
            return true;
        }

        private static double ConvertSizeTextToMegabytes(string value)
        {
            Match match = SizeValueRegex.Match(value.Trim());
            if (!match.Success ||
                !double.TryParse(
                    match.Groups["number"].Value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double number))
            {
                return 0;
            }

            return match.Groups["unit"].Value.ToUpperInvariant() switch
            {
                "B" => number / (1024 * 1024),
                "KB" or "KIB" => number / 1024,
                "MB" or "MIB" => number,
                "GB" or "GIB" => number * 1024,
                "TB" or "TIB" => number * 1024 * 1024,
                "PB" or "PIB" => number * 1024 * 1024 * 1024,
                _ => 0
            };
        }

        private static string QuoteArgument(string value)
        {
            var quoted = new StringBuilder(value.Length + 2);
            quoted.Append('"');

            int pendingBackslashes = 0;
            foreach (char character in value)
            {
                if (character == '\\')
                {
                    pendingBackslashes++;
                    continue;
                }

                if (character == '"')
                {
                    quoted.Append('\\', pendingBackslashes * 2 + 1);
                    quoted.Append('"');
                    pendingBackslashes = 0;
                    continue;
                }

                quoted.Append('\\', pendingBackslashes);
                pendingBackslashes = 0;
                quoted.Append(character);
            }

            quoted.Append('\\', pendingBackslashes * 2);
            quoted.Append('"');
            return quoted.ToString();
        }
    }
}
