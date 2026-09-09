using System.Text;
using OpenGisDAF.Cli;

// Force UTF-8 console output so redirected/CI logs are not mangled by the system code page (GBK on zh-CN Windows).
Console.OutputEncoding = Encoding.UTF8;

await using var app = new DafApplication();
return await app.RunAsync(args);
