namespace Yu.Modular.Qualcomm.Firehose.Parsers;

/// <summary>
/// 最小化 LZ4 块解压器 - 仅支持 LZ4 block format (非 frame format)
/// 用于解压 erofs 压缩数据块
/// </summary>
public static class Lz4Decompressor
{
    /// <summary>
    /// 解压 LZ4 块数据
    /// </summary>
    /// <param name="src">压缩数据</param>
    /// <param name="srcOffset">源偏移</param>
    /// <param name="srcLength">压缩数据长度</param>
    /// <param name="dst">输出缓冲区 (需预分配)</param>
    /// <param name="dstOffset">目标偏移</param>
    /// <param name="dstLength">期望解压后的大小</param>
    /// <returns>实际解压字节数, -1 表示失败</returns>
    public static int Decompress(byte[] src, int srcOffset, int srcLength,
        byte[] dst, int dstOffset, int dstLength)
    {
        var sIdx = srcOffset;
        var sEnd = srcOffset + srcLength;
        var dIdx = dstOffset;
        var dEnd = dstOffset + dstLength;

        while (sIdx < sEnd && dIdx < dEnd)
        {
            // Token byte
            var token = src[sIdx++];
            var literalLen = (token >> 4) & 0x0F;
            var matchLen = token & 0x0F;

            // 扩展 literal 长度
            if (literalLen == 15)
            {
                while (sIdx < sEnd)
                {
                    var b = src[sIdx++];
                    literalLen += b;
                    if (b != 255) break;
                }
            }

            // 复制 literal 数据
            if (literalLen > 0)
            {
                if (sIdx + literalLen > sEnd || dIdx + literalLen > dEnd)
                    return dIdx - dstOffset; // 部分解压
                Buffer.BlockCopy(src, sIdx, dst, dIdx, literalLen);
                sIdx += literalLen;
                dIdx += literalLen;
            }

            // 检查是否到达末尾 (最后一个 sequence 可以没有 match)
            if (sIdx >= sEnd) break;

            // Match offset (2 bytes, little-endian)
            if (sIdx + 2 > sEnd) break;
            var offset = src[sIdx] | (src[sIdx + 1] << 8);
            sIdx += 2;
            if (offset == 0) return -1; // 无效偏移

            // 扩展 match 长度 (最小 match = 4)
            matchLen += 4;
            if (matchLen == 19) // 15 + 4
            {
                while (sIdx < sEnd)
                {
                    var b = src[sIdx++];
                    matchLen += b;
                    if (b != 255) break;
                }
            }

            // 复制 match 数据 (可能与目标重叠)
            var matchSrc = dIdx - offset;
            if (matchSrc < dstOffset) return -1;

            var toCopy = Math.Min(matchLen, dEnd - dIdx);
            for (var i = 0; i < toCopy; i++)
                dst[dIdx + i] = dst[matchSrc + i];
            dIdx += toCopy;
        }

        return dIdx - dstOffset;
    }
}
