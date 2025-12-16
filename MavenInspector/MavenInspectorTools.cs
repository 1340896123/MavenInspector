using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Linq;

namespace MavenInspector;

[McpServerToolType]
public class MavenInspectorTools
{
    // Persistent cache: key = pomPath, value = CachedDependencyInfo
    private Dictionary<string, CachedDependencyInfo> _cache;
    private readonly string _cacheFilePath;
    private string _localRepoPath;

    public MavenInspectorTools()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var navDir = Path.Combine(appData, "MavenInspector");
        Directory.CreateDirectory(navDir);
        _cacheFilePath = Path.Combine(navDir, "dependency_cache.json");
        LoadCache();
    }

    private class CachedDependencyInfo
    {
        public DateTime LastModified { get; set; }
        public List<string> JarPaths { get; set; } = new();
    }

    private void LoadCache()
    {
        try
        {
            using (var mutex = new Mutex(false, "Global\\MavenInspector_DependencyCache_Mutex")) 
            {
               try
               { 
                   mutex.WaitOne(3000); // 3 sec timeout
                   if (File.Exists(_cacheFilePath))
                   {
                       var json = File.ReadAllText(_cacheFilePath);
                       _cache = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, CachedDependencyInfo>>(json) 
                                ?? new Dictionary<string, CachedDependencyInfo>();
                   }
                   else
                   {
                       _cache = new Dictionary<string, CachedDependencyInfo>();
                   }
               }
               finally
               {
                   mutex.ReleaseMutex();
               }
            }
        }
        catch
        {
            _cache = new Dictionary<string, CachedDependencyInfo>();
        }
    }

    private void SaveCache()
    {
        try
        {
            using (var mutex = new Mutex(false, "Global\\MavenInspector_DependencyCache_Mutex"))
            {
                try
                {
                    mutex.WaitOne(3000);
                    var json = System.Text.Json.JsonSerializer.Serialize(_cache);
                    File.WriteAllText(_cacheFilePath, json);
                }
                finally
                {
                    mutex.ReleaseMutex();
                }
            }
        }
        catch { /* Ignore save errors */ }
    }


    private async Task<DependencyScanResult> AnalyzePomDependenciesByPom([Description("pom.xml 文件的绝对路径")] string pomPath)
    {
        if (!File.Exists(pomPath))
        {
            return new DependencyScanResult { Error = $"File not found: {pomPath}" };
        }

        var workingDir = Path.GetDirectoryName(pomPath);
        if (string.IsNullOrEmpty(workingDir)) workingDir = Directory.GetCurrentDirectory();

        // Check cache validity
        try
        {
            var currentLastWrite = File.GetLastWriteTimeUtc(pomPath);
            if (_cache.TryGetValue(pomPath, out var cachedInfo))
            {
                if (cachedInfo.LastModified == currentLastWrite && cachedInfo.JarPaths != null && cachedInfo.JarPaths.Count > 0)
                {
                    return new DependencyScanResult
                    {
                        ProjectRoot = workingDir,
                        JarPaths = cachedInfo.JarPaths
                    };
                }
            }
        }
        catch { /* Ignore cache errors and re-analyze */ }

        var localRepo = await GetLocalRepositoryPath(workingDir);
        if (string.IsNullOrEmpty(localRepo))
            return new DependencyScanResult { Error = "Could not determine local maven repository path." };

        // 使用 WinCMDHelper 执行 Maven 命令 (PowerShell)
        // Check environment variables for custom mvn path and settings
        var mvnPath = Environment.GetEnvironmentVariable("MavenInspectorMavnPath");
        var mvnCmd = string.IsNullOrWhiteSpace(mvnPath) ? "mvn" : $"& '{mvnPath}'"; // Use call operator for paths

        var settingsPath = Environment.GetEnvironmentVariable("MavenInspectorMavnSettingPath");
        var settingsArg = string.IsNullOrWhiteSpace(settingsPath) ? "" : $" -s '{settingsPath}'";

        var mvnCommand = $"{mvnCmd}{settingsArg} -B org.cyclonedx:cyclonedx-maven-plugin:makeAggregateBom";
        // Use Set-Location and ; for PowerShell
        var cmdArgs = $"Set-Location -Path '{workingDir}'; {mvnCommand}";

        try
        {
            // WinCMDHelper 是同步方法，但在 async 方法中调用主要为了兼容签名
            // 为了防止阻塞主线程太久（虽然是在 Task 中），可以直接调用
            var output = WinCMDHelper.GetInstance().RunPowerShell(cmdArgs);

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

            _cache[pomPath] = new CachedDependencyInfo
            {
                LastModified = File.GetLastWriteTimeUtc(pomPath),
                JarPaths = jarPaths
            };
            SaveCache();

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

    [McpServerTool(Name = "analyze_pom_dependencies")]
    [Description("解析 pom.xml 获取所有依赖 jar 包的本地路径")]
    public async Task<List<string>> AnalyzePomDependencies(string pomPath)
    {
        var result = await AnalyzePomDependenciesByPom(pomPath);
        return result.JarPaths.Select(x => new FileInfo(x).Name).ToList();
    }


    private async Task<string> GetLocalRepositoryPath(string workingDir)
    {
        if (!string.IsNullOrEmpty(_localRepoPath)) return _localRepoPath;

        // 1. Check Env Var for local repo override
        var envRepoPath = Environment.GetEnvironmentVariable("MavenInspectorMavnlocalRepositoryPath");
        if (!string.IsNullOrWhiteSpace(envRepoPath))
        {
            _localRepoPath = envRepoPath;
            return _localRepoPath;
        }

        // 2. Fallback to asking Maven
        var mvnPath = Environment.GetEnvironmentVariable("MavenInspectorMavnPath");
        var mvnCmd = string.IsNullOrWhiteSpace(mvnPath) ? "mvn" : $"& '{mvnPath}'";

        var settingsPath = Environment.GetEnvironmentVariable("MavenInspectorMavnSettingPath");
        var settingsArg = string.IsNullOrWhiteSpace(settingsPath) ? "" : $" -s '{settingsPath}'";
        
        var cmdArgs = $"Set-Location -Path '{workingDir}'; {mvnCmd}{settingsArg} -B help:effective-settings";

        try
        {
            var output = WinCMDHelper.GetInstance().RunPowerShell(cmdArgs).Trim();
            //RunDosCmd("", cmdArgs).Trim();

            if (!output.StartsWith("Err:") && !string.IsNullOrWhiteSpace(output))
            {
                Regex repoRegex = new(@"<localRepository>(.*?)</localRepository>", RegexOptions.IgnoreCase);
                var repo = repoRegex.Match(output);
                _localRepoPath = repo.Groups[1].Value.Trim();
                return _localRepoPath;
            }
        }
        catch { }

        // Fallback to default
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _localRepoPath = Path.Combine(home, ".m2", "repository");
        return _localRepoPath;
    }
    [McpServerTool(Name = "search_class_in_dependencies")]
    [Description("在所有依赖包中搜索指定的类名")]
    public List<ClassLocation> SearchClass([Description("pom.xml 文件的绝对路径")] string pomPath, [Description("要搜索的类名（支持部分匹配，*为通配符）")] string classNameQuery)
    {
        // 1. 从缓存获取 JAR 列表
        List<string> jarPaths;
        
        bool needsAnalyze = false;
        if (!_cache.TryGetValue(pomPath, out var cachedInfo))
        {
            needsAnalyze = true;
        }
        else
        {
             try
             {
                 if (File.GetLastWriteTimeUtc(pomPath) != cachedInfo.LastModified)
                 {
                     needsAnalyze = true;
                 }
             }
             catch { needsAnalyze = true; }
        }

        if (needsAnalyze)
        {
             var task = AnalyzePomDependenciesByPom(pomPath);
             task.Wait();
             var res = task.Result;
             if (res.JarPaths == null || res.JarPaths.Count == 0) return new List<ClassLocation>();
             jarPaths = res.JarPaths;
        }
        else
        {
            jarPaths = cachedInfo!.JarPaths;
        }

        var results = new List<ClassLocation>();
        var lockObj = new object();

        // 预处理查询词：将 * 转换为 Regex
        Regex regex = null;
        string simpleContains = null;

        if (classNameQuery.Contains("*"))
        {
            var pattern = "^" + Regex.Escape(classNameQuery).Replace("\\*", ".*") + "$";
            regex = new Regex(pattern, RegexOptions.IgnoreCase);
        }
        else
        {
            simpleContains = classNameQuery;
        }
        
        // 2. 遍历每一个 JAR 文件 (使用 JarCacheManager)
        Parallel.ForEach(jarPaths, (jarPath, state) =>
        {
             if (state.IsStopped) return;
             
             // 获取或分析 JAR (自动处理缓存)
             var info = JarCacheManager.GetInstance().GetOrAnalyze(jarPath);
             if (info == null) return;

             foreach (var cls in info.Classes)
             {
                 if (state.IsStopped) break;
                 
                 // 匹配逻辑
                 bool match = false;
                 if (regex != null)
                 {
                     match = regex.IsMatch(cls.FullName) || regex.IsMatch(cls.SimpleName);
                 }
                 else
                 {
                     match = cls.FullName.Contains(simpleContains, StringComparison.OrdinalIgnoreCase);
                 }

                 if (match)
                 {
                     lock (lockObj)
                     {
                         if (results.Count >= 20)
                         {
                             state.Stop();
                             break;
                         }

                         results.Add(new ClassLocation
                         {
                             SimpleName = cls.SimpleName,
                             FullName = cls.FullName,
                             JarPath = jarPath
                         });
                     }
                 }
             }
        });

        // 只要有任何更新，保存一次 Jar 缓存
        JarCacheManager.GetInstance().SaveCache();

        return results;
    }

    [McpServerTool(Name = "inspect_java_class")]
    [Description("读取特定类的详细结构（方法、字段、签名）")]
    public async Task<ClassDetail> InspectClass([Description("jar 包的绝对路径")] string jarPath, [Description("完整的类名（如 com.example.MyClass）")] string fullClassName)
    {
        // 1. 构建 javap 命令
        var args = $"-p -s -cp \"{jarPath}\" {fullClassName}";

        try
        {
            // 2. 执行并获取输出
            // 使用 WinCMDHelper
            var output = WinCMDHelper.GetInstance().RunPowerShell("javap " + args);

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
                detail.Methods.Add(new JavaMethodInfo
                {
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
    [McpServerTool(Name = "inspect_class_by_name")]
    [Description("仅通过类名（全限定名）在所有已分析的依赖中查找并解析类结构")]
    public async Task<ClassDetail> InspectClassByName([Description("完整的类名（如 com.example.MyClass）")] string fullClassName)
    {
        // 收集所有已知的 JAR 路径
        var allJars = _cache.Values.SelectMany(x => x.JarPaths).Distinct();

        foreach (var jarPath in allJars)
        {
            if (!File.Exists(jarPath)) continue;

            try
            {
                using var zip = ZipFile.OpenRead(jarPath);
                var entryName = fullClassName.Replace('.', '/') + ".class";
                if (zip.GetEntry(entryName) != null)
                {
                    // 找到类，进行检查
                    return await InspectClass(jarPath, fullClassName);
                }
            }
            catch { /* Ignore invalid zip */ }
        }

        return new ClassDetail
        {
            Name = fullClassName,
            Type = "Error: Class not found in any analyzed dependencies. Please run 'analyze_pom_dependencies' first."
        };
    }

    [McpServerTool(Name = "search_method_in_dependencies")]
    [Description("在所有依赖包中搜索包含指定方法名的类")]
    public async  Task<List<ClassLocation>> SearchMethod([Description("pom.xml 文件的绝对路径")] string pomPath, [Description("方法名称（支持部分匹配，*为通配符）")] string methodName)
    {
        List<string> jarPaths;

        bool needsAnalyze = false;
        if (!_cache.TryGetValue(pomPath, out var cachedInfo))
        {
            needsAnalyze = true;
        }
        else
        {
             try
             {
                 if (File.GetLastWriteTimeUtc(pomPath) != cachedInfo.LastModified)
                 {
                     needsAnalyze = true;
                 }
             }
             catch { needsAnalyze = true; }
        }

        if (needsAnalyze)
        {
             var res = await AnalyzePomDependenciesByPom(pomPath);
             if (res.JarPaths == null || res.JarPaths.Count == 0) return new List<ClassLocation>();
             jarPaths = res.JarPaths;
        }
        else
        {
            jarPaths = cachedInfo!.JarPaths;
        }

        var results = new List<ClassLocation>();
        var lockObj = new object();

        // 预处理查询词：将 * 转换为 Regex
        Regex regex = null;
        string simpleContains = null;

        if (methodName.Contains("*"))
        {
            var pattern = "^" + Regex.Escape(methodName).Replace("\\*", ".*") + "$";
            regex = new Regex(pattern, RegexOptions.IgnoreCase);
        }
        else
        {
            simpleContains = methodName;
        }

        Parallel.ForEach(jarPaths, (jarPath, state) =>
        {
            if (state.IsStopped) return;
            
             // 获取或分析 JAR (自动处理缓存)
             var info = JarCacheManager.GetInstance().GetOrAnalyze(jarPath);
             if (info == null) return;

             foreach (var cls in info.Classes)
             {
                 if (state.IsStopped) break;
                 
                 // 检查是否有匹配的方法
                 foreach (var mName in cls.MethodNames)
                 {
                     bool match = false;
                     if (regex != null)
                     {
                         match = regex.IsMatch(mName);
                     }
                     else
                     {
                         match = mName.Contains(simpleContains, StringComparison.OrdinalIgnoreCase);
                     }

                     if (match)
                     {
                         lock (lockObj)
                         {
                             if (results.Count >= 50)
                             {
                                 state.Stop();
                                 break;
                             }
                             // 为了避免重复添加同一个类（如果该类有多个匹配方法）
                             // 这里简单查重一下（虽然 List.Contains 慢，但 results 很小）
                             // 或者直接 Add，最后 DistinctBy
                             bool exists = results.Any(r => r.FullName == cls.FullName && r.JarPath == jarPath);
                             if (!exists)
                             {
                                 results.Add(new ClassLocation
                                 {
                                     SimpleName = cls.SimpleName,
                                     FullName = cls.FullName,
                                     JarPath = jarPath
                                 });
                             }
                         }
                         break; // 只要找到一个方法匹配，这个类就符合条件
                     }
                 }
             }
        });

        JarCacheManager.GetInstance().SaveCache();
        return  results;
    }

    private bool ContainsBytes(byte[] source, byte[] pattern)
    {
        for (int i = 0; i <= source.Length - pattern.Length; i++)
        {
            bool match = true;
            for (int k = 0; k < pattern.Length; k++)
            {
                if (source[i + k] != pattern[k])
                {
                    match = false;
                    break;
                }
            }
            if (match) return true;
        }
        return false;
    }
}
