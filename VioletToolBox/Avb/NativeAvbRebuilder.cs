using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace WpfApp1.Avb
{
    internal sealed class NativeAvbRebuilder
    {
        private const int BlockSize = 4096;
        private const int MaxVbmetaSize = 64 * 1024;
        private const int MaxFooterSize = 4096;
        private const int FooterSize = 64;
        private const int VbmetaHeaderSize = 256;
        private static readonly string[] AospChainPartitions = { "boot", "recovery", "vbmeta_system" };

        private readonly Action<string, string> _log;

        public NativeAvbRebuilder(Action<string, string> log)
        {
            _log = log;
        }

        public AvbPublicKeyProfile AnalyzePublicKeys(string imagePath)
        {
            var info = ParseImage(imagePath)
                ?? throw new InvalidDataException("无法解析 vbmeta 镜像，未检测到有效的 AVB0 Header。");
            if (info.PublicKey.Length < 8)
            {
                throw new InvalidDataException("vbmeta 中没有可分析的 AVB 公钥。");
            }

            var chainKeys = info.ChainPublicKeys.ToDictionary(
                item => item.Key,
                item => CreatePublicKeyDetails(item.Value),
                StringComparer.OrdinalIgnoreCase);
            return new AvbPublicKeyProfile(
                CreatePublicKeyDetails(info.PublicKey),
                chainKeys);
        }

        private static AvbPublicKeyDetails CreatePublicKeyDetails(byte[] publicKey)
        {
            if (publicKey.Length < 8)
            {
                throw new InvalidDataException("AVB 公钥数据长度无效。");
            }

            var keyBits = checked((int)ReadU32(publicKey, 0));
            var expectedSize = checked(8 + (keyBits / 8 * 2));
            if (keyBits <= 0 || keyBits % 8 != 0 || publicKey.Length != expectedSize)
            {
                throw new InvalidDataException("AVB 公钥结构或长度无效。");
            }

            return new AvbPublicKeyDetails(
                keyBits,
                ToHex(SHA256.HashData(publicKey)));
        }

        public string DetectPartitionName(string imagePath, IEnumerable<string> allowedNames, string fallbackName)
        {
            var allowed = new HashSet<string>(allowedNames, StringComparer.OrdinalIgnoreCase);
            var info = ParseImage(imagePath);
            if (info != null)
            {
                var detected = info.Descriptors
                    .OfType<AvbHashDescriptor>()
                    .Select(x => x.PartitionName)
                    .FirstOrDefault(x => allowed.Contains(x));
                if (!string.IsNullOrWhiteSpace(detected))
                {
                    return detected;
                }
            }

            var fileName = Path.GetFileNameWithoutExtension(imagePath);
            var inferred = allowed
                .OrderByDescending(x => x.Length)
                .FirstOrDefault(x =>
                    string.Equals(fileName, x, StringComparison.OrdinalIgnoreCase) ||
                    fileName.Contains(x, StringComparison.OrdinalIgnoreCase));
            return string.IsNullOrWhiteSpace(inferred) ? fallbackName : inferred;
        }

        public void Verify(string? vbmetaPath, IReadOnlyDictionary<string, string> partitionImages)
        {
            Log("开始验证AVB镜像...", "yellow");

            if (!string.IsNullOrWhiteSpace(vbmetaPath) && File.Exists(vbmetaPath))
            {
                LogImageInfo(vbmetaPath);
            }
            else
            {
                Log("未找到 vbmeta.img，可能是纯链式分区模式", "yellow");
            }

            foreach (var imagePath in partitionImages.Values.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (File.Exists(imagePath))
                {
                    LogImageInfo(imagePath);
                }
            }

            Log("AVB验证完成", "green");
        }

        public void RebuildAospChain(
            IReadOnlyDictionary<string, string> selectedPartitionImages,
            string vbmetaPath,
            string avbRoot)
        {
            if (string.IsNullOrWhiteSpace(vbmetaPath) || !File.Exists(vbmetaPath))
            {
                throw new FileNotFoundException("未找到 vbmeta 镜像。", vbmetaPath);
            }

            var images = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var partition in AospChainPartitions)
            {
                if (!selectedPartitionImages.TryGetValue(partition, out var imagePath) ||
                    string.IsNullOrWhiteSpace(imagePath) ||
                    !File.Exists(imagePath))
                {
                    throw new FileNotFoundException($"未找到 {partition} 镜像。", imagePath);
                }

                images[partition] = Path.GetFullPath(imagePath);
            }

            var keyPath = Path.Combine(avbRoot, "tools", "pem", "testkey_rsa4096.pem");
            if (!File.Exists(keyPath))
            {
                throw new FileNotFoundException("未找到 testkey_rsa4096.pem。", keyPath);
            }

            Log("开始新版本 AOSP 链式 AVB 重构...", "yellow");
            Log($"使用私钥: {Path.GetFileName(keyPath)}");

            SignAospPartition(images["boot"], "boot", keyPath);
            SignAospPartition(images["recovery"], "recovery", keyPath);
            RebuildAospVbmetaSystem(images["vbmeta_system"], keyPath);
            RebuildAospMainVbmeta(vbmetaPath, keyPath);

            Verify(vbmetaPath, images);
            Log("新版本 AOSP 签名完成", "green");
            Log($"签名文件目录: {Path.GetDirectoryName(vbmetaPath)}", "green");
        }

        public void Rebuild(
            IReadOnlyDictionary<string, string> selectedPartitionImages,
            string? vbmetaPath,
            string avbRoot,
            bool useOriginalSalt,
            bool chainedMode,
            int keySelection)
        {
            Log("开始AVB签名...", "yellow");
            Log($"模式: {(chainedMode ? "链式" : "非链式")}");

            var partitionImages = selectedPartitionImages
                .Where(x => !string.IsNullOrWhiteSpace(x.Key) && File.Exists(x.Value))
                .ToDictionary(x => x.Key, x => Path.GetFullPath(x.Value), StringComparer.OrdinalIgnoreCase);
            if (partitionImages.Count == 0)
            {
                Log("未检测到任何分区镜像文件", "red");
                return;
            }

            var chained = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var regular = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in partitionImages)
            {
                var info = ParseImage(item.Value);
                if (info != null && !string.IsNullOrWhiteSpace(info.AlgorithmName) &&
                    !string.Equals(info.AlgorithmName, "NONE", StringComparison.OrdinalIgnoreCase))
                {
                    chained[item.Key] = item.Value;
                    Log($"{item.Key} 是链式分区");
                }
                else
                {
                    regular[item.Key] = item.Value;
                    Log($"{item.Key} 是普通分区");
                }
            }

            if (chainedMode)
            {
                if (regular.Count > 0)
                {
                    Log($"链式模式跳过普通分区: {string.Join(", ", regular.Keys)}", "yellow");
                }

                if (chained.Count == 0)
                {
                    Log("链式模式下未发现任何链式分区，请使用非链式签名", "red");
                    return;
                }
            }
            else if (regular.Count > 0 &&
                     (string.IsNullOrWhiteSpace(vbmetaPath) || !File.Exists(vbmetaPath)))
            {
                Log("发现普通分区但缺少 vbmeta.img；如只处理链式分区请选择链式模式", "red");
                return;
            }

            var successCount = 0;
            foreach (var item in chained)
            {
                if (RebuildPartition(item.Key, item.Value, avbRoot, useOriginalSalt, keySelection))
                {
                    successCount++;
                }
            }

            if (!chainedMode)
            {
                foreach (var item in regular)
                {
                    if (RebuildPartition(item.Key, item.Value, avbRoot, useOriginalSalt, keySelection))
                    {
                        successCount++;
                    }
                }

                if (regular.Count > 0)
                {
                    RebuildVbmeta(vbmetaPath!, regular, avbRoot, keySelection);
                }
            }

            if (successCount == 0)
            {
                Log("没有分区重建成功", "red");
                return;
            }

            Verify(vbmetaPath, partitionImages);
            Log($"AVB签名完成，成功重建 {successCount} 个分区", "green");
            Log($"签名文件目录: {Path.GetDirectoryName(partitionImages.Values.First())}", "green");
        }

        private void SignAospPartition(string imagePath, string partitionName, string keyPath)
        {
            Log($"签名 {partitionName}: {Path.GetFileName(imagePath)}", "yellow");

            var partitionSize = new FileInfo(imagePath).Length;
            var info = ParseImage(imagePath) ??
                       throw new InvalidDataException($"无法解析 {partitionName} 的 AVB 信息。");
            var props = ExtractAospBuildProps(info, partitionName);

            EraseFooter(imagePath);

            var imageSize = new FileInfo(imagePath).Length;
            var salt = RandomBytes(32);
            var digest = ComputeHash(File.ReadAllBytes(imagePath), salt, "sha256");
            var originalHash = info.Descriptors.OfType<AvbHashDescriptor>().FirstOrDefault(x =>
                string.Equals(x.PartitionName, partitionName, StringComparison.OrdinalIgnoreCase));

            var descriptors = new List<byte[]>
            {
                EncodeHashDescriptor(new AvbHashDescriptor
                {
                    ImageSize = imageSize,
                    HashAlgorithm = "sha256",
                    PartitionName = partitionName,
                    Salt = salt,
                    Digest = digest,
                    Flags = originalHash?.Flags ?? 0
                }),
                EncodePropertyDescriptor(new AvbPropertyDescriptor
                {
                    Key = $"com.android.build.{partitionName}.os_version",
                    Value = props.OsVersion
                }),
                EncodePropertyDescriptor(new AvbPropertyDescriptor
                {
                    Key = $"com.android.build.{partitionName}.fingerprint",
                    Value = props.Fingerprint
                }),
                EncodePropertyDescriptor(new AvbPropertyDescriptor
                {
                    Key = $"com.android.build.{partitionName}.security_patch",
                    Value = props.SecurityPatch
                })
            };

            var vbmeta = GenerateVbmetaBlob(
                "SHA256_RSA4096",
                keyPath,
                descriptors,
                info.RollbackIndex,
                info.Flags,
                info.RollbackIndexLocation,
                info.RequiredMinor);

            AppendVbmetaAndFooter(imagePath, partitionSize, vbmeta, imageSize);
            Log($"{partitionName} 签名完成", "green");
        }

        private void RebuildAospVbmetaSystem(string imagePath, string keyPath)
        {
            Log("重签 vbmeta_system", "yellow");

            var info = ParseImage(imagePath) ?? throw new InvalidDataException("无法解析 vbmeta_system.img。");
            var originalSize = new FileInfo(imagePath).Length;
            var vbmeta = GenerateVbmetaBlob(
                "SHA256_RSA4096",
                keyPath,
                info.RawDescriptors.ToList(),
                info.RollbackIndex,
                info.Flags,
                info.RollbackIndexLocation,
                info.RequiredMinor);

            WriteWithAvbtoolPadding(imagePath, vbmeta, originalSize);
            Log("vbmeta_system 重签完成", "green");
        }

        private void RebuildAospMainVbmeta(string imagePath, string keyPath)
        {
            Log("重构主 vbmeta 链式描述符", "yellow");

            var info = ParseImage(imagePath) ?? throw new InvalidDataException("无法解析 vbmeta.img。");
            var originalSize = new FileInfo(imagePath).Length;
            var chains = new Dictionary<string, AospChainDescriptor>(StringComparer.OrdinalIgnoreCase);
            var keptDescriptors = new List<byte[]>();

            foreach (var raw in info.RawDescriptors)
            {
                if (TryParseAospChainDescriptor(raw, out var chain) &&
                    AospChainPartitions.Contains(chain.PartitionName, StringComparer.OrdinalIgnoreCase))
                {
                    chains[chain.PartitionName] = chain;
                    Log($"移除旧链式描述符: {chain.PartitionName} location={chain.RollbackIndexLocation}");
                    continue;
                }

                keptDescriptors.Add(raw);
            }

            foreach (var partition in AospChainPartitions)
            {
                if (!chains.ContainsKey(partition))
                {
                    throw new InvalidDataException($"主 vbmeta 中缺少 {partition} 的 chain descriptor。");
                }
            }

            var publicKey = EncodeAvbPublicKey(keyPath);
            foreach (var partition in AospChainPartitions)
            {
                var chain = chains[partition];
                keptDescriptors.Add(EncodeAospChainDescriptor(
                    partition,
                    chain.RollbackIndexLocation,
                    publicKey,
                    chain.Flags));
                Log($"添加链式描述符: {partition}:{chain.RollbackIndexLocation}");
            }

            var vbmeta = GenerateVbmetaBlob(
                "SHA256_RSA4096",
                keyPath,
                keptDescriptors,
                info.RollbackIndex,
                info.Flags,
                info.RollbackIndexLocation,
                info.RequiredMinor);

            WriteWithAvbtoolPadding(imagePath, vbmeta, originalSize);
            Log("主 vbmeta 重构完成", "green");
        }

        private static AospBuildProps ExtractAospBuildProps(AvbImageInfo info, string partitionName)
        {
            var props = info.Descriptors
                .OfType<AvbPropertyDescriptor>()
                .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.Last().Value, StringComparer.OrdinalIgnoreCase);

            return new AospBuildProps(
                GetAospBuildProp(props, $"com.android.build.{partitionName}.os_version", "15"),
                GetAospBuildProp(props, $"com.android.build.{partitionName}.fingerprint", string.Empty),
                GetAospBuildProp(props, $"com.android.build.{partitionName}.security_patch", "2025-02-05"));
        }

        private static string GetAospBuildProp(
            IReadOnlyDictionary<string, string> props,
            string key,
            string fallback)
        {
            return props.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : fallback;
        }

        private bool RebuildPartition(string partitionName, string imagePath, string avbRoot, bool useOriginalSalt, int keySelection)
        {
            Log($"重建分区: {partitionName}", "yellow");
            var info = ParseImage(imagePath);
            var partitionSize = new FileInfo(imagePath).Length;
            if (info == null)
            {
                info = new AvbImageInfo { OriginalImageSize = partitionSize, ImageSize = partitionSize, AlgorithmName = "NONE" };
            }

            var isChained = !string.IsNullOrWhiteSpace(info.AlgorithmName) &&
                            !string.Equals(info.AlgorithmName, "NONE", StringComparison.OrdinalIgnoreCase);

            EraseFooter(imagePath);

            return isChained
                ? RebuildChainedPartition(partitionName, imagePath, partitionSize, info, avbRoot, useOriginalSalt, keySelection)
                : RebuildHashPartition(partitionName, imagePath, partitionSize, info, useOriginalSalt);
        }

        private bool RebuildChainedPartition(
            string partitionName,
            string imagePath,
            long partitionSize,
            AvbImageInfo info,
            string avbRoot,
            bool useOriginalSalt,
            int keySelection)
        {
            var hashDesc = info.Descriptors.OfType<AvbHashDescriptor>().FirstOrDefault(x =>
                string.Equals(x.PartitionName, partitionName, StringComparison.OrdinalIgnoreCase));
            var salt = useOriginalSalt && hashDesc?.Salt.Length > 0 ? hashDesc.Salt : RandomBytes(32);
            var hashAlgorithm = string.IsNullOrWhiteSpace(hashDesc?.HashAlgorithm) ? "sha256" : hashDesc.HashAlgorithm;
            var imageSize = new FileInfo(imagePath).Length;
            var digest = ComputeHash(File.ReadAllBytes(imagePath), salt, hashAlgorithm);

            var newHash = new AvbHashDescriptor
            {
                ImageSize = imageSize,
                HashAlgorithm = hashAlgorithm,
                PartitionName = partitionName,
                Salt = salt,
                Digest = digest,
                Flags = hashDesc?.Flags ?? 0
            };

            var descriptors = new List<byte[]> { EncodeHashDescriptor(newHash) };
            descriptors.AddRange(info.Descriptors.OfType<AvbPropertyDescriptor>().Select(EncodePropertyDescriptor));

            var keyPath = ResolvePrivateKey(avbRoot, info.AlgorithmName, keySelection);
            if (!IsNoneAlgorithm(info.AlgorithmName) && string.IsNullOrWhiteSpace(keyPath)) return false;

            var vbmeta = GenerateVbmetaBlob(
                info.AlgorithmName,
                keyPath,
                descriptors,
                info.RollbackIndex,
                info.Flags,
                0,
                info.RequiredMinor);

            AppendVbmetaAndFooter(imagePath, partitionSize, vbmeta, info.OriginalImageSize > 0 ? info.OriginalImageSize : imageSize);
            Log($"链式分区 {partitionName} 重建成功", "green");
            return true;
        }

        private bool RebuildHashPartition(
            string partitionName,
            string imagePath,
            long partitionSize,
            AvbImageInfo info,
            bool useOriginalSalt)
        {
            var hashDesc = info.Descriptors.OfType<AvbHashDescriptor>().FirstOrDefault(x =>
                string.Equals(x.PartitionName, partitionName, StringComparison.OrdinalIgnoreCase));
            var salt = useOriginalSalt && hashDesc?.Salt.Length > 0 ? hashDesc.Salt : RandomBytes(32);
            var hashAlgorithm = string.IsNullOrWhiteSpace(hashDesc?.HashAlgorithm) ? "sha256" : hashDesc.HashAlgorithm;
            var imageSize = new FileInfo(imagePath).Length;
            var digest = ComputeHash(File.ReadAllBytes(imagePath), salt, hashAlgorithm);

            if (useOriginalSalt && hashDesc?.Salt.Length > 0)
            {
                Log($"使用原有salt: {ToHex(salt)}");
            }
            else
            {
                Log($"生成新的salt: {ToHex(salt)[..16]}...");
            }

            var newHash = new AvbHashDescriptor
            {
                ImageSize = imageSize,
                HashAlgorithm = hashAlgorithm,
                PartitionName = partitionName,
                Salt = salt,
                Digest = digest,
                Flags = hashDesc?.Flags ?? 0
            };

            var descriptors = new List<byte[]> { EncodeHashDescriptor(newHash) };
            descriptors.AddRange(info.Descriptors.OfType<AvbPropertyDescriptor>().Select(EncodePropertyDescriptor));
            var vbmeta = GenerateVbmetaBlob("NONE", null, descriptors, 0, 0, 0, info.RequiredMinor);
            AppendVbmetaAndFooter(imagePath, partitionSize, vbmeta, info.OriginalImageSize > 0 ? info.OriginalImageSize : imageSize);
            Log($"普通分区 {partitionName} 重建成功", "green");
            return true;
        }

        private void RebuildVbmeta(
            string vbmetaPath,
            Dictionary<string, string> partitionImages,
            string avbRoot,
            int keySelection)
        {
            Log("重建 vbmeta.img", "yellow");
            if (!File.Exists(vbmetaPath))
            {
                Log("未找到原始 vbmeta 镜像", "red");
                return;
            }

            var originalInfo = ParseImage(vbmetaPath);
            if (originalInfo == null)
            {
                Log("无法解析原 vbmeta.img", "red");
                return;
            }

            var includedImages = new List<AvbImageInfo> { originalInfo };
            foreach (var image in partitionImages.Values)
            {
                var info = ParseImage(image);
                if (info != null)
                {
                    includedImages.Add(info);
                }
            }

            var descriptors = MergeIncludedDescriptors(includedImages);
            var requiredMinor = includedImages.Max(x => x.RequiredMinor);
            var keyPath = ResolvePrivateKey(avbRoot, originalInfo.AlgorithmName, keySelection);
            if (!IsNoneAlgorithm(originalInfo.AlgorithmName) && string.IsNullOrWhiteSpace(keyPath)) return;

            var vbmeta = GenerateVbmetaBlob(
                originalInfo.AlgorithmName,
                keyPath,
                descriptors,
                originalInfo.RollbackIndex,
                originalInfo.Flags,
                0,
                requiredMinor);

            File.WriteAllBytes(vbmetaPath, PadTo(vbmeta, BlockSize));
            Log("vbmeta.img 重建成功", "green");
        }

        private static List<byte[]> MergeIncludedDescriptors(IEnumerable<AvbImageInfo> images)
        {
            var descriptorsWithoutPartitionName = new List<byte[]>();
            var partitionDescriptors = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);

            foreach (var info in images)
            {
                foreach (var raw in info.RawDescriptors)
                {
                    if (TryGetPartitionDescriptorKey(raw, out var key))
                    {
                        partitionDescriptors[key] = raw;
                    }
                    else
                    {
                        descriptorsWithoutPartitionName.Add(raw);
                    }
                }
            }

            descriptorsWithoutPartitionName.AddRange(partitionDescriptors.Values);
            return descriptorsWithoutPartitionName;
        }

        private static bool TryGetPartitionDescriptorKey(byte[] raw, out string key)
        {
            key = string.Empty;
            if (raw.Length < 16)
            {
                return false;
            }

            try
            {
                var tag = ReadU64(raw, 0);
                int nameLength;
                int nameOffset;
                string descriptorType;
                switch (tag)
                {
                    case 1:
                        if (raw.Length < 180) return false;
                        nameLength = checked((int)ReadU32(raw, 104));
                        nameOffset = 180;
                        descriptorType = "AvbHashtreeDescriptor";
                        break;
                    case 2:
                        if (raw.Length < 132) return false;
                        nameLength = checked((int)ReadU32(raw, 56));
                        nameOffset = 132;
                        descriptorType = "AvbHashDescriptor";
                        break;
                    case 4:
                        if (raw.Length < 92) return false;
                        nameLength = checked((int)ReadU32(raw, 20));
                        nameOffset = 92;
                        descriptorType = "AvbChainPartitionDescriptor";
                        break;
                    default:
                        return false;
                }

                if (nameLength <= 0 || nameOffset + nameLength > raw.Length)
                {
                    return false;
                }

                var partitionName = Encoding.UTF8.GetString(raw, nameOffset, nameLength);
                if (string.IsNullOrWhiteSpace(partitionName))
                {
                    return false;
                }

                key = descriptorType + "_" + partitionName;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static byte[] EncodeAospChainDescriptor(
            string partitionNameText,
            uint rollbackIndexLocation,
            byte[] publicKey,
            uint flags)
        {
            var partitionName = Encoding.UTF8.GetBytes(partitionNameText);
            var nbf = 92 - 16 + partitionName.Length + publicKey.Length;
            var paddedNbf = RoundToMultiple(nbf, 8);
            var result = new byte[16 + paddedNbf];

            WriteU64(result, 0, 4);
            WriteU64(result, 8, (ulong)paddedNbf);
            WriteU32(result, 16, rollbackIndexLocation);
            WriteU32(result, 20, (uint)partitionName.Length);
            WriteU32(result, 24, (uint)publicKey.Length);
            WriteU32(result, 28, flags);

            var offset = 92;
            Array.Copy(partitionName, 0, result, offset, partitionName.Length);
            offset += partitionName.Length;
            Array.Copy(publicKey, 0, result, offset, publicKey.Length);
            return result;
        }

        private static bool TryParseAospChainDescriptor(byte[] raw, out AospChainDescriptor chain)
        {
            chain = default;
            if (raw.Length < 92 || ReadU64(raw, 0) != 4) return false;

            try
            {
                var nameLength = checked((int)ReadU32(raw, 20));
                var keyLength = checked((int)ReadU32(raw, 24));
                if (nameLength <= 0 || 92 + nameLength + keyLength > raw.Length) return false;

                chain = new AospChainDescriptor(
                    ReadU32(raw, 16),
                    Encoding.UTF8.GetString(raw, 92, nameLength),
                    ReadU32(raw, 28));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private AvbImageInfo? ParseImage(string imagePath)
        {
            try
            {
                var fileSize = new FileInfo(imagePath).Length;
                byte[] vbmeta;
                AvbFooter? footer = TryReadFooter(imagePath);
                long originalImageSize = fileSize;

                if (footer != null)
                {
                    originalImageSize = (long)footer.OriginalImageSize;
                    using var fs = File.OpenRead(imagePath);
                    fs.Position = (long)footer.VbmetaOffset;
                    vbmeta = new byte[footer.VbmetaSize];
                    ReadExactly(fs, vbmeta);
                }
                else
                {
                    using var fs = File.OpenRead(imagePath);
                    var header = new byte[4];
                    ReadExactly(fs, header);
                    if (Encoding.ASCII.GetString(header) != "AVB0") return null;
                    fs.Position = 0;
                    vbmeta = new byte[fileSize];
                    ReadExactly(fs, vbmeta);
                }

                var info = ParseVbmeta(vbmeta);
                info.ImageSize = fileSize;
                info.OriginalImageSize = originalImageSize;
                if (footer != null)
                {
                    info.VbmetaOffset = (long)footer.VbmetaOffset;
                    info.VbmetaSize = (long)footer.VbmetaSize;
                }
                return info;
            }
            catch
            {
                return null;
            }
        }

        private AvbImageInfo ParseVbmeta(byte[] blob)
        {
            if (blob.Length < VbmetaHeaderSize || Encoding.ASCII.GetString(blob, 0, 4) != "AVB0")
            {
                throw new InvalidDataException("Invalid vbmeta header.");
            }

            var header = AvbVbmetaHeader.Parse(blob.AsSpan(0, VbmetaHeaderSize));
            var info = new AvbImageInfo
            {
                RequiredMajor = header.RequiredMajor,
                RequiredMinor = header.RequiredMinor,
                AlgorithmName = AlgorithmSpec.FromType(header.AlgorithmType).Name,
                RollbackIndex = (long)header.RollbackIndex,
                Flags = header.Flags,
                RollbackIndexLocation = header.RollbackIndexLocation,
                ReleaseString = header.ReleaseString
            };

            var auxiliaryBase = checked(VbmetaHeaderSize + (int)header.AuthenticationDataBlockSize);
            var publicKeyOffset = checked(auxiliaryBase + (int)header.PublicKeyOffset);
            var publicKeySize = checked((int)header.PublicKeySize);
            if (publicKeyOffset < 0 || publicKeySize < 0 ||
                publicKeyOffset > blob.Length - publicKeySize)
            {
                throw new InvalidDataException("AVB public key range is invalid.");
            }
            info.PublicKey = blob.AsSpan(publicKeyOffset, publicKeySize).ToArray();

            var descriptorBase = VbmetaHeaderSize + (int)header.AuthenticationDataBlockSize + (int)header.DescriptorsOffset;
            var descriptorEnd = descriptorBase + (int)header.DescriptorsSize;
            var offset = descriptorBase;
            while (offset + 16 <= descriptorEnd)
            {
                var tag = ReadU64(blob, offset);
                var numBytesFollowing = (int)ReadU64(blob, offset + 8);
                var rawLen = 16 + numBytesFollowing;
                if (rawLen <= 16 || offset + rawLen > blob.Length) break;
                var raw = blob.Skip(offset).Take(rawLen).ToArray();
                info.RawDescriptors.Add(raw);

                if (tag == 2)
                {
                    info.Descriptors.Add(ParseHashDescriptor(raw));
                }
                else if (tag == 0)
                {
                    info.Descriptors.Add(ParsePropertyDescriptor(raw));
                }
                else if (tag == 4 && TryParseAospChainDescriptor(raw, out var chain))
                {
                    var nameLength = checked((int)ReadU32(raw, 20));
                    var keyLength = checked((int)ReadU32(raw, 24));
                    var keyOffset = checked(92 + nameLength);
                    if (keyLength > 0 && keyOffset <= raw.Length - keyLength)
                    {
                        info.ChainPublicKeys[chain.PartitionName] = raw
                            .AsSpan(keyOffset, keyLength)
                            .ToArray();
                    }
                    info.Descriptors.Add(new AvbDescriptor { Type = $"tag:{tag}" });
                }
                else
                {
                    info.Descriptors.Add(new AvbDescriptor { Type = $"tag:{tag}" });
                }

                offset += rawLen;
            }

            return info;
        }

        private void LogImageInfo(string imagePath)
        {
            var info = ParseImage(imagePath);
            if (info == null)
            {
                Log($"{Path.GetFileName(imagePath)}: 未检测到AVB信息", "yellow");
                return;
            }

            Log($"{Path.GetFileName(imagePath)}:", "yellow");
            Log($"  Algorithm: {info.AlgorithmName}");
            Log($"  Rollback Index: {info.RollbackIndex}");
            Log($"  Flags: {info.Flags}");
            Log($"  Image size: {info.ImageSize} bytes");
            Log($"  Original image size: {info.OriginalImageSize} bytes");
            foreach (var hash in info.Descriptors.OfType<AvbHashDescriptor>())
            {
                Log($"  Hash: {hash.PartitionName}, {hash.HashAlgorithm}, size={hash.ImageSize}, salt={ToHex(hash.Salt)}");
            }
            foreach (var prop in info.Descriptors.OfType<AvbPropertyDescriptor>())
            {
                Log($"  Prop: {prop.Key} -> {prop.Value}");
            }
        }

        private Dictionary<string, string> DetectPartitionImages(string workDir, IEnumerable<string> partitions)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var partition in NormalizePartitions(partitions))
            {
                var imagePath = ResolvePartitionImagePath(workDir, partition);
                if (File.Exists(imagePath))
                {
                    var name = Path.GetFileNameWithoutExtension(imagePath);
                    result[name] = imagePath;
                    Log($"发现分区镜像: {name} -> {Path.GetFileName(imagePath)}");
                }
                else
                {
                    Log($"指定的分区镜像不存在: {Path.GetFileName(imagePath)}", "yellow");
                }
            }
            return result;
        }

        private static List<string> NormalizePartitions(IEnumerable<string> partitions)
        {
            var list = partitions
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return list.Count > 0 ? list : new List<string> { "boot", "init_boot" };
        }

        private void EraseFooter(string imagePath)
        {
            var footer = TryReadFooter(imagePath);
            if (footer == null) return;
            using var fs = new FileStream(imagePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            fs.SetLength((long)footer.OriginalImageSize);
            Log($"擦除AVB footer: {Path.GetFileName(imagePath)}");
        }

        private void AppendVbmetaAndFooter(string imagePath, long partitionSize, byte[] vbmetaBlob, long originalImageSize)
        {
            using var fs = new FileStream(imagePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            if (fs.Length % BlockSize != 0)
            {
                fs.SetLength(RoundToMultipleLong(fs.Length, BlockSize));
            }

            var vbmetaOffset = fs.Length;
            var paddedVbmeta = PadTo(vbmetaBlob, BlockSize);
            fs.Position = fs.Length;
            fs.Write(paddedVbmeta);

            var footerBlockOffset = partitionSize - BlockSize;
            if (footerBlockOffset < fs.Length)
            {
                throw new InvalidOperationException("分区空间不足，无法写入 vbmeta/footer。");
            }

            fs.SetLength(footerBlockOffset);
            fs.Position = footerBlockOffset;
            fs.Write(new byte[BlockSize - FooterSize]);
            fs.Write(EncodeFooter(new AvbFooter
            {
                OriginalImageSize = (ulong)originalImageSize,
                VbmetaOffset = (ulong)vbmetaOffset,
                VbmetaSize = (ulong)vbmetaBlob.Length
            }));
            fs.SetLength(partitionSize);
        }

        private void WriteWithAvbtoolPadding(string path, byte[] data, long paddingSize)
        {
            if (paddingSize <= 0)
            {
                File.WriteAllBytes(path, data);
                return;
            }

            var alignment = checked((int)Math.Min(paddingSize, int.MaxValue));
            var paddedSize = RoundToMultipleLong(data.Length, alignment);
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            fs.Write(data);
            if (fs.Length < paddedSize)
            {
                fs.Write(new byte[checked((int)(paddedSize - fs.Length))]);
            }

            if (paddedSize > paddingSize)
            {
                Log($"{Path.GetFileName(path)} 新 vbmeta 大于原大小，已按 avbtool padding 规则扩展到 {paddedSize} bytes", "yellow");
            }
        }

        private byte[] GenerateVbmetaBlob(
            string algorithmName,
            string? keyPath,
            List<byte[]> encodedDescriptors,
            long rollbackIndex,
            uint flags,
            uint rollbackIndexLocation,
            uint requiredMinor = 0)
        {
            var alg = AlgorithmSpec.FromName(algorithmName);
            var descriptorsBlob = Concat(encodedDescriptors);
            var encodedKey = alg.Type == 0 ? Array.Empty<byte>() : EncodeAvbPublicKey(keyPath!);

            var auxSize = RoundToMultiple(descriptorsBlob.Length + encodedKey.Length, 64);
            var authSize = RoundToMultiple(alg.HashSize + alg.SignatureSize, 64);
            var header = new AvbVbmetaHeader
            {
                RequiredMajor = 1,
                RequiredMinor = requiredMinor,
                AuthenticationDataBlockSize = (ulong)authSize,
                AuxiliaryDataBlockSize = (ulong)auxSize,
                AlgorithmType = alg.Type,
                HashOffset = 0,
                HashSize = (ulong)alg.HashSize,
                SignatureOffset = (ulong)alg.HashSize,
                SignatureSize = (ulong)alg.SignatureSize,
                PublicKeyOffset = (ulong)descriptorsBlob.Length,
                PublicKeySize = (ulong)encodedKey.Length,
                PublicKeyMetadataOffset = (ulong)(descriptorsBlob.Length + encodedKey.Length),
                PublicKeyMetadataSize = 0,
                DescriptorsOffset = 0,
                DescriptorsSize = (ulong)descriptorsBlob.Length,
                RollbackIndex = (ulong)Math.Max(0, rollbackIndex),
                Flags = flags,
                RollbackIndexLocation = rollbackIndexLocation,
                ReleaseString = "avbtool 1.3.0"
            };

            var headerBlob = EncodeHeader(header);
            var auxBlob = PadTo(Concat(new[] { descriptorsBlob, encodedKey }), 64);
            Array.Resize(ref auxBlob, auxSize);

            byte[] binaryHash = Array.Empty<byte>();
            byte[] binarySignature = Array.Empty<byte>();
            if (alg.Type != 0)
            {
                var signedData = Concat(new[] { headerBlob, auxBlob });
                binaryHash = ComputeDigest(signedData, alg.HashName);
                binarySignature = SignDigest(keyPath!, binaryHash, alg.HashName);
                if (binarySignature.Length != alg.SignatureSize)
                {
                    throw new InvalidOperationException("RSA签名长度与AVB算法不匹配。");
                }
            }

            var authBlob = PadTo(Concat(new[] { binaryHash, binarySignature }), 64);
            Array.Resize(ref authBlob, authSize);
            return Concat(new[] { headerBlob, authBlob, auxBlob });
        }

        private string ResolvePrivateKey(string avbRoot, string algorithmName, int keySelection)
        {
            if (IsNoneAlgorithm(algorithmName))
            {
                return string.Empty;
            }

            var keyPath = keySelection switch
            {
                1 => Path.Combine(avbRoot, "tools", "pem", "testkey_rsa4096.pem"),
                2 => Path.Combine(avbRoot, "tools", "pem", "testkey_rsa2048.pem"),
                _ when algorithmName.Contains("RSA4096", StringComparison.OrdinalIgnoreCase) => Path.Combine(avbRoot, "tools", "pem", "testkey_rsa4096.pem"),
                _ when algorithmName.Contains("RSA2048", StringComparison.OrdinalIgnoreCase) => Path.Combine(avbRoot, "tools", "pem", "testkey_rsa2048.pem"),
                _ => string.Empty
            };

            if (string.IsNullOrWhiteSpace(keyPath))
            {
                Log($"不支持的AVB算法: {algorithmName}", "red");
                return string.Empty;
            }

            if (!File.Exists(keyPath))
            {
                Log($"未找到私钥: {keyPath}", "red");
                return string.Empty;
            }

            Log($"算法 {algorithmName} 使用私钥: {Path.GetFileName(keyPath)}");
            return keyPath;
        }

        private static bool IsNoneAlgorithm(string algorithmName)
        {
            return string.Equals(algorithmName, "NONE", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolvePartitionImagePath(string workDir, string partition)
        {
            return partition.EndsWith(".img", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(workDir, partition)
                : Path.Combine(workDir, partition + ".img");
        }

        private static byte[] ComputeHash(byte[] data, byte[] salt, string hashAlgorithm)
        {
            var input = Concat(new[] { salt, data });
            return ComputeDigest(input, hashAlgorithm);
        }

        private static byte[] ComputeDigest(byte[] data, string hashAlgorithm)
        {
            return hashAlgorithm.Equals("sha512", StringComparison.OrdinalIgnoreCase)
                ? SHA512.HashData(data)
                : SHA256.HashData(data);
        }

        private static byte[] SignDigest(string keyPath, byte[] digest, string hashAlgorithm)
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(File.ReadAllText(keyPath));
            var hashName = hashAlgorithm.Equals("sha512", StringComparison.OrdinalIgnoreCase)
                ? HashAlgorithmName.SHA512
                : HashAlgorithmName.SHA256;
            return rsa.SignHash(digest, hashName, RSASignaturePadding.Pkcs1);
        }

        private static byte[] EncodeAvbPublicKey(string keyPath)
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(File.ReadAllText(keyPath));
            var p = rsa.ExportParameters(false);
            if (p.Modulus == null || p.Exponent == null) throw new InvalidDataException("Invalid RSA key.");

            var modulus = new BigInteger(p.Modulus, isUnsigned: true, isBigEndian: true);
            var numBits = RoundToPow2(BitLength(modulus));
            var n0inv = unchecked((uint)(0x1_0000_0000UL - ModInverse32((uint)(modulus & 0xffffffff))));
            var rr = BigInteger.ModPow(new BigInteger(2), numBits * 2, modulus);

            var result = new byte[8 + (numBits / 8) * 2];
            WriteU32(result, 0, (uint)numBits);
            WriteU32(result, 4, n0inv);
            WriteBigEndianInteger(result.AsSpan(8, numBits / 8), modulus);
            WriteBigEndianInteger(result.AsSpan(8 + numBits / 8, numBits / 8), rr);
            return result;
        }

        private static AvbHashDescriptor ParseHashDescriptor(byte[] raw)
        {
            var partitionNameLen = (int)ReadU32(raw, 56);
            var saltLen = (int)ReadU32(raw, 60);
            var digestLen = (int)ReadU32(raw, 64);
            var desc = new AvbHashDescriptor
            {
                Type = "hash",
                ImageSize = (long)ReadU64(raw, 16),
                HashAlgorithm = Encoding.ASCII.GetString(raw, 24, 32).TrimEnd('\0'),
                Flags = ReadU32(raw, 68)
            };

            var offset = 132;
            desc.PartitionName = Encoding.UTF8.GetString(raw, offset, partitionNameLen);
            offset += partitionNameLen;
            desc.Salt = raw.Skip(offset).Take(saltLen).ToArray();
            offset += saltLen;
            desc.Digest = raw.Skip(offset).Take(digestLen).ToArray();
            return desc;
        }

        private static AvbPropertyDescriptor ParsePropertyDescriptor(byte[] raw)
        {
            var keySize = (int)ReadU64(raw, 16);
            var valueSize = (int)ReadU64(raw, 24);
            var key = Encoding.UTF8.GetString(raw, 32, keySize);
            var value = Encoding.UTF8.GetString(raw, 32 + keySize + 1, valueSize);
            return new AvbPropertyDescriptor { Type = "prop", Key = key, Value = value };
        }

        private static byte[] EncodeHashDescriptor(AvbHashDescriptor desc)
        {
            var hashAlgorithm = Encoding.ASCII.GetBytes(desc.HashAlgorithm);
            var partitionName = Encoding.UTF8.GetBytes(desc.PartitionName);
            var nbf = 132 - 16 + partitionName.Length + desc.Salt.Length + desc.Digest.Length;
            var paddedNbf = RoundToMultiple(nbf, 8);
            var result = new byte[16 + paddedNbf];
            WriteU64(result, 0, 2);
            WriteU64(result, 8, (ulong)paddedNbf);
            WriteU64(result, 16, (ulong)desc.ImageSize);
            Array.Copy(hashAlgorithm, 0, result, 24, Math.Min(hashAlgorithm.Length, 32));
            WriteU32(result, 56, (uint)partitionName.Length);
            WriteU32(result, 60, (uint)desc.Salt.Length);
            WriteU32(result, 64, (uint)desc.Digest.Length);
            WriteU32(result, 68, desc.Flags);
            var offset = 132;
            Array.Copy(partitionName, 0, result, offset, partitionName.Length);
            offset += partitionName.Length;
            Array.Copy(desc.Salt, 0, result, offset, desc.Salt.Length);
            offset += desc.Salt.Length;
            Array.Copy(desc.Digest, 0, result, offset, desc.Digest.Length);
            return result;
        }

        private static byte[] EncodePropertyDescriptor(AvbPropertyDescriptor desc)
        {
            var key = Encoding.UTF8.GetBytes(desc.Key);
            var value = Encoding.UTF8.GetBytes(desc.Value);
            var nbf = 32 - 16 + key.Length + 1 + value.Length + 1;
            var paddedNbf = RoundToMultiple(nbf, 8);
            var result = new byte[16 + paddedNbf];
            WriteU64(result, 0, 0);
            WriteU64(result, 8, (ulong)paddedNbf);
            WriteU64(result, 16, (ulong)key.Length);
            WriteU64(result, 24, (ulong)value.Length);
            Array.Copy(key, 0, result, 32, key.Length);
            Array.Copy(value, 0, result, 32 + key.Length + 1, value.Length);
            return result;
        }

        private static AvbFooter? TryReadFooter(string imagePath)
        {
            var file = new FileInfo(imagePath);
            if (file.Length < FooterSize) return null;
            var data = new byte[FooterSize];
            using var fs = File.OpenRead(imagePath);
            fs.Position = file.Length - FooterSize;
            ReadExactly(fs, data);
            if (Encoding.ASCII.GetString(data, 0, 4) != "AVBf") return null;
            return new AvbFooter
            {
                OriginalImageSize = ReadU64(data, 12),
                VbmetaOffset = ReadU64(data, 20),
                VbmetaSize = ReadU64(data, 28)
            };
        }

        private static byte[] EncodeFooter(AvbFooter footer)
        {
            var data = new byte[FooterSize];
            Encoding.ASCII.GetBytes("AVBf").CopyTo(data, 0);
            WriteU32(data, 4, 1);
            WriteU32(data, 8, 0);
            WriteU64(data, 12, footer.OriginalImageSize);
            WriteU64(data, 20, footer.VbmetaOffset);
            WriteU64(data, 28, footer.VbmetaSize);
            return data;
        }

        private static byte[] EncodeHeader(AvbVbmetaHeader h)
        {
            var data = new byte[VbmetaHeaderSize];
            Encoding.ASCII.GetBytes("AVB0").CopyTo(data, 0);
            WriteU32(data, 4, h.RequiredMajor);
            WriteU32(data, 8, h.RequiredMinor);
            WriteU64(data, 12, h.AuthenticationDataBlockSize);
            WriteU64(data, 20, h.AuxiliaryDataBlockSize);
            WriteU32(data, 28, h.AlgorithmType);
            WriteU64(data, 32, h.HashOffset);
            WriteU64(data, 40, h.HashSize);
            WriteU64(data, 48, h.SignatureOffset);
            WriteU64(data, 56, h.SignatureSize);
            WriteU64(data, 64, h.PublicKeyOffset);
            WriteU64(data, 72, h.PublicKeySize);
            WriteU64(data, 80, h.PublicKeyMetadataOffset);
            WriteU64(data, 88, h.PublicKeyMetadataSize);
            WriteU64(data, 96, h.DescriptorsOffset);
            WriteU64(data, 104, h.DescriptorsSize);
            WriteU64(data, 112, h.RollbackIndex);
            WriteU32(data, 120, h.Flags);
            WriteU32(data, 124, h.RollbackIndexLocation);
            var release = Encoding.UTF8.GetBytes(h.ReleaseString);
            Array.Copy(release, 0, data, 128, Math.Min(47, release.Length));
            return data;
        }

        private static byte[] RandomBytes(int count)
        {
            var bytes = new byte[count];
            RandomNumberGenerator.Fill(bytes);
            return bytes;
        }

        private static byte[] PadTo(byte[] data, int size)
        {
            var paddedLen = RoundToMultiple(data.Length, size);
            if (paddedLen == data.Length) return data;
            var result = new byte[paddedLen];
            Array.Copy(data, result, data.Length);
            return result;
        }

        private static byte[] Concat(IEnumerable<byte[]> arrays)
        {
            var list = arrays.ToList();
            var result = new byte[list.Sum(x => x.Length)];
            var offset = 0;
            foreach (var arr in list)
            {
                Array.Copy(arr, 0, result, offset, arr.Length);
                offset += arr.Length;
            }
            return result;
        }

        private static void ReadExactly(Stream stream, byte[] buffer)
        {
            var offset = 0;
            while (offset < buffer.Length)
            {
                var read = stream.Read(buffer, offset, buffer.Length - offset);
                if (read <= 0) throw new EndOfStreamException();
                offset += read;
            }
        }

        private static int RoundToMultiple(long value, int size)
        {
            var rem = value % size;
            return checked((int)(rem == 0 ? value : value + size - rem));
        }

        private static long RoundToMultipleLong(long value, int size)
        {
            var rem = value % size;
            return rem == 0 ? value : value + size - rem;
        }

        private static int RoundToPow2(int number)
        {
            var result = 1;
            while (result < number) result <<= 1;
            return result;
        }

        private static int BitLength(BigInteger value)
        {
            return value.ToByteArray(isUnsigned: true, isBigEndian: true).Length * 8;
        }

        private static uint ModInverse32(uint value)
        {
            long t = 0, newT = 1;
            long r = 1L << 32, newR = value;
            while (newR != 0)
            {
                var q = r / newR;
                (t, newT) = (newT, t - q * newT);
                (r, newR) = (newR, r - q * newR);
            }
            if (t < 0) t += 1L << 32;
            return (uint)t;
        }

        private static void WriteBigEndianInteger(Span<byte> target, BigInteger value)
        {
            target.Clear();
            var bytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);
            bytes.CopyTo(target[^bytes.Length..]);
        }

        private static ulong ReadU64(byte[] data, int offset) => BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(offset, 8));
        private static uint ReadU32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));
        private static void WriteU64(byte[] data, int offset, ulong value) => BinaryPrimitives.WriteUInt64BigEndian(data.AsSpan(offset, 8), value);
        private static void WriteU32(byte[] data, int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset, 4), value);
        private static string ToHex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
        private void Log(string message, string color = "Black") => _log(message, color);

        private sealed record AospBuildProps(string OsVersion, string Fingerprint, string SecurityPatch);

        private readonly record struct AospChainDescriptor(
            uint RollbackIndexLocation,
            string PartitionName,
            uint Flags);

        private sealed class AlgorithmSpec
        {
            public string Name { get; init; } = "NONE";
            public uint Type { get; init; }
            public string HashName { get; init; } = string.Empty;
            public int HashSize { get; init; }
            public int SignatureSize { get; init; }

            public static AlgorithmSpec FromName(string name)
            {
                return Specs.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))
                       ?? throw new NotSupportedException($"Unsupported AVB algorithm: {name}");
            }

            public static AlgorithmSpec FromType(uint type)
            {
                return Specs.FirstOrDefault(x => x.Type == type)
                       ?? throw new NotSupportedException($"Unsupported AVB algorithm type: {type}");
            }

            private static readonly List<AlgorithmSpec> Specs = new()
            {
                new AlgorithmSpec { Name = "NONE", Type = 0 },
                new AlgorithmSpec { Name = "SHA256_RSA2048", Type = 1, HashName = "sha256", HashSize = 32, SignatureSize = 256 },
                new AlgorithmSpec { Name = "SHA256_RSA4096", Type = 2, HashName = "sha256", HashSize = 32, SignatureSize = 512 },
                new AlgorithmSpec { Name = "SHA512_RSA2048", Type = 4, HashName = "sha512", HashSize = 64, SignatureSize = 256 },
                new AlgorithmSpec { Name = "SHA512_RSA4096", Type = 5, HashName = "sha512", HashSize = 64, SignatureSize = 512 }
            };
        }

        private sealed class AvbFooter
        {
            public ulong OriginalImageSize { get; set; }
            public ulong VbmetaOffset { get; set; }
            public ulong VbmetaSize { get; set; }
        }

        private sealed class AvbVbmetaHeader
        {
            public uint RequiredMajor { get; set; }
            public uint RequiredMinor { get; set; }
            public ulong AuthenticationDataBlockSize { get; set; }
            public ulong AuxiliaryDataBlockSize { get; set; }
            public uint AlgorithmType { get; set; }
            public ulong HashOffset { get; set; }
            public ulong HashSize { get; set; }
            public ulong SignatureOffset { get; set; }
            public ulong SignatureSize { get; set; }
            public ulong PublicKeyOffset { get; set; }
            public ulong PublicKeySize { get; set; }
            public ulong PublicKeyMetadataOffset { get; set; }
            public ulong PublicKeyMetadataSize { get; set; }
            public ulong DescriptorsOffset { get; set; }
            public ulong DescriptorsSize { get; set; }
            public ulong RollbackIndex { get; set; }
            public uint Flags { get; set; }
            public uint RollbackIndexLocation { get; set; }
            public string ReleaseString { get; set; } = string.Empty;

            public static AvbVbmetaHeader Parse(ReadOnlySpan<byte> data)
            {
                return new AvbVbmetaHeader
                {
                    RequiredMajor = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(4, 4)),
                    RequiredMinor = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(8, 4)),
                    AuthenticationDataBlockSize = BinaryPrimitives.ReadUInt64BigEndian(data.Slice(12, 8)),
                    AuxiliaryDataBlockSize = BinaryPrimitives.ReadUInt64BigEndian(data.Slice(20, 8)),
                    AlgorithmType = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(28, 4)),
                    HashOffset = BinaryPrimitives.ReadUInt64BigEndian(data.Slice(32, 8)),
                    HashSize = BinaryPrimitives.ReadUInt64BigEndian(data.Slice(40, 8)),
                    SignatureOffset = BinaryPrimitives.ReadUInt64BigEndian(data.Slice(48, 8)),
                    SignatureSize = BinaryPrimitives.ReadUInt64BigEndian(data.Slice(56, 8)),
                    PublicKeyOffset = BinaryPrimitives.ReadUInt64BigEndian(data.Slice(64, 8)),
                    PublicKeySize = BinaryPrimitives.ReadUInt64BigEndian(data.Slice(72, 8)),
                    PublicKeyMetadataOffset = BinaryPrimitives.ReadUInt64BigEndian(data.Slice(80, 8)),
                    PublicKeyMetadataSize = BinaryPrimitives.ReadUInt64BigEndian(data.Slice(88, 8)),
                    DescriptorsOffset = BinaryPrimitives.ReadUInt64BigEndian(data.Slice(96, 8)),
                    DescriptorsSize = BinaryPrimitives.ReadUInt64BigEndian(data.Slice(104, 8)),
                    RollbackIndex = BinaryPrimitives.ReadUInt64BigEndian(data.Slice(112, 8)),
                    Flags = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(120, 4)),
                    RollbackIndexLocation = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(124, 4)),
                    ReleaseString = Encoding.UTF8.GetString(data.Slice(128, 48)).TrimEnd('\0')
                };
            }
        }

        private class AvbDescriptor
        {
            public string Type { get; set; } = string.Empty;
        }

        private sealed class AvbHashDescriptor : AvbDescriptor
        {
            public long ImageSize { get; set; }
            public string HashAlgorithm { get; set; } = "sha256";
            public string PartitionName { get; set; } = string.Empty;
            public byte[] Salt { get; set; } = Array.Empty<byte>();
            public byte[] Digest { get; set; } = Array.Empty<byte>();
            public uint Flags { get; set; }
        }

        private sealed class AvbPropertyDescriptor : AvbDescriptor
        {
            public string Key { get; set; } = string.Empty;
            public string Value { get; set; } = string.Empty;
        }

        private sealed class AvbImageInfo
        {
            public uint RequiredMajor { get; set; } = 1;
            public uint RequiredMinor { get; set; }
            public string AlgorithmName { get; set; } = "NONE";
            public long RollbackIndex { get; set; }
            public uint Flags { get; set; }
            public uint RollbackIndexLocation { get; set; }
            public string ReleaseString { get; set; } = string.Empty;
            public long ImageSize { get; set; }
            public long OriginalImageSize { get; set; }
            public long VbmetaOffset { get; set; }
            public long VbmetaSize { get; set; }
            public List<AvbDescriptor> Descriptors { get; } = new();
            public List<byte[]> RawDescriptors { get; } = new();
            public byte[] PublicKey { get; set; } = Array.Empty<byte>();
            public Dictionary<string, byte[]> ChainPublicKeys { get; } =
                new(StringComparer.OrdinalIgnoreCase);
        }

        public sealed record AvbPublicKeyDetails(int KeyBits, string Sha256);

        public sealed record AvbPublicKeyProfile(
            AvbPublicKeyDetails MainKey,
            IReadOnlyDictionary<string, AvbPublicKeyDetails> ChainKeys);
    }
}
