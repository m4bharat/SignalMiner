using Hangfire;
using Npgsql;
using System.Text.Json.Serialization;
using SignalMiner.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddSignalMinerLocalSettings();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .WithOrigins("http://localhost:4200", "http://localhost:58982", "https://localhost:58982", "http://127.0.0.1:4200")
        .AllowAnyHeader()
        .AllowAnyMethod());
});
builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSignalMinerCore(builder.Configuration);

var databaseAvailable = await TryInitializeDatabaseAsync(builder.Configuration);
if (databaseAvailable)
{
    builder.Services.AddSignalMinerHangfire(builder.Configuration);
}

var app = builder.Build();

app.UseCors();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (databaseAvailable)
{
    app.MapHangfireDashboard("/jobs");
}
else
{
    app.Logger.LogWarning("PostgreSQL is unavailable. Swagger will run, but database-backed endpoints and Hangfire are disabled until PostgreSQL is available.");
}

app.MapControllers();

app.Run();

static async Task<bool> TryInitializeDatabaseAsync(IConfiguration configuration)
{
    try
    {
        await configuration.InitializeSignalMinerDatabaseAsync();
        return true;
    }
    catch (NpgsqlException)
    {
        return false;
    }
}
