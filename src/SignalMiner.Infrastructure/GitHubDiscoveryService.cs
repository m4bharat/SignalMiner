using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using AngleSharp;
using AngleSharp.Html.Dom;
using SignalMiner.Application;
using SignalMiner.Domain;

namespace SignalMiner.Infrastructure;

public sealed partial class GitHubDiscoveryService(HttpClient httpClient) : IGitHubDiscoveryService
{
    public async Task<IReadOnlyList<Lead>> DiscoverAsync(DiscoverLeadsRequest request, CancellationToken cancellationToken)
    {
        var query = Uri.EscapeDataString($"{request.Query} in:bio type:user");
        var limit = Math.Clamp(request.Limit, 1, 50);
        var response = await SendGitHubRequestAsync<GitHubSearchResponse>(
            $"search/users?q={query}&per_page={limit}",
            cancellationToken);

        var leads = new List<Lead>();
        foreach (var item in response?.Items ?? [])
        {
            var user = await SendGitHubRequestAsync<GitHubUser>(item.Url, cancellationToken);
            if (user is null)
            {
                continue;
            }

            var profileContact = await ExtractPublicGitHubProfileContactAsync(user.HtmlUrl, cancellationToken);
            var socialAccounts = await SendGitHubRequestAsync<GitHubSocialAccount[]>(
                $"users/{item.Login}/social_accounts",
                cancellationToken) ?? [];
            var xUrl = BuildXUrl(user.TwitterUsername)
                ?? FindSocialUrl(socialAccounts, "twitter.com", "x.com")
                ?? FindUrl(profileContact.Links, "twitter.com", "x.com");
            var linkedInUrl = FindSocialUrl(socialAccounts, "linkedin.com/in", "linkedin.com/company")
                ?? FindUrl(profileContact.Links, "linkedin.com/in", "linkedin.com/company");
            var websiteUrl = NormalizeUrl(user.Blog)
                ?? FindNonGitHubWebsite(socialAccounts)
                ?? FindNonGitHubWebsite(profileContact.Links);
            var linkedPublicContact = await ExtractAllowedPublicPageContactAsync(websiteUrl, cancellationToken);
            var companyName = NormalizeCompanyName(user.Company);
            var sourceProfiles = new List<SourceProfile>
            {
                new()
                {
                    Kind = SourceKind.GitHub,
                    Url = user.HtmlUrl,
                    PublicHandle = item.Login,
                    Bio = user.Bio,
                    PublicActivityCount = user.PublicRepos
                }
            };

            if (!string.IsNullOrWhiteSpace(xUrl))
            {
                sourceProfiles.Add(new SourceProfile
                {
                    Kind = SourceKind.X,
                    Url = xUrl,
                    PublicHandle = user.TwitterUsername ?? item.Login,
                    Bio = user.Bio
                });
            }

            if (!string.IsNullOrWhiteSpace(linkedInUrl))
            {
                sourceProfiles.Add(new SourceProfile
                {
                    Kind = SourceKind.LinkedInProfileUrlOnly,
                    Url = linkedInUrl,
                    PublicHandle = item.Login,
                    Bio = "Public LinkedIn URL from GitHub social links; not scraped."
                });
            }

            if (!string.IsNullOrWhiteSpace(websiteUrl))
            {
                sourceProfiles.Add(new SourceProfile
                {
                    Kind = SourceKind.Website,
                    Url = websiteUrl,
                    PublicHandle = TryGetDomain(websiteUrl) ?? websiteUrl,
                    Bio = user.Bio
                });
            }

            leads.Add(new Lead
            {
                DisplayName = string.IsNullOrWhiteSpace(user.Name) ? item.Login : user.Name,
                PublicEmail = user.Email ?? profileContact.Email ?? linkedPublicContact.Email ?? ExtractEmail(user.Bio),
                WebsiteUrl = websiteUrl,
                GitHubUrl = user.HtmlUrl,
                XUrl = xUrl,
                LinkedInUrl = linkedInUrl,
                Notes = BuildNotes(user),
                Company = string.IsNullOrWhiteSpace(companyName)
                    ? null
                    : new Company
                    {
                        Name = companyName,
                        Summary = user.Bio,
                        Domain = TryGetDomain(websiteUrl),
                        Keywords = ExtractKeywords(user.Bio)
                    },
                SourceProfiles = sourceProfiles
            });
        }

        return leads;
    }

    private async Task<T?> SendGitHubRequestAsync<T>(string requestUri, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(requestUri, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            var retryAfter = GetRateLimitReset(response);
            var resetText = retryAfter is null ? "later" : retryAfter.Value.ToLocalTime().ToString("g");
            throw new DiscoveryRateLimitException(
                $"GitHub API rate limit exceeded. Try again {resetText}, or configure a GitHub token.",
                retryAfter);
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }

    private static DateTimeOffset? GetRateLimitReset(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("x-ratelimit-reset", out var values) &&
            long.TryParse(values.FirstOrDefault(), out var unixSeconds))
        {
            return DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        }

        return null;
    }

    private static string? NormalizeUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? value : $"https://{value}";
    }

    private static string? BuildXUrl(string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return null;
        }

        return $"https://x.com/{username.Trim().TrimStart('@')}";
    }

    private static string? FindSocialUrl(GitHubSocialAccount[] socialAccounts, params string[] needles) =>
        socialAccounts
            .Select(account => NormalizeUrl(account.Url))
            .FirstOrDefault(url => url is not null && needles.Any(needle => url.Contains(needle, StringComparison.OrdinalIgnoreCase)));

    private static string? FindUrl(IEnumerable<string> urls, params string[] needles) =>
        urls.Select(NormalizeUrl)
            .FirstOrDefault(url => url is not null && needles.Any(needle => url.Contains(needle, StringComparison.OrdinalIgnoreCase)));

    private static string? FindNonGitHubWebsite(GitHubSocialAccount[] socialAccounts) =>
        socialAccounts
            .Select(account => NormalizeUrl(account.Url))
            .FirstOrDefault(IsNonGitHubWebsite);

    private static string? FindNonGitHubWebsite(IEnumerable<string> urls) =>
        urls.Select(NormalizeUrl).FirstOrDefault(IsNonGitHubWebsite);

    private static bool IsNonGitHubWebsite(string? url) =>
        url is not null &&
                !url.Contains("github.com", StringComparison.OrdinalIgnoreCase) &&
                !url.Contains("linkedin.com", StringComparison.OrdinalIgnoreCase) &&
                !url.Contains("twitter.com", StringComparison.OrdinalIgnoreCase) &&
                !url.Contains("x.com", StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeCompanyName(string? company)
    {
        if (string.IsNullOrWhiteSpace(company))
        {
            return null;
        }

        return company.Trim().TrimStart('@');
    }

    private static string? ExtractEmail(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return EmailRegex().Match(text).Value is { Length: > 0 } email ? email.ToLowerInvariant() : null;
    }

    private static string BuildNotes(GitHubUser user)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(user.Bio)) parts.Add(user.Bio);
        if (!string.IsNullOrWhiteSpace(user.Location)) parts.Add($"Location: {user.Location}");
        if (!string.IsNullOrWhiteSpace(user.Company)) parts.Add($"Company: {user.Company}");
        return string.Join(Environment.NewLine, parts);
    }

    private static string? TryGetDomain(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        return uri.Host;
    }

    private static string[] ExtractKeywords(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var keywords = new[] { "founder", "saas", "ai", "laravel", "startup", "automation", "builder" };
        return keywords.Where(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    private async Task<PublicProfileContact> ExtractPublicGitHubProfileContactAsync(string profileUrl, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, profileUrl);
            request.Headers.Accept.Clear();
            request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml");
            request.Headers.UserAgent.Clear();
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 SignalMiner/0.1");

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new PublicProfileContact(null, []);
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var context = BrowsingContext.New(Configuration.Default);
            var document = await context.OpenAsync(req => req.Content(html).Address(profileUrl), cancellationToken);
            var links = document.Links
                .OfType<IHtmlAnchorElement>()
                .Select(anchor => anchor.Href)
                .Where(href => !string.IsNullOrWhiteSpace(href))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var email = ExtractEmailFromHtml(html, document, links);
            return new PublicProfileContact(email, links.Where(link => !link.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)).ToArray());
        }
        catch (HttpRequestException)
        {
            return new PublicProfileContact(null, []);
        }
    }

    private async Task<PublicProfileContact> ExtractAllowedPublicPageContactAsync(string? pageUrl, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var uri) || CompliancePolicy.IsBlockedScrapeTarget(uri))
        {
            return new PublicProfileContact(null, []);
        }

        if (uri.Host.Contains("github.com", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.Contains("twitter.com", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.Equals("x.com", StringComparison.OrdinalIgnoreCase))
        {
            return new PublicProfileContact(null, []);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Clear();
            request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml");
            request.Headers.UserAgent.Clear();
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 SignalMiner/0.1");

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new PublicProfileContact(null, []);
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var context = BrowsingContext.New(Configuration.Default);
            var document = await context.OpenAsync(req => req.Content(html).Address(uri), cancellationToken);
            var links = document.Links
                .OfType<IHtmlAnchorElement>()
                .Select(anchor => anchor.Href)
                .Where(href => !string.IsNullOrWhiteSpace(href))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var email = ExtractEmailFromHtml(html, document, links);
            return new PublicProfileContact(email, links.Where(link => !link.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)).ToArray());
        }
        catch (HttpRequestException)
        {
            return new PublicProfileContact(null, []);
        }
    }

    private static string? ExtractEmailFromHtml(string html, AngleSharp.Dom.IDocument document, string[] links)
    {
        var mailtoEmail = links
            .Where(link => link.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            .Select(link => Uri.UnescapeDataString(link["mailto:".Length..].Split('?')[0]))
            .FirstOrDefault(email => EmailRegex().IsMatch(email));
        if (!string.IsNullOrWhiteSpace(mailtoEmail))
        {
            return mailtoEmail.ToLowerInvariant();
        }

        var anchorTextEmail = document.Links
            .OfType<IHtmlAnchorElement>()
            .Select(anchor => anchor.TextContent)
            .Select(ExtractEmail)
            .FirstOrDefault(email => !string.IsNullOrWhiteSpace(email));
        if (!string.IsNullOrWhiteSpace(anchorTextEmail))
        {
            return anchorTextEmail;
        }

        return ExtractEmail(document.Body?.TextContent)
            ?? ExtractEmail(WebUtility.HtmlDecode(html));
    }

    private sealed record GitHubSearchResponse([property: JsonPropertyName("items")] GitHubSearchItem[] Items);
    private sealed record GitHubSearchItem(string Login, string Url, [property: JsonPropertyName("html_url")] string HtmlUrl);
    private sealed record GitHubUser(
        string? Name,
        string? Email,
        string? Blog,
        string? Bio,
        string? Company,
        string? Location,
        [property: JsonPropertyName("twitter_username")] string? TwitterUsername,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("public_repos")] int PublicRepos);
    private sealed record GitHubSocialAccount(string Provider, string Url);
    private sealed record PublicProfileContact(string? Email, string[] Links);

    [GeneratedRegex(@"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase)]
    private static partial Regex EmailRegex();
}
