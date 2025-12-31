using ErdcServerMcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// 配置 MCP 服务器，使用 STDIO 传输
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<ErdcMcpTool>();

// 注册配置服务 (单例)
builder.Services.AddSingleton<ErdcConfig>();

// 注册请求服务 (作用域)
builder.Services.AddScoped<ErdcRequestService>();

// 配置日志输出到标准错误流
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});
//ErdcConfig erdcConfig = new ErdcConfig();
//var ser = new ErdcRequestService(erdcConfig);


//ErdcMcpTool erdcMcpTool = new ErdcMcpTool(ser);

//var fwt = await erdcMcpTool.GetServerInfosAsync();

////var fwt2 = await erdcMcpTool.GetServerInfoSummaryAsync();
//var fwt3 = await erdcMcpTool.ListTypeAttributesAsync("OR:erd.cloud.foundation.type.entity.TypeDefinition:1689235656964952065");

// 构建并运行主机
using var host = builder.Build();
await host.RunAsync();
