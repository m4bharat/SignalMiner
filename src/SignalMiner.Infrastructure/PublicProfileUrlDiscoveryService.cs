using System.Net;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Html.Dom;
using SignalMiner.Application;
using SignalMiner.Domain;

namespace SignalMiner.Infrastructure;

public sealed partial class PublicProfileUrlDiscoveryService(HttpClient httpClient) : ILinkedInDiscoveryService
{
    private static readonly string[] DiscoveryHosts = ["linkedin.com/in", "linkedin.com/company"];
    private static readonly string[] PersonaSignals =
    [
        "founder", "co-founder", "creator", "personal brand", "startup",
        "saas", "ai", "gtm", "sales", "recruiter", "consultant"
    ];

    public async Task<IReadOnlyList<Lead>> DiscoverAsync(DiscoverLeadsRequest request, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(request.Limit, 1, 50);
        var candidateUrls = await FindCandidateUrlsAsync(request.Query, limit, cancellationToken);

        return candidateUrls
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(ToLead)
            .ToArray();
    }

    public static string? NormalizeLinkedInUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = WebUtility.HtmlDecode(value.Trim())
            .TrimEnd('.', ',', ')', ']', '"', '\'');
        if (!trimmed.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = $"https://{trimmed}";
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || !IsLinkedInHost(uri.Host))
        {
            return null;
        }

        var segments = uri.AbsolutePath
            .Trim('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2 ||
            segments[0] is not ("in" or "company") ||
            string.IsNullOrWhiteSpace(segments[1]))
        {
            return null;
        }

        return $"https://www.linkedin.com/{segments[0]}/{segments[1]}";
    }

    private async Task<IReadOnlyList<string>> FindCandidateUrlsAsync(string query, int limit, CancellationToken cancellationToken)
    {
        var directUrl = NormalizeLinkedInUrl(query);
        var directUrls = new[] { directUrl }
            .Concat(ExtractCandidateUrls(query))
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToArray();
        if (directUrls.Length > 0)
        {
            return directUrls;
        }

        var candidates = new List<string>();
        var blockedSearchPages = 0;
        var unavailableSearchPages = 0;
        foreach (var searchUrl in BuildSearchUrls(query))
        {
            HttpResponseMessage response;
            try
            {
                response = await httpClient.GetAsync(searchUrl, cancellationToken);
            }
            catch (Exception exception) when ((exception is HttpRequestException || exception is TaskCanceledException) && !cancellationToken.IsCancellationRequested)
            {
                unavailableSearchPages++;
                continue;
            }

            using (response)
            {
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
                candidates.AddRange(ExtractCandidateUrls(document, html));
                if (candidates.Count >= limit * 3)
                {
                    break;
                }
            }
        }

        var distinct = candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(limit * 3)
            .ToArray();
        if (distinct.Length == 0 && blockedSearchPages > 0)
        {
            throw new DiscoveryRateLimitException("Public search results for LinkedIn URLs are currently blocked or limited. SignalMiner does not scrape LinkedIn, use login sessions, cookies, CAPTCHA bypass, or anti-detection workarounds.");
        }

        if (distinct.Length == 0 && unavailableSearchPages > 0)
        {
            throw new DiscoverySourceUnavailableException("LinkedIn URL discovery could not reach public search-result providers. Try again later, use a more specific query, or paste LinkedIn profile/company URLs directly.");
        }

        return distinct;
    }

    private static IReadOnlyList<Uri> BuildSearchUrls(string query)
    {
        var terms = string.IsNullOrWhiteSpace(query) ? "founder personal brand" : query.Trim();
        var searchPhrases = BuildPersonaSearchPhrases(terms).Take(5).ToArray();
        return searchPhrases
            .SelectMany(searchPhrase => DiscoveryHosts.SelectMany(host => new[]
            {
                BuildBingSearchUrl(host, searchPhrase),
                BuildDuckDuckGoHtmlSearchUrl(host, searchPhrase),
                BuildDuckDuckGoLiteSearchUrl(host, searchPhrase)
            }))
            .ToArray();
    }

    private static string[] BuildPersonaSearchPhrases(string query)
    {
        var normalized = query.Trim();
        var lower = normalized.ToLowerInvariant();
        var phrases = new List<string> { normalized };

        if (lower.Contains("personal brand") || lower.Contains("creator") || lower.Contains("content"))
        {
            phrases.AddRange([
                "\"personal brand\" OR \"creator\" OR \"content\"",
                "\"linkedin\" \"creator\" OR \"linkedin\" \"personal brand\""
            ]);
        }

        if (lower.Contains("founder") || lower.Contains("startup") || lower.Contains("saas"))
        {
            phrases.AddRange([
                "\"startup founder\" OR \"SaaS founder\" OR \"co-founder\"",
                "\"founder\" \"SaaS\" OR \"founder\" \"AI\""
            ]);
        }

        var fallbackSignals = string.Join(" OR ", PersonaSignals.Select(signal => $"\"{signal}\""));
        phrases.Add($"({fallbackSignals}) {normalized}");
        phrases.Add($"{normalized} founder CEO startup SaaS");

        return phrases
            .Where(phrase => !string.IsNullOrWhiteSpace(phrase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static Uri BuildDuckDuckGoHtmlSearchUrl(string host, string searchPhrase)
    {
        var search = BuildSearchExpression(host, searchPhrase);
        return new Uri($"https://html.duckduckgo.com/html/?q={Uri.EscapeDataString(search)}");
    }

    private static Uri BuildDuckDuckGoLiteSearchUrl(string host, string searchPhrase)
    {
        var search = BuildSearchExpression(host, searchPhrase);
        return new Uri($"https://lite.duckduckgo.com/lite/?q={Uri.EscapeDataString(search)}");
    }

    private static Uri BuildBingSearchUrl(string host, string searchPhrase)
    {
        var search = BuildSearchExpression(host, searchPhrase);
        return new Uri($"https://www.bing.com/search?q={Uri.EscapeDataString(search)}");
    }

    private static string BuildSearchExpression(string host, string searchPhrase) =>
        $"site:{host} {searchPhrase} -jobs -pulse -posts";

    private static IEnumerable<string> ExtractCandidateUrls(AngleSharp.Dom.IDocument document, string html)
    {
        var anchorUrls = document.Links
            .OfType<IHtmlAnchorElement>()
            .Select(anchor => anchor.Href)
            .SelectMany(ExtractCandidateUrlsFromMaybeRedirect)
            .OfType<string>();

        return anchorUrls.Concat(ExtractCandidateUrls(html));
    }

    private static IEnumerable<string> ExtractCandidateUrls(string text)
    {
        var decoded = WebUtility.HtmlDecode(Uri.UnescapeDataString(text));
        var rawUrls = LinkedInUrlRegex().Matches(decoded)
            .Select(match => NormalizeLinkedInUrl(match.Value))
            .OfType<string>();
        var embeddedRedirectUrls = UrlParameterRegex().Matches(decoded)
            .Select(match => Uri.UnescapeDataString(match.Groups["url"].Value))
            .SelectMany(ExtractCandidateUrls)
            .OfType<string>();

        return rawUrls.Concat(embeddedRedirectUrls);
    }

    private static async Task<AngleSharp.Dom.IDocument> ParseHtmlAsync(string html, string address, CancellationToken cancellationToken)
    {
        var context = BrowsingContext.New(Configuration.Default);
        return await context.OpenAsync(req => req.Content(html).Address(address), cancellationToken);
    }

    private static IEnumerable<string> ExtractCandidateUrlsFromMaybeRedirect(string? url)
    {
        var normalized = NormalizeLinkedInUrl(url);
        if (normalized is not null)
        {
            yield return normalized;
            yield break;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            yield break;
        }

        foreach (var value in ExtractQueryParameterValues(uri.Query))
        {
            var decoded = DecodePossiblyEncodedUrl(value);
            foreach (var candidate in ExtractCandidateUrls(decoded))
            {
                yield return candidate;
            }
        }
    }

    private static Lead ToLead(string linkedInUrl)
    {
        var publicHandle = ExtractPublicHandle(linkedInUrl);
        return new Lead
        {
            DisplayName = ToDisplayName(publicHandle),
            LinkedInUrl = linkedInUrl,
            Notes = "Discovered as a public LinkedIn URL only. SignalMiner does not scrape LinkedIn; manual review is required before outreach.",
            Status = LeadStatus.NeedsManualReview,
            SourceProfiles =
            [
                new SourceProfile
                {
                    Kind = SourceKind.LinkedInProfileUrlOnly,
                    Url = linkedInUrl,
                    PublicHandle = publicHandle,
                    Bio = "Public LinkedIn URL discovered from a direct query or public search result; not scraped."
                }
            ]
        };
    }

    private static string ExtractPublicHandle(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url;
        }

        return uri.AbsolutePath
            .Trim('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .FirstOrDefault() ?? url;
    }

    private static string ToDisplayName(string publicHandle)
    {
        var text = publicHandle.Replace('-', ' ').Replace('_', ' ');
        return string.IsNullOrWhiteSpace(text)
            ? "LinkedIn lead"
            : string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
    }

    private static bool IsLinkedInHost(string host) =>
        host.Equals("linkedin.com", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("www.linkedin.com", StringComparison.OrdinalIgnoreCase);

    private static bool IsBlockedOrLimited(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests;

    private static bool LooksBlocked(string html) =>
        html.Contains("captcha", StringComparison.OrdinalIgnoreCase) ||
        html.Contains("unusual traffic", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> ExtractQueryParameterValues(string query) =>
        query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .Select(parts => parts[1]);

    private static string DecodePossiblyEncodedUrl(string value)
    {
        var decoded = Uri.UnescapeDataString(value);
        if (decoded.StartsWith("a1", StringComparison.OrdinalIgnoreCase))
        {
            var base64 = decoded[2..].Replace('-', '+').Replace('_', '/');
            switch (base64.Length % 4)
            {
                case 2:
                    base64 += "==";
                    break;
                case 3:
                    base64 += "=";
                    break;
            }

            try
            {
                return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            }
            catch (FormatException)
            {
                return decoded;
            }
        }

        return decoded;
    }

    [GeneratedRegex(@"https?://(?:www\.)?linkedin\.com/(?:in|company)/[A-Za-z0-9._%+-]+/?", RegexOptions.IgnoreCase)]
    private static partial Regex LinkedInUrlRegex();

    [GeneratedRegex(@"(?:uddg|url|u|q)= (?<url>https?%3A%2F%2F[^&\s""']+)", RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex UrlParameterRegex();
}
