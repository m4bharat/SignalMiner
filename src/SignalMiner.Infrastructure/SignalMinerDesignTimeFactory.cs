using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SignalMiner.Infrastructure;

public sealed class SignalMinerDesignTimeFactory : IDesignTimeDbContextFactory<SignalMinerDbContext>
{
    public SignalMinerDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<SignalMinerDbContext>()
        .UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__SignalMiner") ?? "Host=localhost;Database=signalminer;Username=postgres").Options);
}
