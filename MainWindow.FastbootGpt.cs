using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Xml.Linq;

namespace WpfApp1
{
    public partial class MainWindow
    {
        private CancellationTokenSource? _fastbootVisualizationOperationCancellation;
        private bool _fastbootVisualizationStopAtCommandBoundary;
        private bool _fastbootVisualizationWaitingForDevice;
        private bool _fastbootVisualizationStopRequestedWhileWaiting;

        private sealed class AdbGptPartition
        {
            public string Name { get; init; } = "";
            public int Lun { get; init; }
            public int SectorSize { get; init; }
            public ulong FirstLba { get; init; }
            public ulong LastLba { get; init; }
            public ulong SectorCount => LastLba >= FirstLba ? LastLba - FirstLba + 1 : 0;
        }

        private sealed class AdbGptDisk
        {
            public string BlockName { get; init; } = "";
            public int Lun { get; init; }
            public int SectorSize { get; init; }
            public ulong TotalLogicalSectors { get; init; }
            public ulong MainSectorCount { get; init; }
            public ulong BackupStartLba { get; init; }
            public ulong BackupSectorCount { get; init; }
            public byte[] MainBytes { get; init; } = Array.Empty<byte>();
            public byte[] BackupBytes { get; init; } = Array.Empty<byte>();
            public List<AdbGptPartition> Partitions { get; init; } = new();
        }

        private sealed class RawProgramEntry
        {
            public string FileName { get; init; } = "";
            public string Label { get; init; } = "";
            public int Lun { get; init; }
            public int SectorSize { get; init; }
            public ulong StartSector { get; init; }
            public ulong SectorCount { get; init; }
        }

        private async void BackupGptButton_Click(object sender, RoutedEventArgs e)
        {
            CancellationTokenSource? operationCancellation = null;
            BackupGptButton.IsEnabled = false;
            try
            {
                string adbPath = GetAdbPath();
                var device = await ResolveRootAdbDeviceAsync(adbPath);
                if (!device.Success)
                {
                    LogToFastboot(device.Error, "Red");
                    return;
                }

                LogToFastbootStyled(
                    ("连接设备...", "Black", false),
                    ($"[{device.Serial}]", "Purple", true));
                LogToFastbootStyled(
                    ("已授予 ", "Black", false),
                    ("Shell ROOT", "Blue", true),
                    (" 权限", "Black", false));

                string parentDirectory = SelectSaveDirectory("请选择保存GPT备份的目录");
                if (string.IsNullOrWhiteSpace(parentDirectory))
                {
                    LogToFastboot("用户取消了选择保存目录", "Yellow");
                    return;
                }

                string saveDirectory = Path.Combine(
                    parentDirectory,
                    $"GPT_Backup_{DateTime.Now:yyyyMMdd_HHmmss}");
                Directory.CreateDirectory(saveDirectory);
                LogToFastbootStyled(("保存路径：", "Black", false), (saveDirectory, "Blue", false));

                operationCancellation = BeginFastbootVisualizationOperation();
                CancellationToken cancellationToken = operationCancellation.Token;
                _transferStartTime = DateTime.Now;
                ResetOperationTransferDisplay();
                List<AdbGptDisk> disks = await ReadDeviceGptDisksAsync(
                    adbPath,
                    device.Serial,
                    async disk =>
                    {
                        string mainName = $"gpt_main{disk.Lun}.bin";
                        string backupName = $"gpt_backup{disk.Lun}.bin";
                        await File.WriteAllBytesAsync(
                            Path.Combine(saveDirectory, mainName),
                            disk.MainBytes,
                            cancellationToken);
                        await File.WriteAllBytesAsync(
                            Path.Combine(saveDirectory, backupName),
                            disk.BackupBytes,
                            cancellationToken);
                        LogToFastbootStyled(
                            ($"LUN{disk.Lun} GPT", "Black", false),
                            ("...OK", "Green", true));
                    },
                    UpdateGptReadProgress,
                    cancellationToken);
                if (disks.Count == 0)
                {
                    LogToFastboot("未检测到有效GPT，请确认设备存储类型和ROOT权限", "Red");
                    return;
                }
                CompleteOperationTransferDisplay();

                if (GenerateRawProgramXmlCheckBox?.IsChecked == true)
                {
                    GenerateRawProgramXmlFiles(saveDirectory, disks, new Dictionary<string, string>());
                }

                LogToFastbootStyled(
                    ("GPT备份完成，共", "Black", false),
                    ($"{disks.Count}", "Purple", true),
                    ("个物理LUN", "Black", false));
                OpenDirectorySilently(saveDirectory);
            }
            catch (OperationCanceledException)
            {
                LogToFastbootStyled(
                    ("GPT备份", "Black", false),
                    ("...已停止", "Red", true));
            }
            catch (Exception ex)
            {
                LogToFastboot($"GPT备份失败：{ex.Message}", "Red");
            }
            finally
            {
                EndFastbootVisualizationOperation(operationCancellation);
                UpdatePartitionButtonStates();
            }
        }

        private CancellationTokenSource BeginFastbootVisualizationOperation(
            bool stopAtCommandBoundary = false)
        {
            if (_fastbootVisualizationOperationCancellation != null)
            {
                throw new InvalidOperationException("当前页面已有操作正在执行");
            }

            var cancellation = new CancellationTokenSource();
            _fastbootVisualizationOperationCancellation = cancellation;
            _fastbootVisualizationStopAtCommandBoundary = stopAtCommandBoundary;
            SetFastbootVisualizationOperationButtonsEnabled(false);
            StopFastbootVisualizationOperationButton.IsEnabled = true;
            return cancellation;
        }

        private void EndFastbootVisualizationOperation(CancellationTokenSource? cancellation)
        {
            if (cancellation == null)
            {
                return;
            }

            if (ReferenceEquals(_fastbootVisualizationOperationCancellation, cancellation))
            {
                _fastbootVisualizationOperationCancellation = null;
                _fastbootVisualizationStopAtCommandBoundary = false;
                _fastbootVisualizationWaitingForDevice = false;
                _fastbootVisualizationStopRequestedWhileWaiting = false;
                StopFastbootVisualizationOperationButton.IsEnabled = false;
                SetFastbootVisualizationOperationButtonsEnabled(true);
            }

            cancellation.Dispose();
        }

        private void StopFastbootVisualizationOperationButton_Click(object sender, RoutedEventArgs e)
        {
            CancellationTokenSource? cancellation = _fastbootVisualizationOperationCancellation;
            if (cancellation == null || cancellation.IsCancellationRequested)
            {
                return;
            }

            StopFastbootVisualizationOperationButton.IsEnabled = false;
            if (_fastbootVisualizationWaitingForDevice)
            {
                _fastbootVisualizationStopRequestedWhileWaiting = true;
            }
            else if (_fastbootVisualizationStopAtCommandBoundary)
            {
                LogToFastbootStyled(
                    ("用户请求停止操作，等待当前分区刷写完成", "Black", false),
                    ("...", "Orange", true));
            }
            else
            {
                LogToFastbootStyled(
                    ("正在停止当前操作", "Black", false),
                    ("...", "Orange", true));
            }
            cancellation.Cancel();
        }

        private void SetFastbootVisualizationOperationButtonsEnabled(bool isEnabled)
        {
            bool adbOnlyEnabled = isEnabled && string.Equals(
                BottomConnectionTypeText?.Text,
                "系统",
                StringComparison.OrdinalIgnoreCase);
            AdbReadPartitionTableButton.IsEnabled = isEnabled;
            ReadPartitionTableButton.IsEnabled = isEnabled;
            WritePartitionButton.IsEnabled = isEnabled;
            ErasePartitionButton.IsEnabled = isEnabled;
            ReadPartitionButton.IsEnabled = adbOnlyEnabled;
            BackupBasebandButton.IsEnabled = adbOnlyEnabled;
            BackupGptButton.IsEnabled = adbOnlyEnabled;
        }

        private void LogFastbootVisualizationOperationStopped(string operationName)
        {
            if (_fastbootVisualizationStopRequestedWhileWaiting)
            {
                LogToFastbootStyled(
                    ("等待设备", "Black", false),
                    ("...已停止", "Red", true));
                return;
            }

            LogToFastbootStyled(
                (operationName, "Black", false),
                ("...已停止", "Red", true));
        }

        private async Task TryGenerateRawProgramXmlAsync(
            string adbPath,
            string saveDirectory,
            IReadOnlyDictionary<string, string> imageFiles,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (GenerateRawProgramXmlCheckBox?.IsChecked != true || imageFiles.Count == 0)
            {
                return;
            }

            var device = await ResolveRootAdbDeviceAsync(adbPath);
            cancellationToken.ThrowIfCancellationRequested();
            if (!device.Success)
            {
                LogToFastboot($"识别镜像LUN失败：{device.Error}", "Red");
                return;
            }

            _transferStartTime = DateTime.Now;
            ResetOperationTransferDisplay();
            List<AdbGptDisk> disks = await ReadDeviceGptDisksAsync(
                adbPath,
                device.Serial,
                onDiskCompleted: null,
                onProbeProgress: UpdateGptReadProgress,
                cancellationToken: cancellationToken);
            if (disks.Count == 0)
            {
                LogToFastboot("识别镜像LUN失败：未读取到有效GPT", "Red");
                return;
            }
            CompleteOperationTransferDisplay();

            var partitionsByName = disks
                .SelectMany(disk => disk.Partitions)
                .GroupBy(partition => partition.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
            var renamedImageFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var relevantLuns = new HashSet<int>();

            foreach ((string partitionName, string sourcePath) in imageFiles)
            {
                string currentPath = sourcePath;
                if (!File.Exists(sourcePath) ||
                    !partitionsByName.TryGetValue(partitionName, out List<AdbGptPartition>? matches) ||
                    matches.Count != 1)
                {
                    renamedImageFiles[partitionName] = sourcePath;
                    continue;
                }

                AdbGptPartition match = matches[0];
                relevantLuns.Add(match.Lun);
                string extension = Path.GetExtension(sourcePath);
                string targetPath = Path.Combine(
                    Path.GetDirectoryName(sourcePath) ?? saveDirectory,
                    $"LUN{match.Lun}_{partitionName}{extension}");

                if (!sourcePath.Equals(targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(targetPath))
                    {
                        LogToFastboot($"文件重命名失败：{Path.GetFileName(targetPath)} 已存在", "Yellow");
                    }
                    else
                    {
                        File.Move(sourcePath, targetPath);
                        currentPath = targetPath;
                    }
                }

                renamedImageFiles[partitionName] = currentPath;
            }

            List<AdbGptDisk> relevantDisks = disks
                .Where(disk => relevantLuns.Contains(disk.Lun))
                .ToList();
            if (relevantDisks.Count == 0)
            {
                LogToFastboot("生成XML失败：所选分区未在物理GPT中唯一匹配", "Red");
                return;
            }

            foreach (AdbGptDisk disk in relevantDisks)
            {
                await File.WriteAllBytesAsync(
                    Path.Combine(saveDirectory, $"gpt_main{disk.Lun}.bin"),
                    disk.MainBytes);
                await File.WriteAllBytesAsync(
                    Path.Combine(saveDirectory, $"gpt_backup{disk.Lun}.bin"),
                    disk.BackupBytes);
            }

            List<string> generatedFiles = GenerateRawProgramXmlFiles(saveDirectory, relevantDisks, renamedImageFiles);
            foreach (string generatedFile in generatedFiles)
            {
                LogToFastbootStyled(
                    ($"生成{generatedFile}", "Black", false),
                    ("...Done", "Green", true));
            }
        }

        private List<string> GenerateRawProgramXmlFiles(
            string saveDirectory,
            IReadOnlyCollection<AdbGptDisk> disks,
            IReadOnlyDictionary<string, string> imageFiles)
        {
            var entriesByLun = new Dictionary<int, List<RawProgramEntry>>();
            var partitionsByName = disks
                .SelectMany(disk => disk.Partitions)
                .GroupBy(partition => partition.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

            foreach ((string partitionName, string imagePath) in imageFiles)
            {
                if (!File.Exists(imagePath))
                {
                    continue;
                }

                if (!partitionsByName.TryGetValue(partitionName, out List<AdbGptPartition>? matches) ||
                    matches.Count != 1)
                {
                    LogToFastboot($"生成XML时跳过 {partitionName}：未在物理GPT中唯一匹配", "Yellow");
                    continue;
                }

                AdbGptPartition partition = matches[0];
                ulong expectedBytes = checked(partition.SectorCount * (ulong)partition.SectorSize);
                ulong actualBytes = (ulong)new FileInfo(imagePath).Length;
                if (actualBytes != expectedBytes)
                {
                    LogToFastboot(
                        $"生成XML时跳过 {partitionName}：镜像大小与GPT记录不一致",
                        "Red");
                    continue;
                }

                AddRawProgramEntry(entriesByLun, new RawProgramEntry
                {
                    FileName = Path.GetFileName(imagePath),
                    Label = partition.Name,
                    Lun = partition.Lun,
                    SectorSize = partition.SectorSize,
                    StartSector = partition.FirstLba,
                    SectorCount = partition.SectorCount
                });
            }

            foreach (AdbGptDisk disk in disks)
            {
                string mainName = $"gpt_main{disk.Lun}.bin";
                string backupName = $"gpt_backup{disk.Lun}.bin";
                if (File.Exists(Path.Combine(saveDirectory, mainName)))
                {
                    AddRawProgramEntry(entriesByLun, new RawProgramEntry
                    {
                        FileName = mainName,
                        Label = "PrimaryGPT",
                        Lun = disk.Lun,
                        SectorSize = disk.SectorSize,
                        StartSector = 0,
                        SectorCount = disk.MainSectorCount
                    });
                }

                if (File.Exists(Path.Combine(saveDirectory, backupName)))
                {
                    AddRawProgramEntry(entriesByLun, new RawProgramEntry
                    {
                        FileName = backupName,
                        Label = "BackupGPT",
                        Lun = disk.Lun,
                        SectorSize = disk.SectorSize,
                        StartSector = disk.BackupStartLba,
                        SectorCount = disk.BackupSectorCount
                    });
                }
            }

            var generatedFiles = new List<string>();
            foreach ((int lun, List<RawProgramEntry> entries) in entriesByLun.OrderBy(item => item.Key))
            {
                if (entries.Count == 0)
                {
                    continue;
                }

                var data = new XElement("data",
                    entries
                        .OrderBy(entry => entry.StartSector)
                        .Select(entry => new XElement("program",
                            new XAttribute("SECTOR_SIZE_IN_BYTES", entry.SectorSize),
                            new XAttribute("file_sector_offset", "0"),
                            new XAttribute("filename", entry.FileName),
                            new XAttribute("label", entry.Label),
                            new XAttribute("num_partition_sectors", entry.SectorCount.ToString(CultureInfo.InvariantCulture)),
                            new XAttribute("physical_partition_number", entry.Lun),
                            new XAttribute("size_in_KB", ((decimal)entry.SectorCount * entry.SectorSize / 1024m).ToString("0.###", CultureInfo.InvariantCulture)),
                            new XAttribute("sparse", "false"),
                            new XAttribute("start_byte_hex", $"0x{entry.StartSector * (ulong)entry.SectorSize:X}"),
                            new XAttribute("start_sector", entry.StartSector.ToString(CultureInfo.InvariantCulture)))));
                var document = new XDocument(new XDeclaration("1.0", "utf-8", null), data);
                string xmlFileName = $"rawprogram{lun}.xml";
                string xmlPath = Path.Combine(saveDirectory, xmlFileName);
                document.Save(xmlPath);
                generatedFiles.Add(xmlFileName);
            }

            return generatedFiles;
        }

        private static void AddRawProgramEntry(
            IDictionary<int, List<RawProgramEntry>> entriesByLun,
            RawProgramEntry entry)
        {
            if (!entriesByLun.TryGetValue(entry.Lun, out List<RawProgramEntry>? entries))
            {
                entries = new List<RawProgramEntry>();
                entriesByLun[entry.Lun] = entries;
            }
            entries.Add(entry);
        }

        private async Task<(bool Success, string Serial, string Error)> ResolveRootAdbDeviceAsync(string adbPath)
        {
            if (string.IsNullOrWhiteSpace(adbPath) || !File.Exists(adbPath))
            {
                return (false, "", "未找到ADB工具");
            }

            string devicesOutput = await GetCommandOutput(adbPath, "devices");
            List<(string Serial, string State)> devices = devicesOutput
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => Regex.Match(line.Trim(), @"^(\S+)\s+(device|unauthorized|offline)$", RegexOptions.IgnoreCase))
                .Where(match => match.Success)
                .Select(match => (match.Groups[1].Value, match.Groups[2].Value.ToLowerInvariant()))
                .ToList();

            string selectedSerial = GetSelectedDeviceSerial();
            (string Serial, string State) target = !string.IsNullOrWhiteSpace(selectedSerial)
                ? devices.FirstOrDefault(device => device.Serial.Equals(selectedSerial, StringComparison.OrdinalIgnoreCase))
                : devices.Count == 1 ? devices[0] : default;

            if (string.IsNullOrWhiteSpace(target.Serial))
            {
                return devices.Count > 1
                    ? (false, "", "检测到多个ADB设备，请先选择目标设备")
                    : (false, "", "连接设备...未检测到设备");
            }
            if (!target.State.Equals("device", StringComparison.OrdinalIgnoreCase))
            {
                return (false, target.Serial, "ADB设备未授权或处于离线状态");
            }

            string rootOutput = await GetCommandOutput(
                adbPath,
                $"-s {target.Serial} shell \"su -c 'id'\"");
            if (!Regex.IsMatch(rootOutput ?? "", @"\buid=0\b", RegexOptions.IgnoreCase))
            {
                return (false, target.Serial, "未授予Shell ROOT权限");
            }

            return (true, target.Serial, "");
        }

        private async Task<List<AdbGptDisk>> ReadDeviceGptDisksAsync(
            string adbPath,
            string serial,
            Func<AdbGptDisk, Task>? onDiskCompleted = null,
            Action<int, int>? onProbeProgress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = new List<AdbGptDisk>();
            string availableBlocksOutput = await GetCommandOutput(
                adbPath,
                $"-s {serial} shell \"su -c 'ls -1 /sys/class/block'\"");
            cancellationToken.ThrowIfCancellationRequested();
            string[] candidates = (availableBlocksOutput ?? "")
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(name => Regex.IsMatch(name, @"^sd[a-z]$", RegexOptions.IgnoreCase) ||
                               name.Equals("mmcblk0", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            int processedBlockCount = 0;
            foreach (string blockName in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    string probe = await GetCommandOutput(
                        adbPath,
                        $"-s {serial} shell \"su -c 'test -b /dev/block/{blockName} && cat /sys/class/block/{blockName}/queue/logical_block_size && cat /sys/class/block/{blockName}/size && readlink -f /sys/class/block/{blockName}/device'\"");
                    cancellationToken.ThrowIfCancellationRequested();
                    string[] lines = (probe ?? "")
                        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(line => line.Trim())
                        .ToArray();
                    if (lines.Length < 2 ||
                        !int.TryParse(lines[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int sectorSize) ||
                        !ulong.TryParse(lines[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong linux512Sectors) ||
                        sectorSize < 512 || sectorSize > 65536 || (sectorSize & (sectorSize - 1)) != 0)
                    {
                        continue;
                    }

                    ulong totalBytes = checked(linux512Sectors * 512UL);
                    ulong logicalSectors = totalBytes / (ulong)sectorSize;
                    if (logicalSectors < 6)
                    {
                        continue;
                    }

                    int lun = ResolveLunNumber(blockName, lines.Skip(2).FirstOrDefault() ?? "");
                    AdbGptDisk? disk;
                    try
                    {
                        disk = await ReadAndValidateGptDiskAsync(
                            adbPath,
                            serial,
                            blockName,
                            lun,
                            sectorSize,
                            logicalSectors,
                            cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"GPT probe skipped {blockName}: {ex.Message}");
                        continue;
                    }

                    if (disk != null && result.All(item => item.Lun != disk.Lun))
                    {
                        result.Add(disk);
                        if (onDiskCompleted != null)
                        {
                            await onDiskCompleted(disk);
                            cancellationToken.ThrowIfCancellationRequested();
                        }
                    }
                }
                finally
                {
                    processedBlockCount++;
                    onProbeProgress?.Invoke(processedBlockCount, candidates.Length);
                }
            }

            return result
                .GroupBy(disk => disk.Lun)
                .Select(group => group.First())
                .OrderBy(disk => disk.Lun)
                .ToList();
        }

        private void UpdateGptReadProgress(int completed, int total)
        {
            double percent = total <= 0
                ? 0
                : Math.Clamp(completed * 100d / total, 0, 100);
            SetOperationTransferProgress(percent);
            UpdateOperationTransferElapsed();
        }

        private static int ResolveLunNumber(string blockName, string devicePath)
        {
            MatchCollection matches = Regex.Matches(devicePath ?? "", @":(\d+)(?:/|$)");
            if (matches.Count > 0 &&
                int.TryParse(matches[^1].Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int lun))
            {
                return lun;
            }

            if (Regex.IsMatch(blockName, @"^sd[a-z]$", RegexOptions.IgnoreCase))
            {
                return char.ToLowerInvariant(blockName[2]) - 'a';
            }
            return 0;
        }

        private async Task<AdbGptDisk?> ReadAndValidateGptDiskAsync(
            string adbPath,
            string serial,
            string blockName,
            int lun,
            int sectorSize,
            ulong totalLogicalSectors,
            CancellationToken cancellationToken)
        {
            byte[] initial = await ReadAdbBlockRangeAsync(
                adbPath, serial, blockName, sectorSize, 0, 2, cancellationToken);
            if (initial.Length < sectorSize * 2 ||
                Encoding.ASCII.GetString(initial, sectorSize, 8) != "EFI PART")
            {
                return null;
            }

            byte[] primaryHeader = initial.Skip(sectorSize).Take(sectorSize).ToArray();
            ulong backupHeaderLba = BinaryPrimitives.ReadUInt64LittleEndian(primaryHeader.AsSpan(32, 8));
            ulong entryLba = BinaryPrimitives.ReadUInt64LittleEndian(primaryHeader.AsSpan(72, 8));
            uint entryCount = BinaryPrimitives.ReadUInt32LittleEndian(primaryHeader.AsSpan(80, 4));
            uint entrySize = BinaryPrimitives.ReadUInt32LittleEndian(primaryHeader.AsSpan(84, 4));
            ValidateGptGeometry(entryLba, entryCount, entrySize, backupHeaderLba, totalLogicalSectors, sectorSize);

            ulong entryBytes = checked((ulong)entryCount * entrySize);
            ulong mainSectorCount = DivideRoundUp(checked(entryLba * (ulong)sectorSize + entryBytes), (ulong)sectorSize);
            byte[] mainBytes = await ReadAdbBlockRangeAsync(
                adbPath, serial, blockName, sectorSize, 0, mainSectorCount, cancellationToken);
            ValidateGptHeaderAndEntries(mainBytes, checked((int)(entryLba * (ulong)sectorSize)), sectorSize, entryCount, entrySize);

            byte[] backupHeaderBytes = await ReadAdbBlockRangeAsync(
                adbPath, serial, blockName, sectorSize, backupHeaderLba, 1, cancellationToken);
            if (backupHeaderBytes.Length < sectorSize || Encoding.ASCII.GetString(backupHeaderBytes, 0, 8) != "EFI PART")
            {
                throw new InvalidDataException("备份GPT Header签名无效");
            }
            ulong backupEntryLba = BinaryPrimitives.ReadUInt64LittleEndian(backupHeaderBytes.AsSpan(72, 8));
            if (backupEntryLba >= backupHeaderLba)
            {
                throw new InvalidDataException("备份GPT条目位置无效");
            }
            ulong backupSectorCount = backupHeaderLba - backupEntryLba + 1;
            byte[] backupBytes = await ReadAdbBlockRangeAsync(
                adbPath, serial, blockName, sectorSize, backupEntryLba, backupSectorCount, cancellationToken);
            int backupHeaderOffset = checked((int)((backupHeaderLba - backupEntryLba) * (ulong)sectorSize));
            ValidateGptHeaderAndEntries(backupBytes, 0, sectorSize, entryCount, entrySize, backupHeaderOffset);

            byte[] primaryEntries = mainBytes
                .Skip(checked((int)(entryLba * (ulong)sectorSize)))
                .Take(checked((int)entryBytes))
                .ToArray();
            byte[] backupEntries = backupBytes.Take(checked((int)entryBytes)).ToArray();
            if (!primaryEntries.SequenceEqual(backupEntries))
            {
                throw new InvalidDataException("主、备GPT分区条目不一致");
            }

            return new AdbGptDisk
            {
                BlockName = blockName,
                Lun = lun,
                SectorSize = sectorSize,
                TotalLogicalSectors = totalLogicalSectors,
                MainSectorCount = mainSectorCount,
                BackupStartLba = backupEntryLba,
                BackupSectorCount = backupSectorCount,
                MainBytes = mainBytes,
                BackupBytes = backupBytes,
                Partitions = ParseGptPartitions(primaryEntries, entryCount, entrySize, lun, sectorSize)
            };
        }

        private static void ValidateGptGeometry(
            ulong entryLba,
            uint entryCount,
            uint entrySize,
            ulong backupHeaderLba,
            ulong totalLogicalSectors,
            int sectorSize)
        {
            if (entryLba < 2 || entryCount == 0 || entryCount > 16384 ||
                entrySize < 128 || entrySize > 4096 || entrySize % 8 != 0 ||
                backupHeaderLba <= entryLba || backupHeaderLba >= totalLogicalSectors)
            {
                throw new InvalidDataException("GPT几何信息无效");
            }

            ulong bytes = checked((ulong)entryCount * entrySize);
            if (bytes > 64UL * 1024 * 1024 || entryLba + DivideRoundUp(bytes, (ulong)sectorSize) >= backupHeaderLba)
            {
                throw new InvalidDataException("GPT条目区域超出安全范围");
            }
        }

        private static void ValidateGptHeaderAndEntries(
            byte[] region,
            int entriesOffset,
            int sectorSize,
            uint expectedEntryCount,
            uint expectedEntrySize,
            int? headerOffsetOverride = null)
        {
            int headerOffset = headerOffsetOverride ?? sectorSize;
            if (headerOffset < 0 || headerOffset + sectorSize > region.Length ||
                Encoding.ASCII.GetString(region, headerOffset, 8) != "EFI PART")
            {
                throw new InvalidDataException("GPT Header签名无效");
            }

            ReadOnlySpan<byte> header = region.AsSpan(headerOffset, sectorSize);
            uint headerSize = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(12, 4));
            uint storedHeaderCrc = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(16, 4));
            uint entryCount = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(80, 4));
            uint entrySize = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(84, 4));
            uint storedEntriesCrc = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(88, 4));
            if (headerSize < 92 || headerSize > sectorSize || entryCount != expectedEntryCount || entrySize != expectedEntrySize)
            {
                throw new InvalidDataException("GPT Header字段无效");
            }

            byte[] headerForCrc = header.Slice(0, checked((int)headerSize)).ToArray();
            Array.Clear(headerForCrc, 16, 4);
            if (ComputeCrc32(headerForCrc) != storedHeaderCrc)
            {
                throw new InvalidDataException("GPT Header CRC校验失败");
            }

            int entryBytes = checked((int)((ulong)entryCount * entrySize));
            if (entriesOffset < 0 || entriesOffset + entryBytes > region.Length ||
                ComputeCrc32(region.AsSpan(entriesOffset, entryBytes)) != storedEntriesCrc)
            {
                throw new InvalidDataException("GPT分区条目CRC校验失败");
            }
        }

        private static List<AdbGptPartition> ParseGptPartitions(
            ReadOnlySpan<byte> entries,
            uint entryCount,
            uint entrySize,
            int lun,
            int sectorSize)
        {
            var result = new List<AdbGptPartition>();
            for (int index = 0; index < entryCount; index++)
            {
                ReadOnlySpan<byte> entry = entries.Slice(checked(index * (int)entrySize), checked((int)entrySize));
                bool emptyTypeGuid = true;
                for (int i = 0; i < 16; i++)
                {
                    if (entry[i] != 0)
                    {
                        emptyTypeGuid = false;
                        break;
                    }
                }
                if (emptyTypeGuid)
                {
                    continue;
                }

                ulong firstLba = BinaryPrimitives.ReadUInt64LittleEndian(entry.Slice(32, 8));
                ulong lastLba = BinaryPrimitives.ReadUInt64LittleEndian(entry.Slice(40, 8));
                int nameLength = Math.Min(72, entry.Length - 56);
                string name = Encoding.Unicode.GetString(entry.Slice(56, nameLength)).TrimEnd('\0').Trim();
                if (string.IsNullOrWhiteSpace(name) || lastLba < firstLba)
                {
                    continue;
                }

                result.Add(new AdbGptPartition
                {
                    Name = name,
                    Lun = lun,
                    SectorSize = sectorSize,
                    FirstLba = firstLba,
                    LastLba = lastLba
                });
            }
            return result;
        }

        private async Task<byte[]> ReadAdbBlockRangeAsync(
            string adbPath,
            string serial,
            string blockName,
            int sectorSize,
            ulong startSector,
            ulong sectorCount,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sectorCount == 0 || sectorCount > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(sectorCount));
            }

            string temporaryFile = Path.Combine(Path.GetTempPath(), $"violet_gpt_{Guid.NewGuid():N}.bin");
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = adbPath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                startInfo.ArgumentList.Add("-s");
                startInfo.ArgumentList.Add(serial);
                startInfo.ArgumentList.Add("exec-out");
                startInfo.ArgumentList.Add("su");
                startInfo.ArgumentList.Add("-c");
                startInfo.ArgumentList.Add(
                    $"dd if=/dev/block/{blockName} bs={sectorSize} skip={startSector} count={sectorCount} 2>/dev/null");

                using Process process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("无法启动ADB进程");
                try
                {
                    Task<string> errorTask = process.StandardError.ReadToEndAsync();
                    await using (var output = new FileStream(
                        temporaryFile, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, true))
                    {
                        await process.StandardOutput.BaseStream.CopyToAsync(output, cancellationToken);
                    }
                    await process.WaitForExitAsync(cancellationToken);
                    string error = await errorTask;
                    if (process.ExitCode != 0)
                    {
                        throw new IOException(string.IsNullOrWhiteSpace(error) ? "ADB读取块设备失败" : error.Trim());
                    }

                    byte[] bytes = await File.ReadAllBytesAsync(temporaryFile, cancellationToken);
                    ulong expected = checked((ulong)sectorSize * sectorCount);
                    if ((ulong)bytes.LongLength != expected)
                    {
                        throw new EndOfStreamException($"块设备读取长度异常：期望{expected}，实际{bytes.LongLength}");
                    }
                    return bytes;
                }
                catch (OperationCanceledException)
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
                    throw;
                }
            }
            finally
            {
                try { File.Delete(temporaryFile); } catch { }
            }
        }

        private static uint ComputeCrc32(ReadOnlySpan<byte> data)
        {
            uint crc = 0xFFFFFFFFu;
            foreach (byte value in data)
            {
                crc ^= value;
                for (int bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
                }
            }
            return ~crc;
        }

        private string? ShowPartitionBackupFormatDialog(string operationName)
        {
            var dialog = new System.Windows.Window
            {
                Title = "选择镜像格式",
                Width = 430,
                Height = 220,
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Background = System.Windows.Media.Brushes.White,
                FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI")
            };

            var root = new System.Windows.Controls.Grid
            {
                Margin = new Thickness(24, 20, 24, 18)
            };
            root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });

            var heading = new System.Windows.Controls.TextBlock
            {
                Text = $"请选择{operationName}文件格式",
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(38, 49, 66))
            };
            System.Windows.Controls.Grid.SetRow(heading, 0);
            root.Children.Add(heading);

            var options = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            var imgRadio = new System.Windows.Controls.RadioButton
            {
                GroupName = "PartitionBackupFormat",
                Content = ".img 格式",
                IsChecked = true,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 58, 0)
            };
            var binRadio = new System.Windows.Controls.RadioButton
            {
                GroupName = "PartitionBackupFormat",
                Content = ".bin 格式",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold
            };
            options.Children.Add(imgRadio);
            options.Children.Add(binRadio);
            var optionsBorder = new System.Windows.Controls.Border
            {
                Margin = new Thickness(0, 14, 0, 14),
                Padding = new Thickness(18, 15, 18, 15),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(226, 232, 240)),
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(250, 251, 253)),
                Child = options
            };
            System.Windows.Controls.Grid.SetRow(optionsBorder, 1);
            root.Children.Add(optionsBorder);

            string? selectedExtension = null;
            var buttonPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };
            var cancelButton = new System.Windows.Controls.Button
            {
                Content = "取消",
                Width = 86,
                Height = 32,
                Margin = new Thickness(0, 0, 10, 0),
                Background = System.Windows.Media.Brushes.White,
                BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(203, 213, 225))
            };
            var confirmButton = new System.Windows.Controls.Button
            {
                Content = "确定",
                Width = 96,
                Height = 32,
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(190, 112, 225)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                IsDefault = true
            };
            cancelButton.Click += (_, _) => dialog.DialogResult = false;
            confirmButton.Click += (_, _) =>
            {
                selectedExtension = binRadio.IsChecked == true ? ".bin" : ".img";
                dialog.DialogResult = true;
            };
            buttonPanel.Children.Add(cancelButton);
            buttonPanel.Children.Add(confirmButton);
            System.Windows.Controls.Grid.SetRow(buttonPanel, 2);
            root.Children.Add(buttonPanel);

            dialog.Content = root;
            return dialog.ShowDialog() == true ? selectedExtension : null;
        }

        private bool ShowErasePartitionConfirmationDialog(IReadOnlyList<string> partitionNames)
        {
            var dialog = new System.Windows.Window
            {
                Title = "确认擦除分区",
                Width = 430,
                Height = 265,
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Background = System.Windows.Media.Brushes.White,
                FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI")
            };

            var root = new System.Windows.Controls.Grid
            {
                Margin = new Thickness(24, 20, 24, 18)
            };
            root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });

            var heading = new System.Windows.Controls.TextBlock
            {
                Text = "请确认本次擦除的分区",
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(38, 49, 66))
            };
            System.Windows.Controls.Grid.SetRow(heading, 0);
            root.Children.Add(heading);

            const int visiblePartitionLimit = 5;
            string partitionPreview = string.Join("、", partitionNames.Take(visiblePartitionLimit));
            if (partitionNames.Count > visiblePartitionLimit)
            {
                partitionPreview += $" 等 {partitionNames.Count} 个分区";
            }

            var contentPanel = new System.Windows.Controls.StackPanel
            {
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            contentPanel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = $"即将擦除 {partitionNames.Count} 个分区",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(51, 65, 85))
            });
            contentPanel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = partitionPreview,
                Margin = new Thickness(0, 7, 0, 0),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 38,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(100, 116, 139))
            });
            contentPanel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "擦除分区不等于清理设备，擦除所有分区会导致黑砖，小白请谨慎操作哦！！！",
                Margin = new Thickness(0, 12, 0, 0),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(217, 119, 6))
            });

            var contentBorder = new System.Windows.Controls.Border
            {
                Margin = new Thickness(0, 14, 0, 14),
                Padding = new Thickness(12, 15, 12, 15),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(226, 232, 240)),
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(250, 251, 253)),
                Child = contentPanel
            };
            System.Windows.Controls.Grid.SetRow(contentBorder, 1);
            root.Children.Add(contentBorder);

            var buttonPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };
            var cancelButton = new System.Windows.Controls.Button
            {
                Content = "取消",
                Width = 86,
                Height = 32,
                Margin = new Thickness(0, 0, 10, 0),
                Background = System.Windows.Media.Brushes.White,
                BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(203, 213, 225)),
                IsCancel = true
            };
            var confirmButton = new System.Windows.Controls.Button
            {
                Content = "确认擦除",
                Width = 96,
                Height = 32,
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(190, 112, 225)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                IsDefault = true
            };
            cancelButton.Click += (_, _) => dialog.DialogResult = false;
            confirmButton.Click += (_, _) => dialog.DialogResult = true;
            buttonPanel.Children.Add(cancelButton);
            buttonPanel.Children.Add(confirmButton);
            System.Windows.Controls.Grid.SetRow(buttonPanel, 2);
            root.Children.Add(buttonPanel);

            dialog.Content = root;
            return dialog.ShowDialog() == true;
        }

        private static ulong DivideRoundUp(ulong value, ulong divisor) =>
            value / divisor + (value % divisor == 0 ? 0UL : 1UL);

        private static void OpenDirectorySilently(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{path}\"",
                        UseShellExecute = true
                    });
                }
            }
            catch
            {
            }
        }
    }
}
