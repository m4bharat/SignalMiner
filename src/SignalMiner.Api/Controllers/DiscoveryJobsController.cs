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
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return BadRequest(new { message = "query is required." });
        }

        if (request.Limit is < 1 or > 50)
        {
            return BadRequest(new { message = "limit must be between 1 and 50." });
        }

        if (!Enum.IsDefined(request.Source))
        {
            return BadRequest(new { message = "source must be a valid discovery source." });
        }

        var jobId = jobs.Enqueue<DiscoveryJobs>(job => job.DiscoverAsync(request.Query, request.Limit, request.Source, CancellationToken.None));
        return Accepted($"/jobs/{jobId}", new { jobId });
    }
}
