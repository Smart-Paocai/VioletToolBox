using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Reflection;
using System.Linq;

namespace OPFlashTool.Services
{
    // [新增] 定义单个分区的刷写动作
    public class SuperFlashAction
    {
        public string? PartitionName { get; set; }        // 分区名 (如 system_a)
        public string? FilePath { get; set; }             // 本地文件绝对路径
        public long RelativeSectorOffset { get; set; }   // 相对于 Super 分区头部的扇区偏移
        public long SizeInBytes { get; set; }            // 文件大小
        public string? DebugInfo { get; set; }            // 调试信息 (方便日志输出)
    }

    public class SuperMaker
    {
        private readonly Action<string> _log;
        private readonly string _lpmakePath;
        
        // [新增] 记录参与构建的分区名称列表
        public List<string> ProcessedPartitions { get; private set; } = new List<string>();

        public SuperMaker(string binDir, Action<string> logCallback)
        {
            _log = logCallback;
            // [Modified] Use binDir or AppDomain.CurrentDomain.BaseDirectory
            string installDir = binDir; 
            if (string.IsNullOrEmpty(installDir)) installDir = AppDomain.CurrentDomain.BaseDirectory;
            
            // Assuming lpmake.exe is in bin/exe or root
            _lpmakePath = Path.Combine(installDir, "platform-tools", "lpmake.exe");
            if (!File.Exists(_lpmakePath))
            {
                _lpmakePath = Path.Combine(installDir, "exe", "lpmake.exe");
            }
            if (!File.Exists(_lpmakePath))
            {
                _lpmakePath = Path.Combine(installDir, "bin", "exe", "lpmake.exe");
            }
            if (!File.Exists(_lpmakePath))
            {
                _lpmakePath = Path.Combine(installDir, "lpmake.exe");
            }
        }

        // [新增] 智能入口：从固件根目录构建 super.img
        public async Task<bool> MakeSuperFromDirectoryAsync(string rootDirectory, string outputDir)
        {
            // 隐藏：_log($"[SuperMaker] 启动智能构建模式 (根目录: {Path.GetFileName(rootDirectory)})");

            if (!Directory.Exists(rootDirectory))
            {
                _log("[Error] 目录不存在。");
                return false;
            }

            // 1. 自动寻找配置文件 (META/*.json)
            string metaDir = Path.Combine(rootDirectory, "META");
            string jsonPath = "";

            if (Directory.Exists(metaDir))
            {
                var jsonFiles = Directory.GetFiles(metaDir, "*.json");
                jsonPath = jsonFiles.FirstOrDefault(f => Path.GetFileName(f).StartsWith("super_def", StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrEmpty(jsonPath))
                {
                    jsonPath = jsonFiles.FirstOrDefault(f =>
                        !Path.GetFileName(f).Equals("config.json", StringComparison.OrdinalIgnoreCase) &&
                        !Path.GetFileName(f).Equals("manifest.json", StringComparison.OrdinalIgnoreCase)
                    );
                }
            }

            if (string.IsNullOrEmpty(jsonPath))
            {
                var rootJsons = Directory.GetFiles(rootDirectory, "*.json");
                jsonPath = rootJsons.FirstOrDefault(f => Path.GetFileName(f).StartsWith("super_def", StringComparison.OrdinalIgnoreCase));
            }

            if (string.IsNullOrEmpty(jsonPath) || !File.Exists(jsonPath))
            {
                _log("[Error] 无法找到有效的 super 分区定义文件 (.json)。请确保 META 文件夹中包含该文件。");
                return false;
            }

            // 隐藏：_log($"[锁定] 配置文件: {Path.GetFileName(jsonPath)}");

            // 2. 验证 IMAGES 目录 (仅做提示)
            string imagesDir = Path.Combine(rootDirectory, "IMAGES");
            if (!Directory.Exists(imagesDir))
            {
                _log("[警告] 根目录下未找到 IMAGES 文件夹，正在尝试继续...");
            }

            // 3. 调用核心构建方法
            return await MakeSuperImgAsync(jsonPath, outputDir, rootDirectory);
        }

        public async Task<bool> MakeSuperImgAsync(string jsonPath, string outputDir, string? imageRootDir = null)
        {
            // 隐藏：_log("[Info] SuperMaker v20 (Smart Path) Initialized.");
            if (!File.Exists(_lpmakePath))
            {
                _log($"[Error] 找不到 lpmake.exe ({_lpmakePath})，请检查依赖是否正确解压。");
                return false;
            }

            string? tempRawDir = null;
            string? tempParentDir = null;
            bool ownsTempDirectory = false;
            try
            {
                jsonPath = Path.GetFullPath(jsonPath);
                // Keep expanded images and native-tool scratch files on the firmware disk.
                tempParentDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(string.IsNullOrWhiteSpace(imageRootDir)
                    ? Path.GetDirectoryName(jsonPath)!
                    : imageRootDir));
                if (!Directory.Exists(tempParentDir))
                    throw new DirectoryNotFoundException($"散包目录不存在: {tempParentDir}");

                tempRawDir = Path.Combine(tempParentDir, ".violet-super-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempRawDir);
                ownsTempDirectory = true;
                _log($"临时目录: {tempRawDir}");
                // 隐藏：_log($"[Info] 正在解析配置文件: {Path.GetFileName(jsonPath)}");

                string jsonContent;
                using (var reader = File.OpenText(jsonPath))
                {
                    jsonContent = await reader.ReadToEndAsync();
                }
                
                // [Modified] Removed AppJsonContext dependence
                SuperDef? def = JsonSerializer.Deserialize<SuperDef>(jsonContent);
                
                // 清空上一轮的分区记录
                ProcessedPartitions.Clear();

                if (def?.BlockDevices == null || def.BlockDevices.Count == 0)
                {
                    _log("[Error] JSON 格式无效: 缺少 block_devices 定义");
                    return false;
                }

                string baseDir = tempParentDir;
                // 隐藏：_log($"[Info] 镜像搜索根目录: {baseDir}");

                var device = def!.BlockDevices[0];
                long deviceSize = 0;
                if (!string.IsNullOrEmpty(device.Size))
                {
                    string sizeStr = device.Size.Replace(",", "").Replace("_", "").Trim();
                    if (sizeStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    {
                        try { deviceSize = Convert.ToInt64(sizeStr, 16); } catch { }
                    }
                    else
                    {
                        long.TryParse(sizeStr, out deviceSize);
                    }
                }

                if (deviceSize == 0) _log("[Warn] Device Size 解析结果为 0! 请检查 JSON。");
                // 隐藏：else _log($"[Info] 解析后的 Device Size: {deviceSize}");

                long alignment = 0;
                if (!string.IsNullOrEmpty(device.Alignment))
                {
                    string alignStr = device.Alignment.Replace(",", "").Replace("_", "").Trim();
                    if (alignStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    {
                        try { alignment = Convert.ToInt64(alignStr, 16); } catch { }
                    }
                    else
                    {
                        long.TryParse(alignStr, out alignment);
                    }
                }

                var args = new StringBuilder();
                args.Append($"--device-size {deviceSize} ");
                args.Append("--super-name super ");
                args.Append("--metadata-size 65536 ");
                args.Append("--metadata-slots 2 ");
                args.Append($"--alignment {alignment} ");
                args.Append("--force-full-image ");

                // 输出路径：优先用户指定目录，其次 imageRootDir/IMAGES，其次 imageRootDir，其次 JSON 目录
                string finalOutputDir = outputDir;
                if (string.IsNullOrEmpty(finalOutputDir))
                {
                    if (!string.IsNullOrEmpty(imageRootDir))
                    {
                        if (imageRootDir!.EndsWith("IMAGES", StringComparison.OrdinalIgnoreCase))
                        {
                            finalOutputDir = imageRootDir;
                        }
                        else
                        {
                            string imagesSubDir = Path.Combine(imageRootDir, "IMAGES");
                            finalOutputDir = Directory.Exists(imagesSubDir) ? imagesSubDir : imageRootDir;
                        }
                    }
                    else
                    {
                        finalOutputDir = Path.GetDirectoryName(jsonPath) ?? "";
                    }
                }

                string outputPath = Path.Combine(Path.GetFullPath(finalOutputDir), "super.img");

                if (def.Groups != null)
                {
                    foreach (var group in def.Groups)
                    {
                        if (group.Name == "default") continue;
                        if (long.TryParse(group.MaximumSize, out long maxSize))
                        {
                             args.Append($"--group {group.Name}:{maxSize} ");
                        }
                    }
                }

                if (def.Partitions != null)
                {
                    foreach (var part in def.Partitions)
                    {
                        if (part == null) continue;
                        
                        // 记录分区名
                        if (!string.IsNullOrEmpty(part.Name)) ProcessedPartitions.Add(part.Name);

                        if (!long.TryParse(part.Size, out long size)) size = 0;
                        string? imgPath = null;

                        if (!string.IsNullOrEmpty(part.Path))
                        {
                            string relativePath = part!.Path!.Replace("/", "\\");
                            string tempPath = Path.Combine(baseDir, relativePath);
                            if (!File.Exists(tempPath) && relativePath.StartsWith("IMAGES\\", StringComparison.OrdinalIgnoreCase))
                            {
                                string altPath = Path.Combine(baseDir, relativePath.Substring(7));
                                if (File.Exists(altPath))
                                {
                                    tempPath = altPath;
                                    _log($"[Info] 路径自动修正: {part.Path} -> {tempPath}");
                                }
                            }

                            if (File.Exists(tempPath))
                            {
                                imgPath = tempPath;
                                
                                if (SparseImageHandler.IsSparseImage(imgPath))
                                {
                                    string rawFileName = Path.GetFileNameWithoutExtension(imgPath) + ".raw";
                                    string rawPath = Path.Combine(tempRawDir, rawFileName);
                                    _log($"{Path.GetFileName(imgPath)} > {rawFileName}...");
                                    
                                    if (await Task.Run(() => SparseImageHandler.Unsparse(imgPath, rawPath)))
                                    {
                                        _log("COLOR:Green| OK");
                                        imgPath = rawPath;
                                    }
                                    else
                                    {
                                        _log(" Failed! (Using original)");
                                    }
                                }

                                try 
                                {
                                    using (var fs = new FileStream(imgPath, FileMode.Open, FileAccess.ReadWrite))
                                    {
                                        long imgSize = fs.Length;
                                        if (imgSize % 4096 != 0)
                                        {
                                            long padding = 4096 - (imgSize % 4096);
                                            fs.Seek(0, SeekOrigin.End);
                                            fs.Write(new byte[padding], 0, (int)padding);
                                            imgSize += padding;
                                        }
                                        if (imgSize > size) size = imgSize;
                                    }
                                }
                                catch (Exception ex) { _log($"[Warn] 镜像检查失败: {ex.Message}"); }
                            }
                            else
                            {
                                _log($"[Warn] 文件缺失: {part.Name} ({part.Path}) -> 生成空分区");
                            }
                        }

                        string groupName = !string.IsNullOrEmpty(part.GroupName) ? part.GroupName : part.Group;
                        if (string.IsNullOrEmpty(groupName)) groupName = "default";

                        args.Append($"--partition {part.Name}:readonly:{size}:{groupName} ");
                        if (imgPath != null) args.Append($"--image {part.Name}=\"{imgPath}\" ");
                    }
                }

                args.Append($"--output \"{outputPath}\" ");

                _log("正在制作刷机包，速度由电脑磁盘决定，请耐心等待...");
                var outputDirPath = Path.GetDirectoryName(outputPath);
                Directory.CreateDirectory(string.IsNullOrEmpty(outputDirPath) ? "." : outputDirPath);
                if (File.Exists(outputPath)) File.Delete(outputPath);

                var psi = new ProcessStartInfo
                {
                    FileName = Path.GetFullPath(_lpmakePath),
                    Arguments = args.ToString(),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = tempRawDir
                };
                // Only the child process inherits these overrides; system TEMP stays unchanged.
                psi.Environment["TEMP"] = tempRawDir;
                psi.Environment["TMP"] = tempRawDir;
                psi.Environment["TMPDIR"] = tempRawDir;

                var outputBuffer = new StringBuilder();
                using (var proc = new Process { StartInfo = psi })
                {
                    // 过滤 lpmake 的冗余输出，仅保留关键信息
                    DataReceivedEventHandler handler = (s, e) => {
                        if (!string.IsNullOrWhiteSpace(e.Data))
                        {
                            if (e.Data.Contains("will resize")) return;
                            if (e.Data.Contains("Invalid sparse")) return;
                            if (e.Data.Contains("I lpmake")) return;
                            if (e.Data.StartsWith(" ")) return; // 过滤缩进信息

                            _log($"[信息] {e.Data}");
                            outputBuffer.AppendLine(e.Data);
                        }
                    };
                    proc.OutputDataReceived += handler;
                    proc.ErrorDataReceived += handler;

                    proc.Start();
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();
                    await Task.Run(() => proc.WaitForExit());

                    if (proc.ExitCode == 0)
                    {
                        // 隐藏：_log($"[成功] super.img 构建完毕");

                        // 等待文件句柄释放
                        for (int retries = 0; retries < 5; retries++)
                        {
                            try
                            {
                                using (var fs = File.Open(outputPath, FileMode.Open, FileAccess.Read, FileShare.None)) { }
                                break;
                            }
                            catch
                            {
                                await Task.Delay(800);
                            }
                        }

                        // [Modified] Removed post-processing that depends on SparseImageHandler and complex logic for now
                        // 隐藏：_log($"[路径] {outputPath}");
                        // 不再打开文件夹：try { Process.Start("explorer.exe", $"/select,\"{outputPath}\""); } catch { }
                        return true;
                    }
                    else
                    {
                        _log($"[失败] lpmake 错误码: {proc.ExitCode}");
                        if (outputBuffer.Length > 0) _log($"[失败] 详细错误:\n{outputBuffer}");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                _log($"[Exception] {ex.Message}");
                return false;
            }
            finally
            {
                if (ownsTempDirectory && tempRawDir != null &&
                    string.Equals(Path.GetDirectoryName(tempRawDir), tempParentDir, StringComparison.OrdinalIgnoreCase) &&
                    Directory.Exists(tempRawDir))
                {
                    try { Directory.Delete(tempRawDir, true); }
                    catch (Exception cleanupError)
                    {
                        _log($"[Warn] 临时目录清理失败，请在任务结束后手动清理: {tempRawDir} ({cleanupError.Message})");
                    }
                }
            }
        }

        private long AlignOffset(long current, long alignment)
        {
            if (alignment == 0) return current;
            long remainder = current % alignment;
            if (remainder == 0) return current;
            return current + (alignment - remainder);
        }
    }

    // --- JSON 数据模型 ---

    public class SuperDef
    {
        [JsonPropertyName("super_meta")]
        public SuperMeta? Meta { get; set; }

        [JsonPropertyName("block_devices")]
        public List<BlockDevice>? BlockDevices { get; set; }

        [JsonPropertyName("groups")]
        public List<Group>? Groups { get; set; }

        [JsonPropertyName("partitions")]
        public List<Partition>? Partitions { get; set; }
    }

    public class SuperMeta
    {
        [JsonPropertyName("path")]
        public string? Path { get; set; }

        [JsonPropertyName("size")]
        public string? Size { get; set; }
    }

    public class BlockDevice
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("size")]
        public string Size { get; set; } = "0";

        [JsonPropertyName("alignment")]
        public string Alignment { get; set; } = "0";
    }

    public class Group
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("maximum_size")]
        public string MaximumSize { get; set; } = "0";
    }

    public class Partition
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("group_name")]
        public string GroupName { get; set; } = "";

        [JsonPropertyName("group")]
        public string Group { get; set; } = "";

        [JsonPropertyName("size")]
        public string Size { get; set; } = "0";

        [JsonPropertyName("path")]
        public string? Path { get; set; }
    }

    public static class SparseImageHandler
    {
        private const uint SPARSE_HEADER_MAGIC = 0xED26FF3A;
        private const ushort CHUNK_TYPE_RAW = 0xCAC1;
        private const ushort CHUNK_TYPE_FILL = 0xCAC2;
        private const ushort CHUNK_TYPE_DONT_CARE = 0xCAC3;
        private const ushort CHUNK_TYPE_CRC32 = 0xCAC4;

        public static bool IsSparseImage(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                using (var br = new BinaryReader(fs))
                {
                    if (fs.Length < 28) return false;
                    uint magic = br.ReadUInt32();
                    return magic == SPARSE_HEADER_MAGIC;
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool Unsparse(string inputPath, string outputPath)
        {
            try
            {
                using (var fsIn = new FileStream(inputPath, FileMode.Open, FileAccess.Read))
                using (var br = new BinaryReader(fsIn))
                using (var fsOut = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                using (var bw = new BinaryWriter(fsOut))
                {
                    // Read Header
                    uint magic = br.ReadUInt32();
                    if (magic != SPARSE_HEADER_MAGIC) return false;

                    ushort major = br.ReadUInt16();
                    ushort minor = br.ReadUInt16();
                    ushort fileHdrSz = br.ReadUInt16();
                    ushort chunkHdrSz = br.ReadUInt16();
                    uint blkSz = br.ReadUInt32();
                    uint totalBlks = br.ReadUInt32();
                    uint totalChunks = br.ReadUInt32();
                    uint checksum = br.ReadUInt32();

                    // Skip to first chunk
                    fsIn.Seek(fileHdrSz, SeekOrigin.Begin);

                    for (int i = 0; i < totalChunks; i++)
                    {
                        ushort chunkType = br.ReadUInt16();
                        ushort reserved1 = br.ReadUInt16();
                        uint chunkSz = br.ReadUInt32(); // in blocks
                        uint totalSz = br.ReadUInt32(); // in bytes (header + data)

                        long dataSz = totalSz - chunkHdrSz;

                        switch (chunkType)
                        {
                            case CHUNK_TYPE_RAW:
                                {
                                    long remaining = dataSz;
                                    byte[] buffer = new byte[4096 * 1024]; // 4MB buffer
                                    while (remaining > 0)
                                    {
                                        int count = (int)Math.Min(remaining, buffer.Length);
                                        int read = br.Read(buffer, 0, count);
                                        bw.Write(buffer, 0, read);
                                        remaining -= read;
                                    }
                                    break;
                                }
                            case CHUNK_TYPE_FILL:
                                {
                                    uint fillVal = br.ReadUInt32();
                                    byte[] fillBytes = BitConverter.GetBytes(fillVal);
                                    // Fill chunkSz * blkSz bytes
                                    // Writing byte by byte is slow, optimize this
                                    byte[] blockBuffer = new byte[blkSz];
                                    for(int k=0; k<blkSz; k+=4)
                                        Array.Copy(fillBytes, 0, blockBuffer, k, 4);
                                    
                                    for(int j=0; j<chunkSz; j++)
                                        bw.Write(blockBuffer);
                                    
                                    break;
                                }
                            case CHUNK_TYPE_DONT_CARE:
                                {
                                    // For raw image, we should write zeros to preserve offset
                                    long bytesToSkip = (long)chunkSz * blkSz;
                                    // Efficiently write zeros
                                    byte[] zeroBuffer = new byte[Math.Min(bytesToSkip, 4096 * 1024)]; // 4MB chunks
                                    Array.Clear(zeroBuffer, 0, zeroBuffer.Length);
                                    while (bytesToSkip > 0)
                                    {
                                        int count = (int)Math.Min(bytesToSkip, zeroBuffer.Length);
                                        bw.Write(zeroBuffer, 0, count);
                                        bytesToSkip -= count;
                                    }
                                    break;
                                }
                            case CHUNK_TYPE_CRC32:
                                {
                                    // Skip
                                    br.ReadBytes((int)dataSz);
                                    break;
                                }
                            default:
                                // Unknown chunk type
                                return false;
                        }
                    }
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
