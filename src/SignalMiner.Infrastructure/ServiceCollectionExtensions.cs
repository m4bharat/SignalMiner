using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using SignalMiner.Application;

namespace SignalMiner.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSignalMinerCore(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("SignalMiner")
            ?? "Host=localhost;Port=5432;Database=signalminer;Username=postgres;Password=postgres";

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
        var connectionString = configuration.GetConnectionString("SignalMiner")
            ?? "Host=localhost;Port=5432;Database=signalminer;Username=postgres;Password=postgres";

        services.AddHangfire(config => config.UsePostgreSqlStorage(options => options.UseNpgsqlConnection(connectionString)));
        services.AddHangfireServer();
        return services;
    }
}
