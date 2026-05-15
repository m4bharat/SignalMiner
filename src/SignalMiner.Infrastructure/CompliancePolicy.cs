namespace SignalMiner.Infrastructure;

public static class CompliancePolicy
{
    public const string ManualReviewFirst = "All discovered leads must be reviewed by a human before outreach.";
    public const string NoAutomatedMessaging = "SignalMiner stores templates and manual events only; it does not send automated messages.";

    public static bool IsBlockedScrapeTarget(Uri uri)
    {
        var host = uri.Host.ToLowerInvariant();
        return host.Contains("linkedin.com");
    }
}
