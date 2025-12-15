using System.Text.Json.Serialization;

namespace MavenInspector;

public class DependencyScanResult
{
    public string? ProjectRoot { get; set; }
    public List<string> JarPaths { get; set; } = new();
    public string? Error { get; set; }
}

public class ClassLocation
{
    public string SimpleName { get; set; } = "";
    public string FullName { get; set; } = "";
    public string JarPath { get; set; } = "";
}

public class ClassDetail
{
    public string? Package { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "class";
    public List<string> Fields { get; set; } = new();
    public List<JavaMethodInfo> Methods { get; set; } = new();
}

public class JavaMethodInfo
{
    public string Signature { get; set; } = "";
    public string? ReturnType { get; set; }
    public List<string> Parameters { get; set; } = new();
    public string? Documentation { get; set; }
}
