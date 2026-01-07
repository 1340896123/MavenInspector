using ErdcServerMcp;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

// 注册配置服务 (单例)
builder.Services.AddSingleton<ErdcConfig>();

// 注册请求服务 (作用域)
builder.Services.AddScoped<ErdcRequestService>();

// 配置 MCP 服务器，使用 HTTP 流式传输
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<ErdcMcpTool>();

var app = builder.Build();

// 映射 MCP 端点
app.MapMcp("mcp");

app.Run();
