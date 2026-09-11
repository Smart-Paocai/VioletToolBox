using System.Windows;
using SuperFix.Core;

namespace WpfApp1.SuperRepair;

public partial class SuperSizeMismatchWindow : Window
{
    public SuperSizeMismatchWindow(
        string selectedDevice,
        ulong reportedSuperSize,
        ulong definitionSuperSize)
    {
        InitializeComponent();
        DeviceText.Text = string.IsNullOrWhiteSpace(selectedDevice)
            ? "未声明"
            : selectedDevice.Replace('_', ' ').Trim();
        DeviceSizeText.Text = reportedSuperSize == 0
            ? "未提供"
            : $"0x{reportedSuperSize:X} ({BinaryHelpers.FormatBytes(reportedSuperSize)})";
        DefinitionSizeText.Text =
            $"0x{definitionSuperSize:X} ({BinaryHelpers.FormatBytes(definitionSuperSize)})";
    }

    private void ContinueButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
