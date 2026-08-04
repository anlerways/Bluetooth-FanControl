using System.Windows.Markup;

namespace FanControl.UI.Localization;

/// <summary>XAML 翻译标记：Text="{loc:Translate Key}"。</summary>
public sealed class TranslateExtension : MarkupExtension
{
    public TranslateExtension()
    {
    }

    public TranslateExtension(string key)
    {
        Key = key;
    }

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        LocalizationManager.Get(Key);
}
