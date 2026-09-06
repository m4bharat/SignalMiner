using SignalMiner.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddSignalMinerLocalSettings();
builder.Services.AddSignalMinerCore(builder.Configuration);
await builder.Configuration.InitializeSignalMinerDatabaseAsync();
builder.Services.AddSignalMinerHangfire(builder.Configuration);

var host = builder.Build();

host.Run();
