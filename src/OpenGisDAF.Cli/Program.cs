using OpenGisDAF.Cli;

await using var app = new DafApplication();
return await app.RunAsync(args);
