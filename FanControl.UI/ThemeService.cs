using System.Windows;
using System.Windows.Media;
using FanControl.Shared.Enums;
using Microsoft.Win32;

namespace FanControl.UI;

/// <summary>主题切换：在 App 资源中调整深浅色字典顺序，并同步 Windows 暗色标题栏。</summary>
public static class ThemeService
{
    public static bool IsDark { get; private set; } = true;

    public static void ApplyThemeType(ThemeType theme)
    {
        var dark = theme switch
        {
            ThemeType.Dark => true,
            ThemeType.Light => false,
            _ => IsSystemDark(),
        };
        IsDark = dark;

        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        var merged = app.Resources.MergedDictionaries;
        var darkDict = merged.FirstOrDefault(d =>
            d.Source?.OriginalString.EndsWith("Dark.xaml", StringComparison.OrdinalIgnoreCase) == true);
        var lightDict = merged.FirstOrDefault(d =>
            d.Source?.OriginalString.EndsWith("Light.xaml", StringComparison.OrdinalIgnoreCase) == true);

        if (darkDict is not null)
        {
            merged.Remove(darkDict);
        }

        if (lightDict is not null)
        {
            merged.Remove(lightDict);
        }

        // WPF 合并字典查找顺序为"最后一个优先"，目标主题必须放在列表末尾
        if (dark)
        {
            if (lightDict is not null) merged.Insert(0, lightDict);
            if (darkDict is not null) merged.Insert(1, darkDict);
        }
        else
        {
            if (darkDict is not null) merged.Insert(0, darkDict);
            if (lightDict is not null) merged.Insert(1, lightDict);
        }
    }

    public static Brush Brush(string key) =>
        (Brush)Application.Current.Resources[key];

    public static Color Color(string key) =>
        ((SolidColorBrush)Application.Current.Resources[key]).Color;

    private static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");

            // 与 G-Helper 一致：注册表值缺失时默认浅色，避免"跟随系统"把浅色界面变成深色
            if (value is not int i)
            {
                return false;
            }

            return i <= 0;
        }
        catch
        {
            return false;
        }
    }
}
