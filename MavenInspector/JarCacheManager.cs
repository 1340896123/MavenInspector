using System.Collections.Concurrent;
using System.Threading;
using System.Security.Cryptography;
using System.Text.Json;

namespace MavenInspector;

public class JarCacheManager
{
    private static JarCacheManager _instance;
    private readonly string _cacheFilePath;
    private readonly ConcurrentDictionary<string, JarAnalysisInfo> _memoryCache = new();
    private readonly object _fileLock = new();

    public static JarCacheManager GetInstance()
    {
        if (_instance == null)
            _instance = new JarCacheManager();
        return _instance;
    }

    private JarCacheManager()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(appData, "MavenInspector");
        Directory.CreateDirectory(dir);
        _cacheFilePath = Path.Combine(dir, "jar_content_cache.json");
        LoadCache();
    }

    private void LoadCache()
    {
        lock (_fileLock)
        {
            if (File.Exists(_cacheFilePath))
            {
                try
                {
                    using (var mutex = new Mutex(false, "Global\\MavenInspector_JarCache_Mutex"))
                    {
                        try
                        {
                            mutex.WaitOne(3000);
                            if (File.Exists(_cacheFilePath))
                            {
                                var json = File.ReadAllText(_cacheFilePath);
                                var list = JsonSerializer.Deserialize<List<JarAnalysisInfo>>(json);
                                if (list != null)
                                {
                                    foreach (var item in list)
                                    {
                                        _memoryCache[item.JarPath] = item;
                                    }
                                }
                            }
                        }
                        finally
                        {
                            mutex.ReleaseMutex();
                        }
                    }
                }
                catch { /* Ignore corrupt cache */ }
            }
        }
    }

    public void SaveCache()
    {
        lock (_fileLock)
        {
            try
            {
                using (var mutex = new Mutex(false, "Global\\MavenInspector_JarCache_Mutex"))
                {
                    try
                    {
                        mutex.WaitOne(3000);
                        var list = _memoryCache.Values.ToList();
                        var json = JsonSerializer.Serialize(list);
                        File.WriteAllText(_cacheFilePath, json);
                    }
                    finally
                    {
                        mutex.ReleaseMutex();
                    }
                }

            }
            catch { }
        }
    }

    public JarAnalysisInfo GetOrAnalyze(string jarPath)
    {
        if (!File.Exists(jarPath)) return null;

        string hash = ComputeHash(jarPath);

        // Check memory cache
        if (_memoryCache.TryGetValue(jarPath, out var info))
        {
            if (info.FileHash == hash)
            {
                return info;
            }
        }

        // Analyze
        var newInfo = AnalyzeJar(jarPath, hash);
        _memoryCache[jarPath] = newInfo;
        
        // Save periodically or essentially on change to ensure persistence
        // For performance, maybe don't save on *every* new jar if doing bulk scan, 
        // but for now simplest is just save. Or caller can call Save explicitly.
        // We'll leave Save explicit or periodic if needed, but here simple is OK.
        // Let's rely on caller to save or save on dispose? 
        // Or just save here to be safe.
        // To avoid too much I/O during parallel scan, maybe we don't save instant.
        // But for consistency let's just update memory. Caller (MavenInspectorTools) can call SaveCache at end.
        
        return newInfo;
    }

    private string ComputeHash(string filePath)
    {
        try
        {
            using var md5 = MD5.Create();
            using var stream = File.OpenRead(filePath);
            var hash = md5.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
        catch
        {
            return "";
        }
    }

    private JarAnalysisInfo AnalyzeJar(string jarPath, string hash)
    {
        var info = new JarAnalysisInfo
        {
            JarPath = jarPath,
            FileHash = hash,
            Classes = new List<ClassAnalysisInfo>()
        };

        try
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(jarPath);
            foreach (var entry in zip.Entries)
            {
                if (!entry.Name.EndsWith(".class")) continue;
                
                using var stream = entry.Open();
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                var bytes = ms.ToArray();

                var parser = new JavaClassParser(bytes);
                parser.Parse();

                if (!string.IsNullOrEmpty(parser.ClassName))
                {
                    info.Classes.Add(new ClassAnalysisInfo
                    {
                        FullName = parser.ClassName,
                        SimpleName = Path.GetFileNameWithoutExtension(entry.Name), 
                        MethodNames = parser.MethodNames
                    });
                }
            }
        }
        catch { /* Ignore bad jars */ }

        return info;
    }
}

public class JarAnalysisInfo
{
    public string JarPath { get; set; }
    public string FileHash { get; set; }
    public List<ClassAnalysisInfo> Classes { get; set; } = new();
}

public class ClassAnalysisInfo
{
    public string FullName { get; set; }
    public string SimpleName { get; set; }
    public List<string> MethodNames { get; set; } = new();
}
