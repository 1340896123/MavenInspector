using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace ErdcServerMcp
{
    /// <summary>
    /// 全局 JSON 序列化配置
    /// </summary>
    public static class JsonOptions
    {
        /// <summary>
        /// 默认序列化选项 - 支持中文字符、忽略循环引用、忽略 null 值、格式化输出
        /// </summary>
        public static readonly JsonSerializerOptions Default = new()
        {
            // 支持中文字符（不转义 Unicode）
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            // 忽略循环引用
            ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
            // 忽略 null 值
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            // 驼峰式命名（首字母小写）
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            // 格式化输出（缩进）
            WriteIndented = true
        };

        /// <summary>
        /// 反序列化选项 - 大小写不敏感
        /// </summary>
        public static readonly JsonSerializerOptions Deserialize = new()
        {
            // 大小写不敏感
            PropertyNameCaseInsensitive = true,
            // 驼峰式命名（首字母小写）
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>
        /// 紧凑序列化选项 - 不格式化，用于日志或网络传输
        /// </summary>
        public static readonly JsonSerializerOptions Compact = new()
        {
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            // 驼峰式命名（首字母小写）
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }
}
