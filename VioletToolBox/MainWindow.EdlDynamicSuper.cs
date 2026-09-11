using System.Diagnostics;
using System.Globalization;
using AFlashTool.Modular.Super;
using AFlashTool.Modular.Super.LpMake;
using EdlPartitionInfo = SharpEDL.DataClass.PartitionInfo;

namespace WpfApp1;

internal partial class EdlEngine
{
    private DynamicSuperPlan CreateDynamicSuperPlan(string programPath)
    {
        string? definitionPath = DynamicSuperPlanner.FindDefinition(programPath);
        if (definitionPath == null)
            throw new FileNotFoundException("未找到 META/super_def*.json，无法执行 super 免合并写入");

        _appendNativeLog($"[DynamicSuper] definition={definitionPath}");
        DynamicSuperPlan plan = DynamicSuperPlanner.Create(programPath, definitionPath);
        _appendNativeLog(
            $"[DynamicSuper] block_size={plan.BlockDeviceSize}, metadata_slots={plan.Metadata.Geometry.MetadataSlotCount}, " +
            $"metadata_version={plan.Metadata.Header.MajorVersion}.{plan.Metadata.Header.MinorVersion}, " +
            $"flags=0x{plan.Metadata.Header.Flags:X}, partitions={plan.Images.Count}, write_bytes={plan.TotalWriteBytes}");
        return plan;
    }

    private void WriteDynamicSuperPlan(EdlPartitionInfo superPartition, DynamicSuperPlan plan)
    {
        ThrowIfStopRequested();
        if (_currentPort == null || !_currentPort.IsOpen)
            throw new InvalidOperationException("串口未打开");
        if (!TryParseLongAllowHex(superPartition.StartSector, out long superStartSector))
            throw new InvalidOperationException($"无法解析 super 起始扇区: {superPartition.StartSector}");

        int sectorSize = _firehoseSectorSize > 0 ? _firehoseSectorSize : 4096;
        int partitionSectorSize = superPartition.BytesPerSector > 0
            ? superPartition.BytesPerSector
            : sectorSize;
        if (partitionSectorSize != sectorSize)
        {
            throw new InvalidOperationException(
                $"super 分区扇区大小与 Firehose 不一致: GPT={partitionSectorSize}, Firehose={sectorSize}");
        }

        long physicalPartitionBytes = checked(superPartition.SectorLen * (long)partitionSectorSize);
        if (physicalPartitionBytes <= 0)
            throw new InvalidOperationException("super 分区大小无效");
        if (plan.BlockDeviceSize > (ulong)physicalPartitionBytes)
        {
            throw new InvalidOperationException(
                $"super_def 定义容量大于设备物理分区: {plan.BlockDeviceSize} > {physicalPartitionBytes}");
        }

        long totalWritten = 0;
        long lastSpeedBytes = 0;
        long lastSpeedTick = Stopwatch.GetTimestamp();
        long lastSpeedUiTick = 0;

        void ReportSpeed(long delta)
        {
            totalWritten = checked(totalWritten + delta);
            long now = Stopwatch.GetTimestamp();
            long elapsedTicks = now - lastSpeedTick;
            if (elapsedTicks <= 0 || now - lastSpeedUiTick < Stopwatch.Frequency / 5)
                return;

            double seconds = elapsedTicks / (double)Stopwatch.Frequency;
            long speedBytes = totalWritten - lastSpeedBytes;
            if (speedBytes >= 0)
                UpdateSmoothedTransferSpeed(speedBytes / seconds);
            lastSpeedBytes = totalWritten;
            lastSpeedTick = now;
            lastSpeedUiTick = now;
        }

        _updateCurrentPartitionText("super metadata");
        _updateProgress(0);
        _log(
            $"__EDL_FLASH_BEGIN__|{FormatEdlPartitionWriteLine(plan.DefinitionPath, $"LUN{superPartition.Lun}", "super metadata")}",
            null);
        try
        {
            long metadataWritten = 0;
            using var metadataStream = new MemoryStream(plan.MetadataImage, writable: false);
            WriteDynamicSuperRange(
                metadataStream,
                plan.MetadataImage.LongLength,
                plan.MetadataImage.LongLength,
                superPartition.Lun,
                superStartSector,
                "super",
                current =>
                {
                    metadataWritten = checked(metadataWritten + current);
                    _updateProgress(metadataWritten * 100.0 / plan.MetadataImage.LongLength);
                },
                ReportSpeed);
            _log("__EDL_FLASH_OK__", null);
        }
        catch
        {
            _log("__EDL_FLASH_ERROR__", null);
            throw;
        }

        ThrowIfStopRequested();
        foreach (DynamicSuperImage image in plan.Images)
        {
            ThrowIfStopRequested();
            LpMetadataPartition metadataPartition = plan.Metadata.Partitions.First(partition =>
                partition.Name.Equals(image.PartitionName, StringComparison.Ordinal));
            long allocatedSize = checked((long)image.AllocatedSize);
            long sourceRemaining = image.ExpandedSize;
            long partitionWritten = 0;
            string sourceName = string.IsNullOrWhiteSpace(image.SourcePath)
                ? "空分区"
                : Path.GetFileName(image.SourcePath);

            _updateCurrentPartitionText(image.PartitionName);
            _updateProgress(0);
            _log(
                $"__EDL_FLASH_BEGIN__|{FormatEdlPartitionWriteLine(sourceName, "Super", image.PartitionName)}",
                null);

            try
            {
                using Stream source = DynamicSuperPlanner.OpenExpandedImage(image);
                for (uint extentIndex = 0; extentIndex < metadataPartition.NumExtents; extentIndex++)
                {
                    LpMetadataExtent extent = plan.Metadata.Extents[
                        (int)(metadataPartition.FirstExtentIndex + extentIndex)];
                    if (extent.TargetType != LpMakeConstants.LP_TARGET_TYPE_LINEAR)
                        continue;
                    if (extent.TargetSource != 0)
                        throw new NotSupportedException($"{image.PartitionName} 使用了不支持的块设备");

                    long extentOffsetBytes = checked(
                        (long)extent.TargetData * LpMakeConstants.LP_SECTOR_SIZE);
                    long extentBytes = checked(
                        (long)extent.NumSectors * LpMakeConstants.LP_SECTOR_SIZE);
                    if ((extentOffsetBytes % sectorSize) != 0 || (extentBytes % sectorSize) != 0)
                    {
                        throw new InvalidOperationException(
                            $"{image.PartitionName} 的 extent 未按 Firehose 扇区对齐");
                    }

                    long physicalStartSector = checked(
                        superStartSector + extentOffsetBytes / sectorSize);
                    WriteDynamicSuperRange(
                        source,
                        extentBytes,
                        sourceRemaining,
                        superPartition.Lun,
                        physicalStartSector,
                        "super",
                        current =>
                        {
                            partitionWritten = checked(partitionWritten + current);
                            _updateProgress(partitionWritten * 100.0 / allocatedSize);
                        },
                        ReportSpeed);
                    sourceRemaining = Math.Max(0, sourceRemaining - extentBytes);
                }

                if (sourceRemaining != 0)
                    throw new EndOfStreamException($"{image.PartitionName} 未能完整写入 super extent");

                _log("__EDL_FLASH_OK__", null);
            }
            catch
            {
                _log("__EDL_FLASH_ERROR__", null);
                throw;
            }

            ThrowIfStopRequested();
        }

        _updateProgress(100);
        _updateSpeedText(null);
        _updateCurrentPartitionText(null);
    }

    private void WriteDynamicSuperRange(
        Stream source,
        long rangeBytes,
        long sourceBytesAvailable,
        int lun,
        long startSector,
        string physicalPartitionName,
        Action<long> reportRangeProgress,
        Action<long> reportSpeed)
    {
        if (rangeBytes < 0 || sourceBytesAvailable < 0)
            throw new ArgumentOutOfRangeException();

        int sectorSize = _firehoseSectorSize > 0 ? _firehoseSectorSize : 4096;
        if ((rangeBytes % sectorSize) != 0)
            throw new InvalidOperationException($"动态分区写入范围未对齐: {rangeBytes}");

        int payloadSize = Math.Max(
            sectorSize,
            _firehoseMaxPayloadSize > 0 ? _firehoseMaxPayloadSize : 1024 * 1024);
        payloadSize = Math.Max(sectorSize, payloadSize / sectorSize * sectorSize);
        byte[] buffer = new byte[payloadSize];

        long rangeWritten = 0;
        long sourceRemaining = Math.Min(rangeBytes, sourceBytesAvailable);
        long currentSector = startSector;
        while (rangeWritten < rangeBytes)
        {
            int chunkBytes = (int)Math.Min(buffer.Length, rangeBytes - rangeWritten);
            int sourceBytes = (int)Math.Min(chunkBytes, sourceRemaining);
            if (sourceBytes > 0)
                ReadExactly(source, buffer.AsSpan(0, sourceBytes));
            if (sourceBytes < chunkBytes)
                Array.Clear(buffer, sourceBytes, chunkBytes - sourceBytes);

            if (!WriteSectorsWithVip(
                    lun,
                    currentSector,
                    buffer,
                    chunkBytes,
                    true,
                    physicalPartitionName))
            {
                throw new IOException($"写入失败 @ sector {currentSector}");
            }

            currentSector += chunkBytes / sectorSize;
            rangeWritten += chunkBytes;
            sourceRemaining -= sourceBytes;
            reportRangeProgress(chunkBytes);
            reportSpeed(chunkBytes);
        }
    }

    private static void ReadExactly(Stream source, Span<byte> destination)
    {
        int total = 0;
        while (total < destination.Length)
        {
            int read = source.Read(destination[total..]);
            if (read == 0)
                throw new EndOfStreamException("动态分区镜像数据提前结束");
            total += read;
        }
    }
}
