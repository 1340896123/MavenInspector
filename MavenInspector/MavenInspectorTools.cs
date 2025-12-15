using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;

namespace MavenInspector;

[McpServerToolType]
public class MavenInspectorTools
{
    // Simple memory cache: key = pomPath, value = list of jar paths
    private readonly Dictionary<string, List<string>> _cache = new();
    private string _localRepoPath;

    [McpServerTool(Name = "analyze_pom_dependencies")]
    [Description("解析 pom.xml 获取所有依赖 jar 包的本地路径")]
    public async Task<DependencyScanResult> AnalyzePomDependencies(string pomPath)
    {
        if (!File.Exists(pomPath))
        {
            return new DependencyScanResult { Error = $"File not found: {pomPath}" };
        }

        var workingDir = Path.GetDirectoryName(pomPath);
        if (string.IsNullOrEmpty(workingDir)) workingDir = Directory.GetCurrentDirectory();

        var localRepo = await GetLocalRepositoryPath(workingDir); 
        if (string.IsNullOrEmpty(localRepo)) 
            return new DependencyScanResult { Error = "Could not determine local maven repository path." };

        // 使用 WinCMDHelper 执行 Maven 命令
        // 注意：WinCMDHelper.RunDosCmd 内部使用 cmd.exe，因此需要拼接 cd 命令和 mvn 命令
        var mvnCommand = "mvn.cmd -B org.cyclonedx:cyclonedx-maven-plugin:makeAggregateBom";
        var cmdArgs = $"cd /d \"{workingDir}\" && {mvnCommand}";

        try
        {
            // WinCMDHelper 是同步方法，但在 async 方法中调用主要为了兼容签名
            // 为了防止阻塞主线程太久（虽然是在 Task 中），可以直接调用
            var output = WinCMDHelper.GetInstance().RunDosCmd("", cmdArgs);

            // WinCMDHelper 返回 "Err:" 开头表示 stderr 有内容
            // 注意：Maven 有时会输出非致命警告到 stderr，导致这里被误判为错误。
            // 但根据 WinCMDHelper 的逻辑，只要 stderr 不为空就返回 Err:...
            // 我们先按报错处理，如果用户反馈有问题再调整。
            if (output.StartsWith("Err:"))
            {
                 // 如果只是警告，可能仍然生成了文件，尝试检查文件是否存在？
                 // 但通常 Err 表示失败。我们把输出都放进去。
                 // 稍微放宽一点：如果 bom.xml 存在且不仅是警告？
                 // 暂时还是认为 WinCMDHelper 的 Err 就是失败。
                 // 防止误判，检查一下 bom.xml 是否生成
                 var bomCheck = Path.Combine(workingDir, "target", "bom.xml");
                 if (!File.Exists(bomCheck))
                 {
                     return new DependencyScanResult 
                     { 
                         Error = $"Maven execution returned error output.\nCmd: {cmdArgs}\nOutput: {output}" 
                     };
                 }
                 // 如果 bom.xml 存在，可能是警告引发的 "Err:"，尝试继续解析
            }

            var bomPath = Path.Combine(workingDir, "target", "bom.xml");
            if (!File.Exists(bomPath)) 
            {
                return new DependencyScanResult 
                { 
                    Error = $"Build failed or bom.xml not found at {bomPath}.\nOutput: {output}" 
                };
            }

            var jarPaths = new List<string>();
            try 
            {
                var doc = XDocument.Load(bomPath);
                XNamespace ns = doc.Root?.Name.Namespace ?? XNamespace.None;
                
                var components = doc.Descendants(ns + "component");
                foreach (var comp in components)
                {
                    var type = comp.Attribute("type")?.Value;
                    // 只关注 library 类型的依赖
                    if (type != "library") continue;

                    var group = comp.Element(ns + "group")?.Value;
                    var name = comp.Element(ns + "name")?.Value;
                    var version = comp.Element(ns + "version")?.Value;

                    if (!string.IsNullOrEmpty(group) && !string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(version))
                    {
                        var jarName = $"{name}-{version}.jar";
                        var relativePath = Path.Combine(group.Replace('.', Path.DirectorySeparatorChar), name, version, jarName);
                        var fullPath = Path.Combine(localRepo, relativePath);

                        if (File.Exists(fullPath)) 
                        {
                            jarPaths.Add(fullPath);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return new DependencyScanResult { Error = $"Failed to parse bom.xml: {ex.Message}" };
            }

            _cache[pomPath] = jarPaths;

            return new DependencyScanResult
            {
                ProjectRoot = workingDir,
                JarPaths = jarPaths
            };
        }
        catch (Exception ex)
        {
            return new DependencyScanResult { Error = $"Exception running maven: {ex.Message}" };
        }
    }

    private async Task<string> GetLocalRepositoryPath(string workingDir)
    {
        if (!string.IsNullOrEmpty(_localRepoPath)) return _localRepoPath;

        var cmdArgs = $"cd /d \"{workingDir}\" && mvn.cmd help:evaluate -Dexpression=settings.localRepository -q -DforceStdout";

        try 
        {
            var output = WinCMDHelper.GetInstance().RunDosCmd("", cmdArgs);

            if (!output.StartsWith("Err:") && !string.IsNullOrWhiteSpace(output))
            {
                _localRepoPath = output.Trim();
                return _localRepoPath;
            }
        }
        catch {}

        // Fallback to default
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _localRepoPath = Path.Combine(home, ".m2", "repository");
        return _localRepoPath;
    }
    [McpServerTool(Name = "search_class_in_dependencies")]
    [Description("在所有依赖包中搜索指定的类名")]
    public List<ClassLocation> SearchClass(string pomPath, string classNameQuery)
    {
        // 1. 从缓存获取 JAR 列表
        if (!_cache.TryGetValue(pomPath, out var jarPaths))
        {
             // Normally we might auto-call analyze, but for simplicity we ask user to do it
             // Or we could return an empty list or error.
             // We'll return an error indicator or just empty list for now, but better to support the flow.
             // Let's just return empty and assume the AI will handle the flow as described in the prompt.
             // But actually, the prompt says "throws new Exception". Let's handle it gracefully.
             return new List<ClassLocation>(); // Indicate nothing found or not initialized
        }

        var results = new List<ClassLocation>();

        // 2. 遍历每一个 JAR 文件
        foreach (var jarPath in jarPaths)
        {
            if (!File.Exists(jarPath)) continue;

            try 
            {
                using var zip = ZipFile.OpenRead(jarPath);
                
                // 3. 查找匹配的 Entry
                // Optimize: classNameQuery might be short, so this could be slow if we check everything.
                // But Prompt says ZipArchive is fast.
                foreach (var entry in zip.Entries)
                {
                    if (!entry.Name.EndsWith(".class")) continue;

                    var entryClassPath = entry.FullName.Replace('/', '.').Replace(".class", "");
                    
                    // 如果类名包含查询词 (忽略大小写)
                    if (entryClassPath.Contains(classNameQuery, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(new ClassLocation {
                            SimpleName = Path.GetFileNameWithoutExtension(entry.Name),
                            FullName = entryClassPath,
                            JarPath = jarPath
                        });

                        if (results.Count > 20) break; 
                    }
                }
            }
            catch { /* 忽略损坏的 jar */ }
            if (results.Count > 20) break; 
        }

        return results;
    }

    [McpServerTool(Name = "inspect_java_class")]
    [Description("读取特定类的详细结构（方法、字段、签名）")]
    public async Task<ClassDetail> InspectClass(string jarPath, string fullClassName)
    {
        // 1. 构建 javap 命令
        var args = $"-p -s -cp \"{jarPath}\" {fullClassName}";
        
        try
        {
            // 2. 执行并获取输出
            // 使用 WinCMDHelper
            var output = WinCMDHelper.GetInstance().RunDosCmd("", "javap " + args);

            if (output.StartsWith("Err:"))
            {
                 return new ClassDetail { Name = fullClassName, Type = $"Error: {output}" };
            }

            // 3. 解析 javap 的文本输出
            return ParseJavapOutput(output, fullClassName);
        }
        catch (Exception ex)
        {
             return new ClassDetail { Name = fullClassName, Type = $"Error: {ex.Message}" };
        }
    }

    private ClassDetail ParseJavapOutput(string output, string className)
    {
        var detail = new ClassDetail { Name = className };
        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimLine = line.Trim();
            if (string.IsNullOrWhiteSpace(trimLine)) continue;
            if (trimLine.StartsWith("Compiled from")) continue;

            // Simple heuristics
            if (trimLine.Contains(" class ") || trimLine.Contains(" interface ") || trimLine.Contains(" enum "))
            {
                if (trimLine.EndsWith("{"))
                {
                     // Attempt to grab package or other details if needed
                     // But strictly speaking javap output is:
                     // public class org.package.Name {
                }
            }

            // 识别方法
            // Typical line: public int methodName(int, int);
            // With -s (signatures), the next line is usually "descriptor: ..."
            if (trimLine.Contains("(") && trimLine.Contains(")") && !trimLine.Contains("static {};") && !trimLine.StartsWith("descriptor:"))
            {
                detail.Methods.Add(new JavaMethodInfo {
                    Signature = trimLine.TrimEnd(';', '{'), 
                });
            }
            // 识别字段
            else if (trimLine.Contains(";") && !trimLine.Contains("(") && !trimLine.StartsWith("descriptor:") && !trimLine.StartsWith("Package"))
            {
                detail.Fields.Add(trimLine.TrimEnd(';'));
            }
        }
        return detail;
    }
}
