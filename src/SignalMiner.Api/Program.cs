using Hangfire;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Text.Json.Serialization;
using SignalMiner.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddSignalMinerCore(builder.Configuration);

var connectionString = builder.Configuration.GetConnectionString("SignalMiner")
    ?? "Host=localhost;Port=5432;Database=signalminer;Username=postgres;Password=postgres";
var databaseAvailable = await CanConnectToPostgresAsync(connectionString);
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
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<SignalMinerDbContext>();
    await db.Database.EnsureCreatedAsync();
    app.MapHangfireDashboard("/jobs");
}
else
{
    app.Logger.LogWarning("PostgreSQL is unavailable. Swagger will run, but database-backed endpoints and Hangfire are disabled until PostgreSQL is available.");
}

app.MapControllers();

app.Run();

static async Task<bool> CanConnectToPostgresAsync(string connectionString)
{
    try
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return true;
    }
    catch
    {
        return false;
    }
}
