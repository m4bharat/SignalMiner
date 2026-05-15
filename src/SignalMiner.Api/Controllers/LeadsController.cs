using Hangfire;
using Microsoft.AspNetCore.Mvc;
using SignalMiner.Api.Dtos;
using SignalMiner.Application;
using SignalMiner.Domain;
using SignalMiner.Infrastructure;

namespace SignalMiner.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class LeadsController(
    ILeadWorkflow workflow,
    ILeadRepository repository) : ControllerBase
{
    [HttpPost("discover")]
    public async Task<ActionResult<IReadOnlyList<LeadDto>>> Discover(
        DiscoverLeadsRequest request,
        CancellationToken cancellationToken)
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

        try
        {
            var discovered = await workflow.DiscoverAsync(request, cancellationToken);
            return Ok(discovered.Select(LeadDto.From).ToArray());
        }
        catch (DiscoveryRateLimitException ex)
        {
            if (ex.RetryAfter is not null)
            {
                Response.Headers.RetryAfter = ((int)Math.Max(0, (ex.RetryAfter.Value - DateTimeOffset.UtcNow).TotalSeconds)).ToString();
            }

            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                message = ex.Message,
                retryAfter = ex.RetryAfter
            });
        }
    }

    [HttpPost("{id:guid}/enrich")]
    public async Task<ActionResult<LeadDto>> Enrich(Guid id, CancellationToken cancellationToken)
    {
        var lead = await workflow.EnrichAsync(id, cancellationToken);
        return lead is null ? NotFound() : Ok(LeadDto.From(lead));
    }

    [HttpPatch("{id:guid}/outreach-status")]
    public async Task<ActionResult<LeadDto>> UpdateOutreachStatus(
        Guid id,
        UpdateOutreachStatusRequest request,
        CancellationToken cancellationToken)
    {
        var lead = await workflow.UpdateOutreachStatusAsync(id, request, cancellationToken);
        return lead is null ? NotFound() : Ok(LeadDto.From(lead));
    }

    [HttpPost("{id:guid}/outreach-events")]
    public async Task<ActionResult<OutreachEventDto>> AddOutreachEvent(
        Guid id,
        OutreachEventRequest request,
        CancellationToken cancellationToken)
    {
        var evt = await workflow.AddOutreachEventAsync(id, request, cancellationToken);
        return evt is null ? NotFound() : Ok(OutreachEventDto.From(evt));
    }

    [HttpGet("search")]
    public async Task<ActionResult<LeadSearchDto>> Search(
        [FromQuery] string? query,
        [FromQuery] int? minFitScore,
        [FromQuery] ContactStatus? contactStatus,
        [FromQuery] LeadStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var result = await repository.SearchAsync(
            new LeadSearchRequest(query, minFitScore, contactStatus, status, null, page <= 0 ? 1 : page, pageSize <= 0 ? 25 : pageSize),
            cancellationToken);

        return Ok(new LeadSearchDto(result.Items.Select(LeadDto.From).ToArray(), result.Total));
    }
}
