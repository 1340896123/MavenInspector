using MavenInspector;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

// Add Services
builder.Services.AddSingleton<MavenInspectorTools>();
builder.Services.AddSingleton<MavenInspectorResources>();
builder.Services.AddSingleton<MavenInspectorPrompts>();

// 添加MCP服务：使用stdio传输模式并注册工具、资源和提示
builder.Services.AddMcpServer().WithStdioServerTransport()
    .WithTools<MavenInspectorTools>();
    //.WithResources<MavenInspectorResources>()
    //.WithPrompts<MavenInspectorPrompts>();

var app = builder.Build();



var tools = app.Services.GetRequiredService<MavenInspectorTools>();


var liwniwt= await tools.AnalyzePomDependenciesByPom("E:\\TDT\\项目\\沈阳新松半导体\\服务工程\\erdcloud-xspdm\\erdcloud-xspdm-service\\pom.xml");
//var LLLL = tools.SearchClass("E:\\TDT\\项目\\沈阳新松半导体\\服务工程\\erdcloud-xspdm\\erdcloud-xspdm-service\\pom.xml", "erd.cloud.core.raw.rawdata");

 var linfwt=await tools.SearchClass("E:\\TDT\\项目\\沈阳新松半导体\\服务工程\\erdcloud-xspdm\\erdcloud-xspdm-service\\pom.xml", "MenuValidatorFilter");

var llllddw= await tools.SearchMethodUsageByDefinition("erd.cloud.core.access.business.EtAccessControlService", "hasPermission(JudgePermissionDTO)");


// //var lwfwt =await tools.InspectClass(LLLL.FirstOrDefault().JarPath, LLLL.FirstOrDefault().FullName);

// //var lll = JsonConvert.SerializeObject(lwfwt);

await app.RunAsync();


