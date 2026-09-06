using Microsoft.Extensions.Configuration;
using SignalMiner.Infrastructure;
using Xunit;

namespace SignalMiner.Tests;

public sealed class ConfigurationTests
{
    [Fact]
    public void LocalSettingsOverrideJsonButNotEnvironmentOrCommandLine()
    {
        var directory = Path.Combine(Path.GetTempPath(), "signalminer-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "appsettings.json"), """{"Setting":"base","BaseOnly":"base"}""");
            File.WriteAllText(Path.Combine(directory, "appsettings.Local.json"), """{"Setting":"local","LocalOnly":"local"}""");
            using var configuration = new ConfigurationManager();
            configuration.SetBasePath(directory).AddJsonFile("appsettings.json");
            configuration.AddSignalMinerLocalSettings();
            Assert.Equal("local", configuration["Setting"]);
            Assert.Equal("base", configuration["BaseOnly"]);

            // Default hosts register these override sources before the local file is inserted.
            using var withOverrides = new ConfigurationManager();
            withOverrides.SetBasePath(directory).AddJsonFile("appsettings.json")
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Setting"] = "environment" })
                .AddCommandLine(["--Setting=command-line"]);
            withOverrides.AddSignalMinerLocalSettings();
            Assert.Equal("command-line", withOverrides["Setting"]);
            Assert.Equal("local", withOverrides["LocalOnly"]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
