using System.Text.Json.Serialization;

namespace MavenInspector;

public class DependencyScanResult
{
    public string? ProjectRoot { get; set; }
    public List<string> JarPaths { get; set; } = new();
    public string? Error { get; set; }

    public override string ToString()
    {
        if (!string.IsNullOrEmpty(Error)) return $"Error: {Error}";
        return $"Success: {JarPaths.Count} jars found in {ProjectRoot}";
    }
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
    public string? RawClassData { get; set; }
    public List<string> Imports { get; set; } = new();
}

public class JavaMethodInfo
{
    public string Signature { get; set; } = "";
    public string? ReturnType { get; set; }
    public List<string> Parameters { get; set; } = new();
    public string? Documentation { get; set; }
    public string? RawMethodData { get; set; }
    public string? NormalizedDefinition { get; set; }
}
