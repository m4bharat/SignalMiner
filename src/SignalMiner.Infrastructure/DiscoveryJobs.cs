using SignalMiner.Application;

namespace SignalMiner.Infrastructure;

public sealed class DiscoveryJobs(ILeadWorkflow workflow)
{
    public Task DiscoverGitHubAsync(string query, int limit, CancellationToken cancellationToken) =>
        workflow.DiscoverAsync(new DiscoverLeadsRequest(query, limit), cancellationToken);
}
