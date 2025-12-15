using MavenInspector;


var builder = Host.CreateApplicationBuilder(args);

// 配置日志输出到stderr (stdout用于MCP协议消息)
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

//var ll=new MavenInspectorTools();
//var dddwwtw = await ll.AnalyzePomDependencies("E:\\TDT\\项目\\沈阳新松半导体\\erdcloud-xspdm-plugin\\pom.xml");

//Console.WriteLine(dddwwtw.ToString()); ;


// 添加MCP服务：使用stdio传输模式并注册工具
builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<MavenInspectorTools>();
await builder.Build().RunAsync();


