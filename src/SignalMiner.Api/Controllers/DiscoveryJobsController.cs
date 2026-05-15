using Hangfire;
using Microsoft.AspNetCore.Mvc;
using SignalMiner.Application;
using SignalMiner.Infrastructure;

namespace SignalMiner.Api.Controllers;

[ApiController]
[Route("api/leads/discover/jobs")]
public sealed class DiscoveryJobsController(IBackgroundJobClient jobs) : ControllerBase
{
    [HttpPost]
    public ActionResult QueueDiscovery(DiscoverLeadsRequest request)
    {
        var jobId = jobs.Enqueue<DiscoveryJobs>(job => job.DiscoverGitHubAsync(request.Query, request.Limit, CancellationToken.None));
        return Accepted($"/jobs/{jobId}", new { jobId });
    }
}
