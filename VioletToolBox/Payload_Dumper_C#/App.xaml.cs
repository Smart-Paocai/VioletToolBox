using System.Runtime.InteropServices;
using Payload_Dumper_C_.Core;
using System.Windows;

namespace Payload_Dumper_C_
{
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            if (e.Args is { Length: > 0 })
            {
                EnsureConsole();
                RunCliAndShutdownAsync(e.Args);
                return;
            }

            var w = new MainWindow();
            MainWindow = w;
            w.Show();
        }

        private async void RunCliAndShutdownAsync(string[] args)
        {
            int code = 1;
            try
            {
                code = await RunCliAsync(args).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                code = 1;
            }

            await Dispatcher.InvokeAsync(() => Shutdown(code));
        }

        private static async Task<int> RunCliAsync(string[] args)
        {
            var opt = new CliOptions();
            var parseCode = TryParseArgs(args, opt, out var error);
            if (parseCode != 0)
            {
                if (!string.IsNullOrWhiteSpace(error)) Console.Error.WriteLine(error);
                Console.WriteLine(GetHelpText());
                return parseCode;
            }

            if (opt.ShowHelp)
            {
                Console.WriteLine(GetHelpText());
                return 0;
            }

            if (string.IsNullOrWhiteSpace(opt.PayloadFile))
            {
                Console.Error.WriteLine("缺少 payloadfile");
                Console.WriteLine(GetHelpText());
                return 2;
            }

            if (opt.Diff)
            {
                Console.Error.WriteLine("暂不支持差分 OTA（--diff / --old）。");
                return 3;
            }

            using var cts = new CancellationTokenSource();
            try
            {
                await using var source = await PayloadProcessing.OpenSourceAsync(opt.PayloadFile, cts.Token).ConfigureAwait(false);

                if (opt.Metadata)
                {
                    var (path, text) = await PayloadProcessing.ExtractAndroidMetadataAsync(source, opt.OutDir, cts.Token).ConfigureAwait(false);
                    Console.WriteLine(text);

                    if (opt.List || opt.Partitions is not null)
                    {
                    }
                    else
                    {
                        return 0;
                    }
                }

                IProgress<double>? unzipProgress = null;
                if (opt.List || opt.Partitions is not null)
                {
                    unzipProgress = new Progress<double>(_ => { });
                }

                var (payloadReader, _) = await PayloadProcessing.OpenPayloadReaderAsync(
                        source,
                        cts.Token,
                        log: Console.WriteLine,
                        extractProgress: unzipProgress,
                        eagerExtract: false)
                    .ConfigureAwait(false);

                await using var payloadReaderScope = ReferenceEquals(payloadReader, source) ? null : payloadReader;
                var ctx = await PayloadProcessing.ReadManifestAsync(payloadReader, cts.Token).ConfigureAwait(false);

                var parts = PayloadProcessing.GetPartitions(ctx);
                if (opt.List)
                {
                    foreach (var p in parts)
                    {
                        Console.WriteLine($"{p.Name}\t{p.SizeReadable}\t{p.SizeBytes}");
                    }
                    return 0;
                }

                var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(opt.Partitions))
                {
                    foreach (var s in opt.Partitions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        selected.Add(s);
                    }
                }
                else
                {
                    foreach (var p in parts) selected.Add(p.Name);
                }

                if (ctx.Reader is ZipPayloadLazyReader lazy && !lazy.IsExtracted)
                {
                    Console.WriteLine("正在全量解压 payload.bin 以支持导出...");
                    var p = new ConsolePercentProgress();
                    await lazy.EnsureExtractedAsync(p, cts.Token).ConfigureAwait(false);
                    Console.WriteLine();
                }

                int workers = opt.Workers ?? Math.Max(1, Environment.ProcessorCount - 20);

                var opProgress = new Progress<(long DoneOps, long TotalOps)>(p =>
                {
                    if (p.TotalOps <= 0) return;
                    double percent = p.DoneOps * 100d / p.TotalOps;
                    Console.Write($"\r进度: {percent:0}% ({p.DoneOps}/{p.TotalOps})");
                });

                await PayloadProcessing.ExtractPartitionsAsync(
                        ctx,
                        selected,
                        opt.OutDir,
                        workers,
                        log: Console.WriteLine,
                        progress: opProgress,
                        cancellationToken: cts.Token)
                    .ConfigureAwait(false);

                Console.WriteLine();
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }

        private sealed class CliOptions
        {
            public string? PayloadFile { get; set; }
            public string OutDir { get; set; } = "output";
            public bool Diff { get; set; }
            public string? OldDir { get; set; }
            public string? Partitions { get; set; }
            public int? Workers { get; set; }
            public bool List { get; set; }
            public bool Metadata { get; set; }
            public bool ShowHelp { get; set; }
        }

        private static int TryParseArgs(string[] args, CliOptions opt, out string? error)
        {
            error = null;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a is "-h" or "--help")
                {
                    opt.ShowHelp = true;
                    return 0;
                }

                if (a == "--out")
                {
                    if (i + 1 >= args.Length) { error = "--out 缺少参数"; return 2; }
                    opt.OutDir = args[++i];
                    continue;
                }

                if (a == "--diff")
                {
                    opt.Diff = true;
                    continue;
                }

                if (a == "--old")
                {
                    if (i + 1 >= args.Length) { error = "--old 缺少参数"; return 2; }
                    opt.OldDir = args[++i];
                    continue;
                }

                if (a == "--partitions")
                {
                    if (i + 1 >= args.Length) { error = "--partitions 缺少参数"; return 2; }
                    opt.Partitions = args[++i];
                    continue;
                }

                if (a == "--workers")
                {
                    if (i + 1 >= args.Length) { error = "--workers 缺少参数"; return 2; }
                    if (!int.TryParse(args[++i], out var w) || w <= 0) { error = "--workers 必须是正整数"; return 2; }
                    opt.Workers = w;
                    continue;
                }

                if (a == "--list")
                {
                    opt.List = true;
                    continue;
                }

                if (a == "--metadata")
                {
                    opt.Metadata = true;
                    continue;
                }

                if (a.StartsWith("-", StringComparison.Ordinal))
                {
                    error = $"未知参数: {a}";
                    return 2;
                }

                if (opt.PayloadFile is null)
                {
                    opt.PayloadFile = a;
                    continue;
                }

                error = $"多余的参数: {a}";
                return 2;
            }

            return 0;
        }

        private static string GetHelpText()
        {
            return
                "usage: violet_payload.exe [-h] [--out OUT] [--diff] [--old OLD] [--partitions PARTITIONS] [--workers WORKERS] [--list] [--metadata] payloadfile\n\n" +
                "OTA payload dumper\n\n" +
                "positional arguments:\n" +
                "  payloadfile           payload file name (payload.bin / OTA ZIP / URL)\n\n" +
                "options:\n" +
                "  -h, --help            show this help message and exit\n" +
                "  --out OUT             output directory (default: 'output')\n" +
                "  --diff                extract differential OTA (not supported yet)\n" +
                "  --old OLD             directory with original images for differential OTA (not supported yet)\n" +
                "  --partitions PARTITIONS\n" +
                "                        comma separated list of partitions to extract (default: extract all)\n" +
                "  --workers WORKERS     number of workers (default: CPU count - 20)\n" +
                "  --list                list partitions in the payload file\n" +
                "  --metadata            extract and display metadata file from the OTA ZIP\n";
        }

        private sealed class ConsolePercentProgress : IProgress<double>
        {
            private int _last = -1;

            public void Report(double value)
            {
                int v = (int)Math.Round(Math.Clamp(value, 0, 100));
                if (v == _last) return;
                _last = v;
                Console.Write($"\r解压: {v}%");
            }
        }

        private static void EnsureConsole()
        {
            if (AttachConsole(ATTACH_PARENT_PROCESS)) return;
            AllocConsole();
        }

        private const int ATTACH_PARENT_PROCESS = -1;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();
    }

}
