using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EdlPartitionInfo = SharpEDL.DataClass.PartitionInfo;

namespace WpfApp1;

internal partial class EdlEngine
{
    private enum EdlSessionState
    {
        Disconnected,
        Opening,
        Ready,
        Faulted
    }

    private enum EdlPartitionWriteStrategy
    {
        Unknown,
        Direct,
        SpoofRequired,
        Unsupported
    }

    private sealed class EdlDeviceCapabilitySnapshot
    {
        public long Generation { get; init; }
        public string MemoryName { get; set; } = "Unknown";
        public int SectorSize { get; set; } = 4096;
        public int MaxPayloadSize { get; set; } = 16 * 1024 * 1024;
        public int MaxXmlSize { get; set; }
        public int[] PhysicalLuns { get; set; } = Array.Empty<int>();
        public bool UsesOplusReadWithoutSpoof { get; set; }
        public bool UsesFastVipProgramWrite { get; set; }
        public bool OmitsFinalReadAck { get; set; }
        public string FirehoseProfile { get; set; } = "generic_direct";
        public string AuthType { get; set; } = "none";
    }

    private sealed class EdlReadFailureException : IOException
    {
        public EdlReadFailureException(string message, bool breaksSession, Exception? innerException = null)
            : base(message, innerException)
        {
            BreaksSession = breaksSession;
        }

        public bool BreaksSession { get; }
    }

    private readonly object _edlSessionSync = new();
    private EdlSessionState _edlSessionState = EdlSessionState.Disconnected;
    private string? _edlSessionFaultReason;
    private long _edlSessionGeneration;
    private EdlDeviceCapabilitySnapshot _edlCapabilities = new();
    private readonly Dictionary<string, EdlPartitionInfo> _knownDevicePartitionTargets =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, EdlPartitionWriteStrategy> _partitionWriteStrategies =
        new(StringComparer.OrdinalIgnoreCase);
    private string? _lastReadFailureReason;
    private bool _lastReadFailureBreaksSession;

    private bool IsCurrentFirehoseSessionReady
    {
        get
        {
            lock (_edlSessionSync)
            {
                return _edlSessionState == EdlSessionState.Ready
                    && FirehoseServer != null
                    && _currentPort != null
                    && _currentPort.IsOpen;
            }
        }
    }

    private long CurrentSessionGeneration
    {
        get
        {
            lock (_edlSessionSync)
                return _edlSessionGeneration;
        }
    }

    private void BeginFirehoseSession(string reason)
    {
        lock (_edlSessionSync)
        {
            _edlSessionGeneration++;
            _edlSessionState = EdlSessionState.Opening;
            _edlSessionFaultReason = null;
            _edlCapabilities = new EdlDeviceCapabilitySnapshot
            {
                Generation = _edlSessionGeneration,
                UsesOplusReadWithoutSpoof = _useOplusReadWithoutSpoof,
                UsesFastVipProgramWrite = _fastVipProgramWrite,
                FirehoseProfile = _requestedFirehoseProfile,
                AuthType = _requestedAuthType
            };
            _lastReadFailureReason = null;
            _lastReadFailureBreaksSession = false;
            _lastSuccessfulReadStrategyFilename = null;
            _lastSuccessfulReadStrategyLabel = null;
            _knownDevicePartitionTargets.Clear();
            _partitionWriteStrategies.Clear();
            _firehoseSectorSize = 4096;
            _firehoseMaxPayloadSize = 16 * 1024 * 1024;
            _knownPhysicalLuns = Array.Empty<int>();
        }

        _appendNativeLog($"[Session] generation={CurrentSessionGeneration} opening: {reason}");
    }

    private void MarkFirehoseSessionReady()
    {
        EdlDeviceCapabilitySnapshot snapshot;
        lock (_edlSessionSync)
        {
            _edlSessionState = EdlSessionState.Ready;
            _edlSessionFaultReason = null;
            snapshot = _edlCapabilities;
        }

        string luns = snapshot.PhysicalLuns.Length == 0
            ? "pending"
            : string.Join(",", snapshot.PhysicalLuns);
        _appendNativeLog(
            $"[Capabilities] session={snapshot.Generation}, memory={snapshot.MemoryName}, " +
            $"sector={snapshot.SectorSize}, maxPayload={snapshot.MaxPayloadSize}, " +
            $"maxXml={snapshot.MaxXmlSize}, luns={luns}, " +
            $"profile={snapshot.FirehoseProfile}, auth={snapshot.AuthType}, " +
            $"oplusSpecialRead={snapshot.UsesOplusReadWithoutSpoof}, fastVipWrite={snapshot.UsesFastVipProgramWrite}");
    }

    private void MarkFirehoseSessionDisconnected()
    {
        lock (_edlSessionSync)
        {
            _edlSessionState = EdlSessionState.Disconnected;
            _edlSessionFaultReason = null;
            _edlCapabilities = new EdlDeviceCapabilitySnapshot
            {
                Generation = _edlSessionGeneration
            };
            _lastReadFailureReason = null;
            _lastReadFailureBreaksSession = false;
            _lastSuccessfulReadStrategyFilename = null;
            _lastSuccessfulReadStrategyLabel = null;
            _knownDevicePartitionTargets.Clear();
            _partitionWriteStrategies.Clear();
        }
    }

    private void MarkFirehoseSessionFaulted(string reason)
    {
        string normalizedReason = string.IsNullOrWhiteSpace(reason)
            ? "未知通信错误"
            : reason.Trim();

        DisposeCurrentSerialPort();
        lock (_edlSessionSync)
        {
            _edlSessionState = EdlSessionState.Faulted;
            _edlSessionFaultReason = normalizedReason;
        }
        _appendNativeLog(
            $"[Session] generation={CurrentSessionGeneration} invalidated: {normalizedReason}");
    }

    private void UpdateFirehoseCapabilities(
        string? memoryName = null,
        int? sectorSize = null,
        int? maxPayloadSize = null,
        int? maxXmlSize = null)
    {
        lock (_edlSessionSync)
        {
            if (!string.IsNullOrWhiteSpace(memoryName))
                _edlCapabilities.MemoryName = memoryName.Trim().ToUpperInvariant();
            if (sectorSize is > 0)
                _edlCapabilities.SectorSize = sectorSize.Value;
            if (maxPayloadSize is > 0)
                _edlCapabilities.MaxPayloadSize = maxPayloadSize.Value;
            if (maxXmlSize is >= 0)
                _edlCapabilities.MaxXmlSize = maxXmlSize.Value;
            _edlCapabilities.UsesOplusReadWithoutSpoof = _useOplusReadWithoutSpoof;
            _edlCapabilities.UsesFastVipProgramWrite = _fastVipProgramWrite;
        }
    }

    private string GetDetectedMemoryName()
    {
        lock (_edlSessionSync)
            return _edlCapabilities.MemoryName;
    }

    private void ObservePhysicalLuns(int[] luns)
    {
        lock (_edlSessionSync)
            _edlCapabilities.PhysicalLuns = luns.ToArray();
        _appendNativeLog(
            $"[Capabilities] session={CurrentSessionGeneration}, physicalLuns={string.Join(",", luns)}");
    }

    private void ObserveDevicePartitions(IEnumerable<EdlPartitionInfo> partitions)
    {
        lock (_edlSessionSync)
        {
            _knownDevicePartitionTargets.Clear();
            foreach (EdlPartitionInfo partition in partitions)
            {
                string key = BuildPartitionTargetKey(partition.Label, partition.Lun);
                _knownDevicePartitionTargets[key] = new EdlPartitionInfo
                {
                    Label = partition.Label,
                    Lun = partition.Lun,
                    StartSector = partition.StartSector,
                    SectorLen = partition.SectorLen,
                    BytesPerSector = partition.BytesPerSector,
                    Sparse = partition.Sparse,
                    FilePath = partition.FilePath
                };
            }
        }
    }

    private bool TryGetKnownDevicePartitionTarget(
        string? label,
        int lun,
        out EdlPartitionInfo? partition)
    {
        lock (_edlSessionSync)
        {
            return _knownDevicePartitionTargets.TryGetValue(
                BuildPartitionTargetKey(label, lun),
                out partition);
        }
    }

    private bool HasKnownDevicePartitionTargets
    {
        get
        {
            lock (_edlSessionSync)
                return _knownDevicePartitionTargets.Count > 0;
        }
    }

    private EdlPartitionWriteStrategy GetPartitionWriteStrategy(EdlPartitionInfo partition)
    {
        lock (_edlSessionSync)
        {
            return _partitionWriteStrategies.TryGetValue(BuildPartitionWriteKey(partition), out var strategy)
                ? strategy
                : EdlPartitionWriteStrategy.Unknown;
        }
    }

    private void RememberPartitionWriteStrategy(
        EdlPartitionInfo partition,
        EdlPartitionWriteStrategy strategy)
    {
        lock (_edlSessionSync)
            _partitionWriteStrategies[BuildPartitionWriteKey(partition)] = strategy;
    }

    private static string BuildPartitionTargetKey(string? label, int lun)
        => $"{lun}:{(label ?? string.Empty).Trim()}";

    private static string BuildPartitionWriteKey(EdlPartitionInfo partition)
        => BuildPartitionTargetKey(partition.Label, partition.Lun);

    private void ObserveMissingFinalReadAck()
    {
        lock (_edlSessionSync)
            _edlCapabilities.OmitsFinalReadAck = true;
    }

    private bool IsCurrentSession(long generation, object server)
    {
        lock (_edlSessionSync)
        {
            return generation == _edlSessionGeneration
                && ReferenceEquals(FirehoseServer, server)
                && _edlSessionState != EdlSessionState.Faulted;
        }
    }

    private void SetLastReadFailure(string reason, bool breaksSession)
    {
        _lastReadFailureReason = reason;
        _lastReadFailureBreaksSession = breaksSession;
    }

    private EdlReadFailureException CreateLastReadFailureException(string operation)
    {
        string reason = string.IsNullOrWhiteSpace(_lastReadFailureReason)
            ? "设备未返回有效读取结果"
            : _lastReadFailureReason!;
        return new EdlReadFailureException(
            $"{operation}失败：{reason}",
            _lastReadFailureBreaksSession);
    }

    internal void HandleOperationFailure(Exception ex)
    {
        if (!IsSessionBreakingFailure(ex))
            return;

        string reason = BuildSessionFailureReason(ex);
        lock (_edlSessionSync)
        {
            if (_edlSessionState == EdlSessionState.Faulted)
                return;
        }
        MarkFirehoseSessionFaulted(reason);
    }

    private static bool IsSessionBreakingFailure(Exception ex)
    {
        if (ex is EdlReadFailureException readFailure)
            return readFailure.BreaksSession;
        if (ex is TimeoutException || ex is ObjectDisposedException)
            return true;
        if (ex.InnerException != null && IsSessionBreakingFailure(ex.InnerException))
            return true;

        string message = ex.Message ?? string.Empty;
        return ex is UnauthorizedAccessException
            || message.Contains("串口未打开", StringComparison.OrdinalIgnoreCase)
            || message.Contains("端口已关闭", StringComparison.OrdinalIgnoreCase)
            || message.Contains("port is closed", StringComparison.OrdinalIgnoreCase)
            || message.Contains("the port is closed", StringComparison.OrdinalIgnoreCase)
            || message.Contains("port is not open", StringComparison.OrdinalIgnoreCase)
            || message.Contains("closed port", StringComparison.OrdinalIgnoreCase)
            || message.Contains("port does not exist", StringComparison.OrdinalIgnoreCase)
            || message.Contains("端口不存在", StringComparison.OrdinalIgnoreCase)
            || message.Contains("指定的端口不存在", StringComparison.OrdinalIgnoreCase)
            || message.Contains("device disconnected", StringComparison.OrdinalIgnoreCase)
            || message.Contains("device is not connected", StringComparison.OrdinalIgnoreCase)
            || message.Contains("device attached to the system is not functioning", StringComparison.OrdinalIgnoreCase)
            || message.Contains("连到系统上的设备没有发挥作用", StringComparison.OrdinalIgnoreCase)
            || message.Contains("cannot access a disposed object", StringComparison.OrdinalIgnoreCase)
            || message.Contains("I/O operation has been aborted", StringComparison.OrdinalIgnoreCase)
            || message.Contains("I/O 操作已中止", StringComparison.OrdinalIgnoreCase)
            || message.Contains("由于线程退出或应用程序请求，已中止 I/O 操作", StringComparison.OrdinalIgnoreCase)
            || message.Contains("semaphore timeout", StringComparison.OrdinalIgnoreCase)
            || message.Contains("信号灯超时时间已到", StringComparison.OrdinalIgnoreCase)
            || message.Contains("写入端口失败: 31", StringComparison.OrdinalIgnoreCase)
            || message.Contains("读取端口失败: 31", StringComparison.OrdinalIgnoreCase)
            || message.Contains("通信超时", StringComparison.OrdinalIgnoreCase)
            || message.Contains("读取数据超时", StringComparison.OrdinalIgnoreCase)
            || message.Contains("final ACK", StringComparison.OrdinalIgnoreCase)
            || message.Contains("会话已失效", StringComparison.OrdinalIgnoreCase)
            || message.Contains("会话不存在", StringComparison.OrdinalIgnoreCase)
            || message.Contains("会话已被替换", StringComparison.OrdinalIgnoreCase)
            || message.Contains("会话在等待响应期间已被替换", StringComparison.OrdinalIgnoreCase)
            || message.Contains("旧会话已关闭", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildSessionFailureReason(Exception ex)
    {
        Exception current = ex;
        while (current.InnerException != null
               && string.IsNullOrWhiteSpace(current.Message))
            current = current.InnerException;

        string reason = current.Message
            .Replace("\r\n", " ")
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        return reason.Length > 240 ? reason[..240] + "..." : reason;
    }
}
