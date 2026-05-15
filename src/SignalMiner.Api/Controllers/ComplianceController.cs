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
            "GitHub discovery uses public GitHub API/profile signals.",
            "X discovery uses only public profile pages and public search-result pages.",
            "No LinkedIn scraping; only LinkedIn URLs visible on allowed public websites/profiles are stored.",
            "No automated messaging.",
            "No credential, cookie, or browser-session handling.",
            "No CAPTCHA bypass, proxy, or anti-detection logic.",
            "Every lead starts in manual review."
        }
    });
}
