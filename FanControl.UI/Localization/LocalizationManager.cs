using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Resources;

namespace FanControl.UI.Localization;

/// <summary>
/// 多语言支持：启动时把中英两套资源直接读入内存字典，
/// 不依赖 ResourceManager 的卫星程序集探测（该机制在本项目构建下不可靠）。
/// </summary>
public static class LocalizationManager
{
    private const string ResourceName = "FanControl.UI.Resources.Strings.resources";
    private static readonly Dictionary<string, string> Chinese = Load(GetResourceStream(null));
    private static readonly Dictionary<string, string> English = Load(GetResourceStream("en-US"));
    private static string _language = "zh-CN";

    public static string Get(string key)
    {
        if (_language == "en-US"
            && English.TryGetValue(key, out var english))
        {
            return english;
        }

        return Chinese.TryGetValue(key, out var chinese) ? chinese : key;
    }

    public static void SetLanguage(string cultureName)
    {
        // 显式静态语言状态：不依赖环境文化，避免线程/异步上下文导致中英混杂
        _language = cultureName.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "en-US" : "zh-CN";

        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
        }
        catch
        {
            // 未知语言保持默认
        }
    }

    public static string CurrentLanguage => _language;

    private static Stream? GetResourceStream(string? culture)
    {
        var assembly = typeof(LocalizationManager).Assembly;

        if (culture is not null)
        {
            try
            {
                var satellitePath = Path.Combine(
                    AppContext.BaseDirectory,
                    culture,
                    $"{assembly.GetName().Name}.resources.dll");
                var satellite = Assembly.LoadFrom(satellitePath);
                return satellite.GetManifestResourceStream(ResourceName);
            }
            catch
            {
                return null;
            }
        }

        return assembly.GetManifestResourceStream(ResourceName);
    }

    private static Dictionary<string, string> Load(Stream? stream)
    {
        var dictionary = new Dictionary<string, string>(StringComparer.Ordinal);
        if (stream is null)
        {
            return dictionary;
        }

        using (stream)
        using (var reader = new ResourceReader(stream))
        {
            foreach (DictionaryEntry entry in reader)
            {
                if (entry.Value is string value)
                {
                    dictionary[(string)entry.Key] = value;
                }
            }
        }

        return dictionary;
    }
}
