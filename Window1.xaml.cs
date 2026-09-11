using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net;
using MessageBox = System.Windows.MessageBox;

namespace SmartTool
{
    /// <summary>
    /// Window1.xaml 的交互逻辑
    /// </summary>
    public partial class Window1 : Window
    {
        public Window1()
        {
            InitializeComponent();
        }

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // 关闭当前窗口，不影响主窗口
            this.Close();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private async void WiredToWirelessButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string port = (PortTextBox.Text ?? "").Trim();
                string ip = (IpTextBox.Text ?? "").Trim();

                if (string.IsNullOrWhiteSpace(port) || !int.TryParse(port, out _))
                {
                    MessageBox.Show("请输入有效的端口号，例如 5555。", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (string.IsNullOrWhiteSpace(ip))
                {
                    MessageBox.Show("请输入有效的 IPv4 地址。", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                int codeTcpip = await RunAdbAsync($"tcpip {port}");
                if (codeTcpip != 0)
                {
                    MessageBox.Show("请确保工具已连接正确设备", "失败", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                int codeConnect = await RunAdbAsync($"connect {ip}:{port}");
                if (codeConnect == 0)
                {
                    MessageBox.Show($"已连接到 {ip}:{port}", "连接成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("执行 adb connect 失败，请确认IP地址与端口可达。", "执行失败", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"执行无线连接过程时发生错误：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private Task<int> RunAdbAsync(string args)
        {
            return Task.Run(() =>
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string platformTools = System.IO.Path.Combine(baseDir, "platform-tools");
                string adbPath = System.IO.Path.Combine(platformTools, "adb.exe");

                var psi = new ProcessStartInfo
                {
                    FileName = adbPath,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = platformTools
                };

                using (var p = new Process { StartInfo = psi })
                {
                    if (!System.IO.File.Exists(adbPath))
                    {
                        throw new System.IO.FileNotFoundException("未找到 adb.exe，请将 platform-tools 放在程序同目录。", adbPath);
                    }
                    p.Start();
                    string output = p.StandardOutput.ReadToEnd();
                    string error = p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    return p.ExitCode;
                }
            });
        }


    }
}
