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
        try
        {
            await page.GotoAsync(uri.ToString(), new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 15000 });
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 3000 });
        }
        catch (TimeoutException)
        {
            // Some public sites keep analytics or live connections open. Use the rendered HTML captured so far.
        }
        catch (PlaywrightException exception) when (exception.Message.Contains("Timeout", StringComparison.OrdinalIgnoreCase))
        {
            // Some public sites keep analytics or live connections open. Use the rendered HTML captured so far.
        }
        catch (PlaywrightException exception) when (IsNavigationFailure(exception))
        {
            throw new WebsiteExtractionException(BuildNavigationFailureMessage(uri, exception));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await CaptureContentAsync(uri, page, cancellationToken);
    }

    private static async Task<string> CaptureContentAsync(Uri uri, IPage page, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                return await page.ContentAsync();
            }
            catch (PlaywrightException exception) when (IsPageStillChanging(exception) && attempt < 3)
            {
                await Task.Delay(500, cancellationToken);
            }
            catch (PlaywrightException exception) when (IsPageStillChanging(exception))
            {
                throw new WebsiteExtractionException($"Website is still redirecting or changing content: {uri.Host}. Try again later or use the final website URL.");
            }
        }

        throw new WebsiteExtractionException($"Website content could not be read: {uri.Host}. Try again later or use another website URL.");
    }

    private static bool IsNavigationFailure(PlaywrightException exception) =>
        exception.Message.Contains("net::ERR_", StringComparison.OrdinalIgnoreCase);

    private static bool IsPageStillChanging(PlaywrightException exception) =>
        exception.Message.Contains("page is navigating", StringComparison.OrdinalIgnoreCase) ||
        exception.Message.Contains("changing the content", StringComparison.OrdinalIgnoreCase);

    private static string BuildNavigationFailureMessage(Uri uri, PlaywrightException exception)
    {
        if (exception.Message.Contains("ERR_NAME_NOT_RESOLVED", StringComparison.OrdinalIgnoreCase))
        {
            return $"Website could not be reached: {uri.Host} did not resolve. Check the domain or use another website URL.";
        }

        if (exception.Message.Contains("ERR_CONNECTION_REFUSED", StringComparison.OrdinalIgnoreCase))
        {
            return $"Website refused the connection: {uri.Host}. Try again later or use another website URL.";
        }

        if (exception.Message.Contains("ERR_CONNECTION_TIMED_OUT", StringComparison.OrdinalIgnoreCase))
        {
            return $"Website timed out: {uri.Host}. Try again later or use another website URL.";
        }

        if (exception.Message.Contains("ERR_CERT", StringComparison.OrdinalIgnoreCase))
        {
            return $"Website has a certificate problem: {uri.Host}. Use a valid website URL before enriching.";
        }

        return $"Website could not be reached: {uri.Host}. Try again later or use another website URL.";
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
