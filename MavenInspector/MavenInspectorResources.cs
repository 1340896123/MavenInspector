using ModelContextProtocol.Server;
using System;
using System.ComponentModel;
using System.IO;

namespace MavenInspector;

public class MavenInspectorResources
{
    private readonly string _cacheDir;

    public MavenInspectorResources()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _cacheDir = Path.Combine(appData, "MavenInspector");
    }

    [McpServerResource]
    [Description("Get the contents of the Maven Inspector log file")]
    public string GetLogs()
    {
        var logFile = Path.Combine(_cacheDir, "mvn_inspector.log");
        if (File.Exists(logFile))
        {
            return File.ReadAllText(logFile);
        }
        return "Log file not found.";
    }

    [McpServerResource]
    [Description("Get the raw content of the dependency cache JSON")]
    public string GetDependencyCache()
    {
        var cacheFile = Path.Combine(_cacheDir, "dependency_cache.json");
        if (File.Exists(cacheFile))
        {
            return File.ReadAllText(cacheFile);
        }
        return "Cache file not found.";
    }
}
