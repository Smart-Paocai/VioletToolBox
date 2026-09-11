using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SuperFix.Core;

namespace SuperFix.Services;

public sealed record FastbootCommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Elapsed)
{
    public string CombinedOutput => string.Join(
        Environment.NewLine,
        new[] { StandardOutput.Trim(), StandardError.Trim() }.Where(value => value.Length > 0));
}

public sealed record FastbootPreflight(
    string ExecutablePath,
    string Serial,
    string Product,
    string CurrentSlot,
    string Unlocked,
    ulong SuperSize,
    string IsUserspace);

public sealed class FastbootService
{
    public string ResolveExecutable()
    {
        string executable = Path.Combine(AppContext.BaseDirectory, "platform-tools", "fastboot.exe");
        if (!File.Exists(executable))
        {
            throw new FileNotFoundException(
                "程序组件不完整：缺少 platform-tools\\fastboot.exe，请重新安装或重新解压紫罗兰工具箱",
                executable);
        }
        return executable;
    }

    public async Task<FastbootPreflight> PreflightAsync(
        ulong expectedSuperSize,
        CancellationToken cancellationToken = default,
        bool enforceSuperSize = true)
    {
        FastbootPreflight preflight = await InspectAsync(
            cancellationToken,
            requireSuperSize: enforceSuperSize);
        if (enforceSuperSize && preflight.SuperSize != expectedSuperSize)
        {
            throw new InvalidOperationException(
                $"设备 Super 容量与镜像不一致，已阻止刷写: " +
                $"0x{preflight.SuperSize:X} != 0x{expectedSuperSize:X}");
        }
        return preflight;
    }

    public async Task<FastbootPreflight> InspectAsync(
        CancellationToken cancellationToken = default,
        bool requireSuperSize = true)
    {
        string executable = ResolveExecutable();
        string serial = await GetSingleDeviceSerialAsync(executable, cancellationToken);
        string isUserspace = await GetRequiredVariableAsync(
            executable,
            serial,
            "is-userspace",
            cancellationToken);
        if (!IsBootloaderFastboot(isUserspace))
        {
            throw new InvalidOperationException(
                $"当前是 userspace fastbootd（is-userspace={isUserspace}），请重启到 bootloader fastboot");
        }

        return await ReadPreflightAsync(
            executable,
            serial,
            isUserspace,
            cancellationToken,
            requireSuperSize);
    }

    public async Task<FastbootPreflight> EnsureBootloaderAsync(
        Action<string>? progress = null,
        CancellationToken cancellationToken = default,
        bool requireSuperSize = true)
    {
        string executable = ResolveExecutable();
        string serial = await GetSingleDeviceSerialAsync(executable, cancellationToken);
        string isUserspace = await GetRequiredVariableAsync(
            executable,
            serial,
            "is-userspace",
            cancellationToken);
        if (IsBootloaderFastboot(isUserspace))
            return await ReadPreflightAsync(
                executable,
                serial,
                isUserspace,
                cancellationToken,
                requireSuperSize);

        progress?.Invoke($"检测到 fastbootd（{serial}），正在自动重启到 Bootloader...");
        FastbootCommandResult reboot = await RunAsync(
            executable,
            ["-s", serial],
            ["reboot", "bootloader"],
            TimeSpan.FromSeconds(30),
            cancellationToken);
        if (reboot.ExitCode != 0)
            throw CreateCommandException("fastbootd 重启到 Bootloader 失败", reboot);

        progress?.Invoke("重启命令已接受，正在等待同一台设备进入 Bootloader...");
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(75))
        {
            cancellationToken.ThrowIfCancellationRequested();
            FastbootCommandResult devices = await RunAsync(
                executable,
                [],
                ["devices"],
                TimeSpan.FromSeconds(10),
                cancellationToken);
            if (devices.ExitCode == 0)
            {
                string[] serials = ParseDeviceSerials(devices.CombinedOutput);
                if (serials.Length > 1)
                    throw new InvalidOperationException("设备重启期间检测到多个 Fastboot 设备，已停止自动修复");
                if (serials.Length == 1 &&
                    !serials[0].Equals(serial, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"设备重启后序列号发生变化（{serial} → {serials[0]}），已停止自动修复");
                }
                if (serials.Length == 1)
                {
                    string currentMode = await GetOptionalVariableAsync(
                        executable,
                        serial,
                        "is-userspace",
                        cancellationToken);
                    if (IsBootloaderFastboot(currentMode))
                    {
                        progress?.Invoke("已确认设备进入 Bootloader Fastboot。");
                        return await ReadPreflightAsync(
                            executable,
                            serial,
                            currentMode,
                            cancellationToken,
                            requireSuperSize);
                    }
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(800), cancellationToken);
        }

        throw new TimeoutException(
            $"已发送 fastboot reboot bootloader，但 75 秒内未确认设备 {serial} 进入 Bootloader");
    }

    private static async Task<FastbootPreflight> ReadPreflightAsync(
        string executable,
        string serial,
        string isUserspace,
        CancellationToken cancellationToken,
        bool requireSuperSize)
    {
        string superSizeText = requireSuperSize
            ? await GetRequiredVariableAsync(
                executable,
                serial,
                "partition-size:super",
                cancellationToken)
            : await GetOptionalVariableAsync(
                executable,
                serial,
                "partition-size:super",
                cancellationToken);
        ulong actualSuperSize = TryParseUnsigned(superSizeText, out ulong reportedSuperSize)
            ? reportedSuperSize
            : 0;
        if (requireSuperSize && actualSuperSize == 0)
            throw new InvalidDataException($"Fastboot 返回的分区容量无效: {superSizeText}");

        string product = await GetOptionalVariableAsync(executable, serial, "product", cancellationToken);
        string currentSlot = await GetOptionalVariableAsync(executable, serial, "current-slot", cancellationToken);
        string unlocked = await GetOptionalVariableAsync(executable, serial, "unlocked", cancellationToken);
        return new FastbootPreflight(
            executable,
            serial,
            string.IsNullOrWhiteSpace(product) ? "未知" : product,
            string.IsNullOrWhiteSpace(currentSlot) ? "未知" : currentSlot,
            string.IsNullOrWhiteSpace(unlocked) ? "未知（设备未提供）" : unlocked,
            actualSuperSize,
            isUserspace);
    }

    private static async Task<string> GetSingleDeviceSerialAsync(
        string executable,
        CancellationToken cancellationToken)
    {
        FastbootCommandResult devices = await RunAsync(
            executable,
            [],
            ["devices"],
            TimeSpan.FromSeconds(15),
            cancellationToken);
        if (devices.ExitCode != 0)
            throw CreateCommandException("fastboot devices 执行失败", devices);

        string[] serials = ParseDeviceSerials(devices.CombinedOutput);
        if (serials.Length == 0)
            throw new InvalidOperationException("未检测到 Fastboot 设备");
        if (serials.Length > 1)
            throw new InvalidOperationException("检测到多个 Fastboot 设备，请只连接目标设备");
        return serials[0];
    }

    private static bool IsBootloaderFastboot(string value) =>
        value.Equals("no", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("false", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("0", StringComparison.OrdinalIgnoreCase);

    public async Task<FastbootCommandResult> FlashSuperAsync(
        FastbootPreflight approvedPreflight,
        string imagePath,
        CancellationToken cancellationToken = default,
        bool enforceSuperSize = true)
    {
        EmptySuperVerification image = EmptySuperImageVerifier.VerifyFile(imagePath);
        FastbootPreflight current = await PreflightAsync(
            image.SuperSize,
            cancellationToken,
            enforceSuperSize);
        if (!current.ExecutablePath.Equals(
                approvedPreflight.ExecutablePath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("确认后 Fastboot 程序路径发生变化，已阻止刷写");
        }
        if (!current.Serial.Equals(approvedPreflight.Serial, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("确认后 Fastboot 设备发生变化，已阻止刷写");

        FastbootCommandResult result = await RunAsync(
            current.ExecutablePath,
            ["-s", current.Serial],
            ["flash", "super", Path.GetFullPath(imagePath)],
            timeout: null,
            cancellationToken);
        if (result.ExitCode != 0 ||
            !result.CombinedOutput.Contains("OKAY", StringComparison.OrdinalIgnoreCase))
        {
            throw CreateCommandException("fastboot flash super 未得到成功确认", result);
        }
        return result;
    }

    private static async Task<string> GetRequiredVariableAsync(
        string executable,
        string serial,
        string name,
        CancellationToken cancellationToken)
    {
        FastbootCommandResult result = await RunAsync(
            executable,
            ["-s", serial],
            ["getvar", name],
            TimeSpan.FromSeconds(15),
            cancellationToken);
        if (result.ExitCode != 0)
            throw CreateCommandException($"无法读取 Fastboot 变量 {name}", result);
        string? value = ParseVariable(result.CombinedOutput, name);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Fastboot 未返回变量 {name}");
        return value;
    }

    private static async Task<string> GetOptionalVariableAsync(
        string executable,
        string serial,
        string name,
        CancellationToken cancellationToken)
    {
        FastbootCommandResult result = await RunAsync(
            executable,
            ["-s", serial],
            ["getvar", name],
            TimeSpan.FromSeconds(15),
            cancellationToken);
        return result.ExitCode == 0
            ? ParseVariable(result.CombinedOutput, name) ?? string.Empty
            : string.Empty;
    }

    private static async Task<FastbootCommandResult> RunAsync(
        string executable,
        IReadOnlyList<string> prefixArguments,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory
        };
        foreach (string argument in prefixArguments)
            startInfo.ArgumentList.Add(argument);
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (!process.Start())
                throw new InvalidOperationException("无法启动 fastboot.exe");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new InvalidOperationException($"启动 fastboot.exe 失败: {ex.Message}", ex);
        }

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        if (timeout == null)
        {
            // Flash is one atomic partition command. Do not kill it mid-write.
            await process.WaitForExitAsync(CancellationToken.None);
        }
        else
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout.Value);
            try
            {
                await process.WaitForExitAsync(timeoutSource.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
                throw new TimeoutException($"Fastboot 命令超时（{timeout.Value.TotalSeconds:0} 秒）");
            }
        }

        string stdout = await stdoutTask;
        string stderr = await stderrTask;
        stopwatch.Stop();
        return new FastbootCommandResult(process.ExitCode, stdout, stderr, stopwatch.Elapsed);
    }

    private static string[] ParseDeviceSerials(string output) => output
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(line => Regex.Match(line, @"^(?<serial>\S+)\s+fastboot(?:\s|$)", RegexOptions.IgnoreCase))
        .Where(match => match.Success)
        .Select(match => match.Groups["serial"].Value)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static string? ParseVariable(string output, string name)
    {
        string pattern = $@"(?im)^(?:\(bootloader\)\s*)?{Regex.Escape(name)}\s*:\s*(?<value>.+?)\s*$";
        Match match = Regex.Match(output, pattern);
        return match.Success ? match.Groups["value"].Value.Trim() : null;
    }

    private static bool TryParseUnsigned(string? text, out ulong value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return ulong.TryParse(
                text[2..],
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out value);
        }
        return ulong.TryParse(
            text,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out value);
    }

    private static InvalidOperationException CreateCommandException(
        string message,
        FastbootCommandResult result)
    {
        string native = result.CombinedOutput;
        return new InvalidOperationException(
            string.IsNullOrWhiteSpace(native)
                ? $"{message}（退出码 {result.ExitCode}）"
                : $"{message}（退出码 {result.ExitCode}）{Environment.NewLine}{native}");
    }
}
