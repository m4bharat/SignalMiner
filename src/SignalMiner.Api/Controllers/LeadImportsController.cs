using Microsoft.AspNetCore.Mvc;
using SignalMiner.Application;

namespace SignalMiner.Api.Controllers;

[ApiController]
[Route("api/leads/import")]
public sealed class LeadImportsController(ILeadImportService imports) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(10_000_000)]
    public async Task<ActionResult<LeadImportResult>> Import(
        IFormFile? file,
        [FromForm] bool commit = false,
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { message = "Upload a CSV or XLSX file." });
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension is not ".csv" and not ".xlsx")
        {
            return BadRequest(new { message = "Only CSV and XLSX files are supported." });
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await imports.ImportAsync(stream, file.FileName, commit, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
