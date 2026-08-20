using Hangfire;
using Microsoft.EntityFrameworkCore;
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
    ILeadRepository repository,
    IManualEmailService manualEmail) : ControllerBase
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
        try
        {
            var lead = await workflow.EnrichAsync(id, cancellationToken);
            return lead is null ? NotFound() : Ok(LeadDto.From(lead));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "This lead changed while enrichment was running. Refresh the list and try again." });
        }
        catch (WebsiteExtractionException ex)
        {
            return UnprocessableEntity(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/email")]
    [RequestSizeLimit(12_000_000)]
    public async Task<ActionResult<LeadDto>> SendManualEmail(
        Guid id,
        [FromForm] string? toEmail,
        [FromForm] string subject,
        [FromForm] string body,
        [FromForm] string? bodyHtml,
        [FromForm] string? replyTo,
        [FromForm] string? cc,
        [FromForm] string? bcc,
        [FromForm] IReadOnlyList<IFormFile>? attachments,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = new SendManualEmailRequest(
                toEmail,
                subject,
                body,
                bodyHtml,
                replyTo,
                cc,
                bcc,
                await ReadAttachmentsAsync(attachments, cancellationToken));
            var lead = await manualEmail.SendAsync(id, request, cancellationToken);
            return lead is null ? NotFound() : Ok(LeadDto.From(lead));
        }
        catch (ManualEmailException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "This lead changed while the email was being sent. Refresh the list and check Titan sent mail before trying again." });
        }
    }

    private static async Task<IReadOnlyList<EmailAttachment>> ReadAttachmentsAsync(
        IReadOnlyList<IFormFile>? files,
        CancellationToken cancellationToken)
    {
        if (files is null || files.Count == 0)
        {
            return [];
        }

        var attachments = new List<EmailAttachment>();
        foreach (var file in files.Where(file => file.Length > 0))
        {
            await using var stream = file.OpenReadStream();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory, cancellationToken);
            attachments.Add(new EmailAttachment(
                Path.GetFileName(file.FileName),
                file.ContentType,
                memory.ToArray()));
        }

        return attachments;
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
        [FromQuery] SourceKind? sourceKind,
        [FromQuery] bool? importedOnly,
        [FromQuery] bool? hasEmail,
        [FromQuery] bool? hasLinkedIn,
        [FromQuery] bool? hasWebsite,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var result = await repository.SearchAsync(
            new LeadSearchRequest(
                query,
                minFitScore,
                contactStatus,
                status,
                sourceKind,
                importedOnly,
                hasEmail,
                hasLinkedIn,
                hasWebsite,
                null,
                page <= 0 ? 1 : page,
                pageSize <= 0 ? 25 : pageSize),
            cancellationToken);

        return Ok(new LeadSearchDto(result.Items.Select(LeadDto.From).ToArray(), result.Total));
    }
}
