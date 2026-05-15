using Microsoft.AspNetCore.Mvc;
using SignalMiner.Infrastructure;

namespace SignalMiner.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class ComplianceController : ControllerBase
{
    [HttpGet]
    public ActionResult Get() => Ok(new
    {
        CompliancePolicy.ManualReviewFirst,
        CompliancePolicy.NoAutomatedMessaging,
        Rules = new[]
        {
            "No LinkedIn scraping; only user-entered public LinkedIn URLs are stored.",
            "No automated messaging.",
            "No credential or browser-session handling.",
            "No CAPTCHA bypass.",
            "Every lead starts in manual review."
        }
    });
}
