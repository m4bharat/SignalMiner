using Microsoft.AspNetCore.Mvc;
using SignalMiner.Application;

namespace SignalMiner.Api.Controllers;

[ApiController]
[Route("api/outreach")]
public sealed class OutreachSafetyController(ISuppressionService suppression, IOutreachSafety safety) : ControllerBase
{
    [HttpGet("policy")]
    public Task<OutreachCapacity> Policy(CancellationToken cancellationToken) => safety.CapacityAsync(cancellationToken);

    [HttpGet("suppressions")]
    public Task<SuppressionSearchResult> Search([FromQuery] string? query, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default) =>
        suppression.SearchAsync(query, page, pageSize, cancellationToken);

    [HttpPost("leads/{id:guid}/suppression")]
    public async Task<IActionResult> Suppress(Guid id, SuppressionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var record = await suppression.RecordAsync(id, request, cancellationToken);
            return record is null ? NotFound() : Ok(record);
        }
        catch (ManualEmailException ex) { return StatusCode(ex.StatusCode, new { message = ex.Message }); }
    }
}
