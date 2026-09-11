using System;
using System.IO;
using System.Text;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Encodings;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Agreement;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace SmartTool
{
    public class DowngradeTool
    {
        private const string AdbServerHost = "127.0.0.1";
        private const int AdbServerPort = 5037;
        private const int AdbSyncChunkSize = 64 * 1024;

        public Action<string> Log;

        private void Print(string msg)
        {
            Log?.Invoke(msg);
        }

        private const string RSA_KEY_PEM = @"-----BEGIN PUBLIC KEY-----
MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAmeQzr0TIbtwZFnDXgatg
6xP9SlNBFho1NTdFQ27SKDF+dBEEfnG9BqRw0na0DUqtpWe2CUtldbU33nnJ0KB6
z7y5f+89o9n8mJxIbh952gpskBxyrhCfpYHV5mt/n9Tkm8OcQWLRFou7/XITuZeZ
ejfUTesQjpfOeCaeKyVSoKQc6WuH7NSYq6B37RMyEn/1+vo8XuHEKD84p29KGpyG
I7ZeL85iOcwBmOD6+e4yideH2RatA1SzEv/9V8BflaFLAWDuPWUjA2WgfOvy5spY
mp/MoMOX4P0d+AkJ9Ms6PUXEUBsbOACmaMFyLCLHmd18+UeGdJR/3I15sXKbJhKe
rwIDAQAB
-----END PUBLIC KEY-----";

        private const long NEG_VERSION = 1636449646204;
        private const string V3_URL = "https://downgrade.coloros.com/downgrade/query-v3";
        private static readonly byte[] MAGIC = Encoding.ASCII.GetBytes("GhostPID");

        public DowngradeTool(Action<string> logAction)
        {
            Log = logAction;
        }

        private JObject SockRecv(NetworkStream s, int timeoutSec = 5)
        {
            s.ReadTimeout = timeoutSec * 1000;
            try
            {
                byte[] h = new byte[24];
                int read = 0;
                while (read < 24)
                {
                    int r = s.Read(h, read, 24 - read);
                    if (r == 0) return null;
                    read += r;
                }
                
                for (int i = 0; i < 8; i++)
                    if (h[i] != MAGIC[i]) return null;
                
                byte[] lenBytes = new byte[8];
                Array.Copy(h, 16, lenBytes, 0, 8);
                if (BitConverter.IsLittleEndian) Array.Reverse(lenBytes);
                ulong n = BitConverter.ToUInt64(lenBytes, 0);

                byte[] d = new byte[n];
                read = 0;
                while (read < (int)n)
                {
                    int r = s.Read(d, read, (int)n - read);
                    if (r == 0) break;
                    read += r;
                }
                
                string jsonStr = Encoding.UTF8.GetString(d);
                return JObject.Parse(jsonStr);
            }
            catch
            {
                return null;
            }
        }

        private void SockSend(NetworkStream s, int cmd, string extra)
        {
            var pObj = new
            {
                cmdId = cmd,
                createTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                extraInfo = extra,
                uuid = ""
            };
            string json = JsonConvert.SerializeObject(pObj);
            byte[] pBytes = Encoding.UTF8.GetBytes(json);

            byte[] header = new byte[24];
            Array.Copy(MAGIC, 0, header, 0, 8);
            
            byte[] typeBytes = BitConverter.GetBytes(2);
            if (BitConverter.IsLittleEndian) Array.Reverse(typeBytes);
            Array.Copy(typeBytes, 0, header, 8, 4);

            byte[] codeBytes = BitConverter.GetBytes(3000);
            if (BitConverter.IsLittleEndian) Array.Reverse(codeBytes);
            Array.Copy(codeBytes, 0, header, 12, 4);

            byte[] lenBytes = BitConverter.GetBytes((ulong)pBytes.Length);
            if (BitConverter.IsLittleEndian) Array.Reverse(lenBytes);
            Array.Copy(lenBytes, 0, header, 16, 8);

            s.Write(header, 0, header.Length);
            s.Write(pBytes, 0, pBytes.Length);
        }

        private byte[] AesCtr(byte[] data, byte[] key, byte[] iv)
        {
            var engine = new AesEngine();
            var ctr = new BufferedBlockCipher(new SicBlockCipher(engine));
            ctr.Init(true, new ParametersWithIV(new KeyParameter(key), iv));
            byte[] outBytes = new byte[ctr.GetOutputSize(data.Length)];
            int len = ctr.ProcessBytes(data, 0, data.Length, outBytes, 0);
            ctr.DoFinal(outBytes, len);
            return outBytes;
        }

        private string DeriveKey(byte[] rawSecret)
        {
            string b64 = Convert.ToBase64String(rawSecret);
            using (var sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(b64));
                return BitConverter.ToString(hash).Replace("-", "").ToLower();
            }
        }

        private string EncSocket(string txt, string kh)
        {
            byte[] key = Encoding.UTF8.GetBytes(kh.Substring(0, 32));
            byte[] iv = Encoding.UTF8.GetBytes(kh.Substring(kh.Length - 16));
            byte[] enc = AesCtr(Encoding.UTF8.GetBytes(txt), key, iv);
            return Convert.ToBase64String(enc);
        }

        private string DecSocket(string b64, string kh)
        {
            byte[] key = Encoding.UTF8.GetBytes(kh.Substring(0, 32));
            byte[] iv = Encoding.UTF8.GetBytes(kh.Substring(kh.Length - 16));
            byte[] dec = AesCtr(Convert.FromBase64String(b64), key, iv);
            return Encoding.UTF8.GetString(dec);
        }

        private JObject QueryDowngrade(string model, string otaVersion, string prjNum, string duid, string nvCarrier = "10010111", string androidVersion = "Android15", string colorosVersion = "ColorOS15.0.0", string serialNo = "00000000")
        {
            byte[] aesKey = new byte[32];
            byte[] nonce = new byte[12];
            using (var rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(aesKey);
                rng.GetBytes(nonce);
            }

            var reader = new StringReader(RSA_KEY_PEM);
            var pemReader = new PemReader(reader);
            var rsaKeyParam = (RsaKeyParameters)pemReader.ReadObject();
            var oaep = new OaepEncoding(new RsaEngine(), new Sha1Digest(), new Sha1Digest(), null);
            oaep.Init(true, rsaKeyParam);
            byte[] ek = oaep.ProcessBlock(Encoding.UTF8.GetBytes(Convert.ToBase64String(aesKey)), 0, 44);

            var gcm = new GcmBlockCipher(new AesEngine());
            var parameters = new AeadParameters(new KeyParameter(aesKey), 128, nonce);
            gcm.Init(true, parameters);
            byte[] duidBytes = Encoding.UTF8.GetBytes(duid);
            byte[] encryptedDuid = new byte[gcm.GetOutputSize(duidBytes.Length)];
            int len = gcm.ProcessBytes(duidBytes, 0, duidBytes.Length, encryptedDuid, 0);
            gcm.DoFinal(encryptedDuid, len);

            var ci = new
            {
                downgrade_server = new
                {
                    negotiationVersion = NEG_VERSION,
                    protectedKey = Convert.ToBase64String(ek),
                    version = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()
                }
            };

            var body = new
            {
                androidVersion = androidVersion,
                colorosVersion = colorosVersion,
                deviceId = new
                {
                    cipher = Convert.ToBase64String(encryptedDuid),
                    iv = Convert.ToBase64String(nonce)
                },
                model = model,
                nvCarrier = nvCarrier,
                otaVersion = otaVersion,
                prjNum = prjNum,
                serialNo = serialNo
            };

            string ciJson = JsonConvert.SerializeObject(ci).Replace("downgrade_server", "downgrade-server");
            string bodyJson = JsonConvert.SerializeObject(body);

            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(30);
                var content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
                content.Headers.Add("cipherInfo", ciJson);
                byte[] r16 = new byte[16];
                using (var rng = new RNGCryptoServiceProvider()) rng.GetBytes(r16);
                content.Headers.Add("deviceId", BitConverter.ToString(r16).Replace("-", ""));

                var response = client.PostAsync(V3_URL, content).Result;
                string respStr = response.Content.ReadAsStringAsync().Result;
                return JObject.Parse(respStr);
            }
        }

        private (TcpClient, NetworkStream, string) SocketConnectAndAuth(int port = 8500)
        {
            var client = new TcpClient("127.0.0.1", port);
            var s = client.GetStream();
            s.ReadTimeout = 10000;

            var m = SockRecv(s);
            if (m == null || m["cmdId"]?.Value<int>() != 30000)
                throw new Exception($"预期收到 cmd 30000, 实际收到: {m}");

            string srvPubB64 = JObject.Parse(m["extraInfo"].ToString())["keyPair_pub"].ToString();
            byte[] srvPubBytes = Convert.FromBase64String(srvPubB64);
            var srvPubKey = (ECPublicKeyParameters)PublicKeyFactory.CreateKey(srvPubBytes);

            var gen = new ECKeyPairGenerator();
            var keyGenParam = new ECKeyGenerationParameters(SecNamedCurves.GetOid("secp256r1"), new SecureRandom());
            gen.Init(keyGenParam);
            var pair = gen.GenerateKeyPair();

            var ecdh = new ECDHBasicAgreement();
            ecdh.Init(pair.Private);
            var sharedSecret = ecdh.CalculateAgreement(srvPubKey).ToByteArrayUnsigned();
            
            if(sharedSecret.Length < 32)
            {
                byte[] padded = new byte[32];
                Array.Copy(sharedSecret, 0, padded, 32 - sharedSecret.Length, sharedSecret.Length);
                sharedSecret = padded;
            }

            string kh = DeriveKey(sharedSecret);

            var pubKeyInfo = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(pair.Public);
            string pubB64 = Convert.ToBase64String(pubKeyInfo.GetEncoded());

            SockSend(s, 40000, JsonConvert.SerializeObject(new { keyPair_pub = pubB64 }));

            var m2 = SockRecv(s, 5);
            if (m2 == null || m2["cmdId"]?.Value<int>() != 30001)
                throw new Exception($"预期收到 cmd 30001, 实际收到: {m2}");

            DecSocket(m2["extraInfo"].ToString(), kh);

            var authGen = new ECKeyPairGenerator();
            authGen.Init(keyGenParam);
            var authPair = authGen.GenerateKeyPair();
            var authPubInfo = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(authPair.Public);
            byte[] authPubBytes = authPubInfo.GetEncoded();

            byte[] r16 = new byte[16];
            new SecureRandom().NextBytes(r16);
            string sd = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() + Convert.ToBase64String(r16).Substring(0, 16);

            var signer = SignerUtilities.GetSigner("SHA-256withECDSA");
            signer.Init(true, authPair.Private);
            signer.BlockUpdate(Encoding.UTF8.GetBytes(sd), 0, sd.Length);
            byte[] sig = signer.GenerateSignature();

            var authJsonObj = new
            {
                verifyPass = true,
                signData = sd,
                signValue = Convert.ToBase64String(sig),
                pubKey = Convert.ToBase64String(authPubBytes)
            };
            
            SockSend(s, 40001, EncSocket(JsonConvert.SerializeObject(authJsonObj), kh));

            var m3 = SockRecv(s, 5);
            if (m3 == null || m3["cmdId"]?.Value<int>() != 30002)
                throw new Exception($"Socket 认证失败, 返回: {m3}");

            Print("  ✓ Socket 连接认证成功");
            return (client, s, kh);
        }

        private JObject GetDeviceInfo(NetworkStream s, string kh)
        {
            SockSend(s, 40003, EncSocket("{}", kh));
            var m = SockRecv(s, 5);
            if (m == null || m["cmdId"]?.Value<int>() != 30003)
                throw new Exception($"预期收到 30003, 实际收到: {m}");
            return JObject.Parse(DecSocket(m["extraInfo"].ToString(), kh));
        }

        private JObject SendDowngradeInstall(NetworkStream s, string kh, string installBeanJson)
        {
            SockSend(s, 40006, EncSocket(installBeanJson, kh));
            var m = SockRecv(s, 300);
            if (m != null)
            {
                try
                {
                    return JObject.Parse(DecSocket(m["extraInfo"].ToString(), kh));
                }
                catch { return m; }
            }
            return null;
        }

        private string GetAdbPath()
        {
            string adbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "platform-tools", "adb.exe");
            return File.Exists(adbPath) ? adbPath : "adb";
        }

        private (string stdout, int rc) AdbCmd(string cmd)
        {
            using var p = new Process();
            p.StartInfo.FileName = GetAdbPath();
            p.StartInfo.Arguments = cmd;
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.RedirectStandardError = true;
            p.StartInfo.CreateNoWindow = true;
            p.Start();
            Task<string> outputTask = p.StandardOutput.ReadToEndAsync();
            Task<string> errorTask = p.StandardError.ReadToEndAsync();
            p.WaitForExit();
            string output = outputTask.GetAwaiter().GetResult().Trim();
            string error = errorTask.GetAwaiter().GetResult().Trim();
            string result = string.IsNullOrWhiteSpace(error)
                ? output
                : string.IsNullOrWhiteSpace(output)
                    ? error
                    : $"{output}{Environment.NewLine}{error}";
            return (result, p.ExitCode);
        }

        private void AdbForward(int localPort, int remotePort = 9527)
        {
            AdbCmd($"forward tcp:{localPort} tcp:{remotePort}");
        }

        private static byte[] GetLittleEndianBytes(int value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }

            return bytes;
        }

        private static async Task<byte[]> ReceiveExactAsync(Stream stream, int length)
        {
            byte[] buffer = new byte[length];
            int offset = 0;

            while (offset < length)
            {
                int read = await stream.ReadAsync(buffer, offset, length - offset);
                if (read <= 0)
                {
                    throw new IOException("ADB 连接被意外关闭。");
                }

                offset += read;
            }

            return buffer;
        }

        private static async Task SendAdbRequestAsync(Stream stream, string payload)
        {
            byte[] data = Encoding.UTF8.GetBytes(payload);
            byte[] header = Encoding.ASCII.GetBytes(data.Length.ToString("x4"));
            await stream.WriteAsync(header, 0, header.Length);
            await stream.WriteAsync(data, 0, data.Length);
            await stream.FlushAsync();
        }

        private static async Task ReadAdbStatusAsync(Stream stream)
        {
            string status = Encoding.ASCII.GetString(await ReceiveExactAsync(stream, 4));
            if (status == "OKAY")
            {
                return;
            }

            if (status == "FAIL")
            {
                string hexLength = Encoding.ASCII.GetString(await ReceiveExactAsync(stream, 4));
                int messageLength = Convert.ToInt32(hexLength, 16);
                string message = Encoding.UTF8.GetString(await ReceiveExactAsync(stream, messageLength));
                throw new InvalidOperationException($"ADB FAIL: {message}");
            }

            throw new InvalidOperationException($"未知 ADB 状态: {status}");
        }

        private static async Task SendSyncPacketAsync(Stream stream, string ident, int length)
        {
            if (ident.Length != 4)
            {
                throw new ArgumentException("SYNC ident 必须是 4 个字符。", nameof(ident));
            }

            byte[] identBytes = Encoding.ASCII.GetBytes(ident);
            byte[] lengthBytes = GetLittleEndianBytes(length);
            await stream.WriteAsync(identBytes, 0, identBytes.Length);
            await stream.WriteAsync(lengthBytes, 0, lengthBytes.Length);
        }

        private static async Task ReadSyncStatusAsync(Stream stream)
        {
            string ident = Encoding.ASCII.GetString(await ReceiveExactAsync(stream, 4));
            int length = BitConverter.ToInt32(await ReceiveExactAsync(stream, 4), 0);

            if (ident == "OKAY")
            {
                return;
            }

            if (ident == "FAIL")
            {
                string message = Encoding.UTF8.GetString(await ReceiveExactAsync(stream, length));
                throw new InvalidOperationException($"SYNC FAIL: {message}");
            }

            throw new InvalidOperationException($"未知 SYNC 响应: ident={ident}, length={length}");
        }

        private async Task EnsureAdbServerRunningAsync()
        {
            var p = new Process();
            p.StartInfo.FileName = GetAdbPath();
            p.StartInfo.Arguments = "start-server";
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.RedirectStandardError = true;
            p.StartInfo.CreateNoWindow = true;
            p.Start();
            await p.WaitForExitAsync();
        }

        private static string FormatProgressText(long currentBytes, long totalBytes, double speedMb)
        {
            double currentGb = currentBytes / (1024.0 * 1024 * 1024);
            double totalGb = Math.Max(1, totalBytes) / (1024.0 * 1024 * 1024);
            return $"{currentGb:0.#}GB/{totalGb:0.#}GB - {Math.Max(0, speedMb):0.0} MB/s";
        }

        private async Task ExecuteAdbSyncPushAsync(string localPath, string remotePath, Action<long, long>? progressCallback = null, int mode = 0x1A4)
        {
            if (!File.Exists(localPath))
            {
                throw new FileNotFoundException($"找不到本地文件: {localPath}");
            }

            await EnsureAdbServerRunningAsync();

            long totalSize = new FileInfo(localPath).Length;
            int mtime = (int)new FileInfo(localPath).LastWriteTimeUtc.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
            string sendTarget = $"{remotePath},{mode}";

            using var client = new TcpClient();
            await client.ConnectAsync(AdbServerHost, AdbServerPort);
            client.ReceiveTimeout = 30000;
            client.SendTimeout = 30000;

            using NetworkStream stream = client.GetStream();
            await SendAdbRequestAsync(stream, "host:transport-any");
            await ReadAdbStatusAsync(stream);

            await SendAdbRequestAsync(stream, "sync:");
            await ReadAdbStatusAsync(stream);

            byte[] sendTargetBytes = Encoding.UTF8.GetBytes(sendTarget);
            await SendSyncPacketAsync(stream, "SEND", sendTargetBytes.Length);
            await stream.WriteAsync(sendTargetBytes, 0, sendTargetBytes.Length);

            long sent = 0;
            byte[] buffer = new byte[AdbSyncChunkSize];

            using FileStream fileStream = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            while (true)
            {
                int read = await fileStream.ReadAsync(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    break;
                }

                await SendSyncPacketAsync(stream, "DATA", read);
                await stream.WriteAsync(buffer, 0, read);
                sent += read;
                progressCallback?.Invoke(sent, totalSize);
            }

            await SendSyncPacketAsync(stream, "DONE", mtime);
            await stream.FlushAsync();
            await ReadSyncStatusAsync(stream);
            progressCallback?.Invoke(totalSize, totalSize);
        }

        private async Task<bool> AdbPush(string localPath, string remotePath)
        {
            Print($"  ADB 推送: {localPath} → {remotePath}");
            try
            {
                long totalSize = Math.Max(1, new FileInfo(localPath).Length);
                var stopwatch = Stopwatch.StartNew();
                long lastSent = 0;
                double lastReportSeconds = 0;
                OnDownloadProgress?.Invoke(0, 0, FormatProgressText(0, totalSize, 0));

                await ExecuteAdbSyncPushAsync(
                    localPath,
                    remotePath,
                    (sentBytes, currentFileTotalBytes) =>
                    {
                        long safeTotal = Math.Max(1, currentFileTotalBytes);
                        long safeSent = Math.Max(0, Math.Min(safeTotal, sentBytes));
                        double nowSeconds = stopwatch.Elapsed.TotalSeconds;
                        if (safeSent < safeTotal && nowSeconds - lastReportSeconds < 0.5)
                        {
                            return;
                        }

                        double pct = safeSent * 100.0 / safeTotal;
                        double elapsedSeconds = Math.Max(nowSeconds - lastReportSeconds, 0.001);
                        double speedMb = (safeSent - lastSent) / elapsedSeconds / 1024 / 1024;

                        OnDownloadProgress?.Invoke(pct, speedMb, FormatProgressText(safeSent, safeTotal, speedMb));
                        lastSent = safeSent;
                        lastReportSeconds = nowSeconds;
                    });

                OnDownloadProgress?.Invoke(100, 0, FormatProgressText(totalSize, totalSize, 0));
                Print("  ✓ ADB 推送完成");
                return true;
            }
            catch (Exception ex)
            {
                Print($"  ✗ ADB 推送失败: {ex.Message}");
                return false;
            }
        }

        private async Task<(bool IsValid, string Reason)> ValidatePackageFileAsync(
            string filePath,
            long expectedSize,
            string expectedMd5)
        {
            if (!File.Exists(filePath))
            {
                OnDownloadProgress?.Invoke(0, 0, "文件不存在，无法校验 MD5");
                return (false, $"找不到文件: {filePath}");
            }

            long actualSize = new FileInfo(filePath).Length;
            if (expectedSize <= 0)
            {
                OnDownloadProgress?.Invoke(0, 0, "缺少文件大小信息，无法校验 MD5");
                return (false, "查询结果未提供有效的文件大小，无法安全校验降级包。");
            }

            if (actualSize != expectedSize)
            {
                OnDownloadProgress?.Invoke(0, 0, "文件大小不匹配，未执行 MD5 校验");
                return (
                    false,
                    $"文件大小不匹配，预期 {expectedSize} 字节，实际 {actualSize} 字节。");
            }

            if (string.IsNullOrWhiteSpace(expectedMd5))
            {
                OnDownloadProgress?.Invoke(0, 0, "缺少 MD5 信息，无法校验文件");
                return (false, "查询结果未提供 MD5，无法安全校验降级包。");
            }

            const int hashBufferSize = 4 * 1024 * 1024;
            byte[] buffer = new byte[hashBufferSize];
            long processedBytes = 0;
            long lastReportedBytes = 0;
            var intervalStopwatch = Stopwatch.StartNew();

            OnDownloadProgress?.Invoke(
                0,
                0,
                $"正在校验 MD5 | 0.00GB/{actualSize / (1024.0 * 1024 * 1024):0.00}GB");

            using var incrementalHash =
                IncrementalHash.CreateHash(HashAlgorithmName.MD5);
            await using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                hashBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            while (true)
            {
                int bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length));
                if (bytesRead <= 0)
                {
                    break;
                }

                incrementalHash.AppendData(buffer, 0, bytesRead);
                processedBytes += bytesRead;

                bool reachedEnd = processedBytes >= actualSize;
                if (!reachedEnd && intervalStopwatch.ElapsedMilliseconds < 200)
                {
                    continue;
                }

                double elapsedSeconds =
                    Math.Max(intervalStopwatch.Elapsed.TotalSeconds, 0.001);
                double speed =
                    (processedBytes - lastReportedBytes) / elapsedSeconds / 1024 / 1024;
                double percentage =
                    Math.Min(100.0, processedBytes * 100.0 / actualSize);
                string progressText =
                    $"正在校验 MD5 | " +
                    $"{processedBytes / (1024.0 * 1024 * 1024):0.00}GB/" +
                    $"{actualSize / (1024.0 * 1024 * 1024):0.00}GB | " +
                    $"{speed:0.0} MB/s";

                OnDownloadProgress?.Invoke(percentage, speed, progressText);
                lastReportedBytes = processedBytes;
                intervalStopwatch.Restart();
            }

            if (processedBytes != actualSize)
            {
                OnDownloadProgress?.Invoke(0, 0, "MD5 校验失败 | 文件读取不完整");
                return (
                    false,
                    $"MD5 校验期间文件读取不完整，预期 {actualSize} 字节，实际读取 {processedBytes} 字节。");
            }

            byte[] hash = incrementalHash.GetHashAndReset();
            string actualMd5 = Convert.ToHexString(hash);

            if (!string.Equals(actualMd5, expectedMd5.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                OnDownloadProgress?.Invoke(100, 0, "MD5 校验完成 | 结果不匹配");
                return (
                    false,
                    $"MD5 不匹配，预期 {expectedMd5.Trim()}，实际 {actualMd5}。");
            }

            OnDownloadProgress?.Invoke(100, 0, "MD5 校验通过");
            return (true, string.Empty);
        }

        private string GetSerialFromPhone()
        {
            var res = AdbCmd("shell getprop ro.serialno");
            if (res.rc == 0 && !string.IsNullOrWhiteSpace(res.stdout))
                return res.stdout;
            return null;
        }

        private bool StartPhoneApp(
            string? apkPath = null,
            bool installApkBeforeQuery = false)
        {
            var res = AdbCmd("shell pm list packages com.color.otaassistant");
            bool assistantIsInstalled =
                res.rc == 0 &&
                res.stdout.Contains("com.color.otaassistant", StringComparison.Ordinal);

            if (installApkBeforeQuery || !assistantIsInstalled)
            {
                string apk = apkPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OTA_113.apk");
                if (File.Exists(apk))
                {
                    Print(installApkBeforeQuery
                        ? $"  正在安装用户选择的降级助手 APK: {Path.GetFileName(apk)}..."
                        : "  正在安装内置降级助手 APK...");
                    var iRes = AdbCmd($"install -r \"{apk}\"");
                    if (iRes.rc == 0)
                        Print($"  ✓ OTA Assistant 已安装: {Path.GetFileName(apk)}");
                    else
                    {
                        Print($"  ✗ APK 安装失败: {iRes.stdout}");
                        return false;
                    }
                }
                else
                {
                    Print($"  ✗ 找不到 OTA Assistant APK 文件: {apk}");
                    return false;
                }
            }
            else
            {
                Print("  ✓ OTA Assistant 已安装");
            }
            AdbCmd("shell am broadcast -a oplus.intent.action.OTA_ASSISTANT_PC_TOOL_RECEIVER -n com.color.otaassistant/.OtaAssistantReceiver");
            Print("  ✓ OTA Assistant 已启动");
            return true;
        }

        public class DowngradePackageInfo
        {
            public int Index { get; set; }
            public string ColorOSVersion { get; set; }
            public string AndroidVersion { get; set; }
            public string AndroidVersionDisplay
            {
                get
                {
                    if (string.IsNullOrWhiteSpace(AndroidVersion))
                        return "--";

                    const string prefix = "Android";
                    return AndroidVersion.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        ? AndroidVersion.Substring(prefix.Length).Trim()
                        : AndroidVersion.Trim();
                }
            }
            public double SizeGB { get; set; }
            public string OtaVersion { get; set; }
            public string DownloadUrl { get; set; }
        }

        public class DowngradeOptions
        {
            public int Port { get; set; } = 8500;
            public int PkgIndex { get; set; } = 0;
            public string DownloadDir { get; set; }
            public bool QueryOnly { get; set; }
            public string PushOnlyFile { get; set; }
            public string ApkPath { get; set; }
            public bool InstallApkBeforeQuery { get; set; }
        }

        public Action<List<DowngradePackageInfo>> OnPackagesFound;
        public Action<double, double, string> OnDownloadProgress; // percentage, speed MB/s, text info

        private async Task DownloadPackageAsync(string url, string localFile, long totalSize)
        {
            const int splitCount = 16;
            if (Aria2DownloadService.ResolveExecutablePath() != null)
            {
                Print($"  → 已启用 aria2c {splitCount} 连接下载");
                OnDownloadProgress?.Invoke(
                    0,
                    0,
                    $"aria2c {splitCount} 连接 | 正在连接服务器...");

                Aria2DownloadResult aria2Result = await Aria2DownloadService.DownloadAsync(
                    url,
                    localFile,
                    splitCount,
                    progress =>
                    {
                        double downloadedBytes =
                            totalSize * Math.Clamp(progress.Percentage, 0, 100) / 100.0;
                        int activeConnections = progress.ConnectionCount > 0
                            ? progress.ConnectionCount
                            : splitCount;
                        var progressParts = new List<string>
                        {
                            $"aria2c {activeConnections} 连接",
                            $"{downloadedBytes / (1024.0 * 1024 * 1024):0.0}GB/{totalSize / (1024.0 * 1024 * 1024):0.0}GB"
                        };

                        if (!string.IsNullOrWhiteSpace(progress.SpeedText))
                        {
                            progressParts.Add($"{progress.SpeedText}/s");
                        }
                        if (!string.IsNullOrWhiteSpace(progress.EtaText))
                        {
                            progressParts.Add($"剩余 {progress.EtaText}");
                        }

                        OnDownloadProgress?.Invoke(
                            progress.Percentage,
                            progress.SpeedMegabytesPerSecond,
                            string.Join(" | ", progressParts));
                    });

                if (aria2Result.Succeeded)
                {
                    TryDeleteAria2ControlFile(localFile);
                    OnDownloadProgress?.Invoke(
                        100,
                        0,
                        $"aria2c {splitCount} 连接 | 下载完成");
                    return;
                }

                string aria2Error = string.IsNullOrWhiteSpace(aria2Result.ErrorMessage)
                    ? $"退出码 {aria2Result.ExitCode?.ToString() ?? "未知"}"
                    : aria2Result.ErrorMessage;
                if (aria2Error.Length > 280)
                {
                    aria2Error = aria2Error.Substring(0, 280) + "...";
                }

                Print($"  ⚠ aria2c 多连接下载未完成，将自动切换单连接下载: {aria2Error}");
                TryDeleteAria2ControlFile(localFile);
            }
            else
            {
                Print("  ⚠ 未找到 aria2c.exe，将使用单连接兼容下载");
            }

            OnDownloadProgress?.Invoke(0, 0, "正在切换单连接兼容下载...");
            await DownloadPackageSingleConnectionAsync(url, localFile, totalSize);
        }

        private async Task DownloadPackageSingleConnectionAsync(
            string url,
            string localFile,
            long totalSize)
        {
            using var httpClient = new HttpClient
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
            using HttpResponseMessage response = await httpClient.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            using Stream stream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(
                localFile,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None);

            long effectiveTotalSize =
                response.Content.Headers.ContentLength.GetValueOrDefault(totalSize);
            if (effectiveTotalSize <= 0)
            {
                effectiveTotalSize = totalSize;
            }

            byte[] buffer = new byte[8192 * 1024];
            int read;
            long downloaded = 0;
            long lastReportedDownloaded = 0;
            var intervalStopwatch = Stopwatch.StartNew();
            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, read);
                downloaded += read;
                if (intervalStopwatch.ElapsedMilliseconds <= 1000)
                {
                    continue;
                }

                long intervalBytes = downloaded - lastReportedDownloaded;
                double elapsedSeconds =
                    Math.Max(intervalStopwatch.Elapsed.TotalSeconds, 0.001);
                double percentage = effectiveTotalSize > 0
                    ? Math.Min(
                        100.0,
                        Math.Max(0.0, downloaded * 100.0 / effectiveTotalSize))
                    : 0.0;
                double speed = intervalBytes / elapsedSeconds / 1024 / 1024;
                string progressText =
                    $"单连接 | {downloaded / (1024.0 * 1024 * 1024):0.#}GB/" +
                    $"{effectiveTotalSize / (1024.0 * 1024 * 1024):0.#}GB | " +
                    $"{speed:0.0} MB/s";
                OnDownloadProgress?.Invoke(percentage, speed, progressText);
                lastReportedDownloaded = downloaded;
                intervalStopwatch.Restart();
            }

            OnDownloadProgress?.Invoke(
                100.0,
                0,
                $"单连接 | {downloaded / (1024.0 * 1024 * 1024):0.#}GB/" +
                $"{effectiveTotalSize / (1024.0 * 1024 * 1024):0.#}GB | 下载完成");
        }

        private static void TryDeleteAria2ControlFile(string localFile)
        {
            string controlFile = localFile + ".aria2";
            try
            {
                if (File.Exists(controlFile))
                {
                    File.Delete(controlFile);
                }
            }
            catch
            {
            }
        }

        public async Task RunAsync(DowngradeOptions options)
        {
            string downloadDir = string.IsNullOrWhiteSpace(options.DownloadDir) 
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "ColorOSData") 
                : options.DownloadDir;
            
            string serial = GetSerialFromPhone();
            int port = options.Port;

            if (string.IsNullOrEmpty(serial))
            {
                Print("  ✗ 无法检测到设备序列号。请确认只连接一台设备、已开启 USB 调试，并已允许此电脑调试。");
                return;
            }
            Print($"  序列号 (Serial): {serial}");
            Print("============================================================");
            Print("  步骤 1: 连接手机并获取 DUID");
            Print("============================================================");

            if (!StartPhoneApp(options.ApkPath, options.InstallApkBeforeQuery)) return;
            await Task.Delay(3000);
            AdbForward(port);

            TcpClient client = null;
            NetworkStream s = null;
            string kh = null;
            JObject info = null;
            JObject dgBody = null;
            string duid = null;

            for (int i = 0; i < 3; i++)
            {
                try
                {
                    var authRes = SocketConnectAndAuth(port);
                    client = authRes.Item1;
                    s = authRes.Item2;
                    kh = authRes.Item3;
                    info = GetDeviceInfo(s, kh);
                    var dgRaw = JObject.Parse(info["downgradeRequestBody"]?.ToString() ?? "{}");
                    if (dgRaw["params"] != null)
                        dgBody = JObject.Parse(dgRaw["params"].ToString());
                    else
                        dgBody = dgRaw;
                    
                    duid = dgBody["deviceId"]?.ToString();
                    if (!string.IsNullOrEmpty(duid) && duid != new string('0', 64))
                    {
                        Print($"  ✓ 成功获取 DUID (尝试次数 {i + 1})");
                        break;
                    }
                    client.Close();
                    Print($"  DUID 为空，5秒后重试...");
                    await Task.Delay(5000);
                    AdbCmd("shell am broadcast -a oplus.intent.action.OTA_ASSISTANT_PC_TOOL_RECEIVER -n com.color.otaassistant/.OtaAssistantReceiver");
                    await Task.Delay(2000);
                    AdbForward(port);
                }
                catch (Exception ex)
                {
                    Print($"  连接错误: {ex.Message}");
                    await Task.Delay(5000);
                    AdbForward(port);
                }
            }

            if (string.IsNullOrEmpty(duid) || duid == new string('0', 64))
            {
                client?.Close();
                Print("  ✗ 无法从手机端降级助手获取设备信息。请确认降级助手已正常安装并启动，然后重新连接设备再试。");
                return;
            }

            string model = dgBody["model"]?.ToString();
            string otaVersion = dgBody["otaVersion"]?.ToString();
            string nvCarrier = dgBody["nvCarrier"]?.ToString() ?? "10010111";
            string prjNum = dgBody["prjNum"]?.ToString();
            string androidVer = dgBody["androidVersion"]?.ToString() ?? "Android15";
            string colorosVer = dgBody["colorosVersion"]?.ToString() ?? "ColorOS15.0.0";

            Print($"  机型: {model}");
            Print($"  DUID: {duid.Substring(0, 16)}...{duid.Substring(duid.Length - 8)}");
            Print($"  当前 OTA 版本: {otaVersion}");
            Print($"  当前 ColorOS 版本: {colorosVer}");

            Print($"\n============================================================");
            Print("  步骤 2: 查询降级包");
            Print("============================================================");

            var resp = QueryDowngrade(model, otaVersion, prjNum, duid, nvCarrier, androidVer, colorosVer, serial);
            if (resp["code"]?.Value<int>() != 200 || resp["data"] == null)
            {
                Print($"  ✗ 查询失败: {resp}");
                client?.Close();
                return;
            }

            string metaData = resp["data"]["metaData"]?.ToString();
            var packages = (JArray)resp["data"]["downgradeVoList"];
            Print($"  ✓ 找到 {packages.Count} 个可用降级包:");
            
            var packageList = new List<DowngradePackageInfo>();
            for (int i = 0; i < packages.Count; i++)
            {
                long fs = packages[i]["fileSize"]?.Value<long>() ?? 0;
                var pkgInfo = new DowngradePackageInfo
                {
                    Index = i,
                    ColorOSVersion = packages[i]["colorosVersion"]?.ToString(),
                    AndroidVersion = packages[i]["androidVersion"]?.ToString(),
                    SizeGB = fs / (1024.0 * 1024 * 1024),
                    OtaVersion = packages[i]["otaVersion"]?.ToString(),
                    DownloadUrl = packages[i]["downloadUrl"]?.ToString()
                };
                packageList.Add(pkgInfo);
                Print($"    [{i}] {pkgInfo.ColorOSVersion} ({pkgInfo.AndroidVersion}) - {pkgInfo.SizeGB:0.00}GB");
            }
            
            OnPackagesFound?.Invoke(packageList);

            if (options.QueryOnly)
            {
                Print("\n  下载链接列表:");
                for (int i = 0; i < packages.Count; i++)
                {
                    Print($"    [{i}] {packages[i]["downloadUrl"]}");
                }
                client?.Close();
                return;
            }

            if (packages.Count == 0)
            {
                client?.Close();
                return;
            }

            int pkgIdx = options.PkgIndex >= 0 && options.PkgIndex < packages.Count ? options.PkgIndex : 0;
            Print($"  → 已选择 [{pkgIdx}] {packages[pkgIdx]["colorosVersion"]}");

            Print($"\n============================================================");
            Print($"  步骤 3: 下载降级包");
            Print("============================================================");

            var pkg = packages[pkgIdx];
            string url = pkg["downloadUrl"].ToString();
            string otaName = pkg["otaVersion"].ToString();
            string filename = url.Split('/').Last();
            string ext = Path.GetExtension(filename);
            if (string.IsNullOrEmpty(ext)) ext = ".ozip";
            
            string localFile = string.IsNullOrWhiteSpace(options.PushOnlyFile) 
                ? Path.Combine(downloadDir, $"{otaName}{ext}") 
                : options.PushOnlyFile;
            
            long totalSize = pkg["fileSize"].Value<long>();
            string expectedMd5 = pkg["fileMd5"]?.ToString() ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(options.PushOnlyFile))
            {
                Print($"  使用已存在的本地文件: {localFile}");
                Print("  正在校验本地降级包的文件大小和 MD5...");
                var localValidation = await ValidatePackageFileAsync(localFile, totalSize, expectedMd5);
                if (!localValidation.IsValid)
                {
                    Print($"  ✗ 本地降级包校验失败: {localValidation.Reason}");
                    client?.Close();
                    return;
                }
                Print("  ✓ 本地降级包校验通过");
            }
            else
            {
                bool existingFileIsValid = false;
                if (File.Exists(localFile))
                {
                    Print("  正在校验已下载的降级包...");
                    var existingValidation = await ValidatePackageFileAsync(localFile, totalSize, expectedMd5);
                    existingFileIsValid = existingValidation.IsValid;
                    if (existingFileIsValid)
                    {
                        Print($"  ✓ 文件大小和 MD5 校验通过，跳过下载: {localFile}");
                    }
                    else
                    {
                        Print($"  ⚠ 现有文件校验未通过，将重新下载: {existingValidation.Reason}");
                    }
                }

                if (!existingFileIsValid)
                {
                    Directory.CreateDirectory(downloadDir);
                    Print($"  正在下载: {url.Substring(0, Math.Min(80, url.Length))}...");
                    Print($"  文件大小: {totalSize / (1024.0 * 1024 * 1024):0.00} GB");

                    await DownloadPackageAsync(url, localFile, totalSize);

                    Print("  正在校验下载文件的大小和 MD5...");
                    var downloadedValidation = await ValidatePackageFileAsync(localFile, totalSize, expectedMd5);
                    if (!downloadedValidation.IsValid)
                    {
                        Print($"  ✗ 下载文件校验失败，已停止后续操作: {downloadedValidation.Reason}");
                        client?.Close();
                        return;
                    }
                    Print("  ✓ 下载文件校验通过");
                    Print($"  ✓ 下载完成: {localFile}");
                }
            }

            Print($"\n============================================================");
            Print("  步骤 4: 推送文件到手机 (ADB Push)");
            Print("============================================================");

            string remoteFile = $"/sdcard/{otaName}{ext}";

            var lsRes = AdbCmd($"shell ls -la \"{remoteFile}\"");
            if (lsRes.stdout.Contains(totalSize.ToString()))
            {
                Print($"  ✓ 文件已在手机中，跳过推送: {remoteFile}");
            }
            else
            {
                if (!await AdbPush(localFile, remoteFile))
                {
                    Print("  ✗ ADB 推送失败");
                    client?.Close();
                    return;
                }
            }

            Print($"\n============================================================");
            Print("  步骤 5: 发送降级安装指令");
            Print("============================================================");

            client?.Close();
            AdbCmd("shell cmd deviceidle whitelist +com.color.otaassistant");
            AdbCmd("shell am set-inactive com.color.otaassistant false");
            AdbCmd("shell cmd deviceidle whitelist +com.oplus.ota");

            Print("  正在重新连接 Socket...");
            AdbForward(port);
            var reAuth = SocketConnectAndAuth(port);
            client = reAuth.Item1;
            s = reAuth.Item2;
            kh = reAuth.Item3;
            Print("  ✓ Socket 已重新连接");

            var installBean = new
            {
                signData = metaData,
                checkData = $"deviceId:{serial}userOtaVersion:{otaVersion}",
                versionName = pkg["otaVersion"].ToString(),
                needDataSpace = totalSize,
                components = new[]
                {
                    new
                    {
                        componentId = "0",
                        componentName = otaName,
                        componentVersion = pkg["colorosVersion"].ToString(),
                        packageFullPath = remoteFile,
                        packetMd5 = pkg["fileMd5"].ToString(),
                        packetSize = totalSize
                    }
                }
            };

            string installJson = JsonConvert.SerializeObject(installBean);
            Print($"\n  正在发送安装指令 (cmd 40006)...");
            var instResult = SendDowngradeInstall(s, kh, installJson);

            if (instResult != null)
            {
                int vr = instResult["verifyResult"]?.Value<int>() ?? -1;
                if (vr == 0)
                {
                    Print("  ✓ 文件推送完成，安装指令发送完成");
                    Print("  [提示] 请打开手机设置 → 系统与更新 → 软件更新 → 点击右上角三个点 → 选择本地安装。");
                    Print($"  [提示] OTA 降级包已存放于手机内部存储根目录：{remoteFile}");
                }
                else
                {
                    Print($"  ✗ 失败，返回错误码: {vr}");
                }
            }
            else
            {
                Print("  ✗ 手机未返回任何响应");
            }

            client?.Close();
        }
    }
}
