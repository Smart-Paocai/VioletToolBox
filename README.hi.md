# वायलेट टूलबॉक्स（VioletToolBox）

Android डिवाइस विकास, रखरखाव और फ्लैशिंग के लिए बहु-कार्यात्मक टूलबॉक्स। रिलीज़ प्रोग्राम का नाम `VioletToolBox` और UI का नाम “紫罗兰工具箱” है। इसमें सामान्य ADB / Fastboot कार्यों सहित लगभग 110 सुविधाएँ हैं।

**भाषाएँ / Languages:** [简体中文](README.md) · [繁體中文](README.zh-TW.md) · [English](README.en.md) · [日本語](README.ja.md) · [हिन्दी](README.hi.md) · [Tiếng Việt](README.vi.md) · [한국어](README.ko.md)

> [!WARNING]
> फ्लैशिंग, अनलॉकिंग, पार्टिशन पढ़ना/लिखना, EDL और Root सुविधाएँ डेटा हानि, डिवाइस ब्रिक होने या वारंटी पर प्रभाव का कारण बन सकती हैं। मॉडल, फर्मवेयर, पार्टिशन और मोड जाँचें; बैकअप लें; और अपने जोखिम पर उपयोग करें।

## सुविधाएँ

1. **होम और कनेक्शन** — डिवाइस स्थिति, रीबूट, वायरलेस डिबगिंग, स्लॉट, CMD/डिवाइस मैनेजर।
2. **स्क्रीन मिररिंग** — scrcpy आधारित वर्चुअल कुंजियाँ, ऑटो मिररिंग, आकार, FPS, गुणवत्ता, शीर्षक।
3. **बेसिक फ्लैशिंग** — `boot`/`init_boot`, अनलॉक/रीलॉक, Xiaomi, फॉर्मेट, FRP, ADB, DDR, OCDT।
4. **विज़ुअल फ्लैशिंग** — ADB/Fastboot पार्टिशन तालिका, पढ़ना/लिखना/मिटाना, GPT, `.bin`, XML, Xiaomi स्क्रिप्ट, Slot A।
5. **OPPO/OnePlus फ्लैशिंग** — URL एक्सट्रैक्शन, सामान्य/फोर्स/AB/FastbootD, ARB, Super रिपेयर, Payload, FB/FBD/ऑटो रिकवरी।
6. **EDL फ्लैशिंग** — Qualcomm 9008 Sahara/Firehose, पार्टिशन, GPT, LUN, बैकअप, OEM, स्लॉट, सॉफ्टवेयर जानकारी।
7. **डाउनग्रेड सहायक** — OPPO/OnePlus OTA, लिंक कॉपी और संस्करण विश्लेषण।
8. **मॉड्यूल केंद्र** — PC से बैच इंस्टॉल, Root मैनेजर पहचान और Root छिपाना।
9. **रिज़्यूमे डाउनलोड** — OPLUS ColorOS 16 OTA का मल्टी-थ्रेड डाउनलोड।
10. **फ़ाइल ट्रांसफर** — ADB बैच ट्रांसफर, APK इंस्टॉल, आंतरिक स्टोरेज/Root डायरेक्टरी।
11. **ऑफ़लाइन पैच** — `boot`/`init_boot`, OnePlus Root, GKI, Lenovo AVB, `vbmeta` विश्लेषण।
12. **ऐप प्रबंधन** — सूची, फ्रीज़/अनफ्रीज़, APK एक्सट्रैक्ट, डेटा मिटाना और अनइंस्टॉल।
13. **Android सामान्य टूल** — OPLUS Super/OFP, OFP/OPS, स्क्रिप्ट, Payload रूपांतरण, TWRP और OCDT।
14. **Payload** — स्थानीय या URL ZIP की जानकारी और एक्सट्रैक्शन।
15. **बैकअप** — फोटो, वीडियो और संपर्क।
16. **फ्लैशिंग संसाधन** — सामान्य फ़ाइलें और संसाधन डाउनलोड।
17. **ROM केंद्र** — OTA, Fastboot पैकेज और Boot इमेज; डेवलपर API का जिम्मेदारी से उपयोग करें।

## बिल्ड

Windows 10/11 और .NET 8 SDK या बाद का संस्करण आवश्यक है।

```powershell
dotnet restore SmartTool.csproj
dotnet build SmartTool.csproj -c Debug
dotnet publish SmartTool.csproj -c Release -r win-x64 --self-contained false
```

## डाउनलोड और चलाना

[Releases](../../releases) से पूरा पैकेज डाउनलोड करके निकालें और `VioletToolBox.exe` चलाएँ। DLL, `exe`, `avbtool`, PEM या अन्य निर्भर फ़ाइलें न हटाएँ। स्रोत से बनाई रिलीज़ के लिए `VioletToolBox-win-x64.zip` की निर्भरताएँ Release डायरेक्टरी में रखें।

## लाइसेंस

GNU General Public License v3.0 या बाद के अंतर्गत। [LICENSE](LICENSE) देखें।
