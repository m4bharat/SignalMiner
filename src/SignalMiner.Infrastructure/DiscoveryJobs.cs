using SignalMiner.Application;

namespace SignalMiner.Infrastructure;

public sealed class DiscoveryJobs(ILeadWorkflow workflow)
{
    public Task DiscoverAsync(string query, int limit, DiscoverySource source, CancellationToken cancellationToken) =>
        workflow.DiscoverAsync(new DiscoverLeadsRequest(query, limit, source), cancellationToken);
}
