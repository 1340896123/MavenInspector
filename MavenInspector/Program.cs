using MavenInspector;
using Newtonsoft.Json;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// 配置日志输出到stderr (stdout用于MCP协议消息)
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

// Add Services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<MavenInspectorTools>();

// 添加MCP服务：使用stdio传输模式并注册工具
builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<MavenInspectorTools>();

var app = builder.Build();

// Configure Swagger
app.UseSwagger();
app.UseSwaggerUI();


var tools = app.Services.GetRequiredService<MavenInspectorTools>();

app.MapGet("/analyze_pom_dependencies", async (string pomPath) => await tools.AnalyzePomDependencies(pomPath))
.WithName("AnalyzePomDependencies");

app.MapGet("/search_class_in_dependencies", (string pomPath, string classNameQuery) => tools.SearchClass(pomPath, classNameQuery))
.WithName("SearchClassInDependencies");

app.MapGet("/inspect_java_class", async (string jarPath, string fullClassName) => await tools.InspectClass(jarPath, fullClassName))
.WithName("InspectJavaClass");

app.MapGet("/inspect_class_by_name", async (string fullClassName) => await tools.InspectClassByName(fullClassName))
.WithName("InspectClassByName");

app.MapGet("/search_method_in_dependencies", async (string pomPath, string methodName) => await tools.SearchMethod(pomPath, methodName))
.WithName("SearchMethodInDependencies");


//var LLLL = tools.SearchClass("E:\\TDT\\项目\\沈阳新松半导体\\服务工程\\erdcloud-xspdm\\erdcloud-xspdm-service\\pom.xml", "erd.cloud.core.raw.rawdata");


//var lwfwt =await tools.InspectClass(LLLL.FirstOrDefault().JarPath, LLLL.FirstOrDefault().FullName);

//var lll = JsonConvert.SerializeObject(lwfwt);

await app.RunAsync();


