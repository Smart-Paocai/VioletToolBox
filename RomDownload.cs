using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Input;
using System.IO;
using System.Text.Json;
using Payload_Dumper_C_.Core;

namespace WpfApp1
{
    public partial class MainWindow
    {
        private const string OnePlusAceConfigUrl = "";
        private const string OnePlusNumberConfigUrl = "";
        private const string OnePlusPadConfigUrl = "";
        private const string OnePlusTurboConfigUrl = "";

        private const string OppoFullASeriesConfigUrl = "";
        private const string OppoFullFindSeriesConfigUrl = "";
        private const string OppoFullKSeriesConfigUrl = "";
        private const string OppoFullPadSeriesConfigUrl = "";
        private const string OppoFullRSeriesConfigUrl = "";
        private const string OppoFullRenoSeriesConfigUrl = "";

        private const string RealmeFullGtNeoSeriesConfigUrl = "";
        private const string RealmeFullNeoSeriesConfigUrl = "";
        private const string RealmeFullQSeriesConfigUrl = "";
        private const string RealmeFullVSeriesConfigUrl = "";
        private const string RealmeFullNumberSeriesConfigUrl = "";
        private const string RealmeFullGtSeriesConfigUrl = "";

        private const string XiaomiFullCiviSeriesConfigUrl = "";
        private const string XiaomiFullMixSeriesConfigUrl = "";
        private const string XiaomiFullNoteSeriesConfigUrl = "";
        private const string XiaomiFullPadSeriesConfigUrl = "";
        private const string XiaomiFullPocoSeriesConfigUrl = "";
        private const string XiaomiFullNumberSeriesConfigUrl = "";

        private const string RedmiFullKSeriesConfigUrl = "";
        private const string RedmiFullNoteSeriesConfigUrl = "";
        private const string RedmiFullNumberSeriesConfigUrl = "";
        private const string RedmiFullPadSeriesConfigUrl = "";
        private const string RedmiFullTurboSeriesConfigUrl = "";
        private const string MeizuCompactIndexUrl = "";
        private const string LenovoMachineQueryUrl = "https://ptstpd.lenovo.com.cn/home/ConfigurationQuery/getMachineSequenceInfo";
        private const string LenovoPackageQueryUrl = "https://ptstpd.lenovo.com.cn/home/ConfigurationQuery/getPadFlashingMachine";
        private const string LenovoPackagePassword = "FC(fv:SknR";
        private static readonly IReadOnlyDictionary<string, string> LenovoPresetMtmMap =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Y700二代"] = "ZACW0006CN",
                ["Y700三代"] = "ZAEF0029CN",
                ["Y700四代"] = "ZAG40005CN",
                ["Y700五代"] = "ZAH20097CN"
            };
        private static readonly Regex LogUrlRegex = new(@"https?://[^\s`""<>]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RomSemanticVersionRegex =
            new(@"(?<!\d)\d+(?:\.\d+){1,5}(?!\d)", RegexOptions.Compiled);
        private static readonly IComparer<string> RomVersionDisplayComparer =
            Comparer<string>.Create(CompareRomVersionDisplayText);

        private static readonly IReadOnlyDictionary<string, string> MeizuCompactSeriesCodeMap =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["数字系列"] = "numeric",
                ["Note系列"] = "note",
                ["Lucky系列"] = "lucky",
                ["魅蓝系列"] = "mblue"
            };

        private static readonly IReadOnlyDictionary<string, string> C16DeviceCodeNameMap =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["PLZ110"] = "一加15T",
                ["PLR110"] = "一加 ACE 6T",
                ["PLQ110"] = "一加 ACE 6",
                ["PLK110"] = "一加15",
                ["PLU110"] = "一加Turbo 6",
                ["PLY110"] = "一加Turbo 6V",
                ["PYS110"] = "一加Turbo 6X",
                ["PYR110"] = "一加Turbo 6X Pro",
                ["PLC110"] = "一加ACE5至尊版",
                ["PLF110"] = "一加ACE5竞速版",
                ["PMB110"] = "一加ACE6至尊版",
                ["OPD2513"] = "一加Pad3 Pro",
                ["OPD2413"] = "一加Pad2 Pro",
                ["PKX110"] = "一加13T",
                ["OPD2508"] = "一加平板 2",
                ["OPD2407"] = "一加平板",
                ["PKR110"] = "一加ACE5 Pro",
                ["PKG110"] = "一加ACE5",
                ["PJZ110"] = "一加13",
                ["OPD2404"] = "一加Pad Pro",
                ["OPD2417"] = "OPPO Pad SE",
                ["OPD2515"] = "OPPO Pad Mini",
                ["OPD2102"] = "OPPO Pad Air",
                ["OPD2301"] = "OPPO Pad Air2",
                ["OPD2405"] = "OPPO Pad 3",
                ["OPD2401"] = "OPPO Pad 3 Pro",
                ["OPD2409"] = "OPPO Pad 4 Pro",
                ["OPD2501"] = "OPPO Pad Air5",
                ["OPD2506"] = "OPPO Pad 5",
                ["OPD2511"] = "OPPO Pad 5 Pro",
                ["OPD2601"] = "OPPO Pad 6",
                ["PJX110"] = "一加ACE3 Pro",
                ["PJF110"] = "一加ACE3V",
                ["PJE110"] = "一加ACE3",
                ["PJD110"] = "一加12",
                ["PJA110"] = "一加ACE2 Pro",
                ["PHP110"] = "一加ACE2v",
                ["PHK110"] = "一加ACE2",
                ["PHB110"] = "一加11",
                ["PGP110"] = "一加ACE Pro",
                ["PGZ110"] = "一加ACE竞速版",
                ["PKGM10"] = "一加ACE",
                ["NE2210"] = "一加10Pro",
                ["martini"] = "一加 9RT",
                ["lemonades"] = "一加 9R",
                ["lemonadep"] = "一加 9 Pro",
                ["lemonade"] = "一加 9",
                ["kebab"] = "一加 8T",
                ["instantnoodlep"] = "一加 8 Pro",
                ["instantnoodle"] = "一加 8",
                ["hotdogg"] = "一加 7T Pro",
                ["RMX3370"] = "真我GT Neo2",
                ["RMX3357"] = "真我GT neo2T",
                ["RMX3562"] = "真我GT neo3 150w",
                ["RMX3560"] = "真我GT neo3 80w",
                ["RMX3706"] = "真我GT neo5 150w",
                ["RMX3708"] = "真我GT neo5 240w",
                ["RMX3700"] = "真我GT neo5 SE",
                ["RMX3850"] = "真我GT neo6 SE",
                ["RMX3852"] = "真我GT neo6",
                ["RMX3366"] = "真我GT大师探索版",
                ["RMX3300"] = "真我GT2 Pro",
                ["RMX3551"] = "真我GT2大师探索版",
                ["RMX3310"] = "真我GT2",
                ["RMX3820"] = "真我GT5 150w",
                ["RMX3823"] = "真我GT5 240w",
                ["RMX3888"] = "真我GT5 Pro",
                ["RMX3800"] = "真我GT6",
                ["RMX5090"] = "真我GT7 Pro竞速版",
                ["RMX5010"] = "真我GT7 Pro",
                ["RMX6688"] = "真我GT7",
                ["RMX5200"] = "真我GT8 Pro",
                ["RMX6699"] = "真我GT8",
                ["RMX8899"] = "真我Neo8",
                ["RMX5080"] = "真我GT neo7 SE",
                ["RMX5062"] = "真我GT neo7 Turbo",
                ["RMX5060"] = "真我GT neo7",
                ["RMX5071"] = "真我GT neo7X"
            };

        private static readonly HashSet<string> C16NativeDeviceCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PLZ110",
            "PLQ110",
            "PLK110",
            "PLU110",
            "PLY110",
            "PYS110",
            "PYR110",
            "OPD2417",
            "OPD2515",
            "OPD2102",
            "OPD2301",
            "OPD2405",
            "OPD2401",
            "OPD2409",
            "OPD2501",
            "OPD2506",
            "OPD2511",
            "OPD2601",
            "RMX3370",
            "RMX3357",
            "RMX3562",
            "RMX3560",
            "RMX3706",
            "RMX3708",
            "RMX3700",
            "RMX3850",
            "RMX3852",
            "RMX3366",
            "RMX3300",
            "RMX3551",
            "RMX3310",
            "RMX3820",
            "RMX3823",
            "RMX3888",
            "RMX3800",
            "RMX5090",
            "RMX5010",
            "RMX6688",
            "RMX5200",
            "RMX6699",
            "RMX8899",
            "RMX5080",
            "RMX5062",
            "RMX5060",
            "RMX5071"
        };

        private static string NormalizeDeviceName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;

            var sb = new StringBuilder(name.Length);
            bool inParen = false;
            foreach (var c in name)
            {
                if (c == '(')
                {
                    inParen = true;
                    continue;
                }
                if (c == ')')
                {
                    inParen = false;
                    continue;
                }
                if (inParen) continue;
                sb.Append(c);
            }

            var s = sb.ToString();
            s = s.Replace("Realme", string.Empty, StringComparison.OrdinalIgnoreCase)
                 .Replace("OnePlus", string.Empty, StringComparison.OrdinalIgnoreCase);

            var chars = s.Where(c => !char.IsWhiteSpace(c) && c != '/' && c != '\\' && c != '-' && c != '_');
            return new string(chars.ToArray());
        }

        private static bool TryExtractC16MappedDeviceCode(string name, out string deviceCode)
        {
            deviceCode = string.Empty;
            if (string.IsNullOrWhiteSpace(name)) return false;

            string? best = null;
            foreach (var key in C16DeviceCodeNameMap.Keys)
            {
                if (name.IndexOf(key, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (best == null || key.Length > best.Length) best = key;
            }

            if (string.IsNullOrWhiteSpace(best)) return false;
            deviceCode = best;
            return true;
        }

        private static bool DeviceNameMatchesC16(string deviceName, string deviceCode)
        {
            if (string.IsNullOrWhiteSpace(deviceName)) return false;
            if (string.IsNullOrWhiteSpace(deviceCode)) return false;

            if (deviceName.IndexOf(deviceCode, StringComparison.OrdinalIgnoreCase) >= 0) return true;

            if (TryExtractC16MappedDeviceCode(deviceName, out var extracted) &&
                string.Equals(extracted, deviceCode, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var normDevice = NormalizeDeviceName(deviceName).ToLowerInvariant();
            var normCode = NormalizeDeviceName(deviceCode).ToLowerInvariant();

            string mappedName;
            C16DeviceCodeNameMap.TryGetValue(deviceCode, out mappedName);

            if (!string.IsNullOrWhiteSpace(mappedName))
            {
                var normMappedWhole = NormalizeDeviceName(mappedName).ToLowerInvariant();
                if (!string.IsNullOrEmpty(normMappedWhole) && normDevice == normMappedWhole) return true;

                var parts = mappedName.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var p in parts)
                {
                    var normMappedPart = NormalizeDeviceName(p).ToLowerInvariant();
                    if (!string.IsNullOrEmpty(normMappedPart) && normDevice == normMappedPart) return true;
                }
            }

            if (!string.IsNullOrEmpty(normCode))
            {
                if (normDevice == normCode) return true;
            }

            return false;
        }

        private static bool IsC16NativeDevice(string deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName)) return false;
            foreach (var kvp in C16DeviceCodeNameMap)
            {
                if (DeviceNameMatchesC16(deviceName, kvp.Key)) return true;
            }
            return false;
        }

        private static string? GetC16BrandFromDisplayName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            if (name.IndexOf("一加", StringComparison.OrdinalIgnoreCase) >= 0) return "OnePlus";
            if (name.IndexOf("真我", StringComparison.OrdinalIgnoreCase) >= 0) return "Realme";
            if (name.IndexOf("OPPO", StringComparison.OrdinalIgnoreCase) >= 0) return "OPPO";
            return null;
        }

        private static bool ShouldIncludeC16DeviceInSeries(string brand, string series, string displayName)
        {
            if (string.IsNullOrWhiteSpace(brand)) return false;
            if (string.IsNullOrWhiteSpace(series)) return false;
            if (string.IsNullOrWhiteSpace(displayName)) return false;

            if (string.Equals(brand, "OPPO", StringComparison.OrdinalIgnoreCase))
            {
                return series.IndexOf("Pad", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       series.IndexOf("平板", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            if (string.Equals(brand, "OnePlus", StringComparison.OrdinalIgnoreCase))
            {
                var hasAce = displayName.IndexOf("ACE", StringComparison.OrdinalIgnoreCase) >= 0;
                var hasPad = displayName.IndexOf("Pad", StringComparison.OrdinalIgnoreCase) >= 0 || displayName.IndexOf("平板", StringComparison.OrdinalIgnoreCase) >= 0;
                var hasTurbo = displayName.IndexOf("Turbo", StringComparison.OrdinalIgnoreCase) >= 0;
                var isOnePlus15T = displayName.IndexOf("一加15T", StringComparison.OrdinalIgnoreCase) >= 0;

                if (series.IndexOf("ACE", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return hasAce;
                }

                if (series.IndexOf("Turbo", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return hasTurbo;
                }

                if (series.IndexOf("平板", StringComparison.OrdinalIgnoreCase) >= 0 || series.IndexOf("Pad", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return hasPad;
                }

                if (series.IndexOf("数字", StringComparison.OrdinalIgnoreCase) >= 0 || series.IndexOf("Number", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return !hasAce && !hasPad && !hasTurbo;
                }

                return !hasAce && !hasPad && !hasTurbo;
            }

            if (string.Equals(brand, "Realme", StringComparison.OrdinalIgnoreCase))
            {
                var lower = displayName.ToLowerInvariant();
                var isNeo = lower.Contains("neo");

                if (series.IndexOf("Neo", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return isNeo;
                }

                if (series.IndexOf("GT", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return lower.Contains("gt") && !isNeo;
                }
            }

            return false;
        }

        private void AddC16DevicesToDeviceConfigs(string brand, string series, Dictionary<string, RomDeviceConfig> dict)
        {
            if (UseRomBackendApi) return;
            if (!_enableC16Dynamic) return;
            if (!IsC16Brand(brand)) return;
            if (_c16Records == null || _c16Records.Count == 0) return;
            if (dict == null) return;

            var existingNames = new HashSet<string>(dict.Keys, StringComparer.OrdinalIgnoreCase);
            var existingNormalizedNames = new HashSet<string>(
                dict.Keys
                    .Select(NormalizeDeviceName)
                    .Where(x => !string.IsNullOrWhiteSpace(x)),
                StringComparer.OrdinalIgnoreCase);
            var existingCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in dict.Keys)
            {
                if (TryExtractC16MappedDeviceCode(name, out var code))
                {
                    existingCodes.Add(code);
                }
            }

            foreach (var kvp in C16DeviceCodeNameMap)
            {
                var code = kvp.Key;
                var displayName = kvp.Value;
                if (string.IsNullOrWhiteSpace(code)) continue;
                if (string.IsNullOrWhiteSpace(displayName)) continue;

                var mappedBrand = GetC16BrandFromDisplayName(displayName);
                if (string.IsNullOrWhiteSpace(mappedBrand)) continue;
                if (!string.Equals(mappedBrand, brand, StringComparison.OrdinalIgnoreCase)) continue;
                if (!ShouldIncludeC16DeviceInSeries(brand, series, displayName)) continue;
                if (existingCodes.Contains(code)) continue;
                if (existingNames.Contains(displayName)) continue;
                if (existingNormalizedNames.Contains(NormalizeDeviceName(displayName))) continue;

                var hasRecord = _c16Records.Any(r => string.Equals(r.DeviceCode?.Trim(), code, StringComparison.OrdinalIgnoreCase));
                // 如果云端还没有包记录，默认会隐藏。这里针对 PLZ110 (一加15T) 放行，使其能在列表显示
                if (!hasRecord && !string.Equals(code, "PLZ110", StringComparison.OrdinalIgnoreCase)) continue;

                var cfg = new RomDeviceConfig
                {
                    Device = displayName.Trim(),
                    Url = string.Empty
                };

                dict[cfg.Device] = cfg;
                existingNames.Add(cfg.Device);
                existingNormalizedNames.Add(NormalizeDeviceName(cfg.Device));
                existingCodes.Add(code);
            }
        }

        private const string OppoASeriesConfigUrl = "";
        private const string OppoFindSeriesConfigUrl = "";
        private const string OppoKSeriesConfigUrl = "";
        private const string OppoPadSeriesConfigUrl = "";
        private const string OppoRSeriesConfigUrl = "";
        private const string OppoRenoSeriesConfigUrl = "";
        private const string OppoWatchSeriesConfigUrl = "";

        private const string RealmeGtSeriesConfigUrl = "";
        private const string RealmeGtNeoSeriesConfigUrl = "";
        private const string RealmeNeoSeriesConfigUrl = "";
        private const string RealmeNumberSeriesConfigUrl = "";
        private const string RealmeQSeriesConfigUrl = "";
        private const string RealmeVSeriesConfigUrl = "";

        private const string OnePlusAceSeriesShouhouConfigUrl = "";
        private const string OnePlusNumberSeriesShouhouConfigUrl = "";
        private const string OnePlusPadSeriesShouhouConfigUrl = "";
        private const string OnePlusTurboSeriesShouhouConfigUrl = "";

        private const string RomSelectPackageTypeFull = "全量包";
        private const string RomSelectPackageTypeAfterSales = "售后包";

        private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> RomSelectAfterSalesSeriesUrlMap =
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["OPPO"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["A系列"] = OppoASeriesConfigUrl,
                    ["Find系列"] = OppoFindSeriesConfigUrl,
                    ["K系列"] = OppoKSeriesConfigUrl,
                    ["Pad系列"] = OppoPadSeriesConfigUrl,
                    ["R系列"] = OppoRSeriesConfigUrl,
                    ["Reno系列"] = OppoRenoSeriesConfigUrl,
                    ["Watch系列"] = OppoWatchSeriesConfigUrl
                },
                ["Realme"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["GT系列"] = RealmeGtSeriesConfigUrl,
                    ["Gt Neo系列"] = RealmeGtNeoSeriesConfigUrl,
                    ["Neo系列"] = RealmeNeoSeriesConfigUrl,
                    ["数字系列"] = RealmeNumberSeriesConfigUrl,
                    ["Q系列"] = RealmeQSeriesConfigUrl,
                    ["V系列"] = RealmeVSeriesConfigUrl
                },
                ["OnePlus"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ACE系列"] = OnePlusAceSeriesShouhouConfigUrl,
                    ["数字系列"] = OnePlusNumberSeriesShouhouConfigUrl,
                    ["Pad系列"] = OnePlusPadSeriesShouhouConfigUrl,
                    ["Turbo系列"] = OnePlusTurboSeriesShouhouConfigUrl
                },
                ["Xiaomi"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["CIVI系列"] = XiaomiFullCiviSeriesConfigUrl,
                    ["MIX系列"] = XiaomiFullMixSeriesConfigUrl,
                    ["Note系列"] = XiaomiFullNoteSeriesConfigUrl,
                    ["Pad系列"] = XiaomiFullPadSeriesConfigUrl,
                    ["POCO系列"] = XiaomiFullPocoSeriesConfigUrl,
                    ["数字系列"] = XiaomiFullNumberSeriesConfigUrl
                },
                ["Redmi"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["K系列"] = RedmiFullKSeriesConfigUrl,
                    ["Note系列"] = RedmiFullNoteSeriesConfigUrl,
                    ["数字系列"] = RedmiFullNumberSeriesConfigUrl,
                    ["Pad系列"] = RedmiFullPadSeriesConfigUrl,
                    ["Turbo系列"] = RedmiFullTurboSeriesConfigUrl
                },
                ["魅族"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["数字系列"] = MeizuCompactIndexUrl,
                    ["Note系列"] = MeizuCompactIndexUrl,
                    ["Lucky系列"] = MeizuCompactIndexUrl,
                    ["魅蓝系列"] = MeizuCompactIndexUrl
                }
            };

        private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> RomSelectFullPackageSeriesUrlMap =
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["OPPO"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["A系列"] = OppoFullASeriesConfigUrl,
                    ["Find系列"] = OppoFullFindSeriesConfigUrl,
                    ["K系列"] = OppoFullKSeriesConfigUrl,
                    ["Pad系列"] = OppoFullPadSeriesConfigUrl,
                    ["R系列"] = OppoFullRSeriesConfigUrl,
                    ["Reno系列"] = OppoFullRenoSeriesConfigUrl
                },
                ["Realme"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["GT Neo系列"] = RealmeFullGtNeoSeriesConfigUrl,
                    ["Neo系列"] = RealmeFullNeoSeriesConfigUrl,
                    ["Q系列"] = RealmeFullQSeriesConfigUrl,
                    ["V系列"] = RealmeFullVSeriesConfigUrl,
                    ["数字系列"] = RealmeFullNumberSeriesConfigUrl,
                    ["GT系列"] = RealmeFullGtSeriesConfigUrl
                },
                ["OnePlus"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ACE系列"] = OnePlusAceConfigUrl,
                    ["数字系列"] = OnePlusNumberConfigUrl,
                    ["Pad系列"] = OnePlusPadConfigUrl,
                    ["Turbo系列"] = OnePlusTurboConfigUrl
                },
                ["Xiaomi"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["CIVI系列"] = XiaomiFullCiviSeriesConfigUrl,
                    ["MIX系列"] = XiaomiFullMixSeriesConfigUrl,
                    ["Note系列"] = XiaomiFullNoteSeriesConfigUrl,
                    ["Pad系列"] = XiaomiFullPadSeriesConfigUrl,
                    ["POCO系列"] = XiaomiFullPocoSeriesConfigUrl,
                    ["数字系列"] = XiaomiFullNumberSeriesConfigUrl
                },
                ["Redmi"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["K系列"] = RedmiFullKSeriesConfigUrl,
                    ["Note系列"] = RedmiFullNoteSeriesConfigUrl,
                    ["数字系列"] = RedmiFullNumberSeriesConfigUrl,
                    ["Pad系列"] = RedmiFullPadSeriesConfigUrl,
                    ["Turbo系列"] = RedmiFullTurboSeriesConfigUrl
                }
            };

        private static readonly HttpClient RomDownloadHttpClient = new HttpClient();
        private static readonly HttpClient C16ServerClient = new HttpClient
        {
            BaseAddress = new Uri(C16ServerBaseUrl)
        };
        private static readonly SemaphoreSlim RomConfigLoadSemaphore = new SemaphoreSlim(1, 1);

        private const string C16ServerBaseUrl = "http://127.0.0.1";
        private const string C16DownloadCheckJsonPath = "";
        private const string C16DownloadCheckTokenSecret = "g28q7yMH8dEEM07jA648Z6C9";
        private const string C16DownloadCheckTokenParamName = "sign";

        private CancellationTokenSource? _romConfigLoadCts;
        private CancellationTokenSource? _romBrandSeriesLoadCts;
        private string _activeRomDownloadBrand = string.Empty;
        private string _activeRomDownloadSeries = string.Empty;
        private bool _romDownloadUiUpdating;
        private bool _romExtractInProgress;
        private readonly Dictionary<string, Dictionary<string, RomDeviceConfig>> _romDownloadDeviceConfigsBySeriesKey = new Dictionary<string, Dictionary<string, RomDeviceConfig>>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, List<string>> _romDownloadLinksByVersionName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, RomItemConfig> _romDownloadItemsByVersionName = new Dictionary<string, RomItemConfig>(StringComparer.OrdinalIgnoreCase);
        private List<string> _selectedRomDownloadLinks = new List<string>();
        private Dictionary<string, string> _selectedRomDownloadHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private readonly SemaphoreSlim _c16RecordsSemaphore = new SemaphoreSlim(1, 1);
        private List<C16DownloadCheckRecord> _c16Records = new List<C16DownloadCheckRecord>();
        private bool _enableC16Dynamic;

        private CancellationTokenSource? _romSelectConfigLoadCts;
        private CancellationTokenSource? _romSelectBrandSeriesLoadCts;
        private bool _romSelectUiUpdating;
        private string _activeRomSelectPackageType = RomSelectPackageTypeFull;
        private string _activeRomSelectBrand = string.Empty;
        private string _activeRomSelectSeries = string.Empty;
        private readonly Dictionary<string, Dictionary<string, RomDeviceConfig>> _romSelectDeviceConfigsBySeriesKey = new Dictionary<string, Dictionary<string, RomDeviceConfig>>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, List<string>> _romSelectLinksByVersionName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, RomItemConfig> _romSelectItemsByVersionName = new Dictionary<string, RomItemConfig>(StringComparer.OrdinalIgnoreCase);
        private string _lenovoQueryResolvedDownloadUrl = string.Empty;
        private string _lenovoQueryLastSummaryText = string.Empty;
        private string _lastRomSelectOutputText = string.Empty;
        private string _lastLenovoQueryOutputText = string.Empty;

        private static System.Windows.Media.Brush GetRomDownloadLogBrush(string level)
        {
            return level switch
            {
                "成功" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 167, 69)),
                "错误" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 53, 69)),
                "警告" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 165, 0)),
                _ => new SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 51, 51))
            };
        }

        private static void AppendLogMessageRuns(Paragraph paragraph, string message, System.Windows.Media.Brush defaultBrush)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            int currentIndex = 0;
            System.Windows.Media.Brush linkBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(123, 63, 228));

            foreach (Match match in LogUrlRegex.Matches(message))
            {
                if (match.Index > currentIndex)
                {
                    paragraph.Inlines.Add(new Run(message.Substring(currentIndex, match.Index - currentIndex))
                    {
                        Foreground = defaultBrush
                    });
                }

                paragraph.Inlines.Add(new Run(match.Value)
                {
                    Foreground = linkBrush,
                    FontWeight = FontWeights.SemiBold
                });

                currentIndex = match.Index + match.Length;
            }

            if (currentIndex < message.Length)
            {
                paragraph.Inlines.Add(new Run(message.Substring(currentIndex))
                {
                    Foreground = defaultBrush
                });
            }
        }

        private void AppendRomDownloadLog(string level, string message)
        {
            var logRichTextBox = RomDownloadLogRichTextBox;
            if (logRichTextBox == null) return;

            string timestamp = DateTime.Now.ToString("HH:mm:ss");

            Dispatcher.Invoke(() =>
            {
                var defaultBrush = GetRomDownloadLogBrush(level);
                logRichTextBox.Document.PagePadding = new Thickness(0);
                var paragraph = new Paragraph
                {
                    Margin = new Thickness(0),
                    Padding = new Thickness(0),
                    LineHeight = 16,
                    LineStackingStrategy = LineStackingStrategy.BlockLineHeight
                };
                paragraph.Inlines.Add(new Run($"[{timestamp}] ")
                {
                    Foreground = defaultBrush
                });
                AppendLogMessageRuns(paragraph, message, defaultBrush);
                logRichTextBox.Document.Blocks.Add(paragraph);
                logRichTextBox.ScrollToEnd();
            });
        }

        private void SetRomSelectOutput(string text, string level = "信息")
        {
            text = CleanLink(text);
            if (string.Equals(_lastRomSelectOutputText, text, StringComparison.Ordinal))
            {
                return;
            }

            _lastRomSelectOutputText = text;
            if (RomSelectParsedLinkTextBox != null)
            {
                RomSelectParsedLinkTextBox.Text = text;
            }

            if (!string.IsNullOrWhiteSpace(text))
            {
                AppendRomDownloadLog(level, $"Rom获取输出：{Environment.NewLine}{text}");
            }
        }

        private void SetLenovoQueryOutput(string text, string level = "信息")
        {
            text = CleanLink(text);
            if (string.Equals(_lastLenovoQueryOutputText, text, StringComparison.Ordinal))
            {
                return;
            }

            _lastLenovoQueryOutputText = text;
            if (LenovoQueryResultTextBox != null)
            {
                LenovoQueryResultTextBox.Text = text;
            }

            if (!string.IsNullOrWhiteSpace(text))
            {
                AppendRomDownloadLog(level, $"联想查包输出：{Environment.NewLine}{text}");
            }
        }

        private sealed class RomItemConfig
        {
            public string Name { get; set; } = string.Empty;
            public string Href { get; set; } = string.Empty;
            public List<string> DownloadLinks { get; set; } = new List<string>();
            public Dictionary<string, string> RequestHeaders { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class RomDeviceConfig
        {
            public string Device { get; set; } = string.Empty;
            public string Url { get; set; } = string.Empty;
            public List<RomItemConfig> Items { get; set; } = new List<RomItemConfig>();
        }

        private sealed class C16DownloadCheckRecord
        {
            public string DeviceCode { get; set; } = string.Empty;
            public string OtaVersion { get; set; } = string.Empty;
            public string Version { get; set; } = string.Empty;
            public string DownloadCheckUrl { get; set; } = string.Empty;
            public string FinalUrl { get; set; } = string.Empty;
            public DateTime? CreatedAt { get; set; }
            public DateTime? ExpiresAt { get; set; }
        }

        private sealed class C16ResolveRequestDto
        {
            public string Url { get; set; }
            public string DeviceCode { get; set; }
            public string OtaVersion { get; set; }
            public bool SaveToStore { get; set; } = true;
        }

        private sealed class C16ResolveResponseDto
        {
            public string url { get; set; }
            public DateTime? expiresAt { get; set; }
        }

        private sealed class LenovoMachineInfo
        {
            public string SN { get; set; } = string.Empty;
            public string MTM { get; set; } = string.Empty;
            public string MachineName { get; set; } = string.Empty;
            public string ProductDate { get; set; } = string.Empty;
            public string ScanDate { get; set; } = string.Empty;
            public string PackingLotNo { get; set; } = string.Empty;
            public string SaleOrder { get; set; } = string.Empty;
            public string SaleArea { get; set; } = string.Empty;
            public string ProductSeries { get; set; } = string.Empty;
        }

        private sealed class LenovoPackageInfo
        {
            public string ProductName { get; set; } = string.Empty;
            public string MarketName { get; set; } = string.Empty;
            public string LatestVersion { get; set; } = string.Empty;
            public string ServerVersionName { get; set; } = string.Empty;
            public string ProductModel { get; set; } = string.Empty;
            public string Platform { get; set; } = string.Empty;
            public string FlashingMethod { get; set; } = string.Empty;
            public string DownloadUrl { get; set; } = string.Empty;
            public string Mtm { get; set; } = string.Empty;
        }

        private sealed class LenovoQueryResult
        {
            public bool Success { get; set; }
            public string InputType { get; set; } = string.Empty;
            public string PresetModelName { get; set; } = string.Empty;
            public string ResolvedMtm { get; set; } = string.Empty;
            public LenovoMachineInfo? MachineInfo { get; set; }
            public LenovoPackageInfo? FullPackage { get; set; }
            public string Error { get; set; } = string.Empty;
        }

        private const string RomBackendDefaultBaseUrl = "https://violettool.top/rom-api";
        private const string RomApiPackageTypeFull = "full";
        private const string RomApiPackageTypeAfterSales = "afterSales";

        private static readonly JsonSerializerOptions RomApiJsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private static bool UseRomBackendApi => true;

        private static string RomBackendBaseUrl
        {
            get
            {
                var configured = Environment.GetEnvironmentVariable("VIOLET_ROM_API_BASE");
                if (string.IsNullOrWhiteSpace(configured)) configured = RomBackendDefaultBaseUrl;
                return configured.Trim().TrimEnd('/');
            }
        }

        private sealed class RomApiListResponse
        {
            public List<string> Items { get; set; } = new List<string>();
        }

        private sealed class RomApiVersionItem
        {
            public string Name { get; set; } = string.Empty;
        }

        private sealed class RomApiVersionResponse
        {
            public List<RomApiVersionItem> Items { get; set; } = new List<RomApiVersionItem>();
        }

        private sealed class RomApiDownloadLinkRequest
        {
            public string PackageType { get; set; } = string.Empty;
            public string Brand { get; set; } = string.Empty;
            public string Series { get; set; } = string.Empty;
            public string Device { get; set; } = string.Empty;
            public string Version { get; set; } = string.Empty;
        }

        private sealed class RomApiDownloadLinkResponse
        {
            public List<string> Links { get; set; } = new List<string>();
            public Dictionary<string, string> RequestHeaders { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public string ResolveRoute { get; set; } = string.Empty;
        }

        private static string ToRomApiPackageType(string? packageType)
        {
            return string.Equals(packageType, RomSelectPackageTypeAfterSales, StringComparison.OrdinalIgnoreCase)
                ? RomApiPackageTypeAfterSales
                : RomApiPackageTypeFull;
        }

        private static string Esc(string value) => Uri.EscapeDataString(value ?? string.Empty);

        private static async Task<T> GetRomApiAsync<T>(string path, CancellationToken cancellationToken) where T : new()
        {
            var json = await RomDownloadHttpClient.GetStringAsync(RomBackendBaseUrl + path, cancellationToken).ConfigureAwait(true);
            return JsonSerializer.Deserialize<T>(json, RomApiJsonOptions) ?? new T();
        }

        private static async Task<List<string>> FetchRomApiDevicesAsync(string packageType, string brand, string series, CancellationToken cancellationToken)
        {
            var result = await GetRomApiAsync<RomApiListResponse>(
                $"/devices?packageType={Esc(packageType)}&brand={Esc(brand)}&series={Esc(series)}",
                cancellationToken).ConfigureAwait(true);
            return result.Items?.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                ?? new List<string>();
        }

        private static async Task<List<string>> FetchRomApiSeriesAsync(string packageType, string brand, CancellationToken cancellationToken)
        {
            var result = await GetRomApiAsync<RomApiListResponse>(
                $"/series?packageType={Esc(packageType)}&brand={Esc(brand)}",
                cancellationToken).ConfigureAwait(true);
            return result.Items?.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                ?? new List<string>();
        }

        private static async Task<List<string>> FetchRomApiVersionsAsync(string packageType, string brand, string series, string device, CancellationToken cancellationToken)
        {
            var result = await GetRomApiAsync<RomApiVersionResponse>(
                $"/versions?packageType={Esc(packageType)}&brand={Esc(brand)}&series={Esc(series)}&device={Esc(device)}",
                cancellationToken).ConfigureAwait(true);
            return result.Items?
                .Select(x => CleanLink(x.Name))
                .Where(IsValidVersionDisplayName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();
        }

        private static async Task<RomApiDownloadLinkResponse> FetchRomApiDownloadLinksAsync(
            string packageType,
            string brand,
            string series,
            string device,
            string version,
            CancellationToken cancellationToken)
        {
            var request = new RomApiDownloadLinkRequest
            {
                PackageType = packageType,
                Brand = brand,
                Series = series,
                Device = device,
                Version = version
            };

            var json = JsonSerializer.Serialize(request);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await RomDownloadHttpClient.PostAsync(RomBackendBaseUrl + "/download-link", content, cancellationToken).ConfigureAwait(true);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(true);
            if (!response.IsSuccessStatusCode)
            {
                var message = ExtractRomApiErrorMessage(body);
                if (string.IsNullOrWhiteSpace(message))
                {
                    message = $"HTTP {(int)response.StatusCode} {response.StatusCode}";
                }
                throw new InvalidOperationException(message);
            }
            return JsonSerializer.Deserialize<RomApiDownloadLinkResponse>(body, RomApiJsonOptions) ?? new RomApiDownloadLinkResponse();
        }

        private static string ExtractRomApiErrorMessage(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return string.Empty;
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("message", out var message) &&
                    message.ValueKind == JsonValueKind.String)
                {
                    return SanitizeRomApiErrorMessage(message.GetString());
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private static string SanitizeRomApiErrorMessage(string? message)
        {
            message = CleanLink(message);
            if (string.IsNullOrWhiteSpace(message)) return string.Empty;

            if (message.Length > 300 ||
                message.Contains("downloadCheck", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("http://", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("https://", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("<html", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("nginx", StringComparison.OrdinalIgnoreCase))
            {
                return "C16直链解析失败，主线路和备用线路暂时均未返回可用链接。";
            }

            return message;
        }

        private static string GetRomResolveRouteDisplayName(string? resolveRoute)
        {
            resolveRoute = CleanLink(resolveRoute).ToLowerInvariant();
            return resolveRoute switch
            {
                "line1" => "线路1（官方）",
                "line2" => "线路2（备用）",
                _ => string.Empty
            };
        }

        private static string CleanLink(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var s = value.Trim();
            s = s.Trim('`', '"', '\'', ' ');
            return s.Trim();
        }

        private static bool IsLikelyRomArchiveLink(string url)
        {
            url = CleanLink(url);
            if (string.IsNullOrWhiteSpace(url)) return false;

            var path = url;
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                path = uri.AbsolutePath;
            }
            else
            {
                var queryIndex = path.IndexOfAny(new[] { '?', '#' });
                if (queryIndex >= 0) path = path[..queryIndex];
            }

            path = Uri.UnescapeDataString(path).TrimEnd();
            var archiveExtensions = new[] { ".zip", ".ozip", ".tgz", ".tar.gz", ".7z", ".rar" };
            return archiveExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
        }

        private static List<string> PreferRomArchiveLinks(string packageType, string brand, IEnumerable<string> links)
        {
            var cleaned = links
                .Select(NormalizeUrlForRequest)
                .Select(CleanLink)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!string.Equals(ToRomApiPackageType(packageType), RomApiPackageTypeFull, StringComparison.OrdinalIgnoreCase) ||
                !IsC16Brand(brand))
            {
                return cleaned;
            }

            var archiveLinks = cleaned
                .Where(IsLikelyRomArchiveLink)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return archiveLinks.Count > 0 ? archiveLinks : cleaned;
        }

        private static string? TryGetString(JsonElement el, string name)
        {
            if (el.ValueKind != JsonValueKind.Object) return null;
            return el.TryGetProperty(name, out var p) ? p.GetString() : null;
        }

        private static string? TryGetStringAny(JsonElement el, params string[] names)
        {
            foreach (var n in names)
            {
                var s = TryGetString(el, n);
                if (!string.IsNullOrWhiteSpace(s)) return s;
            }
            return null;
        }

        private static int? TryGetInt32(JsonElement el, string propertyName)
        {
            if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(propertyName, out var valueEl)) return null;
            if (valueEl.ValueKind == JsonValueKind.Number && valueEl.TryGetInt32(out var number)) return number;
            if (valueEl.ValueKind == JsonValueKind.String && int.TryParse(valueEl.GetString(), out var parsed)) return parsed;
            return null;
        }

        private static LenovoMachineInfo ParseLenovoMachineInfo(JsonElement el)
        {
            return new LenovoMachineInfo
            {
                SN = CleanLink(TryGetStringAny(el, "SN") ?? string.Empty),
                MTM = CleanLink(TryGetStringAny(el, "MTM") ?? string.Empty),
                MachineName = CleanLink(TryGetStringAny(el, "MachineName") ?? string.Empty),
                ProductDate = CleanLink(TryGetStringAny(el, "ProductDate") ?? string.Empty),
                ScanDate = CleanLink(TryGetStringAny(el, "ScanDate") ?? string.Empty),
                PackingLotNo = CleanLink(TryGetStringAny(el, "PackingLotNo") ?? string.Empty),
                SaleOrder = CleanLink(TryGetStringAny(el, "SaleOrder") ?? string.Empty),
                SaleArea = CleanLink(TryGetStringAny(el, "SaleArea") ?? string.Empty),
                ProductSeries = CleanLink(TryGetStringAny(el, "ProductSeries") ?? string.Empty)
            };
        }

        private static LenovoPackageInfo ParseLenovoPackageInfo(JsonElement el)
        {
            return new LenovoPackageInfo
            {
                ProductName = CleanLink(TryGetStringAny(el, "product_name", "productName") ?? string.Empty),
                MarketName = CleanLink(TryGetStringAny(el, "market_name", "marketName") ?? string.Empty),
                LatestVersion = CleanLink(TryGetStringAny(el, "latest_version", "latestVersion") ?? string.Empty),
                ServerVersionName = CleanLink(TryGetStringAny(el, "server_version_name", "serverVersionName") ?? string.Empty),
                ProductModel = CleanLink(TryGetStringAny(el, "product_model", "productModel") ?? string.Empty),
                Platform = CleanLink(TryGetStringAny(el, "platform") ?? string.Empty),
                FlashingMethod = CleanLink(TryGetStringAny(el, "flashing_machine_method", "flashingMachineMethod") ?? string.Empty),
                DownloadUrl = CleanLink(TryGetStringAny(el, "download_url", "downloadUrl") ?? string.Empty),
                Mtm = CleanLink(TryGetStringAny(el, "mtm", "MTM") ?? string.Empty)
            };
        }

        private static LenovoPackageInfo? PickLenovoPackageByMtm(JsonElement packagesEl, string mtm)
        {
            if (packagesEl.ValueKind != JsonValueKind.Array) return null;

            LenovoPackageInfo? fallback = null;
            string normalizedMtm = CleanLink(mtm).ToUpperInvariant();
            foreach (var item in packagesEl.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var parsed = ParseLenovoPackageInfo(item);
                fallback ??= parsed;

                string mtmBlob = CleanLink(TryGetStringAny(item, "mtm", "MTM") ?? string.Empty);
                var mtmList = mtmBlob
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim().ToUpperInvariant());
                if (mtmList.Contains(normalizedMtm))
                {
                    parsed.Mtm = normalizedMtm;
                    return parsed;
                }
            }

            return fallback;
        }

        private async Task<LenovoMachineInfo?> QueryLenovoMachineAsync(string machineNo, CancellationToken cancellationToken)
        {
            string url = $"{LenovoMachineQueryUrl}?MachineNo={Uri.EscapeDataString(machineNo)}";
            string json = await RomDownloadHttpClient.GetStringAsync(url, cancellationToken).ConfigureAwait(true);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (TryGetInt32(root, "StatusCode") != 200) return null;
            if (!root.TryGetProperty("data", out var dataEl) || dataEl.ValueKind != JsonValueKind.Object) return null;
            return ParseLenovoMachineInfo(dataEl);
        }

        private async Task<LenovoPackageInfo?> QueryLenovoPackageAsync(string mtm, CancellationToken cancellationToken)
        {
            string payload = JsonSerializer.Serialize(new { mtm });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await RomDownloadHttpClient.PostAsync(LenovoPackageQueryUrl, content, cancellationToken).ConfigureAwait(true);
            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(true);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (TryGetInt32(root, "code") != 200) return null;
            if (!root.TryGetProperty("data", out var dataEl)) return null;
            return PickLenovoPackageByMtm(dataEl, mtm);
        }

        private async Task<LenovoQueryResult> QueryLenovoPackageByInputAsync(string input, CancellationToken cancellationToken)
        {
            input = CleanLink(input).ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(input))
            {
                return new LenovoQueryResult { Success = false, Error = "请输入 8 位 SN 或 10 位 MTM" };
            }

            if (input.Length == 8)
            {
                var machine = await QueryLenovoMachineAsync(input, cancellationToken).ConfigureAwait(true);
                if (machine == null)
                {
                    return new LenovoQueryResult { Success = false, InputType = "SN", Error = "未找到机器信息" };
                }

                string mtm = CleanLink(machine.MTM).ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(mtm))
                {
                    return new LenovoQueryResult
                    {
                        Success = false,
                        InputType = "SN",
                        MachineInfo = machine,
                        Error = "已找到机器信息，但未返回 MTM"
                    };
                }

                var package = await QueryLenovoPackageAsync(mtm, cancellationToken).ConfigureAwait(true);
                if (package == null)
                {
                    return new LenovoQueryResult
                    {
                        Success = false,
                        InputType = "SN",
                        ResolvedMtm = mtm,
                        MachineInfo = machine,
                        Error = "未找到刷机包"
                    };
                }

                return new LenovoQueryResult
                {
                    Success = true,
                    InputType = "SN",
                    ResolvedMtm = mtm,
                    MachineInfo = machine,
                    FullPackage = package
                };
            }

            if (input.Length == 10)
            {
                var package = await QueryLenovoPackageAsync(input, cancellationToken).ConfigureAwait(true);
                if (package == null)
                {
                    return new LenovoQueryResult
                    {
                        Success = false,
                        InputType = "MTM",
                        ResolvedMtm = input,
                        Error = "未找到刷机包"
                    };
                }

                return new LenovoQueryResult
                {
                    Success = true,
                    InputType = "MTM",
                    ResolvedMtm = input,
                    FullPackage = package
                };
            }

            return new LenovoQueryResult { Success = false, Error = "只支持 8 位 SN 或 10 位 MTM" };
        }

        private static string BuildLenovoQuerySummary(LenovoQueryResult result)
        {
            var lines = new List<string>
            {
                $"输入类型：{(string.IsNullOrWhiteSpace(result.InputType) ? "未知" : result.InputType)}",
                $"解析 MTM：{(string.IsNullOrWhiteSpace(result.ResolvedMtm) ? "-" : result.ResolvedMtm)}"
            };

            if (!string.IsNullOrWhiteSpace(result.PresetModelName))
            {
                lines.Add($"预设机型：{result.PresetModelName}");
            }

            if (result.MachineInfo != null)
            {
                lines.Add($"机器名称：{(string.IsNullOrWhiteSpace(result.MachineInfo.MachineName) ? "-" : result.MachineInfo.MachineName)}");
                lines.Add($"序列号：{(string.IsNullOrWhiteSpace(result.MachineInfo.SN) ? "-" : result.MachineInfo.SN)}");
                lines.Add($"销售区域：{(string.IsNullOrWhiteSpace(result.MachineInfo.SaleArea) ? "-" : result.MachineInfo.SaleArea)}");
                lines.Add($"产品系列：{(string.IsNullOrWhiteSpace(result.MachineInfo.ProductSeries) ? "-" : result.MachineInfo.ProductSeries)}");
            }

            if (result.FullPackage != null)
            {
                lines.Add($"产品名称：{(string.IsNullOrWhiteSpace(result.FullPackage.ProductName) ? "-" : result.FullPackage.ProductName)}");
                lines.Add($"最新版本：{(string.IsNullOrWhiteSpace(result.FullPackage.LatestVersion) ? "-" : result.FullPackage.LatestVersion)}");
                lines.Add($"服务端文件名：{(string.IsNullOrWhiteSpace(result.FullPackage.ServerVersionName) ? "-" : result.FullPackage.ServerVersionName)}");
                lines.Add($"平台：{(string.IsNullOrWhiteSpace(result.FullPackage.Platform) ? "-" : result.FullPackage.Platform)}");
                lines.Add($"刷机方式：{(string.IsNullOrWhiteSpace(result.FullPackage.FlashingMethod) ? "-" : result.FullPackage.FlashingMethod)}");
                string displayUrl = string.IsNullOrWhiteSpace(result.FullPackage.DownloadUrl)
                    ? "-"
                    : $"`{result.FullPackage.DownloadUrl}`";
                lines.Add($"下载链接： {displayUrl}");
                lines.Add($"解压密码：{LenovoPackagePassword}");
            }

            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                lines.Add($"错误信息：{result.Error}");
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static string BuildSignedUrl(string path)
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var rand = GenerateRandomString(12);
            var uid = "0";
            var uriPath = path;

            var raw = uriPath + "-" + timestamp + "-" + rand + "-" + uid + "-" + C16DownloadCheckTokenSecret;
            string md5Hash;
            using (var md5 = MD5.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(raw);
                var hash = md5.ComputeHash(bytes);
                var sb = new StringBuilder();
                for (int i = 0; i < hash.Length; i++)
                {
                    sb.Append(hash[i].ToString("x2"));
                }
                md5Hash = sb.ToString();
            }

            var sign = timestamp + "-" + rand + "-" + uid + "-" + md5Hash;
            return $"{path}?{C16DownloadCheckTokenParamName}={sign}";
        }

        private static string GenerateRandomString(int length)
        {
            const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var bytes = new byte[length];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            var sb = new StringBuilder(length);
            for (int i = 0; i < length; i++)
            {
                sb.Append(chars[bytes[i] % chars.Length]);
            }

            return sb.ToString();
        }

        private static string ApplyViolettoolSignIfNeeded(string url)
        {
            try
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    return url;
                }

                if (!string.Equals(uri.Host, "violettool.top", StringComparison.OrdinalIgnoreCase))
                {
                    return url;
                }

                var signedPath = BuildSignedUrl(uri.AbsolutePath);
                return uri.Scheme + "://" + uri.Host + signedPath;
            }
            catch
            {
                return url;
            }
        }

        private static IEnumerable<JsonElement> EnumerateDeviceElements(JsonElement root)
        {
            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in root.EnumerateArray()) yield return e;
                yield break;
            }

            if (root.ValueKind != JsonValueKind.Object) yield break;

            var keys = new[] { "data", "devices", "list", "result", "rows" };
            foreach (var key in keys)
            {
                if (root.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var e in arr.EnumerateArray()) yield return e;
                    yield break;
                }
            }
        }

        private static IEnumerable<JsonElement> EnumerateItemElements(JsonElement deviceEl)
        {
            if (deviceEl.ValueKind != JsonValueKind.Object) yield break;

            var keys = new[] { "items", "versions", "roms", "list", "data", "packages", "builds", "files" };
            foreach (var key in keys)
            {
                if (deviceEl.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var e in arr.EnumerateArray()) yield return e;
                    yield break;
                }
            }
        }

        private static string ExtractRomItemDisplayName(JsonElement itemEl)
        {
            if (itemEl.ValueKind == JsonValueKind.String)
            {
                return CleanLink(itemEl.GetString());
            }

            var name = TryGetStringAny(
                itemEl,
                "name",
                "version",
                "title",
                "版本信息",
                "version_name",
                "versionName",
                "rom_version",
                "romVersion",
                "ota_version",
                "otaVersion",
                "display_version",
                "displayVersion",
                "build_version",
                "buildVersion",
                "package_name",
                "packageName",
                "filename",
                "file_name",
                "版本号",
                "版本名称");

            if (string.IsNullOrWhiteSpace(name) &&
                itemEl.ValueKind == JsonValueKind.Object &&
                itemEl.TryGetProperty("readme", out var readmeProp) &&
                readmeProp.ValueKind == JsonValueKind.Object)
            {
                name = TryGetStringAny(
                    readmeProp,
                    "name",
                    "version",
                    "title",
                    "版本信息",
                    "version_name",
                    "versionName",
                    "rom_version",
                    "romVersion",
                    "ota_version",
                    "otaVersion",
                    "display_version",
                    "displayVersion",
                    "build_version",
                    "buildVersion",
                    "package_name",
                    "packageName",
                    "filename",
                    "file_name",
                    "版本号",
                    "版本名称");
            }

            return CleanLink(name);
        }

        private static string ExtractRomItemHref(JsonElement itemEl)
        {
            if (itemEl.ValueKind != JsonValueKind.Object)
            {
                return string.Empty;
            }

            var href = CleanLink(TryGetStringAny(
                itemEl,
                "href",
                "url",
                "link",
                "download",
                "下载链接",
                "download_url",
                "downloadUrl",
                "download_link",
                "downloadLink",
                "file_url",
                "fileUrl",
                "path",
                "uri"));

            if (string.IsNullOrWhiteSpace(href) &&
                itemEl.TryGetProperty("readme", out var readmeProp) &&
                readmeProp.ValueKind == JsonValueKind.Object)
            {
                href = CleanLink(TryGetStringAny(
                    readmeProp,
                    "href",
                    "url",
                    "link",
                    "download",
                    "下载链接",
                    "download_url",
                    "downloadUrl",
                    "download_link",
                    "downloadLink",
                    "file_url",
                    "fileUrl",
                    "path",
                    "uri"));
            }

            return href;
        }

        private static bool IsRomSelectShouhouFlatEntry(JsonElement el)
        {
            if (el.ValueKind != JsonValueKind.Object) return false;
            return el.TryGetProperty("机型名称", out _) ||
                   el.TryGetProperty("版本信息", out _) ||
                   el.TryGetProperty("下载链接", out _);
        }

        private static Dictionary<string, RomDeviceConfig> ParseRomSelectShouhouFlat(JsonElement root)
        {
            var dict = new Dictionary<string, RomDeviceConfig>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in EnumerateDeviceElements(root))
            {
                if (entry.ValueKind != JsonValueKind.Object) continue;

                var device = CleanLink(TryGetStringAny(entry, "机型名称", "机型", "device", "model", "name") ?? string.Empty);
                if (string.IsNullOrWhiteSpace(device)) continue;

                var versionInfo = CleanLink(TryGetStringAny(entry, "版本信息", "版本", "version", "title", "name") ?? string.Empty);
                var patch = CleanLink(TryGetStringAny(entry, "补丁", "patch") ?? string.Empty);
                var size = CleanLink(TryGetStringAny(entry, "大小", "size") ?? string.Empty);
                var link = CleanLink(TryGetStringAny(entry, "下载链接", "链接", "url", "href", "link", "download") ?? string.Empty);

                string display = versionInfo;
                if (!string.IsNullOrWhiteSpace(patch))
                {
                    display = string.IsNullOrWhiteSpace(display) ? $"补丁 {patch}" : $"{display} · 补丁 {patch}";
                }
                if (!string.IsNullOrWhiteSpace(size))
                {
                    display = string.IsNullOrWhiteSpace(display) ? size : $"{display} · {size}";
                }
                if (string.IsNullOrWhiteSpace(display))
                {
                    display = link;
                }
                if (string.IsNullOrWhiteSpace(display)) continue;

                if (!dict.TryGetValue(device, out var cfg))
                {
                    cfg = new RomDeviceConfig { Device = device, Url = string.Empty };
                    dict[device] = cfg;
                }

                var downloadLinks = new List<string>();
                if (!string.IsNullOrWhiteSpace(link)) downloadLinks.Add(link);

                cfg.Items.Add(new RomItemConfig
                {
                    Name = display,
                    Href = link,
                    DownloadLinks = downloadLinks
                });
            }

            return dict;
        }

        private static IEnumerable<JsonElement> EnumerateXiaomiRomDeviceElements(JsonElement root)
        {
            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in root.EnumerateArray()) yield return e;
                yield break;
            }

            if (root.ValueKind != JsonValueKind.Object) yield break;

            if (root.TryGetProperty("devices", out var devices) && devices.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in devices.EnumerateArray()) yield return e;
                yield break;
            }

            yield return root;
        }

        private static bool LooksLikeXiaomiRomConfig(JsonElement root)
        {
            foreach (var el in EnumerateXiaomiRomDeviceElements(root).Take(3))
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                if (el.TryGetProperty("device_name", out _) &&
                    el.TryGetProperty("roms", out var roms) &&
                    roms.ValueKind == JsonValueKind.Array)
                {
                    return true;
                }
            }
            return false;
        }

        private static string ExtractXiaomiRomPreferredDownloadUrl(JsonElement fileEl)
        {
            if (fileEl.ValueKind != JsonValueKind.Object) return string.Empty;

            if (!fileEl.TryGetProperty("download_url", out var dlEl)) return string.Empty;

            if (dlEl.ValueKind == JsonValueKind.Array)
            {
                var urls = new List<string>();
                foreach (var u in dlEl.EnumerateArray())
                {
                    if (u.ValueKind != JsonValueKind.String) continue;
                    var s = CleanLink(u.GetString());
                    if (!string.IsNullOrWhiteSpace(s)) urls.Add(s);
                }

                if (urls.Count >= 3) return urls[2];
                return urls.Count > 0 ? urls[urls.Count - 1] : string.Empty;
            }

            if (dlEl.ValueKind == JsonValueKind.String)
            {
                return CleanLink(dlEl.GetString());
            }

            return string.Empty;
        }

        private static string NormalizeXiaomiRomDeviceName(string name)
        {
            name = CleanLink(name);
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;

            var suffixes = new[]
            {
                "官方 ROM 下载",
                "官方ROM下载",
                "ROM 下载",
                "ROM下载"
            };

            foreach (var s in suffixes)
            {
                if (name.EndsWith(s, StringComparison.OrdinalIgnoreCase))
                {
                    name = name.Substring(0, name.Length - s.Length).Trim();
                    break;
                }
            }

            return name;
        }

        private static Dictionary<string, RomDeviceConfig> ParseXiaomiRomRecoveryConfig(JsonElement root)
        {
            var dict = new Dictionary<string, RomDeviceConfig>(StringComparer.OrdinalIgnoreCase);

            foreach (var deviceEl in EnumerateXiaomiRomDeviceElements(root))
            {
                if (deviceEl.ValueKind != JsonValueKind.Object) continue;

                var deviceName = NormalizeXiaomiRomDeviceName(TryGetStringAny(deviceEl, "device_name", "device", "name", "model") ?? string.Empty);
                if (string.IsNullOrWhiteSpace(deviceName)) continue;

                var deviceUrl = CleanLink(TryGetStringAny(deviceEl, "device_url", "url", "href", "link") ?? string.Empty);
                var cfg = new RomDeviceConfig
                {
                    Device = deviceName.Trim(),
                    Url = deviceUrl
                };

                if (deviceEl.TryGetProperty("roms", out var romsEl) && romsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var romEl in romsEl.EnumerateArray())
                    {
                        if (romEl.ValueKind != JsonValueKind.Object) continue;
                        if (!romEl.TryGetProperty("downloads", out var downloadsEl) || downloadsEl.ValueKind != JsonValueKind.Object) continue;
                        if (!downloadsEl.TryGetProperty("recovery_roms", out var recoveryEl) || recoveryEl.ValueKind != JsonValueKind.Array) continue;

                        foreach (var fileEl in recoveryEl.EnumerateArray())
                        {
                            if (fileEl.ValueKind != JsonValueKind.Object) continue;

                            var fileType = CleanLink(TryGetStringAny(fileEl, "file_type", "type") ?? string.Empty);
                            if (!string.Equals(fileType, "zip", StringComparison.OrdinalIgnoreCase)) continue;

                            var version = CleanLink(TryGetStringAny(fileEl, "version", "name", "版本信息", "版本") ?? string.Empty);
                            if (string.IsNullOrWhiteSpace(version)) continue;

                            var href = CleanLink(TryGetStringAny(fileEl, "download_page", "page", "url", "href", "link") ?? string.Empty);
                            var selectedUrl = ExtractXiaomiRomPreferredDownloadUrl(fileEl);

                            var downloadLinks = new List<string>();
                            if (!string.IsNullOrWhiteSpace(selectedUrl)) downloadLinks.Add(selectedUrl);

                            cfg.Items.Add(new RomItemConfig
                            {
                                Name = version.Trim(),
                                Href = href,
                                DownloadLinks = downloadLinks
                            });
                        }
                    }
                }

                cfg.Items = cfg.Items
                    .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();

                dict[cfg.Device] = cfg;
            }

            return dict;
        }

        private static Dictionary<string, RomDeviceConfig> ParseXiaomiRomFastbootConfig(JsonElement root)
        {
            var dict = new Dictionary<string, RomDeviceConfig>(StringComparer.OrdinalIgnoreCase);

            foreach (var deviceEl in EnumerateXiaomiRomDeviceElements(root))
            {
                if (deviceEl.ValueKind != JsonValueKind.Object) continue;

                var deviceName = NormalizeXiaomiRomDeviceName(TryGetStringAny(deviceEl, "device_name", "device", "name", "model") ?? string.Empty);
                if (string.IsNullOrWhiteSpace(deviceName)) continue;

                var deviceUrl = CleanLink(TryGetStringAny(deviceEl, "device_url", "url", "href", "link") ?? string.Empty);
                var cfg = new RomDeviceConfig
                {
                    Device = deviceName.Trim(),
                    Url = deviceUrl
                };

                if (deviceEl.TryGetProperty("roms", out var romsEl) && romsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var romEl in romsEl.EnumerateArray())
                    {
                        if (romEl.ValueKind != JsonValueKind.Object) continue;
                        if (!romEl.TryGetProperty("downloads", out var downloadsEl) || downloadsEl.ValueKind != JsonValueKind.Object) continue;
                        if (!downloadsEl.TryGetProperty("fastboot_roms", out var fastbootEl) || fastbootEl.ValueKind != JsonValueKind.Array) continue;

                        foreach (var fileEl in fastbootEl.EnumerateArray())
                        {
                            if (fileEl.ValueKind != JsonValueKind.Object) continue;

                            var fileType = CleanLink(TryGetStringAny(fileEl, "file_type", "type") ?? string.Empty);
                            if (!string.Equals(fileType, "tgz", StringComparison.OrdinalIgnoreCase)) continue;

                            var version = CleanLink(TryGetStringAny(fileEl, "version", "name", "filename", "版本信息", "版本") ?? string.Empty);
                            if (string.IsNullOrWhiteSpace(version)) continue;

                            var href = CleanLink(TryGetStringAny(fileEl, "download_page", "page", "url", "href", "link") ?? string.Empty);
                            var selectedUrl = ExtractXiaomiRomPreferredDownloadUrl(fileEl);

                            var downloadLinks = new List<string>();
                            if (!string.IsNullOrWhiteSpace(selectedUrl)) downloadLinks.Add(selectedUrl);

                            cfg.Items.Add(new RomItemConfig
                            {
                                Name = version.Trim(),
                                Href = href,
                                DownloadLinks = downloadLinks
                            });
                        }
                    }
                }

                cfg.Items = cfg.Items
                    .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();

                dict[cfg.Device] = cfg;
            }

            return dict;
        }

        private void ClearRomDeviceAndVersion()
        {
            bool wasUpdating = _romDownloadUiUpdating;
            _romDownloadUiUpdating = true;
            try
            {
                RomDeviceComboBox?.Items.Clear();
                RomVersionComboBox?.Items.Clear();
            }
            finally
            {
                _romDownloadUiUpdating = wasUpdating;
            }
            _romDownloadLinksByVersionName.Clear();
            _romDownloadItemsByVersionName.Clear();
            _selectedRomDownloadLinks.Clear();
            _selectedRomDownloadHeaders.Clear();
        }

        private static string NormalizeUrlForRequest(string url)
        {
            url = CleanLink(url);
            if (string.IsNullOrWhiteSpace(url)) return string.Empty;

            string normalized;

            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                normalized = uri.ToString();
            }
            else
            {
                var schemeSepIndex = url.IndexOf("://", StringComparison.Ordinal);
                if (schemeSepIndex < 0) return url;

                var pathStart = url.IndexOf('/', schemeSepIndex + 3);
                if (pathStart < 0) return url;

                var basePart = url.Substring(0, pathStart);
                var rest = url.Substring(pathStart);

                string pathPart;
                string queryPart = string.Empty;
                var queryIndex = rest.IndexOf('?', StringComparison.Ordinal);
                if (queryIndex >= 0)
                {
                    pathPart = rest.Substring(0, queryIndex);
                    queryPart = rest.Substring(queryIndex);
                }
                else
                {
                    pathPart = rest;
                }

                var escapedPath = string.Join("/",
                    pathPart.Split(new[] { '/' }, StringSplitOptions.None)
                        .Select(seg => Uri.EscapeDataString(seg)));

                normalized = basePart + escapedPath + queryPart;
            }

            return ApplyViolettoolSignIfNeeded(normalized);
        }

        private static string MakeRomDownloadSeriesKey(string brand, string series) => $"{brand}|{series}";

        private static bool TryGetRomDownloadSeriesUrl(string brand, string series, out string url)
        {
            url = string.Empty;
            if (UseRomBackendApi)
            {
                url = "backend";
                return !string.IsNullOrWhiteSpace(brand) && !string.IsNullOrWhiteSpace(series);
            }
            if (string.Equals(brand, "魅族", StringComparison.OrdinalIgnoreCase))
            {
                if (MeizuCompactSeriesCodeMap.ContainsKey(series))
                {
                    url = MeizuCompactIndexUrl;
                    return true;
                }
                return false;
            }

            return RomSelectFullPackageSeriesUrlMap.TryGetValue(brand, out var seriesMap) &&
                   seriesMap is not null &&
                   seriesMap.TryGetValue(series, out url) &&
                   !string.IsNullOrWhiteSpace(url);
        }

        private static IEnumerable<string> GetRomDownloadSeriesNames(string brand)
        {
            if (string.Equals(brand, "魅族", StringComparison.OrdinalIgnoreCase))
            {
                return MeizuCompactSeriesCodeMap.Keys;
            }

            if (RomSelectFullPackageSeriesUrlMap.TryGetValue(brand, out var seriesMap) && seriesMap is not null)
            {
                return seriesMap.Keys;
            }

            return Array.Empty<string>();
        }

        private static bool TryGetRomSelectSeriesUrl(string packageType, string brand, string series, out string url)
        {
            url = string.Empty;
            if (UseRomBackendApi)
            {
                url = "backend";
                return !string.IsNullOrWhiteSpace(packageType) &&
                       !string.IsNullOrWhiteSpace(brand) &&
                       !string.IsNullOrWhiteSpace(series);
            }
            if (!string.Equals(packageType, RomSelectPackageTypeAfterSales, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(brand, "魅族", StringComparison.OrdinalIgnoreCase))
            {
                if (MeizuCompactSeriesCodeMap.ContainsKey(series))
                {
                    url = MeizuCompactIndexUrl;
                    return true;
                }
                return false;
            }

            var map = string.Equals(packageType, RomSelectPackageTypeAfterSales, StringComparison.OrdinalIgnoreCase)
                ? RomSelectAfterSalesSeriesUrlMap
                : RomSelectFullPackageSeriesUrlMap;

            return map.TryGetValue(brand, out var seriesMap) &&
                   seriesMap is not null &&
                   seriesMap.TryGetValue(series, out url) &&
                   !string.IsNullOrWhiteSpace(url);
        }

        private static IEnumerable<string> GetRomSelectSeriesNames(string packageType, string brand)
        {
            if (!string.Equals(packageType, RomSelectPackageTypeAfterSales, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(brand, "魅族", StringComparison.OrdinalIgnoreCase))
            {
                return MeizuCompactSeriesCodeMap.Keys;
            }

            var map = string.Equals(packageType, RomSelectPackageTypeAfterSales, StringComparison.OrdinalIgnoreCase)
                ? RomSelectAfterSalesSeriesUrlMap
                : RomSelectFullPackageSeriesUrlMap;

            if (map.TryGetValue(brand, out var seriesMap) && seriesMap is not null)
            {
                return seriesMap.Keys;
            }

            return Array.Empty<string>();
        }

        private static bool IsMeizuCompactIndexConfig(string brand, JsonElement root)
        {
            if (!string.Equals(brand, "魅族", StringComparison.OrdinalIgnoreCase)) return false;
            return root.ValueKind == JsonValueKind.Object &&
                   root.TryGetProperty("brand", out var brandEl) &&
                   string.Equals(brandEl.GetString(), "meizu", StringComparison.OrdinalIgnoreCase) &&
                   root.TryGetProperty("series", out var seriesEl) &&
                   seriesEl.ValueKind == JsonValueKind.Array;
        }

        private static string BuildAbsoluteUrl(string baseUrl, string relativeOrAbsolute)
        {
            relativeOrAbsolute = CleanLink(relativeOrAbsolute);
            if (string.IsNullOrWhiteSpace(relativeOrAbsolute)) return string.Empty;
            if (Uri.TryCreate(relativeOrAbsolute, UriKind.Absolute, out var absoluteUri)) return absoluteUri.ToString();
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)) return relativeOrAbsolute;
            return new Uri(baseUri, relativeOrAbsolute).ToString();
        }

        private static string BuildMeizuVersionDisplayName(JsonElement versionEl)
        {
            string name = CleanLink(TryGetStringAny(versionEl, "name", "version", "title") ?? string.Empty);
            string date = CleanLink(TryGetStringAny(versionEl, "date", "time") ?? string.Empty);
            string password = CleanLink(TryGetStringAny(versionEl, "password", "pwd") ?? string.Empty);

            string display = name;
            if (!string.IsNullOrWhiteSpace(date))
            {
                display = string.IsNullOrWhiteSpace(display) ? date : $"{display} · {date}";
            }
            if (!string.IsNullOrWhiteSpace(password))
            {
                display = string.IsNullOrWhiteSpace(display) ? $"密码 {password}" : $"{display} · 密码 {password}";
            }

            return display;
        }

        private static Dictionary<string, string> BuildMeizuRequestHeaders(string referer)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            referer = CleanLink(referer);
            if (!string.IsNullOrWhiteSpace(referer))
            {
                headers["Referer"] = referer;
            }

            headers["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) SmartTool";
            return headers;
        }

        private async Task<RomDeviceConfig?> LoadMeizuCompactModelConfigAsync(string deviceName, string modelUrl, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(modelUrl)) return null;

            string modelJson = await RomDownloadHttpClient.GetStringAsync(NormalizeUrlForRequest(modelUrl), cancellationToken);
            using var modelDoc = JsonDocument.Parse(modelJson);
            var root = modelDoc.RootElement;

            string parsedDeviceName = CleanLink(TryGetStringAny(root, "model", "name", "device") ?? deviceName);
            if (string.IsNullOrWhiteSpace(parsedDeviceName)) parsedDeviceName = deviceName;
            if (string.IsNullOrWhiteSpace(parsedDeviceName)) return null;

            var cfg = new RomDeviceConfig
            {
                Device = parsedDeviceName.Trim(),
                Url = modelUrl
            };

            if (root.TryGetProperty("versions", out var versionsEl) && versionsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var versionEl in versionsEl.EnumerateArray())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (versionEl.ValueKind != JsonValueKind.Object) continue;

                    string display = BuildMeizuVersionDisplayName(versionEl);
                    string downloadEntry = CleanLink(TryGetStringAny(versionEl, "download_entry", "downloadEntry") ?? string.Empty);
                    string directUrl = CleanLink(TryGetStringAny(versionEl, "url", "href", "link", "download") ?? string.Empty);
                    string referer = CleanLink(TryGetStringAny(versionEl, "referer", "referrer", "firmware_page", "firmwarePage", "page") ?? string.Empty);
                    if (string.IsNullOrWhiteSpace(display)) continue;

                    var downloadLinks = new List<string>();
                    if (!string.IsNullOrWhiteSpace(downloadEntry))
                    {
                        downloadLinks.Add(downloadEntry);
                    }
                    if (!string.IsNullOrWhiteSpace(directUrl) &&
                        !downloadLinks.Contains(directUrl, StringComparer.OrdinalIgnoreCase))
                    {
                        downloadLinks.Add(directUrl);
                    }

                    var requestHeaders = BuildMeizuRequestHeaders(referer);

                    cfg.Items.Add(new RomItemConfig
                    {
                        Name = display,
                        Href = !string.IsNullOrWhiteSpace(downloadEntry) ? downloadEntry : directUrl,
                        DownloadLinks = downloadLinks,
                        RequestHeaders = requestHeaders
                    });
                }
            }

            return cfg;
        }

        private async Task<Dictionary<string, RomDeviceConfig>> ParseMeizuCompactSeriesAsync(string series, string indexUrl, JsonElement root, CancellationToken cancellationToken)
        {
            var dict = new Dictionary<string, RomDeviceConfig>(StringComparer.OrdinalIgnoreCase);
            if (!root.TryGetProperty("series", out var seriesEl) || seriesEl.ValueKind != JsonValueKind.Array) return dict;
            if (!MeizuCompactSeriesCodeMap.TryGetValue(series, out var targetSeriesCode) || string.IsNullOrWhiteSpace(targetSeriesCode)) return dict;

            foreach (var seriesItem in seriesEl.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (seriesItem.ValueKind != JsonValueKind.Object) continue;

                string seriesCode = CleanLink(TryGetStringAny(seriesItem, "code") ?? string.Empty);
                if (!string.Equals(seriesCode, targetSeriesCode, StringComparison.OrdinalIgnoreCase)) continue;

                if (!seriesItem.TryGetProperty("models", out var modelsEl) || modelsEl.ValueKind != JsonValueKind.Array) break;

                foreach (var modelEl in modelsEl.EnumerateArray())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (modelEl.ValueKind != JsonValueKind.Object) continue;

                    string deviceName = CleanLink(TryGetStringAny(modelEl, "name", "model", "device") ?? string.Empty);
                    string modelFile = CleanLink(TryGetStringAny(modelEl, "file", "url", "href", "link") ?? string.Empty);
                    if (string.IsNullOrWhiteSpace(deviceName) || string.IsNullOrWhiteSpace(modelFile)) continue;

                    string modelUrl = BuildAbsoluteUrl(indexUrl, modelFile);
                    var cfg = await LoadMeizuCompactModelConfigAsync(deviceName, modelUrl, cancellationToken);
                    if (cfg == null || string.IsNullOrWhiteSpace(cfg.Device)) continue;

                    dict[cfg.Device] = cfg;
                }

                break;
            }

            return dict;
        }

        private async Task EnsureRomDownloadConfigLoadedAsync(string brand, string series, string url, CancellationToken cancellationToken)
        {
            var key = MakeRomDownloadSeriesKey(brand, series);
            if (_romDownloadDeviceConfigsBySeriesKey.TryGetValue(key, out var existing) && existing != null)
            {
                AddC16DevicesToDeviceConfigs(brand, series, existing);
                if (existing.Count > 0) return;
            }

            if (UseRomBackendApi)
            {
                var devices = await FetchRomApiDevicesAsync(RomApiPackageTypeFull, brand, series, cancellationToken).ConfigureAwait(true);
                var dictFromApi = devices
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        x => x,
                        x => new RomDeviceConfig { Device = x },
                        StringComparer.OrdinalIgnoreCase);

                AddC16DevicesToDeviceConfigs(brand, series, dictFromApi);
                _romDownloadDeviceConfigsBySeriesKey[key] = dictFromApi;
                return;
            }

            await RomConfigLoadSemaphore.WaitAsync(cancellationToken);
            try
            {
                if (_romDownloadDeviceConfigsBySeriesKey.TryGetValue(key, out existing) && existing != null)
                {
                    AddC16DevicesToDeviceConfigs(brand, series, existing);
                    if (existing.Count > 0) return;
                }

                string json;
                try
                {
                    json = await RomDownloadHttpClient.GetStringAsync(NormalizeUrlForRequest(url), cancellationToken);
                }
                catch
                {
                    if (_enableC16Dynamic && IsC16Brand(brand))
                    {
                        var fallback = new Dictionary<string, RomDeviceConfig>(StringComparer.OrdinalIgnoreCase);
                        AddC16DevicesToDeviceConfigs(brand, series, fallback);
                        _romDownloadDeviceConfigsBySeriesKey[key] = fallback;
                        return;
                    }
                    throw;
                }
                using var doc = JsonDocument.Parse(json);

                Dictionary<string, RomDeviceConfig> dict;
                if (IsMeizuCompactIndexConfig(brand, doc.RootElement))
                {
                    dict = await ParseMeizuCompactSeriesAsync(series, url, doc.RootElement, cancellationToken);
                }
                else if (LooksLikeXiaomiRomConfig(doc.RootElement))
                {
                    dict = ParseXiaomiRomRecoveryConfig(doc.RootElement);
                }
                else
                {
                    dict = new Dictionary<string, RomDeviceConfig>(StringComparer.OrdinalIgnoreCase);
                    foreach (var deviceEl in EnumerateDeviceElements(doc.RootElement))
                    {
                        string deviceName = CleanLink(TryGetStringAny(deviceEl, "device", "model", "name", "device_name", "phone", "机型名称", "机型") ?? string.Empty);
                        if (string.IsNullOrWhiteSpace(deviceName)) continue;

                        var deviceUrl = CleanLink(TryGetStringAny(deviceEl, "url", "href", "link"));
                        var cfg = new RomDeviceConfig
                        {
                            Device = deviceName.Trim(),
                            Url = deviceUrl
                        };

                        foreach (var itemEl in EnumerateItemElements(deviceEl))
                        {
                            string name = ExtractRomItemDisplayName(itemEl);
                            string href = ExtractRomItemHref(itemEl);
                            if (string.IsNullOrWhiteSpace(name)) continue;

                            var downloadLinks = new List<string>();

                            if (itemEl.ValueKind == JsonValueKind.Object)
                            {
                                if (itemEl.TryGetProperty("download_links", out var dlProp) && dlProp.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var dl in dlProp.EnumerateArray())
                                    {
                                        string link = dl.ValueKind == JsonValueKind.String
                                            ? CleanLink(dl.GetString())
                                            : CleanLink(TryGetStringAny(dl, "url", "href", "link"));
                                        if (!string.IsNullOrWhiteSpace(link)) downloadLinks.Add(link);
                                    }
                                }

                                if (downloadLinks.Count == 0 &&
                                    itemEl.TryGetProperty("readme", out var readmeProp) &&
                                    readmeProp.ValueKind == JsonValueKind.Object &&
                                    readmeProp.TryGetProperty("download_links", out var dl2Prop) &&
                                    dl2Prop.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var dl in dl2Prop.EnumerateArray())
                                    {
                                        var link = CleanLink(dl.GetString());
                                        if (!string.IsNullOrWhiteSpace(link)) downloadLinks.Add(link);
                                    }
                                }
                            }

                            if (downloadLinks.Count == 0 && !string.IsNullOrWhiteSpace(href))
                            {
                                downloadLinks.Add(href);
                            }

                            cfg.Items.Add(new RomItemConfig
                            {
                                Name = name.Trim(),
                                Href = href,
                                DownloadLinks = downloadLinks
                            });
                        }

                        dict[cfg.Device] = cfg;
                    }
                }

                AddC16DevicesToDeviceConfigs(brand, series, dict);

                _romDownloadDeviceConfigsBySeriesKey[key] = dict;
            }
            finally
            {
                RomConfigLoadSemaphore.Release();
            }
        }

        private void ClearRomSelectDeviceAndVersion()
        {
            bool wasUpdating = _romSelectUiUpdating;
            _romSelectUiUpdating = true;
            try
            {
                RomSelectDeviceComboBox?.Items.Clear();
                RomSelectVersionComboBox?.Items.Clear();
            }
            finally
            {
                _romSelectUiUpdating = wasUpdating;
            }
            _romSelectLinksByVersionName.Clear();
            _romSelectItemsByVersionName.Clear();
        }

        private static string? GetSelectedComboBoxItemText(System.Windows.Controls.ComboBox? comboBox)
        {
            return (comboBox?.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString();
        }

        private string GetSelectedRomSelectPackageType()
        {
            var t = GetSelectedComboBoxItemText(RomSelectPackageTypeComboBox);
            if (string.IsNullOrWhiteSpace(t)) return RomSelectPackageTypeFull;
            return t;
        }

        private static string MakeRomSelectSeriesKey(string packageType, string brand, string series) => $"{packageType}|{brand}|{series}";

        private async Task EnsureRomSelectConfigLoadedAsync(string packageType, string brand, string series, string url, CancellationToken cancellationToken)
        {
            var key = MakeRomSelectSeriesKey(packageType, brand, series);

            if (UseRomBackendApi)
            {
                if (_romSelectDeviceConfigsBySeriesKey.TryGetValue(key, out var existingFromApi) &&
                    existingFromApi != null &&
                    existingFromApi.Count > 0)
                {
                    return;
                }

                var apiPackageType = ToRomApiPackageType(packageType);
                var devices = await FetchRomApiDevicesAsync(apiPackageType, brand, series, cancellationToken).ConfigureAwait(true);
                _romSelectDeviceConfigsBySeriesKey[key] = devices
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        x => x,
                        x => new RomDeviceConfig { Device = x },
                        StringComparer.OrdinalIgnoreCase);
                return;
            }

            if (!string.Equals(packageType, RomSelectPackageTypeAfterSales, StringComparison.OrdinalIgnoreCase))
            {
                if (_romSelectDeviceConfigsBySeriesKey.TryGetValue(key, out var existingFull) && existingFull != null && existingFull.Count > 0)
                {
                    return;
                }

                await EnsureRomDownloadConfigLoadedAsync(brand, series, url, cancellationToken);

                var downloadKey = MakeRomDownloadSeriesKey(brand, series);
                if (_romDownloadDeviceConfigsBySeriesKey.TryGetValue(downloadKey, out var downloadDict) && downloadDict != null && downloadDict.Count > 0)
                {
                    _romSelectDeviceConfigsBySeriesKey[key] = downloadDict;
                }
                return;
            }

            if (_romSelectDeviceConfigsBySeriesKey.TryGetValue(key, out var existing) && existing != null && existing.Count > 0)
            {
                return;
            }

            await RomConfigLoadSemaphore.WaitAsync(cancellationToken);
            try
            {
                if (_romSelectDeviceConfigsBySeriesKey.TryGetValue(key, out existing) && existing != null && existing.Count > 0)
                {
                    return;
                }

                string json;
                try
                {
                    json = await RomDownloadHttpClient.GetStringAsync(NormalizeUrlForRequest(url), cancellationToken);
                }
                catch
                {
                    throw;
                }
                using var doc = JsonDocument.Parse(json);

                if (LooksLikeXiaomiRomConfig(doc.RootElement))
                {
                    _romSelectDeviceConfigsBySeriesKey[key] = ParseXiaomiRomFastbootConfig(doc.RootElement);
                    return;
                }

                bool isShouhouFlat = EnumerateDeviceElements(doc.RootElement).Take(8).Any(IsRomSelectShouhouFlatEntry);
                var dict = isShouhouFlat
                    ? ParseRomSelectShouhouFlat(doc.RootElement)
                    : new Dictionary<string, RomDeviceConfig>(StringComparer.OrdinalIgnoreCase);

                if (!isShouhouFlat)
                {
                    foreach (var deviceEl in EnumerateDeviceElements(doc.RootElement))
                    {
                        string deviceName = CleanLink(TryGetStringAny(deviceEl, "device", "model", "name", "device_name", "phone", "机型名称", "机型") ?? string.Empty);
                        if (string.IsNullOrWhiteSpace(deviceName)) continue;

                        var deviceUrl = CleanLink(TryGetStringAny(deviceEl, "url", "href", "link"));
                        var cfg = new RomDeviceConfig
                        {
                            Device = deviceName.Trim(),
                            Url = deviceUrl
                        };

                        foreach (var itemEl in EnumerateItemElements(deviceEl))
                        {
                            string name = ExtractRomItemDisplayName(itemEl);
                            string href = ExtractRomItemHref(itemEl);
                            if (string.IsNullOrWhiteSpace(name)) continue;

                            var downloadLinks = new List<string>();

                            if (itemEl.ValueKind == JsonValueKind.Object)
                            {
                                if (itemEl.TryGetProperty("download_links", out var dlProp) && dlProp.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var dl in dlProp.EnumerateArray())
                                    {
                                        string link = dl.ValueKind == JsonValueKind.String
                                            ? CleanLink(dl.GetString())
                                            : CleanLink(TryGetStringAny(dl, "url", "href", "link"));
                                        if (!string.IsNullOrWhiteSpace(link)) downloadLinks.Add(link);
                                    }
                                }

                                if (downloadLinks.Count == 0 &&
                                    itemEl.TryGetProperty("readme", out var readmeProp) &&
                                    readmeProp.ValueKind == JsonValueKind.Object &&
                                    readmeProp.TryGetProperty("download_links", out var dl2Prop) &&
                                    dl2Prop.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var dl in dl2Prop.EnumerateArray())
                                    {
                                        var link = CleanLink(dl.GetString());
                                        if (!string.IsNullOrWhiteSpace(link)) downloadLinks.Add(link);
                                    }
                                }
                            }

                            if (downloadLinks.Count == 0 && !string.IsNullOrWhiteSpace(href))
                            {
                                downloadLinks.Add(href);
                            }

                            cfg.Items.Add(new RomItemConfig
                            {
                                Name = name.Trim(),
                                Href = href,
                                DownloadLinks = downloadLinks
                            });
                        }

                        dict[cfg.Device] = cfg;
                    }
                }

                _romSelectDeviceConfigsBySeriesKey[key] = dict;
            }
            finally
            {
                RomConfigLoadSemaphore.Release();
            }
        }

        private void ClearRomSelectAllOptions()
        {
            bool wasUpdating = _romSelectUiUpdating;
            _romSelectUiUpdating = true;
            try
            {
                RomSelectSeriesComboBox?.Items.Clear();
                RomSelectDeviceComboBox?.Items.Clear();
                RomSelectVersionComboBox?.Items.Clear();
            }
            finally
            {
                _romSelectUiUpdating = wasUpdating;
            }
            _romSelectLinksByVersionName.Clear();
            _romSelectItemsByVersionName.Clear();
        }

        private async Task LoadRomSelectSeriesAsync(string packageType, string brand, string series, CancellationToken cancellationToken)
        {
            if (!TryGetRomSelectSeriesUrl(packageType, brand, series, out var url))
            {
                return;
            }

            if (!UseRomBackendApi && _enableC16Dynamic && IsC16Brand(brand))
            {
                try
                {
                    await EnsureC16RecordsLoadedAsync(cancellationToken);
                }
                catch
                {
                }
            }

            AppendRomDownloadLog("信息", $"已选择{series}");

            try
            {
                await EnsureRomSelectConfigLoadedAsync(packageType, brand, series, url, cancellationToken).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                ClearRomSelectDeviceAndVersion();
                AppendRomDownloadLog("错误", $"已选择{series}，加载失败：{ex.Message}");
                AddLogMessage("错误", $"ROM获取加载失败: {ex.Message}");
                return;
            }

            _activeRomSelectPackageType = packageType;
            _activeRomSelectBrand = brand;
            _activeRomSelectSeries = series;

            var key = MakeRomSelectSeriesKey(packageType, brand, series);
            if (_romSelectDeviceConfigsBySeriesKey.TryGetValue(key, out var dict) && dict != null)
            {
                AppendRomDownloadLog("成功", $"在{series}中成功加载{dict.Count}个机型");

                var orderedDevices = dict.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
                PopulateRomSelectDeviceComboBox(orderedDevices);

                var firstDevice = orderedDevices.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(firstDevice))
                {
                    await LoadRomSelectDeviceVersionsAsync(firstDevice).ConfigureAwait(true);
                }
            }
        }

        private void PopulateRomSelectDeviceComboBox(IEnumerable<string> devices)
        {
            if (RomSelectDeviceComboBox == null) return;

            bool wasUpdating = _romSelectUiUpdating;
            _romSelectUiUpdating = true;
            try
            {
                RomSelectDeviceComboBox.Items.Clear();
                foreach (var d in devices)
                {
                    RomSelectDeviceComboBox.Items.Add(new ComboBoxItem { Content = d });
                }

                if (RomSelectDeviceComboBox.Items.Count > 0)
                {
                    RomSelectDeviceComboBox.SelectedIndex = 0;
                }
            }
            finally
            {
                _romSelectUiUpdating = wasUpdating;
            }
        }

        private void PopulateRomSelectVersionComboBox(RomDeviceConfig deviceConfig)
        {
            if (RomSelectVersionComboBox == null) return;

            bool wasUpdating = _romSelectUiUpdating;
            _romSelectUiUpdating = true;
            try
            {
                RomSelectVersionComboBox.Items.Clear();
                _romSelectLinksByVersionName.Clear();
                _romSelectItemsByVersionName.Clear();
                foreach (var item in deviceConfig.Items)
                {
                    var name = (item.Name ?? string.Empty).Trim();
                    if (!IsValidVersionDisplayName(name)) continue;
                    RomSelectVersionComboBox.Items.Add(new ComboBoxItem { Content = name });
                    _romSelectLinksByVersionName[name] = item.DownloadLinks ?? new List<string>();
                    _romSelectItemsByVersionName[name] = item;
                }

                AppendC16VersionsForRomSelect(deviceConfig);
                SortRomVersionComboBoxAscending(RomSelectVersionComboBox);

                if (RomSelectVersionComboBox.Items.Count > 0)
                {
                    RomSelectVersionComboBox.SelectedIndex = 0;
                }
            }
            finally
            {
                _romSelectUiUpdating = wasUpdating;
            }
        }

        private static bool IsValidVersionDisplayName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            var text = value.Trim();

            // 过滤异常脏数据（如 "5"、"1234567"），避免出现在版本下拉框
            if (Regex.IsMatch(text, @"^\d+$")) return false;

            return true;
        }

        private static int CompareRomVersionDisplayText(string? leftText, string? rightText)
        {
            var left = ExtractRomSemanticVersion(leftText);
            var right = ExtractRomSemanticVersion(rightText);

            if (left != null && right != null)
            {
                var componentCount = Math.Max(left.Length, right.Length);
                for (var i = 0; i < componentCount; i++)
                {
                    var leftValue = i < left.Length ? left[i] : 0;
                    var rightValue = i < right.Length ? right[i] : 0;
                    var comparison = leftValue.CompareTo(rightValue);
                    if (comparison != 0) return comparison;
                }

                // 相同语义版本保持数据源原有顺序，例如企业版与稳定版不互相打乱。
                return 0;
            }

            if (left != null) return -1;
            if (right != null) return 1;
            return 0;
        }

        private static int[]? ExtractRomSemanticVersion(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            int[]? bestMatch = null;
            foreach (Match match in RomSemanticVersionRegex.Matches(text))
            {
                var parts = match.Value
                    .Split('.')
                    .Select(part => int.TryParse(part, out var value) ? value : 0)
                    .ToArray();

                // “ColorOS 15.0.0 15.0.0.705”这类文本优先采用组件更多的完整版本号。
                if (bestMatch == null || parts.Length > bestMatch.Length)
                {
                    bestMatch = parts;
                }
            }

            return bestMatch;
        }

        private static void SortRomVersionComboBoxAscending(System.Windows.Controls.ComboBox comboBox)
        {
            var sortedItems = comboBox.Items
                .Cast<object>()
                .Select((item, index) => new
                {
                    Item = item,
                    Index = index,
                    Text = item is ComboBoxItem comboBoxItem
                        ? comboBoxItem.Content?.ToString() ?? string.Empty
                        : item?.ToString() ?? string.Empty
                })
                .OrderBy(entry => entry.Text, RomVersionDisplayComparer)
                .ThenBy(entry => entry.Index)
                .Select(entry => entry.Item)
                .ToList();

            comboBox.Items.Clear();
            foreach (var item in sortedItems)
            {
                comboBox.Items.Add(item);
            }
        }

        private async Task LoadRomSelectDeviceVersionsAsync(string device)
        {
            if (string.IsNullOrWhiteSpace(device)) return;
            var packageType = GetSelectedRomSelectPackageType();
            if (string.IsNullOrWhiteSpace(packageType)) packageType = _activeRomSelectPackageType;
            var brand = (RomSelectBrandComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(brand)) brand = _activeRomSelectBrand;
            var series = (RomSelectSeriesComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(series)) series = _activeRomSelectSeries;
            if (string.IsNullOrWhiteSpace(packageType) || string.IsNullOrWhiteSpace(brand) || string.IsNullOrWhiteSpace(series)) return;

            var key = MakeRomSelectSeriesKey(packageType, brand, series);
            if (_romSelectDeviceConfigsBySeriesKey.TryGetValue(key, out var dict) &&
                dict.TryGetValue(device, out var cfg))
            {
                AppendRomDownloadLog("Info", $"Selected device: {device}");
                if (UseRomBackendApi && (cfg.Items == null || cfg.Items.Count == 0))
                {
                    try
                    {
                        var versions = await FetchRomApiVersionsAsync(ToRomApiPackageType(packageType), brand, series, device, CancellationToken.None).ConfigureAwait(true);
                        cfg.Items = versions.Select(x => new RomItemConfig { Name = x }).ToList();
                    }
                    catch (Exception ex)
                    {
                        AppendRomDownloadLog("Error", $"Failed to load version info: {ex.Message}");
                        return;
                    }
                }
                PopulateRomSelectVersionComboBox(cfg);
                AppendRomDownloadLog("Success", $"Loaded {cfg.Items.Count} version items");
                return;
            }

            _romSelectUiUpdating = true;
            try
            {
                RomSelectVersionComboBox?.Items.Clear();
            }
            finally
            {
                _romSelectUiUpdating = false;
            }
        }

        private void RefreshRomSelectBrandOptions()
        {
            if (RomSelectBrandComboBox == null) return;

            var packageType = GetSelectedRomSelectPackageType();

            _romSelectUiUpdating = true;
            try
            {
                RomSelectBrandComboBox.Items.Clear();
                ClearRomSelectAllOptions();

                if (string.Equals(packageType, RomSelectPackageTypeAfterSales, StringComparison.OrdinalIgnoreCase))
                {
                    RomSelectBrandComboBox.Items.Add(new ComboBoxItem { Content = "OPPO" });
                    RomSelectBrandComboBox.Items.Add(new ComboBoxItem { Content = "OnePlus" });
                    RomSelectBrandComboBox.Items.Add(new ComboBoxItem { Content = "Realme" });
                    RomSelectBrandComboBox.Items.Add(new ComboBoxItem { Content = "Xiaomi" });
                    RomSelectBrandComboBox.Items.Add(new ComboBoxItem { Content = "Redmi" });
                }
                else
                {
                    RomSelectBrandComboBox.Items.Add(new ComboBoxItem { Content = "OPPO" });
                    RomSelectBrandComboBox.Items.Add(new ComboBoxItem { Content = "OnePlus" });
                    RomSelectBrandComboBox.Items.Add(new ComboBoxItem { Content = "Realme" });
                    RomSelectBrandComboBox.Items.Add(new ComboBoxItem { Content = "Xiaomi" });
                    RomSelectBrandComboBox.Items.Add(new ComboBoxItem { Content = "Redmi" });
                    RomSelectBrandComboBox.Items.Add(new ComboBoxItem { Content = "魅族" });
                }

                // 不自动选择，保持空白
                RomSelectBrandComboBox.SelectedIndex = -1;
            }
            finally
            {
                _romSelectUiUpdating = false;
            }

            AppendRomDownloadLog("信息", $"已加载{RomSelectBrandComboBox.Items.Count}个机型品牌");
        }

        private async Task RefreshRomSelectSeriesOptionsAsync(CancellationToken cancellationToken = default)
        {
            if (RomSelectBrandComboBox == null || RomSelectSeriesComboBox == null) return;

            var brand = (RomSelectBrandComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(brand)) return;

            var packageType = GetSelectedRomSelectPackageType();
            var seriesNames = UseRomBackendApi
                ? await FetchRomApiSeriesAsync(ToRomApiPackageType(packageType), brand, cancellationToken).ConfigureAwait(true)
                : GetRomSelectSeriesNames(packageType, brand).ToList();

            cancellationToken.ThrowIfCancellationRequested();
            var currentBrand = (RomSelectBrandComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
            if (!string.Equals(currentBrand, brand, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(GetSelectedRomSelectPackageType(), packageType, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            bool wasUpdating = _romSelectUiUpdating;
            string? firstSeries = null;
            _romSelectUiUpdating = true;
            try
            {
                RomSelectSeriesComboBox.Items.Clear();
                ClearRomSelectDeviceAndVersion();

                foreach (var s in seriesNames)
                {
                    RomSelectSeriesComboBox.Items.Add(new ComboBoxItem { Content = s });
                    firstSeries ??= s;
                }

                if (RomSelectSeriesComboBox.Items.Count > 0)
                {
                    RomSelectSeriesComboBox.SelectedIndex = 0;
                }
            }
            finally
            {
                _romSelectUiUpdating = wasUpdating;
            }

            if (!string.IsNullOrWhiteSpace(firstSeries))
            {
                _romSelectConfigLoadCts?.Cancel();
                _romSelectConfigLoadCts?.Dispose();
                _romSelectConfigLoadCts = new CancellationTokenSource();
                var token = _romSelectConfigLoadCts.Token;
                await LoadRomSelectSeriesAsync(packageType, brand, firstSeries, token).ConfigureAwait(true);
            }
        }

        private void PopulateDeviceComboBox(IEnumerable<string> devices)
        {
            if (RomDeviceComboBox == null) return;

            bool wasUpdating = _romDownloadUiUpdating;
            _romDownloadUiUpdating = true;
            try
            {
                RomDeviceComboBox.Items.Clear();
                foreach (var d in devices)
                {
                    RomDeviceComboBox.Items.Add(new ComboBoxItem { Content = d });
                }

                if (RomDeviceComboBox.Items.Count > 0)
                {
                    RomDeviceComboBox.SelectedIndex = 0;
                }
            }
            finally
            {
                _romDownloadUiUpdating = wasUpdating;
            }
        }

        private void PopulateVersionComboBox(RomDeviceConfig deviceConfig)
        {
            if (RomVersionComboBox == null) return;

            bool wasUpdating = _romDownloadUiUpdating;
            _romDownloadUiUpdating = true;
            try
            {
                RomVersionComboBox.Items.Clear();
                _romDownloadLinksByVersionName.Clear();
                _romDownloadItemsByVersionName.Clear();
                _selectedRomDownloadLinks.Clear();
                _selectedRomDownloadHeaders.Clear();

                foreach (var item in deviceConfig.Items)
                {
                    RomVersionComboBox.Items.Add(new ComboBoxItem { Content = item.Name });
                    _romDownloadLinksByVersionName[item.Name] = item.DownloadLinks ?? new List<string>();
                    _romDownloadItemsByVersionName[item.Name] = item;
                }

                AppendC16VersionsForDevice(deviceConfig);
                SortRomVersionComboBoxAscending(RomVersionComboBox);

                if (RomVersionComboBox.Items.Count > 0)
                {
                    RomVersionComboBox.SelectedIndex = 0;
                }
            }
            finally
            {
                _romDownloadUiUpdating = wasUpdating;
            }
        }

        private void RefreshSelectedLinksFromCurrentVersion()
        {
            var version = (RomVersionComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (string.IsNullOrWhiteSpace(version))
            {
                _selectedRomDownloadLinks.Clear();
                _selectedRomDownloadHeaders.Clear();
                return;
            }

            if (_romDownloadItemsByVersionName.TryGetValue(version, out var item) && item != null)
            {
                var brand = (RomBrandComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? _activeRomDownloadBrand;
                _selectedRomDownloadLinks = PreferRomArchiveLinks(RomSelectPackageTypeFull, brand, item.DownloadLinks ?? new List<string>());
                _selectedRomDownloadHeaders = item.RequestHeaders != null
                    ? new Dictionary<string, string>(item.RequestHeaders, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            else if (_romDownloadLinksByVersionName.TryGetValue(version, out var links) && links != null)
            {
                var brand = (RomBrandComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? _activeRomDownloadBrand;
                _selectedRomDownloadLinks = PreferRomArchiveLinks(RomSelectPackageTypeFull, brand, links);
                _selectedRomDownloadHeaders.Clear();
            }
            else
            {
                _selectedRomDownloadLinks.Clear();
                _selectedRomDownloadHeaders.Clear();
            }
        }

        private async Task LoadDeviceVersionsAndLogAsync(string device)
        {
            if (string.IsNullOrWhiteSpace(device)) return;

            var brand = (RomBrandComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
            var series = (RomSeriesComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(brand)) brand = _activeRomDownloadBrand;
            if (string.IsNullOrWhiteSpace(series)) series = _activeRomDownloadSeries;
            if (!string.IsNullOrWhiteSpace(brand) &&
                !string.IsNullOrWhiteSpace(series))
            {
                var key = MakeRomDownloadSeriesKey(brand, series);
                if (_romDownloadDeviceConfigsBySeriesKey.TryGetValue(key, out var dict) &&
                    dict.TryGetValue(device, out var cfg))
                {
                    AppendRomDownloadLog("信息", $"已选择机型：{device}");
                    if (UseRomBackendApi && (cfg.Items == null || cfg.Items.Count == 0))
                    {
                        try
                        {
                            var versions = await FetchRomApiVersionsAsync(RomApiPackageTypeFull, brand, series, device, CancellationToken.None).ConfigureAwait(true);
                            cfg.Items = versions.Select(x => new RomItemConfig { Name = x }).ToList();
                        }
                        catch (Exception ex)
                        {
                            AppendRomDownloadLog("Error", $"Failed to load versions: {ex.Message}");
                            return;
                        }
                    }
                    PopulateVersionComboBox(cfg);
                    RefreshSelectedLinksFromCurrentVersion();
                    AppendRomDownloadLog("成功", $"成功加载{cfg.Items.Count}个版本信息");
                    return;
                }
            }

            RomVersionComboBox?.Items.Clear();
            _romDownloadLinksByVersionName.Clear();
            _romDownloadItemsByVersionName.Clear();
            _selectedRomDownloadLinks.Clear();
            _selectedRomDownloadHeaders.Clear();
        }

        private async void RomSeriesComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_romDownloadUiUpdating) return;

            var series = (RomSeriesComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (string.IsNullOrWhiteSpace(series)) return;

            _romConfigLoadCts?.Cancel();
            _romConfigLoadCts?.Dispose();
            _romConfigLoadCts = new CancellationTokenSource();
            var token = _romConfigLoadCts.Token;

            try
            {
                var brand = (RomBrandComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(brand))
                {
                    ClearRomDeviceAndVersion();
                    return;
                }

                if (!TryGetRomDownloadSeriesUrl(brand, series, out var url))
                {
                    _activeRomDownloadBrand = string.Empty;
                    _activeRomDownloadSeries = string.Empty;
                    ClearRomDeviceAndVersion();
                    return;
                }

                if (!string.IsNullOrWhiteSpace(url))
                {
                    if (!UseRomBackendApi && _enableC16Dynamic && IsC16Brand(brand))
                    {
                        try
                        {
                            await EnsureC16RecordsLoadedAsync(token);
                        }
                        catch
                        {
                        }
                    }

                    await EnsureRomDownloadConfigLoadedAsync(brand, series, url, token);
                    _activeRomDownloadBrand = brand;
                    _activeRomDownloadSeries = series;

                    var key = MakeRomDownloadSeriesKey(brand, series);
                    if (_romDownloadDeviceConfigsBySeriesKey.TryGetValue(key, out var dict) && dict != null)
                    {
                        AppendRomDownloadLog("信息", $"已选择{series}");
                        AppendRomDownloadLog("成功", $"在{series}中成功加载{dict.Count}个机型");

                        var orderedDevices = dict.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
                        PopulateDeviceComboBox(orderedDevices);

                        var firstDevice = orderedDevices.FirstOrDefault();
                        if (!string.IsNullOrWhiteSpace(firstDevice))
                        {
                            await LoadDeviceVersionsAndLogAsync(firstDevice).ConfigureAwait(true);
                        }
                    }

                    return;
                }

                _activeRomDownloadBrand = string.Empty;
                _activeRomDownloadSeries = string.Empty;
                ClearRomDeviceAndVersion();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                ClearRomDeviceAndVersion();
                AppendRomDownloadLog("错误", $"已选择{series}，加载失败：{ex.Message}");
                AddLogMessage("ROM下载", $"加载失败: {ex.Message}");
            }
        }

        private void RomDeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_romDownloadUiUpdating) return;

            var device = (RomDeviceComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (string.IsNullOrWhiteSpace(device)) return;

            _ = LoadDeviceVersionsAndLogAsync(device);
        }

        private void RomVersionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_romDownloadUiUpdating) return;
            RefreshSelectedLinksFromCurrentVersion();
        }

        private async Task RefreshRomSeriesOptionsAsync(CancellationToken cancellationToken = default)
        {
            if (RomSeriesComboBox == null) return;

            var selectedContent = (RomBrandComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (string.IsNullOrWhiteSpace(selectedContent)) return;

            var seriesNames = UseRomBackendApi
                ? await FetchRomApiSeriesAsync(RomApiPackageTypeFull, selectedContent, cancellationToken).ConfigureAwait(true)
                : GetRomDownloadSeriesNames(selectedContent).ToList();

            cancellationToken.ThrowIfCancellationRequested();
            var currentBrand = (RomBrandComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
            if (!string.Equals(currentBrand, selectedContent, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            bool wasUpdating = _romDownloadUiUpdating;
            _romDownloadUiUpdating = true;
            try
            {
                RomSeriesComboBox.Items.Clear();
                ClearRomDeviceAndVersion();

                foreach (var s in seriesNames)
                {
                    RomSeriesComboBox.Items.Add(new ComboBoxItem { Content = s });
                }
            }
            finally
            {
                _romDownloadUiUpdating = wasUpdating;
            }

            if (!wasUpdating && RomSeriesComboBox.Items.Count > 0)
            {
                RomSeriesComboBox.SelectedIndex = 0;
            }
        }

        private async void RomBrandComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_romDownloadUiUpdating) return;

            var brand = (RomBrandComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(brand)) return;

            _romBrandSeriesLoadCts?.Cancel();
            _romBrandSeriesLoadCts?.Dispose();
            _romBrandSeriesLoadCts = new CancellationTokenSource();
            var cancellationToken = _romBrandSeriesLoadCts.Token;
            _romConfigLoadCts?.Cancel();
            _romConfigLoadCts?.Dispose();
            _romConfigLoadCts = null;

            try
            {
                if (!UseRomBackendApi && _enableC16Dynamic && IsC16Brand(brand))
                {
                    await EnsureC16RecordsLoadedAsync(cancellationToken);
                    AppendRomDownloadLog("信息", "已加载C16机型列表");
                }

                await RefreshRomSeriesOptionsAsync(cancellationToken).ConfigureAwait(true);
                AppendRomDownloadLog("信息", $"已加载{RomSeriesComboBox?.Items.Count ?? 0}个{brand}系列");
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                var currentBrand = (RomBrandComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
                if (cancellationToken.IsCancellationRequested ||
                    !string.Equals(currentBrand, brand, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                ClearRomDeviceAndVersion();
                RomSeriesComboBox?.Items.Clear();
                AppendRomDownloadLog("错误", $"加载{brand}系列失败：{ex.Message}");
                AddLogMessage("ROM下载", $"加载{brand}系列失败：{ex}");
            }
        }

        private async void RomSelectBrandComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_romSelectUiUpdating) return;

            var brand = (RomSelectBrandComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(brand)) return;

            _romSelectBrandSeriesLoadCts?.Cancel();
            _romSelectBrandSeriesLoadCts?.Dispose();
            _romSelectBrandSeriesLoadCts = new CancellationTokenSource();
            var cancellationToken = _romSelectBrandSeriesLoadCts.Token;
            _romSelectConfigLoadCts?.Cancel();
            _romSelectConfigLoadCts?.Dispose();
            _romSelectConfigLoadCts = null;

            try
            {
                await RefreshRomSelectSeriesOptionsAsync(cancellationToken).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                var currentBrand = (RomSelectBrandComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
                if (cancellationToken.IsCancellationRequested ||
                    !string.Equals(currentBrand, brand, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                ClearRomSelectDeviceAndVersion();
                RomSelectSeriesComboBox?.Items.Clear();
                SetRomSelectOutput($"加载{brand}系列失败：{ex.Message}", "错误");
                AddLogMessage("ROM获取", $"加载{brand}系列失败：{ex}");
            }
        }

        private async void RomSelectPackageTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_romSelectUiUpdating) return;
            
            // 保存当前选择
            var currentBrand = (RomSelectBrandComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
            var currentSeries = (RomSelectSeriesComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
            
            // 获取新的包类型
            var packageType = GetSelectedRomSelectPackageType();
            
            // 如果有选择的品牌和系列，重新加载配置
            if (!string.IsNullOrWhiteSpace(currentBrand) && !string.IsNullOrWhiteSpace(currentSeries))
            {
                if (TryGetRomSelectSeriesUrl(packageType, currentBrand, currentSeries, out var url))
                {
                    _romSelectConfigLoadCts?.Cancel();
                    _romSelectConfigLoadCts?.Dispose();
                    _romSelectConfigLoadCts = new CancellationTokenSource();
                    var token = _romSelectConfigLoadCts.Token;

                    try
                    {
                        await EnsureRomSelectConfigLoadedAsync(packageType, currentBrand, currentSeries, url, token);
                        _activeRomSelectPackageType = packageType;
                        _activeRomSelectBrand = currentBrand;
                        _activeRomSelectSeries = currentSeries;

                        var key = MakeRomSelectSeriesKey(packageType, currentBrand, currentSeries);
                        if (_romSelectDeviceConfigsBySeriesKey.TryGetValue(key, out var dict) && dict != null)
                        {
                            var orderedDevices = dict.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
                            PopulateRomSelectDeviceComboBox(orderedDevices);

                            var firstDevice = orderedDevices.FirstOrDefault();
                            if (!string.IsNullOrWhiteSpace(firstDevice))
                            {
                                await LoadRomSelectDeviceVersionsAsync(firstDevice).ConfigureAwait(true);
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // 加载被取消
                    }
                    catch (Exception ex)
                    {
                        AppendRomDownloadLog("错误", $"加载配置失败: {ex.Message}");
                    }
                }
            }
            
            UpdateRomSelectParsedLinkDisplay();
        }

        private async void RomSelectSeriesComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_romSelectUiUpdating) return;

            var packageType = GetSelectedRomSelectPackageType();
            var brand = (RomSelectBrandComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
            var series = (RomSelectSeriesComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(brand) || string.IsNullOrWhiteSpace(series)) return;

            if (!TryGetRomSelectSeriesUrl(packageType, brand, series, out var url))
            {
                return;
            }

            _romSelectConfigLoadCts?.Cancel();
            _romSelectConfigLoadCts?.Dispose();
            _romSelectConfigLoadCts = new CancellationTokenSource();
            var token = _romSelectConfigLoadCts.Token;

            try
            {
                await EnsureRomSelectConfigLoadedAsync(packageType, brand, series, url, token);
                _activeRomSelectPackageType = packageType;
                _activeRomSelectBrand = brand;
                _activeRomSelectSeries = series;

                var key = MakeRomSelectSeriesKey(packageType, brand, series);
                if (_romSelectDeviceConfigsBySeriesKey.TryGetValue(key, out var dict) && dict != null)
                {
                    var orderedDevices = dict.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
                    PopulateRomSelectDeviceComboBox(orderedDevices);

                    var firstDevice = orderedDevices.FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(firstDevice))
                    {
                        await LoadRomSelectDeviceVersionsAsync(firstDevice).ConfigureAwait(true);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                ClearRomSelectDeviceAndVersion();
            }
        }

        private void RomSelectDeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_romSelectUiUpdating) return;

            var device = (RomSelectDeviceComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (string.IsNullOrWhiteSpace(device)) return;
            _ = LoadRomSelectDeviceVersionsAsync(device);
            UpdateRomSelectParsedLinkDisplay();
        }

        private void RomSelectVersionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_romSelectUiUpdating) return;
            UpdateRomSelectParsedLinkDisplay();
        }

        // 更新解析链接显示
        private void UpdateRomSelectParsedLinkDisplay()
        {
            try
            {
                var links = GetSelectedRomSelectDownloadLinks();
                if (links.Count == 0)
                {
                    if (UseRomBackendApi)
                    {
                        SetRomSelectOutput("Download link will be requested only when copy is clicked.");
                        return;
                    }
                    SetRomSelectOutput("当前版本未解析到下载链接", "警告");
                    return;
                }

                if (string.Equals(_activeRomSelectBrand, "魅族", StringComparison.OrdinalIgnoreCase) &&
                    links.Any(IsMeizuDownloadEntryLink))
                {
                    SetRomSelectOutput("检测到魅族官方入口，点击\"复制下载链接\"按钮后将自动解析真实下载链接");
                    return;
                }

                // 检查是否需要C16动态解析
                if (_enableC16Dynamic &&
                    IsC16Brand(_activeRomSelectBrand) &&
                    links.Count == 1 &&
                    links[0].Contains("downloadCheck", StringComparison.OrdinalIgnoreCase))
                {
                    SetRomSelectOutput("检测到C16链接，点击\"复制下载链接\"按钮将自动解析直链");
                }
                else
                {
                    SetRomSelectOutput(string.Join(Environment.NewLine, links));
                }
            }
            catch (Exception ex)
            {
                SetRomSelectOutput($"解析链接时出错：{ex.Message}", "错误");
            }
        }

        private static bool IsMeizuDownloadEntryLink(string url)
        {
            url = CleanLink(url);
            return !string.IsNullOrWhiteSpace(url) &&
                   url.Contains("flyme.com/zh/download?key=", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsMeizuUnsignedFirmwareLink(string url)
        {
            url = CleanLink(url);
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (!url.Contains("firmware-res.flyme.com/", StringComparison.OrdinalIgnoreCase)) return false;
            return !url.Contains("auth_key=", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task<string?> ResolveMeizuDownloadUrlAsync(
            string url,
            IReadOnlyDictionary<string, string>? headers,
            CancellationToken cancellationToken)
        {
            url = CleanLink(url);
            if (string.IsNullOrWhiteSpace(url)) return null;
            if (!IsMeizuDownloadEntryLink(url)) return url;

            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var client = new HttpClient(handler);

            if (headers is not null)
            {
                foreach (var header in headers)
                {
                    client.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(true);

            int statusCode = (int)response.StatusCode;
            if (statusCode is 301 or 302 or 303 or 307 or 308)
            {
                var location = response.Headers.Location;
                if (location == null) return null;
                if (!location.IsAbsoluteUri)
                {
                    location = new Uri(new Uri(url), location);
                }

                return location.ToString();
            }

            return response.RequestMessage?.RequestUri?.ToString() ?? url;
        }

        private Dictionary<string, string> GetSelectedRomSelectRequestHeaders()
        {
            var version = (RomSelectVersionComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(version)) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (_romSelectItemsByVersionName.TryGetValue(version, out var item) &&
                item?.RequestHeaders != null &&
                item.RequestHeaders.Count > 0)
            {
                return new Dictionary<string, string>(item.RequestHeaders, StringComparer.OrdinalIgnoreCase);
            }

            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private async Task<string?> ResolveMeizuDownloadUrlForRomSelectAsync(string url, CancellationToken cancellationToken)
        {
            var headers = GetSelectedRomSelectRequestHeaders();
            return await ResolveMeizuDownloadUrlAsync(url, headers, cancellationToken).ConfigureAwait(true);
        }

        private IReadOnlyList<string> GetSelectedRomSelectDownloadLinks()
        {
            var packageType = GetSelectedRomSelectPackageType();
            var brand = (RomSelectBrandComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
            var series = (RomSelectSeriesComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
            var device = (RomSelectDeviceComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
            var version = (RomSelectVersionComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(brand) ||
                string.IsNullOrWhiteSpace(series) ||
                string.IsNullOrWhiteSpace(device) ||
                string.IsNullOrWhiteSpace(version))
            {
                return Array.Empty<string>();
            }

            if (_romSelectLinksByVersionName.TryGetValue(version, out var mappedLinks) &&
                mappedLinks != null &&
                mappedLinks.Count > 0)
            {
                return PreferRomArchiveLinks(packageType, brand, mappedLinks);
            }

            var key = MakeRomSelectSeriesKey(packageType, brand, series);
            if (!_romSelectDeviceConfigsBySeriesKey.TryGetValue(key, out var dict) || dict == null)
            {
                return Array.Empty<string>();
            }

            if (!dict.TryGetValue(device, out var cfg) || cfg == null)
            {
                return Array.Empty<string>();
            }

            var item = cfg.Items.FirstOrDefault(x => string.Equals(x.Name, version, StringComparison.OrdinalIgnoreCase));
            if (item == null)
            {
                return Array.Empty<string>();
            }

            var result = new List<string>();
            if (item.DownloadLinks != null && item.DownloadLinks.Count > 0)
            {
                result.AddRange(item.DownloadLinks);
            }
            else if (!string.IsNullOrWhiteSpace(item.Href))
            {
                result.Add(item.Href);
            }

            return PreferRomArchiveLinks(packageType, brand, result);
        }

        private async void RomSelectCopyLinksButton_Click(object sender, RoutedEventArgs e)
        {
            var links = GetSelectedRomSelectDownloadLinks();
            var versionName = (RomSelectVersionComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
            var packageType = ToRomApiPackageType(GetSelectedRomSelectPackageType());
            var brand = (RomSelectBrandComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? _activeRomSelectBrand;
            var series = (RomSelectSeriesComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? _activeRomSelectSeries;
            var device = (RomSelectDeviceComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
            bool refreshC16Link = UseRomBackendApi &&
                                  IsC16Brand(brand) &&
                                  device.StartsWith("[C16动态解析]", StringComparison.OrdinalIgnoreCase);

            if (links.Count == 0 || refreshC16Link)
            {
                if (UseRomBackendApi)
                {
                    if (RomSelectCopyLinksButton != null)
                    {
                        RomSelectCopyLinksButton.IsEnabled = false;
                    }

                    try
                    {
                        if (refreshC16Link)
                        {
                            AppendRomDownloadLog("信息", "正在解析C16直链（优先线路1，失败后自动尝试线路2）...");
                            SetRomSelectOutput("正在解析C16直链，请稍候...");
                        }

                        var response = await FetchRomApiDownloadLinksAsync(packageType, brand, series, device, versionName, CancellationToken.None).ConfigureAwait(true);
                        links = PreferRomArchiveLinks(packageType, brand, response.Links);
                        if (links.Count > 0)
                        {
                            var routeDisplayName = GetRomResolveRouteDisplayName(response.ResolveRoute);
                            if (refreshC16Link && !string.IsNullOrWhiteSpace(routeDisplayName))
                            {
                                AppendRomDownloadLog("成功", $"C16直链解析成功：{routeDisplayName}");
                            }

                            if (refreshC16Link)
                            {
                                _romSelectLinksByVersionName.Remove(versionName);
                            }
                            else
                            {
                                _romSelectLinksByVersionName[versionName] = links.ToList();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        var safeMessage = SanitizeRomApiErrorMessage(ex.Message);
                        if (string.IsNullOrWhiteSpace(safeMessage))
                        {
                            safeMessage = "服务器暂时未返回可用下载链接。";
                        }
                        AppendRomDownloadLog("错误", $"获取下载链接失败：{safeMessage}");
                        return;
                    }
                    finally
                    {
                        if (RomSelectCopyLinksButton != null)
                        {
                            RomSelectCopyLinksButton.IsEnabled = true;
                        }
                    }
                }
            }

            if (links.Count == 0)
            {
                AppendRomDownloadLog("警告", "当前版本未解析到下载链接");
                return;
            }

            if (_enableC16Dynamic &&
                IsC16Brand(_activeRomSelectBrand) &&
                links.Count == 1 &&
                links[0].Contains("downloadCheck", StringComparison.OrdinalIgnoreCase))
            {
                AppendRomDownloadLog("信息", "正在解析C16直链...");
                SetRomSelectOutput("正在解析C16直链，请稍候...");
                var cancellationToken = CancellationToken.None;
                var resolved = await ResolveC16DownloadUrlForRomSelectAsync(versionName, links[0], cancellationToken);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    links = new List<string> { resolved };
                    SetRomSelectOutput(resolved, "成功");
                }
                else
                {
                    SetRomSelectOutput("C16直链解析失败", "错误");
                    return;
                }
            }

            if (string.Equals(_activeRomSelectBrand, "魅族", StringComparison.OrdinalIgnoreCase) &&
                links.Any(IsMeizuDownloadEntryLink))
            {
                SetRomSelectOutput("正在解析魅族真实下载链接，请稍候...");

                var meizuEntryLinks = links
                    .Where(IsMeizuDownloadEntryLink)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var resolvedLinks = new List<string>();
                foreach (var link in meizuEntryLinks)
                {
                    var resolved = await ResolveMeizuDownloadUrlForRomSelectAsync(link, CancellationToken.None);
                    if (!string.IsNullOrWhiteSpace(resolved))
                    {
                        resolvedLinks.Add(resolved);
                    }
                }

                resolvedLinks = resolvedLinks
                    .Select(CleanLink)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (resolvedLinks.Count == 0)
                {
                    SetRomSelectOutput("魅族真实下载链接解析失败", "错误");
                    AppendRomDownloadLog("错误", "魅族真实下载链接解析失败");
                    return;
                }

                links = resolvedLinks;
                SetRomSelectOutput(string.Join(Environment.NewLine, links), "成功");
            }

            links = PreferRomArchiveLinks(GetSelectedRomSelectPackageType(), _activeRomSelectBrand, links);

            if (links.Count > 0)
            {
                SetRomSelectOutput(string.Join(Environment.NewLine, links), "成功");
            }

            try
            {
                string linksText = string.Join(Environment.NewLine, links);
                System.Windows.Clipboard.SetText(linksText);
                AppendRomDownloadLog("警告", "已复制下载链接到剪切板，请复制链接到下载器下载");
            }
            catch (Exception ex)
            {
                AppendRomDownloadLog("错误", $"复制失败：{ex.Message}");
            }
        }

        private void LaunchDownloaderExe(string exeFileName, string friendlyName)
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var fullPath = Path.Combine(baseDir, "exe", exeFileName);

            if (!File.Exists(fullPath))
            {
                AppendRomDownloadLog("错误", $"未找到{friendlyName}：{fullPath}");
                return;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fullPath,
                    WorkingDirectory = Path.GetDirectoryName(fullPath) ?? baseDir,
                    UseShellExecute = true
                };
                Process.Start(psi);
                AppendRomDownloadLog("成功", $"已启动{friendlyName}");
            }
            catch (Exception ex)
            {
                AppendRomDownloadLog("错误", $"启动{friendlyName}失败：{ex.Message}");
            }
        }

        private void LaunchNekoDownloader_Click(object sender, RoutedEventArgs e)
        {
            LaunchDownloaderExe("Neko.exe", "官方下载器");
        }

        private void LaunchNdmDownloader_Click(object sender, RoutedEventArgs e)
        {
            LaunchDownloaderExe("NDM.exe", "NDM下载器");
        }

        private async void RomNetworkRepairButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as System.Windows.Controls.Button;
            if (button != null)
            {
                button.IsEnabled = false;
            }

            AppendRomDownloadLog("信息", "正在请求管理员权限并修复网络 DNS...");

            const string repairScript = """
                $ErrorActionPreference = 'Stop'
                try {
                    $adapters = Get-NetAdapter | Where-Object { $_.Status -eq 'Up' -and -not $_.Virtual }
                    if (-not $adapters) {
                        throw 'No connected physical network adapter was found.'
                    }

                    foreach ($adapter in $adapters) {
                        Set-DnsClientServerAddress -InterfaceIndex $adapter.ifIndex -ServerAddresses @('223.5.5.5', '1.1.1.1')
                    }

                    ipconfig /flushdns | Out-Null
                    exit 0
                }
                catch {
                    exit 1
                }
                """;

            try
            {
                string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(repairScript));
                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}",
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    AppendRomDownloadLog("错误", "无法启动网络修复程序");
                    return;
                }

                await process.WaitForExitAsync().ConfigureAwait(true);
                if (process.ExitCode != 0)
                {
                    AppendRomDownloadLog("错误", "网络修复失败，请检查网卡状态或系统权限");
                    return;
                }

                AppendRomDownloadLog("成功", "DNS 已修改为 223.5.5.5 和 1.1.1.1，缓存已刷新");

                try
                {
                    var addresses = await System.Net.Dns.GetHostAddressesAsync("violettool.top").ConfigureAwait(true);
                    string resolvedAddresses = string.Join(", ", addresses.Select(x => x.ToString()));
                    AppendRomDownloadLog("成功", $"网络测试成功：violettool.top -> {resolvedAddresses}");
                }
                catch (Exception ex)
                {
                    AppendRomDownloadLog("警告", $"DNS 已修改，但域名测试仍失败：{ex.Message}");
                }
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                AppendRomDownloadLog("警告", "用户取消了管理员权限授权，未修改 DNS");
            }
            catch (Exception ex)
            {
                AppendRomDownloadLog("错误", $"修复网络异常失败：{ex.Message}");
            }
            finally
            {
                if (button != null)
                {
                    button.IsEnabled = true;
                }
            }
        }

        private const string LenovoQueryInputPlaceholder = "请输入 8 位 SN 或 10 位 MTM";

        private void LenovoQueryInputTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (LenovoQueryInputTextBox == null) return;
            if (string.Equals(LenovoQueryInputTextBox.Text, LenovoQueryInputPlaceholder, StringComparison.Ordinal))
            {
                LenovoQueryInputTextBox.Clear();
            }
        }

        private void LenovoQueryInputTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (LenovoQueryInputTextBox == null) return;
            if (string.IsNullOrWhiteSpace(LenovoQueryInputTextBox.Text))
            {
                LenovoQueryInputTextBox.Text = LenovoQueryInputPlaceholder;
            }
        }

        private async Task ExecuteLenovoQueryAsync(string input, string? presetModelName = null)
        {
            input = CleanLink(input).ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(input) || input.Contains("请输入", StringComparison.OrdinalIgnoreCase))
            {
                AppendRomDownloadLog("警告", "请输入 8 位 SN 或 10 位 MTM，或选择预设机型");
                SetLenovoQueryOutput("请输入 8 位 SN 或 10 位 MTM，或选择预设机型", "警告");
                return;
            }

            _lenovoQueryResolvedDownloadUrl = string.Empty;
            _lenovoQueryLastSummaryText = string.Empty;
            if (LenovoQueryButton != null) LenovoQueryButton.IsEnabled = false;
            if (LenovoPresetModelComboBox != null) LenovoPresetModelComboBox.IsEnabled = false;
            SetLenovoQueryOutput("联想查包查询中，请稍候...");

            try
            {
                if (LenovoQueryInputTextBox != null && !string.Equals(LenovoQueryInputTextBox.Text, input, StringComparison.OrdinalIgnoreCase))
                {
                    LenovoQueryInputTextBox.Text = input;
                }

                string queryTarget = string.IsNullOrWhiteSpace(presetModelName)
                    ? input
                    : $"{presetModelName} ({input})";
                AppendRomDownloadLog("信息", $"联想查包开始查询：{queryTarget}");
                var result = await QueryLenovoPackageByInputAsync(input, CancellationToken.None).ConfigureAwait(true);
                if (!string.IsNullOrWhiteSpace(presetModelName))
                {
                    result.InputType = "预设机型";
                    result.PresetModelName = presetModelName;
                    result.ResolvedMtm = input;
                }

                _lenovoQueryResolvedDownloadUrl = result.FullPackage?.DownloadUrl ?? string.Empty;
                _lenovoQueryLastSummaryText = BuildLenovoQuerySummary(result);
                SetLenovoQueryOutput(_lenovoQueryLastSummaryText, result.Success ? "成功" : "错误");

                if (result.Success)
                {
                    AppendRomDownloadLog("成功", $"联想查包查询成功：{queryTarget}");
                }
                else
                {
                    AppendRomDownloadLog("错误", $"联想查包查询失败：{queryTarget}，{result.Error}");
                }
            }
            catch (Exception ex)
            {
                SetLenovoQueryOutput($"联想查包查询失败：{ex.Message}", "错误");
                AppendRomDownloadLog("错误", $"联想查包查询异常：{ex.Message}");
            }
            finally
            {
                if (LenovoQueryButton != null) LenovoQueryButton.IsEnabled = true;
                if (LenovoPresetModelComboBox != null) LenovoPresetModelComboBox.IsEnabled = true;
            }
        }

        private async void LenovoQueryButton_Click(object sender, RoutedEventArgs e)
        {
            string input = CleanLink(LenovoQueryInputTextBox?.Text ?? string.Empty).ToUpperInvariant();
            await ExecuteLenovoQueryAsync(input).ConfigureAwait(true);
        }

        private async void LenovoPresetModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LenovoPresetModelComboBox?.SelectedItem is not ComboBoxItem selectedItem)
            {
                return;
            }

            string presetModelName = CleanLink(selectedItem.Content?.ToString());
            string mtm = CleanLink(selectedItem.Tag?.ToString()).ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(presetModelName) || string.IsNullOrWhiteSpace(mtm))
            {
                return;
            }

            if (!LenovoPresetMtmMap.TryGetValue(presetModelName, out var mappedMtm))
            {
                mappedMtm = mtm;
            }

            await ExecuteLenovoQueryAsync(mappedMtm, presetModelName).ConfigureAwait(true);
        }

        private void LenovoQueryCopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_lenovoQueryResolvedDownloadUrl))
            {
                AppendRomDownloadLog("警告", "当前没有可复制的联想下载链接");
                return;
            }

            try
            {
                System.Windows.Clipboard.SetText(_lenovoQueryResolvedDownloadUrl);
                AppendRomDownloadLog("成功", "联想下载链接已复制到剪贴板");
            }
            catch (Exception ex)
            {
                AppendRomDownloadLog("错误", $"复制联想下载链接失败：{ex.Message}");
            }
        }

        private async void C16DynamicCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            _enableC16Dynamic = true;
            if (UseRomBackendApi)
            {
                AppendRomDownloadLog("信息", "已启用C16动态解析，记录由后端按需读取");
                return;
            }
            AppendRomDownloadLog("信息", "已启用C16动态解析");
            try
            {
                await EnsureC16RecordsLoadedAsync(CancellationToken.None);
                AppendRomDownloadLog("成功", "已加载C16机型列表");
            }
            catch (Exception ex)
            {
                AppendRomDownloadLog("错误", $"加载C16机型列表失败：{ex.Message}");
            }
        }

        private void C16DynamicCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            _enableC16Dynamic = false;
            AppendRomDownloadLog("信息", "已关闭C16动态解析");
        }

        private static bool IsC16Brand(string brand)
        {
            if (string.IsNullOrWhiteSpace(brand)) return false;
            return string.Equals(brand, "OPPO", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(brand, "OnePlus", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(brand, "Realme", StringComparison.OrdinalIgnoreCase);
        }

        private async Task EnsureC16RecordsLoadedAsync(CancellationToken cancellationToken)
        {
            if (UseRomBackendApi) return;
            if (_c16Records != null && _c16Records.Count > 0) return;

            await _c16RecordsSemaphore.WaitAsync(cancellationToken);
            try
            {
                if (_c16Records != null && _c16Records.Count > 0) return;

                var signedPath = BuildSignedUrl(C16DownloadCheckJsonPath);
                var url = C16ServerBaseUrl + signedPath;
                var json = await RomDownloadHttpClient.GetStringAsync(url, cancellationToken);
                using var doc = JsonDocument.Parse(json);

                var list = new List<C16DownloadCheckRecord>();
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("Items", out var items) &&
                    items.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in items.EnumerateArray())
                    {
                        if (el.ValueKind != JsonValueKind.Object) continue;

                        var deviceCode = CleanLink(TryGetString(el, "DeviceCode") ?? string.Empty);
                        if (string.IsNullOrWhiteSpace(deviceCode)) continue;

                        var record = new C16DownloadCheckRecord
                        {
                            DeviceCode = deviceCode,
                            OtaVersion = CleanLink(TryGetString(el, "OtaVersion") ?? string.Empty),
                            Version = CleanLink(TryGetString(el, "Version") ?? string.Empty),
                            DownloadCheckUrl = CleanLink(TryGetString(el, "DownloadCheckUrl") ?? string.Empty),
                            FinalUrl = CleanLink(TryGetString(el, "FinalUrl") ?? string.Empty)
                        };

                        list.Add(record);
                    }
                }

                _c16Records = list;
            }
            finally
            {
                _c16RecordsSemaphore.Release();
            }
        }

        private async Task<string?> ResolveC16DownloadUrlAsync(string versionName, string url, CancellationToken cancellationToken)
        {
            if (UseRomBackendApi) return url;
            if (!_enableC16Dynamic) return url;
            if (string.IsNullOrWhiteSpace(url)) return url;
            if (!url.Contains("downloadCheck", StringComparison.OrdinalIgnoreCase)) return url;

            try
            {
                await EnsureC16RecordsLoadedAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                AppendRomDownloadLog("错误", $"加载C16记录失败：{ex.Message}");
                return null;
            }

            string deviceName = (RomDeviceComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
            string deviceCode = string.Empty;
            string otaVersion = string.Empty;

            if (!string.IsNullOrWhiteSpace(deviceName) && _c16Records != null && _c16Records.Count > 0)
            {
                foreach (var r in _c16Records)
                {
                    var code = r.DeviceCode?.Trim();
                    if (string.IsNullOrWhiteSpace(code)) continue;

                    if (!DeviceNameMatchesC16(deviceName, code)) continue;

                    var baseName = string.IsNullOrWhiteSpace(r.Version) ? r.OtaVersion : r.Version;
                    baseName = CleanLink(baseName);
                    if (string.IsNullOrWhiteSpace(baseName)) continue;

                    var name = baseName;
                    var normalized = baseName.Trim();

                    if (!string.Equals(name, versionName, StringComparison.OrdinalIgnoreCase)) continue;

                    deviceCode = code;
                    otaVersion = r.OtaVersion ?? string.Empty;
                    break;
                }
            }

            var requestDto = new C16ResolveRequestDto
            {
                Url = url,
                DeviceCode = deviceCode,
                OtaVersion = otaVersion,
                SaveToStore = true
            };

            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(requestDto);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var response = await C16ServerClient.PostAsync("/api/downloadcheck/resolve", content, cancellationToken);
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    var code = (int)response.StatusCode;
                    AppendRomDownloadLog("错误", $"服务器解析失败: {code} {response.StatusCode} {body}");
                    return null;
                }

                var result = System.Text.Json.JsonSerializer.Deserialize<C16ResolveResponseDto>(body);
                var finalUrl = result?.url;
                if (string.IsNullOrWhiteSpace(finalUrl))
                {
                    AppendRomDownloadLog("错误", "解析失败：未得到直链");
                    return null;
                }

                AppendRomDownloadLog("成功", "已获取最新直链");
                return finalUrl;
            }
            catch (Exception ex)
            {
                AppendRomDownloadLog("错误", $"解析直链失败：{ex.Message}");
                return null;
            }
        }

        private async Task<string?> ResolveC16DownloadUrlForRomSelectAsync(string versionName, string url, CancellationToken cancellationToken)
        {
            if (UseRomBackendApi) return url;
            if (!_enableC16Dynamic) return url;
            if (string.IsNullOrWhiteSpace(url)) return url;
            if (!url.Contains("downloadCheck", StringComparison.OrdinalIgnoreCase)) return url;

            try
            {
                await EnsureC16RecordsLoadedAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                AppendRomDownloadLog("错误", $"加载C16记录失败：{ex.Message}");
                return null;
            }

            string deviceName = (RomSelectDeviceComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
            string deviceCode = string.Empty;
            string otaVersion = string.Empty;

            if (!string.IsNullOrWhiteSpace(deviceName) && _c16Records != null && _c16Records.Count > 0)
            {
                foreach (var r in _c16Records)
                {
                    var code = r.DeviceCode?.Trim();
                    if (string.IsNullOrWhiteSpace(code)) continue;

                    if (!DeviceNameMatchesC16(deviceName, code)) continue;

                    var baseName = string.IsNullOrWhiteSpace(r.Version) ? r.OtaVersion : r.Version;
                    baseName = CleanLink(baseName);
                    if (string.IsNullOrWhiteSpace(baseName)) continue;

                    var name = baseName;
                    var normalized = baseName.Trim();

                    if (!string.Equals(name, versionName, StringComparison.OrdinalIgnoreCase)) continue;

                    deviceCode = code;
                    otaVersion = r.OtaVersion ?? string.Empty;
                    break;
                }
            }

            var requestDto = new C16ResolveRequestDto
            {
                Url = url,
                DeviceCode = deviceCode,
                OtaVersion = otaVersion,
                SaveToStore = true
            };

            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(requestDto);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var response = await C16ServerClient.PostAsync("/api/downloadcheck/resolve", content, cancellationToken);
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    var code = (int)response.StatusCode;
                    AppendRomDownloadLog("错误", $"服务器解析失败: {code} {response.StatusCode} {body}");
                    return null;
                }

                var result = System.Text.Json.JsonSerializer.Deserialize<C16ResolveResponseDto>(body);
                var finalUrl = result?.url;
                if (string.IsNullOrWhiteSpace(finalUrl))
                {
                    AppendRomDownloadLog("错误", "解析失败：未得到直链");
                    return null;
                }

                AppendRomDownloadLog("成功", "已获取最新直链");
                return finalUrl;
            }
            catch (Exception ex)
            {
                AppendRomDownloadLog("错误", $"解析直链失败：{ex.Message}");
                return null;
            }
        }

        private void AppendC16VersionsForDevice(RomDeviceConfig deviceConfig)
        {
            if (UseRomBackendApi) return;
            if (!_enableC16Dynamic) return;
            if (deviceConfig == null) return;
            if ((deviceConfig.Items == null || deviceConfig.Items.Count == 0) &&
                !IsC16NativeDevice(deviceConfig.Device ?? string.Empty)) return;

            var brand = (RomBrandComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(brand)) brand = _activeRomDownloadBrand;
            if (!IsC16Brand(brand)) return;
            if (_c16Records == null || _c16Records.Count == 0) return;

            var existingNames = new HashSet<string>(_romDownloadLinksByVersionName.Keys, StringComparer.OrdinalIgnoreCase);

            foreach (var r in _c16Records)
            {
                var code = r.DeviceCode?.Trim();
                if (string.IsNullOrWhiteSpace(code)) continue;

                if (!DeviceNameMatchesC16(deviceConfig.Device ?? string.Empty, code)) continue;

                var baseName = string.IsNullOrWhiteSpace(r.Version) ? r.OtaVersion : r.Version;
                baseName = CleanLink(baseName);
                if (string.IsNullOrWhiteSpace(baseName)) continue;

                var name = baseName;
                var normalized = baseName.Trim();

                if (string.IsNullOrWhiteSpace(name)) continue;
                if (!IsValidVersionDisplayName(name)) continue;
                if (existingNames.Contains(name)) continue;

                var links = new List<string>();
                if (!string.IsNullOrWhiteSpace(r.DownloadCheckUrl))
                {
                    links.Add(CleanLink(r.DownloadCheckUrl));
                }
                else if (!string.IsNullOrWhiteSpace(r.FinalUrl))
                {
                    links.Add(CleanLink(r.FinalUrl));
                }

                if (links.Count == 0) continue;

                RomVersionComboBox?.Items.Add(new ComboBoxItem { Content = name });
                _romDownloadLinksByVersionName[name] = links;
                existingNames.Add(name);
            }
        }

        private void AppendC16VersionsForRomSelect(RomDeviceConfig deviceConfig)
        {
            if (UseRomBackendApi) return;
            if (!_enableC16Dynamic) return;
            var packageType = GetSelectedRomSelectPackageType();
            if (string.IsNullOrWhiteSpace(packageType)) packageType = _activeRomSelectPackageType;
            if (!string.Equals(packageType, RomSelectPackageTypeFull, StringComparison.OrdinalIgnoreCase)) return;
            if (deviceConfig == null) return;
            if ((deviceConfig.Items == null || deviceConfig.Items.Count == 0) &&
                !IsC16NativeDevice(deviceConfig.Device ?? string.Empty)) return;

            var brand = (RomSelectBrandComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(brand)) brand = _activeRomSelectBrand;
            if (!IsC16Brand(brand)) return;
            if (_c16Records == null || _c16Records.Count == 0) return;

            if (RomSelectVersionComboBox == null) return;

            var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in RomSelectVersionComboBox.Items.OfType<ComboBoxItem>())
            {
                var name = item.Content?.ToString();
                if (string.IsNullOrWhiteSpace(name)) continue;
                existingNames.Add(name.Trim());
            }

            foreach (var r in _c16Records)
            {
                var code = r.DeviceCode?.Trim();
                if (string.IsNullOrWhiteSpace(code)) continue;

                if (!DeviceNameMatchesC16(deviceConfig.Device ?? string.Empty, code)) continue;

                var baseName = string.IsNullOrWhiteSpace(r.Version) ? r.OtaVersion : r.Version;
                baseName = CleanLink(baseName);
                if (string.IsNullOrWhiteSpace(baseName)) continue;

                var name = baseName;
                var normalized = baseName.Trim();

                if (string.IsNullOrWhiteSpace(name)) continue;
                if (!IsValidVersionDisplayName(name)) continue;
                if (existingNames.Contains(name)) continue;

                RomSelectVersionComboBox.Items.Add(new ComboBoxItem { Content = name });
                var links = new List<string>();
                if (!string.IsNullOrWhiteSpace(r.DownloadCheckUrl))
                {
                    links.Add(CleanLink(r.DownloadCheckUrl));
                }
                else if (!string.IsNullOrWhiteSpace(r.FinalUrl))
                {
                    links.Add(CleanLink(r.FinalUrl));
                }

                if (links.Count > 0)
                {
                    _romSelectLinksByVersionName[name] = links;
                }

                existingNames.Add(name);
            }
        }

        private async void RomDownloadButton_Click(object sender, RoutedEventArgs e)
        {
            var homeView = this.FindName("HomeView") as Grid;
            var screenMirrorView = this.FindName("ScreenMirrorView") as Grid;
            var basicFlashView = this.FindName("BasicFlashView") as Grid;
            var fastbootVisualizationView = this.FindName("FastbootVisualizationView") as Grid;
            var hiddenEnvironmentView = this.FindName("HiddenEnvironmentView") as Grid;
            var downloadView = this.FindName("DownloadView") as Grid;
            var aboutToolView = this.FindName("AboutToolView") as Grid;
            var systemZoneView = this.FindName("SystemZoneView") as Grid;
            var oujiaFlashView = this.FindName("OujiaFlashView") as Grid;
            var autorootView = this.FindName("AutorootView") as Grid;
            var appManagementView = this.FindName("AppManagementView") as Grid;
            var androidGeneralView = this.FindName("AndroidGeneralView") as Grid;
            var payloadView = this.FindName("PayloadView") as Grid;
            var romDownloadView = this.FindName("RomDownloadview") as Grid;
            var edlFlashView = this.FindName("EdlFlashView") as Grid;
            var colorOSAssistantView = this.FindName("ColorOSAssistantView") as Grid;
            var backupAssistantView = this.FindName("BackupAssistantView") as Grid;
            var violetDownloadView = this.FindName("VioletDownloadView") as Grid;

            if (homeView != null) homeView.Visibility = Visibility.Collapsed;
            if (screenMirrorView != null) screenMirrorView.Visibility = Visibility.Collapsed;
            if (basicFlashView != null) basicFlashView.Visibility = Visibility.Collapsed;
            if (fastbootVisualizationView != null) fastbootVisualizationView.Visibility = Visibility.Collapsed;
            if (hiddenEnvironmentView != null) hiddenEnvironmentView.Visibility = Visibility.Collapsed;
            if (downloadView != null) downloadView.Visibility = Visibility.Collapsed;
            if (aboutToolView != null) aboutToolView.Visibility = Visibility.Collapsed;
            if (systemZoneView != null) systemZoneView.Visibility = Visibility.Collapsed;
            if (oujiaFlashView != null) oujiaFlashView.Visibility = Visibility.Collapsed;
            if (autorootView != null) autorootView.Visibility = Visibility.Collapsed;
            if (appManagementView != null) appManagementView.Visibility = Visibility.Collapsed;
            if (androidGeneralView != null) androidGeneralView.Visibility = Visibility.Collapsed;
            if (payloadView != null) payloadView.Visibility = Visibility.Collapsed;
            if (romDownloadView != null) romDownloadView.Visibility = Visibility.Visible;
            if (edlFlashView != null) edlFlashView.Visibility = Visibility.Collapsed;
            if (colorOSAssistantView != null) colorOSAssistantView.Visibility = Visibility.Collapsed;
            if (backupAssistantView != null) backupAssistantView.Visibility = Visibility.Collapsed;
            if (violetDownloadView != null) violetDownloadView.Visibility = Visibility.Collapsed;

            UpdateButtonStates("RomDownload");

            currentView = "RomDownload";

            // 只在首次加载时刷新选项，避免每次切换都重置内容
            if (RomBrandComboBox?.Items.Count == 0)
            {
                await RefreshRomSeriesOptionsAsync().ConfigureAwait(true);
                RefreshRomSelectBrandOptions();
                await RefreshRomSelectSeriesOptionsAsync().ConfigureAwait(true);
                AppendRomDownloadLog("信息", $"已加载{RomBrandComboBox?.Items.Count ?? 0}个机型品牌");
            }
        }

        private void RomDownloadSavePathTextBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;

            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "请选择保存路径",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };

            var currentPath = RomDownloadSavePathTextBox?.Text?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(currentPath) &&
                currentPath != "双击选择存放路径" &&
                Directory.Exists(currentPath))
            {
                dlg.SelectedPath = currentPath;
            }

            var result = dlg.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
            {
                RomDownloadSavePathTextBox.Text = dlg.SelectedPath;
            }
        }

        private static string? TryFindExtractedImage(string outputDir, string partitionName, DateTime startedAt)
        {
            if (!Directory.Exists(outputDir)) return null;
            var candidates = Directory.EnumerateFiles(outputDir, "*.img", SearchOption.TopDirectoryOnly)
                .Select(p => new FileInfo(p))
                .Where(fi => fi.LastWriteTime >= startedAt.AddSeconds(-2))
                .Where(fi =>
                {
                    var name = Path.GetFileNameWithoutExtension(fi.Name);
                    if (string.Equals(name, partitionName, StringComparison.OrdinalIgnoreCase)) return true;
                    return fi.Name.Contains(partitionName, StringComparison.OrdinalIgnoreCase);
                })
                .OrderByDescending(fi => fi.LastWriteTime)
                .FirstOrDefault();

            return candidates?.FullName;
        }

        private static bool TryFindGenericZipPartitionEntry(
            IReadOnlyList<ZipStoredEntryLocator.ZipEntryInfo> entries,
            string partitionName,
            out ZipStoredEntryLocator.ZipEntryInfo entry)
        {
            string normalized = partitionName.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                entry = default;
                return false;
            }

            string imgName = normalized.EndsWith(".img", StringComparison.OrdinalIgnoreCase)
                ? normalized
                : normalized + ".img";

            entry = entries.FirstOrDefault(e =>
                string.Equals(e.Name, imgName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFileName(e.Name), imgName, StringComparison.OrdinalIgnoreCase) ||
                e.Name.EndsWith("/" + imgName, StringComparison.OrdinalIgnoreCase) ||
                e.Name.EndsWith("\\" + imgName, StringComparison.OrdinalIgnoreCase));

            return !string.IsNullOrEmpty(entry.Name);
        }

        private static string GetGenericZipOutputPath(string outputDir, ZipStoredEntryLocator.ZipEntryInfo entry)
        {
            return Path.Combine(outputDir, entry.Name.Replace('/', Path.DirectorySeparatorChar));
        }

        private async void RomDownloadStartButton_Click(object sender, RoutedEventArgs e)
        {
            if (_romExtractInProgress) return;
            _romExtractInProgress = true;

            var button = sender as System.Windows.Controls.Button;
            if (button != null) button.IsEnabled = false;

            var startedAt = DateTime.Now;
            try
            {
                if (RomDownloadProgressBar != null)
                {
                    RomDownloadProgressBar.Value = 0;
                }

                var saveDir = RomDownloadSavePathTextBox?.Text?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(saveDir) || saveDir == "双击选择存放路径")
                {
                    AppendRomDownloadLog("错误", "请先双击选择保存路径");
                    return;
                }

                Directory.CreateDirectory(saveDir);

                var partitionName = GetSelectedComboBoxItemText(RomPartitionComboBox);
                if (string.IsNullOrWhiteSpace(partitionName)) partitionName = "boot";

                var versionName = GetSelectedComboBoxItemText(RomVersionComboBox) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(versionName)) versionName = "当前版本";

                if (UseRomBackendApi && _selectedRomDownloadLinks.Count == 0)
                {
                    var brand = (RomBrandComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? _activeRomDownloadBrand;
                    var series = (RomSeriesComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? _activeRomDownloadSeries;
                    var device = (RomDeviceComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
                    var response = await FetchRomApiDownloadLinksAsync(RomApiPackageTypeFull, brand, series, device, versionName, CancellationToken.None).ConfigureAwait(true);
                    _selectedRomDownloadLinks = PreferRomArchiveLinks(RomApiPackageTypeFull, brand, response.Links);
                    var routeDisplayName = GetRomResolveRouteDisplayName(response.ResolveRoute);
                    if (!string.IsNullOrWhiteSpace(routeDisplayName))
                    {
                        AppendRomDownloadLog("成功", $"C16直链解析成功：{routeDisplayName}");
                    }
                    _selectedRomDownloadHeaders = response.RequestHeaders != null
                        ? new Dictionary<string, string>(response.RequestHeaders, StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }

                var url = string.Equals(_activeRomDownloadBrand, "魅族", StringComparison.OrdinalIgnoreCase)
                    ? _selectedRomDownloadLinks
                        .Select(CleanLink)
                        .FirstOrDefault(x =>
                            !string.IsNullOrWhiteSpace(x) &&
                            !IsMeizuDownloadEntryLink(x) &&
                            !IsMeizuUnsignedFirmwareLink(x))
                    : null;
                url ??= _selectedRomDownloadLinks.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(url))
                {
                    AppendRomDownloadLog("错误", "当前版本没有可用下载链接");
                    return;
                }
                var requestHeaders = _selectedRomDownloadHeaders.Count > 0
                    ? new Dictionary<string, string>(_selectedRomDownloadHeaders, StringComparer.OrdinalIgnoreCase)
                    : null;
                var cancellationToken = CancellationToken.None;

                if (_enableC16Dynamic &&
                    IsC16Brand(_activeRomDownloadBrand) &&
                    url.Contains("downloadCheck", StringComparison.OrdinalIgnoreCase))
                {
                    AppendRomDownloadLog("信息", "正在解析C16直链...");
                    var resolvedUrl = await ResolveC16DownloadUrlAsync(versionName, url, cancellationToken);
                    if (string.IsNullOrWhiteSpace(resolvedUrl))
                    {
                        return;
                    }

                    url = resolvedUrl;
                }

                if (string.Equals(_activeRomDownloadBrand, "魅族", StringComparison.OrdinalIgnoreCase) &&
                    IsMeizuDownloadEntryLink(url))
                {
                    AppendRomDownloadLog("信息", "正在解析魅族下载直链...");
                    var resolvedMeizuUrl = await ResolveMeizuDownloadUrlAsync(url, requestHeaders, cancellationToken).ConfigureAwait(true);
                    if (string.IsNullOrWhiteSpace(resolvedMeizuUrl))
                    {
                        AppendRomDownloadLog("错误", "魅族下载直链解析失败");
                        return;
                    }

                    url = resolvedMeizuUrl;
                }

                url = NormalizeUrlForRequest(url);

                AppendRomDownloadLog("信息", "请求服务器ing...");
                AppendRomDownloadLog("信息", $"开始下载（{versionName}） > {partitionName}.img");

                void UpdateProgress(double percent)
                {
                    if (RomDownloadProgressBar == null) return;
                    var p = Math.Max(0, Math.Min(100, percent));
                    RomDownloadProgressBar.Value = p;
                }

                var unzipProgress = new Progress<double>(p => UpdateProgress(p));
                var extractProgress = new Progress<(long DoneOps, long TotalOps)>(p =>
                {
                    if (p.TotalOps <= 0) return;
                    double percent = (double)p.DoneOps * 100d / p.TotalOps;
                    UpdateProgress(percent);
                });

                await using var sourceReader = await PayloadProcessing.OpenSourceAsync(url, cancellationToken, requestHeaders).ConfigureAwait(true);
                if (sourceReader is HttpRangeReader)
                {
                    try
                    {
                        var entries = await ZipStoredEntryLocator.ListEntriesAsync(sourceReader, cancellationToken).ConfigureAwait(true);
                        bool hasPayload = entries.Any(e =>
                            !string.IsNullOrEmpty(e.Name) &&
                            e.Name.EndsWith("payload.bin", StringComparison.OrdinalIgnoreCase));

                        if (!hasPayload)
                        {
                            if (!TryFindGenericZipPartitionEntry(entries, partitionName, out var matchedEntry))
                            {
                                throw new InvalidOperationException($"ZIP 包中未找到 {partitionName}.img");
                            }

                            string genericZipOutputPath = GetGenericZipOutputPath(saveDir, matchedEntry);
                            string? genericZipOutputDir = Path.GetDirectoryName(genericZipOutputPath);
                            if (!string.IsNullOrEmpty(genericZipOutputDir))
                            {
                                Directory.CreateDirectory(genericZipOutputDir);
                            }

                            var zipProgress = new Progress<long>(doneBytes =>
                            {
                                long totalBytes = Math.Max(1L, matchedEntry.CompressedSize);
                                double percent = Math.Min(100d, doneBytes * 100d / totalBytes);
                                UpdateProgress(percent);
                            });

                            using (var outFs = new FileStream(genericZipOutputPath, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 20, FileOptions.SequentialScan))
                            {
                                await ZipStoredEntryLocator.ExtractEntryAsync(sourceReader, matchedEntry, outFs, cancellationToken, zipProgress).ConfigureAwait(true);
                            }

                            UpdateProgress(100);
                            AppendRomDownloadLog("成功", $"下载完成：{genericZipOutputPath}");
                            return;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        // 不是 ZIP 目录或远程 ZIP 无法直接列目录时，继续按 payload 流程处理。
                    }
                }

                var (payloadReader, _) = await PayloadProcessing.OpenPayloadReaderAsync(
                        sourceReader,
                        cancellationToken,
                        log: s =>
                        {
                            if (string.IsNullOrWhiteSpace(s)) return;
                            if (s.StartsWith("开始导出:", StringComparison.Ordinal) ||
                                s.StartsWith("完成导出:", StringComparison.Ordinal))
                            {
                                return;
                            }
                            AppendRomDownloadLog("信息", s);
                        },
                        extractProgress: unzipProgress,
                        eagerExtract: false)
                    .ConfigureAwait(true);

                await using IRandomAccessReader? payloadReaderScope = ReferenceEquals(payloadReader, sourceReader) ? null : payloadReader;
                var ctx = await PayloadProcessing.ReadManifestAsync(payloadReader, cancellationToken).ConfigureAwait(true);

                if (ctx.Reader is ZipPayloadLazyReader lazy && !lazy.IsExtracted)
                {
                    AppendRomDownloadLog("信息", "解压 payload.bin 中...");
                    await lazy.EnsureExtractedAsync(unzipProgress, cancellationToken).ConfigureAwait(true);
                    UpdateProgress(0);
                }

                var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { partitionName };
                await PayloadProcessing.ExtractPartitionsAsync(
                        ctx,
                        selected,
                        saveDir,
                        Environment.ProcessorCount,
                        log: s =>
                        {
                            if (string.IsNullOrWhiteSpace(s)) return;
                            if (s.StartsWith("开始导出:", StringComparison.Ordinal) ||
                                s.StartsWith("完成导出:", StringComparison.Ordinal))
                            {
                                return;
                            }
                            AppendRomDownloadLog("信息", s);
                        },
                        progress: extractProgress,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(true);

                UpdateProgress(100);
                var extractedPath = TryFindExtractedImage(saveDir, partitionName, startedAt);
                if (!string.IsNullOrWhiteSpace(extractedPath))
                {
                    AppendRomDownloadLog("成功", $"下载完成：{extractedPath}");
                }
                else
                {
                    AppendRomDownloadLog("成功", $"下载完成：{saveDir}");
                }
            }
            catch (Exception ex)
            {
                AppendRomDownloadLog("错误", $"提取异常：{ex.Message}");
            }
            finally
            {
                if (button != null) button.IsEnabled = true;
                _romExtractInProgress = false;
            }
        }
    }
}
