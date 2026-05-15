using System.Net;
using System.Reflection;
using SignalMiner.Application;
using SignalMiner.Domain;
using SignalMiner.Infrastructure;
using Xunit;

namespace SignalMiner.Tests;

public sealed class DiscoveryTests
{
    [Fact]
    public void DiscoverLeadsRequest_DefaultsToGitHub()
    {
        var request = new DiscoverLeadsRequest("founder AI");

        Assert.Equal(DiscoverySource.GitHub, request.Source);
    }

    [Fact]
    public async Task LeadWorkflow_RoutesGitHubSourceToGitHubService()
    {
        var gitHub = new RecordingDiscoveryService(new Lead { DisplayName = "GitHub Lead", GitHubUrl = "https://github.com/alice" });
        var x = new RecordingDiscoveryService(new Lead { DisplayName = "X Lead", XUrl = "https://x.com/alice" });
        var repository = new RecordingRepository();
        var workflow = new LeadWorkflow(repository, gitHub, x, new NoopWebsiteExtractionService(), new LeadScoringService());

        var leads = await workflow.DiscoverAsync(new DiscoverLeadsRequest("founder AI"), CancellationToken.None);

        Assert.True(gitHub.WasCalled);
        Assert.False(x.WasCalled);
        Assert.Single(leads);
        Assert.Equal(LeadStatus.NeedsManualReview, leads[0].Status);
        Assert.Single(repository.Added);
    }

    [Fact]
    public async Task LeadWorkflow_RoutesXSourceToXService()
    {
        var gitHub = new RecordingDiscoveryService(new Lead { DisplayName = "GitHub Lead", GitHubUrl = "https://github.com/alice" });
        var x = new RecordingDiscoveryService(new Lead { DisplayName = "X Lead", XUrl = "https://x.com/alice" });
        var workflow = new LeadWorkflow(new RecordingRepository(), gitHub, x, new NoopWebsiteExtractionService(), new LeadScoringService());

        var leads = await workflow.DiscoverAsync(new DiscoverLeadsRequest("founder AI", Source: DiscoverySource.X), CancellationToken.None);

        Assert.False(gitHub.WasCalled);
        Assert.True(x.WasCalled);
        Assert.Single(leads);
        Assert.Equal("https://x.com/alice", leads[0].XUrl);
        Assert.Equal(LeadStatus.NeedsManualReview, leads[0].Status);
    }

    [Theory]
    [InlineData("https://twitter.com/alice/status/123", "https://x.com/alice")]
    [InlineData("https://x.com/alice", "https://x.com/alice")]
    [InlineData("@alice", "https://x.com/alice")]
    public void XDiscoveryService_NormalizesXAndTwitterProfileUrls(string input, string expected)
    {
        Assert.Equal(expected, XDiscoveryService.NormalizeProfileUrl(input));
    }

    [Fact]
    public async Task XDiscoveryService_DedupesDuplicateProfileUrls()
    {
        const string searchHtml = """
            <html><body>
              <a href="https://x.com/alice">Alice</a>
              <a href="https://twitter.com/alice/status/123">Duplicate Alice</a>
            </body></html>
            """;
        const string profileHtml = """
            <html><head>
              <title>Alice Founder (@alice) / X</title>
              <meta property="og:description" content="Founder and AI creator. alice@example.com">
            </head><body>
              <a href="https://alice.example">Website</a>
            </body></html>
            """;
        var handler = new StubHttpMessageHandler(uri =>
            uri.Host.Contains("duckduckgo", StringComparison.OrdinalIgnoreCase)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(searchHtml) }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(profileHtml) });
        var service = new XDiscoveryService(new HttpClient(handler));

        var leads = await service.DiscoverAsync(new DiscoverLeadsRequest("founder AI", 25, DiscoverySource.X), CancellationToken.None);

        var lead = Assert.Single(leads);
        Assert.Equal("https://x.com/alice", lead.XUrl);
        Assert.Equal("alice", Assert.Single(lead.SourceProfiles).PublicHandle);
        Assert.Equal("alice@example.com", lead.PublicEmail);
        Assert.Single(handler.RequestedUris, uri => uri.Host.Equals("x.com", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task XDiscoveryService_ExtractsEncodedSearchResultUrls()
    {
        const string searchHtml = """
            <html><body>
              <a href="/l/?uddg=https%3A%2F%2Ftwitter.com%2Fencoded_founder%2Fstatus%2F123">Encoded Founder</a>
            </body></html>
            """;
        const string profileHtml = """
            <html><head>
              <title>Encoded Founder (@encoded_founder) / X</title>
              <meta property="og:description" content="Founder helping SaaS teams with AI GTM.">
            </head><body></body></html>
            """;
        var handler = new StubHttpMessageHandler(uri =>
            uri.Host.Contains("duckduckgo", StringComparison.OrdinalIgnoreCase)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(searchHtml) }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(profileHtml) });
        var service = new XDiscoveryService(new HttpClient(handler));

        var leads = await service.DiscoverAsync(new DiscoverLeadsRequest("founder", 25, DiscoverySource.X), CancellationToken.None);

        var lead = Assert.Single(leads);
        Assert.Equal("https://x.com/encoded_founder", lead.XUrl);
    }

    [Fact]
    public void LeadScoringService_ScoresXZextriSignals()
    {
        var scoring = new LeadScoringService();
        var weakLead = new Lead { DisplayName = "Quiet Person", XUrl = "https://x.com/quiet" };
        var strongLead = new Lead
        {
            DisplayName = "Asha",
            XUrl = "https://x.com/asha",
            LinkedInUrl = "https://linkedin.com/in/asha",
            SourceProfiles =
            [
                new SourceProfile
                {
                    Kind = SourceKind.X,
                    Url = "https://x.com/asha",
                    PublicHandle = "asha",
                    Bio = "Founder, creator, recruiter, sales GTM, LinkedIn personal brand, SaaS AI startup consultant."
                }
            ]
        };

        var weak = scoring.Score(weakLead);
        var strong = scoring.Score(strongLead);

        Assert.True(strong.Score > weak.Score);
        Assert.Contains("X profile shows founder/creator/GTM signals relevant to Zextri", strong.Rationale);
    }

    [Fact]
    public void Compliance_NoLinkedInScrapingServiceIsIntroduced()
    {
        var serviceTypes = typeof(GitHubDiscoveryService).Assembly
            .GetTypes()
            .Where(type => type.Name.EndsWith("Service", StringComparison.Ordinal))
            .Select(type => type.Name);

        Assert.DoesNotContain(serviceTypes, name => name.Contains("LinkedIn", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class RecordingDiscoveryService(params Lead[] leads) : IGitHubDiscoveryService, IXDiscoveryService
    {
        public bool WasCalled { get; private set; }

        public Task<IReadOnlyList<Lead>> DiscoverAsync(DiscoverLeadsRequest request, CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Task.FromResult<IReadOnlyList<Lead>>(leads);
        }
    }

    private sealed class NoopWebsiteExtractionService : IWebsiteExtractionService
    {
        public Task<WebsiteSnapshot?> ExtractAsync(Lead lead, CancellationToken cancellationToken) => Task.FromResult<WebsiteSnapshot?>(null);
    }

    private sealed class RecordingRepository : ILeadRepository
    {
        public List<Lead> Added { get; } = [];

        public Task<Lead?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<Lead?>(null);

        public Task<LeadSearchResult> SearchAsync(LeadSearchRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new LeadSearchResult([], 0));

        public Task AddRangeAsync(IEnumerable<Lead> leads, CancellationToken cancellationToken)
        {
            Added.AddRange(leads);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubHttpMessageHandler(Func<Uri, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<Uri> RequestedUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.NotNull(request.RequestUri);
            RequestedUris.Add(request.RequestUri);
            return Task.FromResult(responder(request.RequestUri));
        }
    }
}
