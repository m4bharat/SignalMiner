using System.Text.RegularExpressions;
using AngleSharp;
using Microsoft.Playwright;
using SignalMiner.Application;
using SignalMiner.Domain;

namespace SignalMiner.Infrastructure;

public sealed partial class WebsiteExtractionService : IWebsiteExtractionService
{
    public async Task<WebsiteSnapshot?> ExtractAsync(Lead lead, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(lead.WebsiteUrl, UriKind.Absolute, out var uri) || CompliancePolicy.IsBlockedScrapeTarget(uri))
        {
            return null;
        }

        var html = await FetchRenderedHtmlAsync(uri, cancellationToken);
        var context = BrowsingContext.New(Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(html).Address(uri), cancellationToken);
        var text = document.Body?.TextContent ?? string.Empty;
        var links = document.Links
            .OfType<AngleSharp.Html.Dom.IHtmlAnchorElement>()
            .Select(a => a.Href)
            .Where(href => Uri.TryCreate(href, UriKind.Absolute, out var link) && !CompliancePolicy.IsBlockedScrapeTarget(link))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .ToArray();

        var emails = EmailRegex().Matches(text)
            .Select(m => m.Value.Trim().ToLowerInvariant())
            .Distinct()
            .Take(10)
            .ToArray();

        var title = document.Title ?? uri.Host;
        var description = document.QuerySelector("meta[name='description']")?.GetAttribute("content");
        return new WebsiteSnapshot
        {
            LeadId = lead.Id,
            Url = uri.ToString(),
            Title = title,
            Description = description,
            PublicEmails = emails,
            Links = links,
            QualityScore = ScoreWebsite(title, description, text, links)
        };
    }

    private static async Task<string> FetchRenderedHtmlAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var page = await browser.NewPageAsync(new BrowserNewPageOptions { JavaScriptEnabled = true });
        await page.GotoAsync(uri.ToString(), new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 15000 });
        cancellationToken.ThrowIfCancellationRequested();
        return await page.ContentAsync();
    }

    private static int ScoreWebsite(string title, string? description, string body, string[] links)
    {
        var score = 20;
        if (!string.IsNullOrWhiteSpace(title)) score += 15;
        if (!string.IsNullOrWhiteSpace(description)) score += 20;
        if (body.Length > 1000) score += 20;
        if (links.Length >= 5) score += 15;
        if (body.Contains("privacy", StringComparison.OrdinalIgnoreCase)) score += 10;
        return Math.Clamp(score, 0, 100);
    }

    [GeneratedRegex(@"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase)]
    private static partial Regex EmailRegex();
}
