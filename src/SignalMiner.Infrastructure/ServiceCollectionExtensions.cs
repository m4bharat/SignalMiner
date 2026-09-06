using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using SignalMiner.Application;

namespace SignalMiner.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static void AddSignalMinerLocalSettings(this ConfigurationManager configuration)
    {
        // Keep machine-specific settings out of Git, while retaining environment
        // variable and command-line overrides from the default host configuration.
        var index = configuration.Sources.ToList().FindLastIndex(source => source is JsonConfigurationSource) + 1;
        configuration.Sources.Insert(index, new JsonConfigurationSource
        {
            Path = "appsettings.Local.json",
            Optional = true,
            ReloadOnChange = false
        });
    }

    public static string GetSignalMinerConnectionString(this IConfiguration configuration) =>
        configuration.GetConnectionString("SignalMiner")
        ?? "Host=localhost;Port=5432;Database=signalminer;Username=postgres;Password=postgres";

    public static async Task InitializeSignalMinerDatabaseAsync(this IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var options = new DbContextOptionsBuilder<SignalMinerDbContext>()
            .UseNpgsql(configuration.GetSignalMinerConnectionString()).Options;
        await using var db = new SignalMinerDbContext(options);
        // Create the application tables before Hangfire creates its own schema.
        await db.Database.EnsureCreatedAsync(cancellationToken);
    }

    public static IServiceCollection AddSignalMinerCore(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetSignalMinerConnectionString();

        services.AddDbContext<SignalMinerDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<ILeadRepository, LeadRepository>();
        services.AddScoped<ILeadScoringService, LeadScoringService>();
        services.AddScoped<ILeadImportService, LeadImportService>();
        services.AddScoped<ILeadWorkflow, LeadWorkflow>();
        services.AddScoped<IManualEmailService, ManualEmailService>();
        services.AddScoped<IEmailDeliveryService, SmtpEmailDeliveryService>();
        services.AddScoped<IWebsiteExtractionService, WebsiteExtractionService>();
        services.AddHttpClient<IXDiscoveryService, XDiscoveryService>(client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SignalMiner/0.1");
            client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml");
        });
        services.AddHttpClient<ILinkedInDiscoveryService, PublicProfileUrlDiscoveryService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SignalMiner/0.1");
            client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml");
        });

        services.AddHttpClient<IGitHubDiscoveryService, GitHubDiscoveryService>((provider, client) =>
        {
            client.BaseAddress = new Uri("https://api.github.com/");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SignalMiner/0.1");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

            var token = configuration["GitHub:Token"];
            var logger = provider.GetRequiredService<ILogger<GitHubDiscoveryService>>();
            if (!string.IsNullOrWhiteSpace(token))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                logger.LogInformation("GitHub API token is configured for discovery requests.");
            }
            else
            {
                logger.LogWarning("GitHub API token is not configured. Discovery is limited to GitHub's low unauthenticated rate limit.");
            }
        });

        return services;
    }

    public static IServiceCollection AddSignalMinerHangfire(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetSignalMinerConnectionString();

        services.AddHangfire(config => config.UsePostgreSqlStorage(options => options.UseNpgsqlConnection(connectionString)));
        services.AddHangfireServer();
        return services;
    }
}
