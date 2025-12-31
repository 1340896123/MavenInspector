using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Linq;
using System.Linq;

namespace MavenInspector;

[McpServerToolType]
public class MavenInspectorTools
{
    // Persistent cache: key = pomPath, value = CachedDependencyInfo
    private Dictionary<string, CachedDependencyInfo> _cache;
    private readonly string _cacheFilePath;
    private string _localRepoPath;
    private readonly Dictionary<string, ClassDetail> _classDetailCache;

    public MavenInspectorTools()
    {
        // 使用大小写不敏感的比较器
        _cache = new Dictionary<string, CachedDependencyInfo>(StringComparer.OrdinalIgnoreCase);
        _classDetailCache = new Dictionary<string, ClassDetail>(StringComparer.OrdinalIgnoreCase);

        var mavenInspectorCachePath = Environment.GetEnvironmentVariable("MavenInspectorCachePath");
        if (mavenInspectorCachePath == null)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            mavenInspectorCachePath = Path.Combine(appData, "MavenInspector");
        }
        Directory.CreateDirectory(mavenInspectorCachePath);
        Logger.Init(mavenInspectorCachePath); // Initialize logger in cache directory
        _cacheFilePath = Path.Combine(mavenInspectorCachePath, "dependency_cache.json");
        Logger.Log($"Cache file path: {_cacheFilePath}");
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
                        var tempCache = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, CachedDependencyInfo>>(json)
                                     ?? new Dictionary<string, CachedDependencyInfo>();

                        // 规范化缓存key，使用大小写不敏感的字典
                        _cache = new Dictionary<string, CachedDependencyInfo>(StringComparer.OrdinalIgnoreCase);
                        foreach (var kvp in tempCache)
                        {
                            var normalizedKey = PathHelper.NormalizeForCache(kvp.Key);
                            _cache[normalizedKey] = kvp.Value;
                        }
                    }
                    else
                    {
                        _cache = new Dictionary<string, CachedDependencyInfo>(StringComparer.OrdinalIgnoreCase);
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
            _cache = new Dictionary<string, CachedDependencyInfo>(StringComparer.OrdinalIgnoreCase);
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




    public async Task<DependencyScanResult> AnalyzePomDependenciesByPom(  string pomPath)
    {
        if (!File.Exists(pomPath))
        {
            return new DependencyScanResult { Error = $"File not found: {pomPath}" };
        }

        var workingDir = Path.GetDirectoryName(pomPath);
        if (string.IsNullOrEmpty(workingDir)) workingDir = Directory.GetCurrentDirectory();

        // 规范化pomPath用于缓存
        var normalizedPomPath = PathHelper.NormalizeForCache(pomPath);

        // Check cache validity
        try
        {
            var currentLastWrite = File.GetLastWriteTimeUtc(pomPath);
            if (_cache.TryGetValue(normalizedPomPath, out var cachedInfo))
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
                        // 使用PathHelper规范化路径
                        var groupPath = group.Replace('.', '/');
                        var relativePath = Path.Combine(groupPath, name, version, jarName);
                        var fullPath = Path.Combine(localRepo, relativePath);
                        // 规范化完整路径
                        fullPath = PathHelper.NormalizePathSeparators(fullPath);

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

            _cache[normalizedPomPath] = new CachedDependencyInfo
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
    public async Task<List<string>> AnalyzePomDependencies([Description("pom.xml 文件的绝对路径")]  string pomPath)
    {
        var result = await AnalyzePomDependenciesByPom(pomPath);
        if (!string.IsNullOrEmpty(result.Error))
        {
            return new List<string> { result.Error };
        }
        return result.JarPaths;
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
    public async  Task<List<ClassLocation>> SearchClass([Description("pom.xml 文件的绝对路径")] string pomPath, [Description("要搜索的类名（支持部分匹配，*为通配符）")] string classNameQuery)
    {
        // 规范化pomPath用于缓存
        var normalizedPomPath = PathHelper.NormalizeForCache(pomPath);

        // 1. 从缓存获取 JAR 列表
        List<string> jarPaths;

        bool needsAnalyze = false;
        if (!_cache.TryGetValue(normalizedPomPath, out var cachedInfo))
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


    public async Task<ClassDetail> InspectClass(string jarPath, string fullClassName)
    {
        Logger.Log($"[InspectClass] Inspecting {fullClassName} in {jarPath}");
        // 1. 尝试查找源码 JAR
        string? sourceCode = null;
        try
        {
            var sourceJarPath = jarPath.Replace(".jar", "-sources.jar");
            if (File.Exists(sourceJarPath))
            {
                Logger.Log($"[InspectClass] Found source JAR: {sourceJarPath}");
                using var zip = ZipFile.OpenRead(sourceJarPath);
                var entryName = fullClassName.Replace('.', '/') + ".java";
                var entry = zip.GetEntry(entryName);
                if (entry != null)
                {
                    using var stream = entry.Open();
                    using var reader = new StreamReader(stream);
                    sourceCode = await reader.ReadToEndAsync();
                    Logger.Log($"[InspectClass] Successfully extracted source from JAR.");
                }
                else
                {
                    Logger.Log($"[InspectClass] Source file {entryName} not found in source JAR.", "WARN");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[InspectClass] Source lookup error: {ex.Message}", "ERROR");
        }


        // 2. 尝试使用 Fernflower 反编译
        if (sourceCode == null)
        {
            try
            {
                var decompilerPath = Environment.GetEnvironmentVariable("MavenInspectorFernflowerPath");
                if (!string.IsNullOrWhiteSpace(decompilerPath) && File.Exists(decompilerPath))
                {
                    Logger.Log($"[InspectClass] Source not found. Attempting decompile with Fernflower at {decompilerPath}");
                    sourceCode = await DecompileWithFernflower(decompilerPath, jarPath, fullClassName);
                }
                else
                {
                    Logger.Log($"[InspectClass] Fernflower decompiler path not set or file missing.", "WARN");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[InspectClass] Decompile error: {ex.Message}", "ERROR");
            }
        }


        if (sourceCode != null)
        {
            var detail = ParseClassDetailFromSource(sourceCode, fullClassName);
            _classDetailCache[fullClassName] = detail;
            return detail;
        }

        Logger.Log($"[InspectClass] ERROR: Could not retrieve source code for {fullClassName}", "ERROR");
        return new ClassDetail
        {
            Name = fullClassName,
            Type = "Error: Source code could not be retrieved."
        };

    }



    [McpServerTool(Name = "inspect_java_class")]
    [Description("读取特定类的详细结构（方法、字段、签名）")]
    public async Task<string> InspectClassSource([Description("jar 包的绝对路径")] string jarPath, [Description("完整的类名（如 com.example.MyClass）")] string fullClassName)
    {
        var res = await InspectClass(jarPath, fullClassName);
        return res.RawClassData!;
    }

    private async Task<string?> DecompileWithFernflower(string fernflowerPath, string jarPath, string fullClassName)
    {
        // Fernflower usage: java -jar fernflower.jar <source> <destination>
        // source can be a file or directory. destination must be a directory.
        // To avoid decompiling the entire jar, we should extract the specific .class file first.

        var tempDir = Path.Combine(Path.GetTempPath(), "MavenInspector", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var classFileRelativePath = fullClassName.Replace('.', '/') + ".class";
            var tempClassFile = Path.Combine(tempDir, "cls", classFileRelativePath);
            var tempOutputDir = Path.Combine(tempDir, "src");
            Directory.CreateDirectory(tempOutputDir);
            Directory.CreateDirectory(Path.GetDirectoryName(tempClassFile)!);

            // Extract .class file
            using (var zip = ZipFile.OpenRead(jarPath))
            {
                var entry = zip.GetEntry(classFileRelativePath);
                if (entry == null) return null;
                entry.ExtractToFile(tempClassFile);
            }

            // Run Fernflower
            // java -jar fernflower.jar <classFile> <outputDir>
            var args = $" -jar \"{fernflowerPath}\" \"{tempClassFile}\" \"{tempOutputDir}\"";
            var output = WinCMDHelper.GetInstance().RunPowerShell("java " + args);

            // Fernflower usually creates a .java file in the output dir
            // The filename might be SimpleName.java
            var javaFiles = Directory.GetFiles(tempOutputDir, "*.java", SearchOption.AllDirectories);
            if (javaFiles.Length > 0)
            {
                return await File.ReadAllTextAsync(javaFiles[0]);
            }

            return null;
        }
        catch
        {
            return null;
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    private static bool IsBasicJavaType(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName)) return true;
        var simple = typeName.Trim();
        simple = simple.Replace("[]", "").Replace("...", "");
        var lastDot = simple.LastIndexOf('.');
        if (lastDot >= 0 && lastDot < simple.Length - 1)
        {
            simple = simple[(lastDot + 1)..];
        }

        switch (simple)
        {
            case "byte":
            case "short":
            case "int":
            case "long":
            case "float":
            case "double":
            case "boolean":
            case "char":
            case "void":
            case "Byte":
            case "Short":
            case "Integer":
            case "Long":
            case "Float":
            case "Double":
            case "Boolean":
            case "Character":
            case "String":
                return true;
            default:
                return false;
        }
    }

    private static string NormalizeMethodDefinition(string className, string signature, JavaMethodInfo method)
    {
        if (string.IsNullOrWhiteSpace(signature)) return "";

        var idxParen = signature.IndexOf('(');
        if (idxParen <= 0) return "";

        var header = signature[..idxParen].Trim();
        var paramPart = signature[(idxParen + 1)..];
        var idxClose = paramPart.LastIndexOf(')');
        if (idxClose >= 0)
        {
            paramPart = paramPart[..idxClose];
        }

        var headerTokens = header.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (headerTokens.Length == 0) return "";

        var methodName = headerTokens[^1];

        var nonBasicParamTypes = new List<string>();
        if (!string.IsNullOrWhiteSpace(paramPart))
        {
            var paramItems = paramPart.Split(',');
            foreach (var raw in paramItems)
            {
                var p = raw.Trim();
                if (p.Length == 0) continue;

                var spaceTokens = p.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (spaceTokens.Length == 0) continue;

                var typeTokens = spaceTokens.Take(spaceTokens.Length - 1).ToArray();
                if (typeTokens.Length == 0)
                {
                    typeTokens = spaceTokens;
                }

                var typePart = string.Join(" ", typeTokens);
                var genericIdx = typePart.IndexOf('<');
                if (genericIdx >= 0)
                {
                    typePart = typePart[..genericIdx];
                }
                typePart = typePart.Replace("[]", "").Replace("...", "").Trim();
                if (typePart.Length == 0) continue;

                if (!IsBasicJavaType(typePart))
                {
                    nonBasicParamTypes.Add(typePart);
                    method.Parameters.Add(typePart);
                }
            }
        }

        return nonBasicParamTypes.Count == 0
            ? methodName + "()"
            : methodName + "(" + string.Join(",", nonBasicParamTypes) + ")";
    }

    private ClassDetail ParseClassDetailFromSource(string sourceCode, string className)
    {
        var detail = new ClassDetail
        {
            Name = className,
            RawClassData = sourceCode
        };

        try
        {
            // 简单移除注释
            string cleanCode = Regex.Replace(sourceCode, @"/\*.*?\*/", "", RegexOptions.Singleline);
            cleanCode = Regex.Replace(cleanCode, @"//.*$", "", RegexOptions.Multiline);

            var pkgMatch = Regex.Match(cleanCode, @"^\s*package\s+([^\s;]+)\s*;", RegexOptions.Multiline);
            if (pkgMatch.Success)
            {
                detail.Package = pkgMatch.Groups[1].Value.Trim();
            }

            var importRegex = new Regex(@"^\s*import\s+([^\s;]+)\s*;", RegexOptions.Multiline);
            foreach (Match im in importRegex.Matches(cleanCode))
            {
                var imp = im.Groups[1].Value.Trim();
                if (imp.Length > 0 && !detail.Imports.Contains(imp))
                {
                    detail.Imports.Add(imp);
                }
            }

            // Regex 提取方法签名
            // 匹配： (修饰符...) 返回值 方法名(参数) ... {
            // 注意：构造函数没有返回值，所以需要考虑这种情况
            // Regex simplified:
            // (modifiers) -> ((public|protected|private|static|final|native|synchronized|abstract|transient)\s+)*
            // (type + name) -> [\w<>\[\]]+\s+(\w+)   <-- method with return type
            //               OR (\w+)                  <-- constructor (name same as class)
            // This is hard to perfect with regex, let's try a best-effort approach identifying method-like patterns ending in { or ;

            // 改进：提取所有看起来像方法定义的行
            // 排除 if/for/while/switch/catch
            var methodRegex = new Regex(@"^\s*((public|protected|private|static|final|native|synchronized|abstract|transient)\s+)+[\w\<\>\[\]\s]+\(.*\)\s*(throws\s+[\w,\s]+)?\s*\{", RegexOptions.Multiline);

            foreach (Match match in methodRegex.Matches(cleanCode))
            {
                var sig = match.Value.Trim().TrimEnd('{').Trim();
                // 排除控制流
                if (sig.StartsWith("if") || sig.StartsWith("for") || sig.StartsWith("while") || sig.StartsWith("switch") || sig.StartsWith("catch")) continue;

                var methodInfo = new JavaMethodInfo
                {
                    Signature = sig,
                    RawMethodData = match.Value
                };

                methodInfo.NormalizedDefinition = NormalizeMethodDefinition(className, sig, methodInfo);
                detail.Methods.Add(methodInfo);
            }

            // Regex 提取字段
            // 排除 package, import
            var fieldRegex = new Regex(@"^\s*((public|protected|private|static|final|volatile|transient)\s+)+[\w\<\>\[\]]+\s+(\w+)\s*(=[^;]+)?;", RegexOptions.Multiline);
            foreach (Match match in fieldRegex.Matches(cleanCode))
            {
                detail.Fields.Add(match.Value.Trim());
            }

        }
        catch (Exception ex)
        {
            detail.Fields.Add($"Error parsing source: {ex.Message}");
        }

        return detail;
    }


    public async Task<ClassDetail> InspectClassByName(string fullClassName)
    {
        Logger.Log($"[InspectClassByName] Searching for class: {fullClassName}");
        // 收集所有已知的 JAR 路径
        var allJars = _cache.Values.SelectMany(x => x.JarPaths).Distinct().ToList();
        Logger.Log($"[InspectClassByName] Scanning {allJars.Count} JAR files from cache.");

        //E:\TDT\项目\沈阳新松半导体\服务工程\erdcloud-xspdm\erdcloud-xspdm-service\pom.xml
        foreach (var jarPath in allJars)
        {
            if (!File.Exists(jarPath))
            {
                Logger.Log($"[InspectClassByName] JAR not found on disk: {jarPath}", "WARN");
                continue;
            }

            try
            {
                using var zip = ZipFile.OpenRead(jarPath);
                var entryName = fullClassName.Replace('.', '/') + ".class";
                if (zip.GetEntry(entryName) != null)
                {
                    Logger.Log($"[InspectClassByName] Found class in JAR: {jarPath}");
                    // 找到类，进行检查
                    return await InspectClass(jarPath, fullClassName);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[InspectClassByName] Error reading JAR {jarPath}: {ex.Message}", "ERROR");
            }
        }

        Logger.Log($"[InspectClassByName] Class {fullClassName} not found in any cached JARs.", "WARN");
        return new ClassDetail
        {
            Name = fullClassName,
            Type = "Error: Class not found in any analyzed dependencies. Please run 'analyze_pom_dependencies' first."
        };
    }


    [McpServerTool(Name = "inspect_class_by_name")]
    [Description("仅通过类名（全限定名）在所有已分析的依赖中查找并解析类结构")]
    public async Task<string> InspectClassSourceByName([Description("完整的类名（如 com.example.MyClass）")] string fullClassName)
    {
        var res = await InspectClassByName(fullClassName);
        return res.RawClassData!;
    }

    [McpServerTool(Name = "search_method_in_dependencies")]
    [Description("在所有依赖包中搜索包含指定方法名的类")]
    public async Task<List<ClassLocation>> SearchMethod([Description("pom.xml 文件的绝对路径")] string pomPath, [Description("方法名称（支持部分匹配，*为通配符）")] string methodName)
    {
        // 规范化pomPath用于缓存
        var normalizedPomPath = PathHelper.NormalizeForCache(pomPath);

        List<string> jarPaths;

        bool needsAnalyze = false;
        if (!_cache.TryGetValue(normalizedPomPath, out var cachedInfo))
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
        return results;
    }


    [McpServerTool(Name = "search_method_usage_by_definition")]
    [Description("根据完整类名和方法定义，在缓存的类信息中查找该方法的源码片段/使用说明")]
    public async Task<string> SearchMethodUsageByDefinition(
        [Description("完整的类名（如 erd.cloud.xspdm.controller.xspdmController）")] string fullClassName,
        [Description("方法的规范定义，例如 doSomething(OrderDto) 或 doSomething()")] string methodDefinition)
    {
        if (string.IsNullOrWhiteSpace(fullClassName) || string.IsNullOrWhiteSpace(methodDefinition))
        {
            return "";
        }

        if (!_classDetailCache.TryGetValue(fullClassName, out var detail))
        {
            detail = await InspectClassByName(fullClassName);
            if (detail != null)
            {
                _classDetailCache[fullClassName] = detail;
            }
        }

        if (detail == null || detail.Methods == null || detail.Methods.Count == 0)
        {
            return "";
        }

        var target = detail.Methods
            .FirstOrDefault(m => string.Equals(m.NormalizedDefinition, methodDefinition, StringComparison.Ordinal));

        if (target == null)
        {
            return "";
        }

        return target.RawMethodData ?? target.Signature;
    }
}
