using FanControl.UI.Localization;

namespace FanControl.Tests;

public class LocalizationTests
{
    [Fact]
    public void ChineseResources_ReturnChinese()
    {
        LocalizationManager.SetLanguage("zh-CN");

        Assert.Equal("仪表盘", LocalizationManager.Get("Nav.Dashboard"));
    }

    [Fact]
    public void EnglishResources_ResolveFromSatellite()
    {
        LocalizationManager.SetLanguage("en-US");

        Assert.Equal("Dashboard", LocalizationManager.Get("Nav.Dashboard"));
        Assert.Equal("Settings", LocalizationManager.Get("Page.Settings"));
        Assert.Equal("Save curve", LocalizationManager.Get("Curve.Save"));
    }
}
