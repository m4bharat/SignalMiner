using Hangfire;
using Microsoft.EntityFrameworkCore;
using SignalMiner.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSignalMinerCore(builder.Configuration);
builder.Services.AddSignalMinerHangfire(builder.Configuration);

var host = builder.Build();

await using (var scope = host.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SignalMinerDbContext>();
    await db.Database.EnsureCreatedAsync();
}

host.Run();
