using System.Text.Json;
using System.Text.Json.Serialization;

namespace FanControl.Shared.Contracts;

/// <summary>前后台统一使用的 JSON 序列化选项（camelCase、忽略 null）。</summary>
public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
