using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Navigation;
using FanControl.UI.Localization;

namespace FanControl.UI.Views;

public partial class AboutView : UserControl
{
    private const string UpdateRepo = "anlerways/Bluetooth-FanControl";

    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FanControl");
        return client;
    }

    public AboutView()
    {
        InitializeComponent();

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        VersionText.Text = $"FanControl v{version} · {LocalizationManager.Get("About.Subtitle")}";
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch
        {
            // 打开浏览器失败时忽略
        }

        e.Handled = true;
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        SetStatus(LocalizationManager.Get("About.Checking"));

        try
        {
            var (tag, url) = await FetchLatestReleaseAsync();
            if (string.IsNullOrEmpty(tag) || string.IsNullOrEmpty(url))
            {
                SetStatus(LocalizationManager.Get("About.NoReleases"));
                return;
            }

            var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
            if (!IsNewerVersion(tag, current))
            {
                SetStatus(LocalizationManager.Get("About.UpToDate"));
                return;
            }

            // 发现新版本：显示版本号 + 可点击的下载页链接
            UpdateStatus.Inlines.Clear();
            UpdateStatus.Inlines.Add(new Run(string.Format(LocalizationManager.Get("About.NewVersion"), tag) + " "));
            var link = new Hyperlink(new Run(LocalizationManager.Get("About.OpenRelease")))
            {
                NavigateUri = new Uri(url),
            };
            link.RequestNavigate += Hyperlink_RequestNavigate;
            UpdateStatus.Inlines.Add(link);
        }
        catch (Exception ex)
        {
            SetStatus(string.Format(LocalizationManager.Get("About.UpdateFailed"), ex.Message));
        }
    }

    private void SetStatus(string text)
    {
        UpdateStatus.Inlines.Clear();
        UpdateStatus.Inlines.Add(new Run(text));
    }

    /// <summary>查询 GitHub 最新 Release（tag_name / html_url）；无发布或请求失败返回空。</summary>
    private static async Task<(string? Tag, string? Url)> FetchLatestReleaseAsync()
    {
        using var response = await Http.GetAsync($"https://api.github.com/repos/{UpdateRepo}/releases/latest");
        if (!response.IsSuccessStatusCode)
        {
            return (null, null);
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        var tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() : null;
        var url = root.TryGetProperty("html_url", out var urlElement) ? urlElement.GetString() : null;
        return (tag, url);
    }

    /// <summary>比较 Release 标签（如 v1.2.3）与当前程序版本。</summary>
    private static bool IsNewerVersion(string? tag, Version current)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var tagVersion))
        {
            return false;
        }

        return tagVersion > current;
    }
}
