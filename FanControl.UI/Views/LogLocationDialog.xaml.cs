using System.IO;
using System.Windows;
using FanControl.Shared.Enums;
using FanControl.Shared.Models;

namespace FanControl.UI.Views;

/// <summary>首次启动引导：选择日志文件存放位置（用户数据目录 data / 安装目录）。</summary>
public partial class LogLocationDialog : Window
{
    public SystemConfig? Result { get; private set; }

    public LogLocationDialog(string installDirectory, string defaultUserDataDirectory)
    {
        InitializeComponent();
        DataPathText.Text = Path.Combine(defaultUserDataDirectory, "Logs");
        InstallPathText.Text = Path.Combine(installDirectory, "Logs");
    }

    private void Data_Click(object sender, RoutedEventArgs e)
    {
        Result = new SystemConfig { LogLocation = ConfigLocation.UserData };
        DialogResult = true;
    }

    private void Install_Click(object sender, RoutedEventArgs e)
    {
        Result = new SystemConfig { LogLocation = ConfigLocation.InstallDirectory };
        DialogResult = true;
    }
}
