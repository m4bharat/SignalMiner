using System.Net;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Html.Dom;
using SignalMiner.Application;
using SignalMiner.Domain;

namespace SignalMiner.Infrastructure;

public sealed partial class XDiscoveryService(HttpClient httpClient) : IXDiscoveryService
{
    private static readonly string[] ReservedPaths =
    [
        "home", "search", "explore", "i", "intent", "share", "settings",
        "login", "logout", "signup", "privacy", "tos", "jobs", "download"
    ];

    public async Task<IReadOnlyList<Lead>> DiscoverAsync(DiscoverLeadsRequest request, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(request.Limit, 1, 50);
        var candidateUrls = await FindCandidateProfileUrlsAsync(request.Query, limit, cancellationToken);
        var leads = new List<Lead>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var blockedProfiles = 0;

        foreach (var profileUrl in candidateUrls)
        {
            if (!seen.Add(profileUrl))
            {
                continue;
            }

            var profile = await ExtractProfileAsync(profileUrl, cancellationToken);
            if (profile is null)
            {
                blockedProfiles++;
                continue;
            }

            leads.Add(profile.ToLead());
            if (leads.Count >= limit)
            {
                break;
            }
        }

        if (leads.Count == 0 && blockedProfiles > 0)
        {
            throw new DiscoveryRateLimitException("X public HTML access was blocked or limited. SignalMiner does not use login sessions, cookies, CAPTCHA bypass, or anti-detection workarounds.");
        }

        return leads;
    }

    public static string? NormalizeProfileUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = WebUtility.HtmlDecode(value.Trim());
        if (trimmed.StartsWith('@'))
        {
            return BuildProfileUrl(trimmed[1..]);
        }

        if (!trimmed.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = $"https://{trimmed}";
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (!IsXHost(uri.Host))
        {
            return null;
        }

        var handle = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return BuildProfileUrl(handle);
    }

    private async Task<IReadOnlyList<string>> FindCandidateProfileUrlsAsync(string query, int limit, CancellationToken cancellationToken)
    {
        var directUrl = NormalizeProfileUrl(query);
        if (directUrl is not null)
        {
            return [directUrl];
        }

        var candidates = new List<string>();
        var blockedSearchPages = 0;
        foreach (var searchUrl in BuildSearchUrls(query))
        {
            using var response = await httpClient.GetAsync(searchUrl, cancellationToken);
            if (IsBlockedOrLimited(response.StatusCode))
            {
                blockedSearchPages++;
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                continue;
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            if (LooksBlocked(html))
            {
                blockedSearchPages++;
                continue;
            }

            var document = await ParseHtmlAsync(html, searchUrl.ToString(), cancellationToken);
            candidates.AddRange(ExtractCandidateProfileUrls(document, html));
            if (candidates.Count >= limit * 3)
            {
                break;
            }
        }

        var distinct = candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(limit * 3)
            .ToArray();
        if (distinct.Length == 0 && blockedSearchPages > 0)
        {
            throw new DiscoveryRateLimitException("Public search results for X profiles are currently blocked, limited, or login-gated.");
        }

        return distinct;
    }

    private async Task<XProfile?> ExtractProfileAsync(string profileUrl, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(profileUrl, cancellationToken);
        if (IsBlockedOrLimited(response.StatusCode))
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        if (LooksBlocked(html))
        {
            return null;
        }

        var document = await ParseHtmlAsync(html, profileUrl, cancellationToken);
        var normalizedUrl = NormalizeProfileUrl(profileUrl);
        var handle = normalizedUrl?.Split('/').LastOrDefault();
        if (string.IsNullOrWhiteSpace(normalizedUrl) || string.IsNullOrWhiteSpace(handle))
        {
            return null;
        }

        var title = document.QuerySelector("meta[property='og:title']")?.GetAttribute("content")
            ?? document.Title;
        var description = document.QuerySelector("meta[property='og:description']")?.GetAttribute("content")
            ?? document.QuerySelector("meta[name='description']")?.GetAttribute("content")
            ?? document.Body?.TextContent;

        var displayName = ExtractDisplayName(title, handle);
        var links = document.Links
            .OfType<IHtmlAnchorElement>()
            .Select(anchor => anchor.Href)
            .Where(link => !string.IsNullOrWhiteSpace(link))
            .Select(UnwrapSearchRedirect)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var websiteUrl = links.FirstOrDefault(IsAllowedExternalWebsite);
        var linkedInUrl = links.FirstOrDefault(IsLinkedInUrl);
        var visibleText = string.Join(' ', description, document.Body?.TextContent);
        var publicEmail = ExtractEmail(visibleText);
        var notes = BuildNotes(description, websiteUrl);

        return new XProfile(
            displayName,
            normalizedUrl,
            handle,
            CleanText(description),
            websiteUrl,
            linkedInUrl,
            publicEmail,
            notes);
    }

    private static IReadOnlyList<Uri> BuildSearchUrls(string query)
    {
        var terms = string.IsNullOrWhiteSpace(query) ? "founder AI" : query.Trim();
        return
        [
            BuildDuckDuckGoHtmlSearchUrl("x.com", terms),
            BuildDuckDuckGoHtmlSearchUrl("twitter.com", terms),
            BuildDuckDuckGoLiteSearchUrl("x.com", terms),
            BuildDuckDuckGoLiteSearchUrl("twitter.com", terms)
        ];
    }

    private static Uri BuildDuckDuckGoHtmlSearchUrl(string host, string terms)
    {
        var search = $"site:{host} (\"founder\" OR \"recruiter\" OR \"sales\" OR \"GTM\" OR \"creator\") {terms}";
        return new Uri($"https://html.duckduckgo.com/html/?q={Uri.EscapeDataString(search)}");
    }

    private static Uri BuildDuckDuckGoLiteSearchUrl(string host, string terms)
    {
        var search = $"site:{host} (\"founder\" OR \"recruiter\" OR \"sales\" OR \"GTM\" OR \"creator\") {terms}";
        return new Uri($"https://lite.duckduckgo.com/lite/?q={Uri.EscapeDataString(search)}");
    }

    private static IEnumerable<string> ExtractCandidateProfileUrls(AngleSharp.Dom.IDocument document, string html)
    {
        var anchorUrls = document.Links
            .OfType<IHtmlAnchorElement>()
            .Select(anchor => anchor.Href)
            .Select(UnwrapSearchRedirect)
            .Select(NormalizeProfileUrl)
            .OfType<string>();

        var decodedHtml = WebUtility.HtmlDecode(Uri.UnescapeDataString(html));
        var rawUrls = XProfileUrlRegex().Matches(decodedHtml)
            .Select(match => NormalizeProfileUrl(match.Value))
            .OfType<string>();

        return anchorUrls.Concat(rawUrls);
    }

    private static async Task<AngleSharp.Dom.IDocument> ParseHtmlAsync(string html, string address, CancellationToken cancellationToken)
    {
        var context = BrowsingContext.New(Configuration.Default);
        return await context.OpenAsync(req => req.Content(html).Address(address), cancellationToken);
    }

    private static string? BuildProfileUrl(string? handle)
    {
        if (string.IsNullOrWhiteSpace(handle))
        {
            return null;
        }

        var cleanHandle = handle.Trim().TrimStart('@');
        if (!HandleRegex().IsMatch(cleanHandle) ||
            ReservedPaths.Contains(cleanHandle, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        return $"https://x.com/{cleanHandle}";
    }

    private static string ExtractDisplayName(string? title, string handle)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return handle;
        }

        var withoutSuffix = title.Replace(" / X", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" on X", string.Empty, StringComparison.OrdinalIgnoreCase);
        var atIndex = withoutSuffix.IndexOf("(@", StringComparison.Ordinal);
        if (atIndex > 0)
        {
            return withoutSuffix[..atIndex].Trim();
        }

        return string.IsNullOrWhiteSpace(withoutSuffix) ? handle : withoutSuffix.Trim();
    }

    private static string? UnwrapSearchRedirect(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url;
        }

        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .FirstOrDefault(parts => parts.Length == 2 && parts[0].Equals("uddg", StringComparison.OrdinalIgnoreCase));
        return query is null ? url : Uri.UnescapeDataString(query[1]);
    }

    private static bool IsAllowedExternalWebsite(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return !IsXHost(uri.Host) &&
            !uri.Host.Contains("linkedin.com", StringComparison.OrdinalIgnoreCase) &&
            !CompliancePolicy.IsBlockedScrapeTarget(uri);
    }

    private static bool IsLinkedInUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Host.Contains("linkedin.com", StringComparison.OrdinalIgnoreCase);

    private static bool IsXHost(string host) =>
        host.Equals("x.com", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("www.x.com", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("twitter.com", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("www.twitter.com", StringComparison.OrdinalIgnoreCase);

    private static bool IsBlockedOrLimited(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests;

    private static bool LooksBlocked(string html) =>
        html.Contains("captcha", StringComparison.OrdinalIgnoreCase) ||
        html.Contains("Log in to X", StringComparison.OrdinalIgnoreCase) ||
        html.Contains("Sign in to X", StringComparison.OrdinalIgnoreCase) ||
        html.Contains("JavaScript is not available", StringComparison.OrdinalIgnoreCase);

    private static string? ExtractEmail(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return EmailRegex().Match(text).Value is { Length: > 0 } email ? email.ToLowerInvariant() : null;
    }

    private static string? CleanText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return WhitespaceRegex().Replace(WebUtility.HtmlDecode(text), " ").Trim();
    }

    private static string? BuildNotes(string? bio, string? websiteUrl)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(bio))
        {
            parts.Add(CleanText(bio)!);
        }

        if (!string.IsNullOrWhiteSpace(websiteUrl))
        {
            parts.Add($"Website link visible on public X profile: {websiteUrl}");
        }

        parts.Add("Discovered from public X/profile-page signals only; requires manual review before outreach.");
        return string.Join(Environment.NewLine, parts);
    }

    private sealed record XProfile(
        string DisplayName,
        string XUrl,
        string Handle,
        string? Bio,
        string? WebsiteUrl,
        string? LinkedInUrl,
        string? PublicEmail,
        string? Notes)
    {
        public Lead ToLead()
        {
            var sourceProfiles = new List<SourceProfile>
            {
                new()
                {
                    Kind = SourceKind.X,
                    Url = XUrl,
                    PublicHandle = Handle,
                    Bio = Bio
                }
            };

            if (!string.IsNullOrWhiteSpace(LinkedInUrl))
            {
                sourceProfiles.Add(new SourceProfile
                {
                    Kind = SourceKind.LinkedInProfileUrlOnly,
                    Url = LinkedInUrl,
                    PublicHandle = Handle,
                    Bio = "Public LinkedIn URL visible on an allowed public profile link; not scraped."
                });
            }

            return new Lead
            {
                DisplayName = DisplayName,
                PublicEmail = PublicEmail,
                WebsiteUrl = WebsiteUrl,
                XUrl = XUrl,
                LinkedInUrl = LinkedInUrl,
                Notes = Notes,
                Status = LeadStatus.NeedsManualReview,
                SourceProfiles = sourceProfiles
            };
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9_]{1,15}$")]
    private static partial Regex HandleRegex();

    [GeneratedRegex(@"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"https?://(?:www\.)?(?:x|twitter)\.com/[A-Za-z0-9_]{1,15}(?:/[^\s""'<>]*)?", RegexOptions.IgnoreCase)]
    private static partial Regex XProfileUrlRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
