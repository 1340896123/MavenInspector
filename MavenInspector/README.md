# MavenInspector

MavenInspector 是一个基于 Model Context Protocol (MCP) 的服务端程序，旨在帮助开发者分析 Maven 项目依赖、查找 Java 类与方法，以及检查 Jar 包结构。

本项目采用 .NET 开发，通过 MCP 协议与客户端（如 AI 助手、IDE 插件）交互。

## 核心功能

MavenInspector 提供以下 MCP 工具 (Tools)：

### 1. 依赖分析
- **`analyze_pom_dependencies`**
  - **功能**: 解析指定的 `pom.xml` 文件，调用 Maven 获取所有依赖的 Jar 包本地路径。
  - **参数**: `pomPath` (pom.xml 的绝对路径)。
  - **缓存**: 分析结果会被缓存，只有当 pom.xml 文件修改后才会重新运行 Maven 命令。

### 2. 类与方法搜索
- **`search_class_in_dependencies`**
  - **功能**: 在 `pom.xml` 对应的所有依赖 Jar 包中搜索指定的类名。
  - **参数**: 
    - `pomPath`: 关联的 pom.xml 路径。
    - `classNameQuery`: 类名关键词（支持 `*` 通配符）。
  
- **`search_method_in_dependencies`**
  - **功能**: 在所有依赖包中搜索包含指定方法名的类。
  - **参数**: 
    - `pomPath`: 关联的 pom.xml 路径。
    - `methodName`: 方法名关键词（支持 `*` 通配符）。

### 3. Java 类反编译/检查
- **`inspect_java_class`**
  - **功能**: 使用 `javap` 读取特定 Jar 包中类的详细结构（方法签名、字段等）。
  - **参数**: 
    - `jarPath`: Jar 包绝对路径。
    - `fullClassName`: 类的全限定名（如 `com.example.MyClass`）。

- **`inspect_class_by_name`**
  - **功能**: 在所有已分析过的依赖中查找指定的类并解析其结构（无需提供 Jar 路径，自动查找）。
  - **参数**: `fullClassName` (类的全限定名)。

## 环境要求

1. **.NET Runtime**: 需要安装对应版本的 .NET SDK/Runtime (本项目配置为 `net10.0`，请确保环境支持或自行调整 `MavenInspector.csproj` 至 `net8.0` 等主流版本)。
2. **Maven (`mvn`)**: 系统必须安装 Maven，且 `mvn` 命令在 PATH 中可用。
3. **JDK (`javap`)**: 系统必须安装 JDK，且 `javap` 命令在 PATH 中可用（用于解析 Class 文件结构）。

## 配置 (环境变量)

可以通过设置以下环境变量来自定义 Maven 行为：

- `MavenInspectorMavnPath`: 指定 `mvn` 可执行文件的完整路径（默认为系统 PATH 中的 `mvn`）。
- `MavenInspectorMavnSettingPath`: 指定 Maven `settings.xml` 文件的路径（默认为空，使用默认设置）。
- `MavenInspectorMavnlocalRepositoryPath`: 指定 Maven 本地仓库路径（若未设置，尝试自动检测或使用 `~/.m2/repository`）。

## 使用说明 (Dotnet CLI)

### 1. 编译与运行

在项目根目录下，使用终端执行以下命令：

```bash
# 恢复依赖
dotnet restore

# 编译项目
dotnet build

# 运行项目 (默认监听 Stdio 进行 MCP 通信)
dotnet run --project MavenInspector
```

### 2. 在 MCP 客户端中使用

#### Visual Studio Code (配合 MCP 扩展)

本项目已包含 VS Code 配置文件 `.vscode/mcp.json`。
如果你在 Visual Studio Code 中安装了 MCP 相关的扩展（如 GitHub Copilot 的 MCP 支持），可以直接打开本项目文件夹，扩展应能自动识别配置。

`.vscode/mcp.json` 内容示例：
```json
{
  "servers": {
    "MavenInspector": {
      "type": "stdio",
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "MavenInspector.csproj"
      ],
      "env": {
        "MavenInspectorMavnPath": "",
        "MavenInspectorMavnSettingPath": "",
        "MavenInspectorMavnlocalRepositoryPath": "",
        "MavenInspectorFernflowerPath":""
      }
    }
  }
}
```

#### Claude Desktop 或其他通用客户端

要将 MavenInspector 集成到 Claude Desktop 或其他 Agent，请在客户端配置文件（如 `claude_desktop_config.json`）中添加：

```json
{
  "mcpServers": {
    "maven-inspector": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "/path/to/MavenInspector/MavenInspector/MavenInspector.csproj" 
      ],
      "env": {
        "MavenInspectorMavnPath": "",
        "MavenInspectorMavnSettingPath": "",
        "MavenInspectorMavnlocalRepositoryPath": ""
      }
    }
  }
}
```
*(请将 `/path/to/...` 替换为实际的绝对路径)*

如果是已编译的二进制文件：
```json
{
  "mcpServers": {
    "maven-inspector": {
      "command": "/path/to/MavenInspector.exe",
      "args": [],
      "env": {
        "MavenInspectorMavnPath": "",
        "MavenInspectorMavnSettingPath": "",
        "MavenInspectorMavnlocalRepositoryPath": ""
      }
    }
  }
}
```

## 注意事项

- **首次运行**: `analyze_pom_dependencies` 首次运行时需要调用 Maven 解析依赖，速度可能较慢，请耐心等待。
- **缓存**: 依赖分析结果和 Jar 包类索引会被缓存到 `LocalAppData/MavenInspector` 目录下，以提高后续搜索速度。
