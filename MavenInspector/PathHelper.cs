using System.Text;

namespace MavenInspector;

public static class PathHelper
{
    /// <summary>
    /// 规范化路径用于缓存键
    /// 统一使用正斜杠 /，统一为小写，去除多余的分隔符，去除末尾分隔符
    /// </summary>
    public static string NormalizeForCache(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        // 统一路径分隔符为正斜杠
        var normalized = NormalizePathSeparators(path);

        // 转换为小写（实现大小写不敏感）
        normalized = normalized.ToLowerInvariant();

        // 去除末尾的分隔符
        normalized = normalized.TrimEnd('/');

        return normalized;
    }

    /// <summary>
    /// 统一路径分隔符为正斜杠
    /// </summary>
    public static string NormalizePathSeparators(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        var sb = new StringBuilder(path.Length);

        for (int i = 0; i < path.Length; i++)
        {
            char c = path[i];

            // 将反斜杠转换为正斜杠
            if (c == '\\')
            {
                // 检查是否需要添加正斜杠（避免重复）
                if (sb.Length == 0 || sb[sb.Length - 1] != '/')
                {
                    sb.Append('/');
                }
            }
            // 如果是正斜杠，也检查是否重复
            else if (c == '/')
            {
                if (sb.Length == 0 || sb[sb.Length - 1] != '/')
                {
                    sb.Append('/');
                }
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
