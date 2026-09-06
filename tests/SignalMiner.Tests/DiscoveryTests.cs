using System.Net;
using System.Reflection;
using System.Text.Json;
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
        var linkedIn = new RecordingDiscoveryService(new Lead { DisplayName = "LinkedIn Lead", LinkedInUrl = "https://www.linkedin.com/in/alice" });
        var repository = new RecordingRepository();
        var workflow = new LeadWorkflow(repository, gitHub, x, linkedIn, new NoopWebsiteExtractionService(), new LeadScoringService());

        var leads = await workflow.DiscoverAsync(new DiscoverLeadsRequest("founder AI"), CancellationToken.None);

        Assert.True(gitHub.WasCalled);
        Assert.False(x.WasCalled);
        Assert.False(linkedIn.WasCalled);
        Assert.Single(leads);
        Assert.Equal(LeadStatus.NeedsManualReview, leads[0].Status);
        Assert.Single(repository.Added);
    }

    [Fact]
    public async Task LeadWorkflow_RoutesXSourceToXService()
    {
        var gitHub = new RecordingDiscoveryService(new Lead { DisplayName = "GitHub Lead", GitHubUrl = "https://github.com/alice" });
        var x = new RecordingDiscoveryService(new Lead { DisplayName = "X Lead", XUrl = "https://x.com/alice" });
        var linkedIn = new RecordingDiscoveryService(new Lead { DisplayName = "LinkedIn Lead", LinkedInUrl = "https://www.linkedin.com/in/alice" });
        var workflow = new LeadWorkflow(new RecordingRepository(), gitHub, x, linkedIn, new NoopWebsiteExtractionService(), new LeadScoringService());

        var leads = await workflow.DiscoverAsync(new DiscoverLeadsRequest("founder AI", Source: DiscoverySource.X), CancellationToken.None);

        Assert.False(gitHub.WasCalled);
        Assert.True(x.WasCalled);
        Assert.False(linkedIn.WasCalled);
        Assert.Single(leads);
        Assert.Equal("https://x.com/alice", leads[0].XUrl);
        Assert.Equal(LeadStatus.NeedsManualReview, leads[0].Status);
    }

    [Fact]
    public async Task LeadWorkflow_RoutesLinkedInSourceToLinkedInService()
    {
        var gitHub = new RecordingDiscoveryService(new Lead { DisplayName = "GitHub Lead", GitHubUrl = "https://github.com/alice" });
        var x = new RecordingDiscoveryService(new Lead { DisplayName = "X Lead", XUrl = "https://x.com/alice" });
        var linkedIn = new RecordingDiscoveryService(new Lead { DisplayName = "LinkedIn Lead", LinkedInUrl = "https://www.linkedin.com/in/alice" });
        var workflow = new LeadWorkflow(new RecordingRepository(), gitHub, x, linkedIn, new NoopWebsiteExtractionService(), new LeadScoringService());

        var leads = await workflow.DiscoverAsync(new DiscoverLeadsRequest("personal brand", Source: DiscoverySource.LinkedIn), CancellationToken.None);

        Assert.False(gitHub.WasCalled);
        Assert.False(x.WasCalled);
        Assert.True(linkedIn.WasCalled);
        Assert.Single(leads);
        Assert.Equal("https://www.linkedin.com/in/alice", leads[0].LinkedInUrl);
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
        var service = new XDiscoveryService(new HttpClient(handler), new NoopWebsiteExtractionService());

        var leads = await service.DiscoverAsync(new DiscoverLeadsRequest("founder AI", 25, DiscoverySource.X), CancellationToken.None);

        var lead = Assert.Single(leads);
        Assert.Equal("https://x.com/alice", lead.XUrl);
        Assert.Equal("alice", Assert.Single(lead.SourceProfiles, profile => profile.Kind == SourceKind.X).PublicHandle);
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
        var service = new XDiscoveryService(new HttpClient(handler), new NoopWebsiteExtractionService());

        var leads = await service.DiscoverAsync(new DiscoverLeadsRequest("founder", 25, DiscoverySource.X), CancellationToken.None);

        var lead = Assert.Single(leads);
        Assert.Equal("https://x.com/encoded_founder", lead.XUrl);
    }

    [Fact]
    public async Task XDiscoveryService_EnrichesWebsiteLinkedFromXProfile()
    {
        const string profileHtml = """
            <html><head>
              <title>SaaS Founder (@saasfounder) / X</title>
              <meta property="og:description" content="Founder building AI SaaS.">
            </head><body>
              <a href="https://founder.example">Website</a>
            </body></html>
            """;
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(profileHtml) });
        var websiteExtraction = new RecordingWebsiteExtractionService(new WebsiteSnapshot
        {
            Url = "https://founder.example/",
            Title = "Founder Example",
            Description = "AI SaaS founder",
            PublicEmails = ["hello@founder.example"],
            Links = ["https://github.com/founder"],
            QualityScore = 80
        });
        var service = new XDiscoveryService(new HttpClient(handler), websiteExtraction);

        var leads = await service.DiscoverAsync(new DiscoverLeadsRequest("https://x.com/saasfounder", 25, DiscoverySource.X), CancellationToken.None);

        var lead = Assert.Single(leads);
        Assert.Equal("https://founder.example/", lead.WebsiteUrl);
        Assert.Equal("hello@founder.example", lead.PublicEmail);
        Assert.Single(lead.WebsiteSnapshots);
        Assert.Single(websiteExtraction.Calls);
        Assert.Equal("https://founder.example/", websiteExtraction.Calls[0].WebsiteUrl);
        Assert.Contains("Public contact found from website linked on X profile.", lead.Notes);
        Assert.Equal(LeadStatus.NeedsManualReview, lead.Status);
    }

    [Fact]
    public async Task XDiscoveryService_StoresLinkedInUrlButDoesNotScrapeIt()
    {
        const string profileHtml = """
            <html><head>
              <title>Brand Creator (@brandcreator) / X</title>
              <meta property="og:description" content="Creator focused on LinkedIn personal brand.">
            </head><body>
              <a href="https://linkedin.com/in/brandcreator">LinkedIn</a>
            </body></html>
            """;
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(profileHtml) });
        var websiteExtraction = new RecordingWebsiteExtractionService((WebsiteSnapshot?)null);
        var service = new XDiscoveryService(new HttpClient(handler), websiteExtraction);

        var leads = await service.DiscoverAsync(new DiscoverLeadsRequest("https://x.com/brandcreator", 25, DiscoverySource.X), CancellationToken.None);

        var lead = Assert.Single(leads);
        Assert.Equal("https://linkedin.com/in/brandcreator", lead.LinkedInUrl);
        Assert.Null(lead.WebsiteUrl);
        Assert.Empty(websiteExtraction.Calls);
        Assert.Contains(lead.SourceProfiles, profile => profile.Kind == SourceKind.LinkedInProfileUrlOnly);
    }

    [Fact]
    public async Task XDiscoveryService_SavesManualReviewLeadWhenNoEmailAndNoWebsite()
    {
        const string profileHtml = """
            <html><head>
              <title>Quiet Founder (@quietfounder) / X</title>
              <meta property="og:description" content="Founder building quietly.">
            </head><body></body></html>
            """;
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(profileHtml) });
        var websiteExtraction = new RecordingWebsiteExtractionService((WebsiteSnapshot?)null);
        var service = new XDiscoveryService(new HttpClient(handler), websiteExtraction);

        var leads = await service.DiscoverAsync(new DiscoverLeadsRequest("https://x.com/quietfounder", 25, DiscoverySource.X), CancellationToken.None);

        var lead = Assert.Single(leads);
        Assert.Null(lead.PublicEmail);
        Assert.Null(lead.WebsiteUrl);
        Assert.Equal(LeadStatus.NeedsManualReview, lead.Status);
        Assert.Empty(websiteExtraction.Calls);
        Assert.Contains("No public email found. Manual review required.", lead.Notes);
    }

    [Fact]
    public async Task XDiscoveryService_SavesLeadWhenWebsiteExtractionFails()
    {
        const string profileHtml = """
            <html><head>
              <title>Resilient Founder (@resilient) / X</title>
              <meta property="og:description" content="Founder building AI SaaS.">
            </head><body>
              <a href="https://resilient.example">Website</a>
            </body></html>
            """;
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(profileHtml) });
        var websiteExtraction = new RecordingWebsiteExtractionService(new InvalidOperationException("site unavailable"));
        var service = new XDiscoveryService(new HttpClient(handler), websiteExtraction);

        var leads = await service.DiscoverAsync(new DiscoverLeadsRequest("https://x.com/resilient", 25, DiscoverySource.X), CancellationToken.None);

        var lead = Assert.Single(leads);
        Assert.Equal("https://resilient.example/", lead.WebsiteUrl);
        Assert.Null(lead.PublicEmail);
        Assert.Equal(LeadStatus.NeedsManualReview, lead.Status);
        Assert.Single(websiteExtraction.Calls);
        Assert.Contains("No public email found. Manual review required.", lead.Notes);
    }

    [Fact]
    public async Task GitHubDiscoveryService_ReportsInvalidToken()
    {
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{}") });
        var service = new GitHubDiscoveryService(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.github.com/")
        });

        var exception = await Assert.ThrowsAsync<DiscoveryAuthenticationException>(() =>
            service.DiscoverAsync(new DiscoverLeadsRequest("personal brand"), CancellationToken.None));

        Assert.Contains("GitHub rejected the configured token", exception.Message);
    }

    [Fact]
    public async Task PublicProfileUrlDiscoveryService_CreatesLinkedInUrlOnlyLeadFromDirectUrl()
    {
        var service = new PublicProfileUrlDiscoveryService(new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError))));

        var leads = await service.DiscoverAsync(
            new DiscoverLeadsRequest("linkedin.com/in/jane-founder", 25, DiscoverySource.LinkedIn),
            CancellationToken.None);

        var lead = Assert.Single(leads);
        Assert.Equal("Jane Founder", lead.DisplayName);
        Assert.Equal("https://www.linkedin.com/in/jane-founder", lead.LinkedInUrl);
        Assert.Null(lead.PublicEmail);
        Assert.Null(lead.WebsiteUrl);
        Assert.Contains("does not scrape LinkedIn", lead.Notes);
        Assert.Equal(SourceKind.LinkedInProfileUrlOnly, Assert.Single(lead.SourceProfiles).Kind);
    }

    [Fact]
    public async Task PublicProfileUrlDiscoveryService_ExtractsLinkedInUrlsFromPublicSearchResults()
    {
        const string searchHtml = """
            <html><body>
              <a href="/l/?uddg=https%3A%2F%2Fwww.linkedin.com%2Fin%2Fencoded-founder">Encoded Founder</a>
              <a href="https://www.linkedin.com/company/acme-ai/">Acme AI</a>
            </body></html>
            """;
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(searchHtml) });
        var service = new PublicProfileUrlDiscoveryService(new HttpClient(handler));

        var leads = await service.DiscoverAsync(
            new DiscoverLeadsRequest("personal brand", 25, DiscoverySource.LinkedIn),
            CancellationToken.None);

        Assert.Equal(2, leads.Count);
        Assert.Contains(leads, lead => lead.LinkedInUrl == "https://www.linkedin.com/in/encoded-founder");
        Assert.Contains(leads, lead => lead.LinkedInUrl == "https://www.linkedin.com/company/acme-ai");
        Assert.All(handler.RequestedUris, uri => Assert.DoesNotContain("linkedin.com", uri.Host, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PublicProfileUrlDiscoveryService_ExtractsBingEncodedLinkedInUrls()
    {
        const string searchHtml = """
            <html><body>
              <a href="https://www.bing.com/ck/a?u=a1aHR0cHM6Ly93d3cubGlua2VkaW4uY29tL2luL2JpbmctZm91bmRlcg">Bing Founder</a>
            </body></html>
            """;
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(searchHtml) });
        var service = new PublicProfileUrlDiscoveryService(new HttpClient(handler));

        var leads = await service.DiscoverAsync(
            new DiscoverLeadsRequest("founder", 25, DiscoverySource.LinkedIn),
            CancellationToken.None);

        var lead = Assert.Single(leads);
        Assert.Equal("https://www.linkedin.com/in/bing-founder", lead.LinkedInUrl);
        Assert.All(handler.RequestedUris, uri => Assert.DoesNotContain("linkedin.com", uri.Host, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PublicProfileUrlDiscoveryService_ReportsUnavailableSearchProviders()
    {
        var service = new PublicProfileUrlDiscoveryService(new HttpClient(new ThrowingHttpMessageHandler(new HttpRequestException("timeout"))));

        var exception = await Assert.ThrowsAsync<DiscoverySourceUnavailableException>(() =>
            service.DiscoverAsync(new DiscoverLeadsRequest("founder", 25, DiscoverySource.LinkedIn), CancellationToken.None));

        Assert.Contains("could not reach public search-result providers", exception.Message);
    }

    [Fact]
    public void LeadScoringService_ScoresXZextriSignals()
    {
        var scoring = new LeadScoringService();
        var weakLead = new Lead { DisplayName = "Quiet Person", XUrl = "https://x.com/quiet" };
        var reachableLead = new Lead
        {
            DisplayName = "Reachable Founder",
            XUrl = "https://x.com/reachable",
            WebsiteUrl = "https://reachable.example",
            PublicEmail = "hello@reachable.example",
            SourceProfiles =
            [
                new SourceProfile
                {
                    Kind = SourceKind.X,
                    Url = "https://x.com/reachable",
                    PublicHandle = "reachable",
                    Bio = "Founder building AI SaaS."
                }
            ]
        };
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
        var reachable = scoring.Score(reachableLead);
        var strong = scoring.Score(strongLead);

        Assert.True(strong.Score > weak.Score);
        Assert.True(reachable.Score > weak.Score);
        Assert.Contains("public email present", reachable.Rationale);
        Assert.Contains("public website present", reachable.Rationale);
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

    [Fact]
    public async Task ManualEmailService_SendsOneLeadAndRecordsOutreach()
    {
        var lead = new Lead
        {
            DisplayName = "Chris Lindolph",
            PublicEmail = "chris@dscturbo.com",
            ContactStatus = ContactStatus.NotContacted
        };
        var repository = new SingleLeadRepository(lead);
        var delivery = new RecordingEmailDeliveryService();
        var service = new ManualEmailService(repository, delivery);

        var updated = await service.SendAsync(
            lead.Id,
            new SendManualEmailRequest(
                "chris@dscturbo.com",
                "Hello Chris",
                "Manual note only.",
                null,
                null,
                null,
                null,
                [new EmailAttachment("overview.pdf", "application/pdf", [1, 2, 3])],
                TemplateId: "quick-introduction",
                TemplateVersion: "1.0.0",
                TemplateName: "Quick Introduction",
                TemplateCategory: "Cold outreach"),
            CancellationToken.None);

        Assert.NotNull(updated);
        var message = Assert.Single(delivery.Messages);
        Assert.Equal("chris@dscturbo.com", message.ToEmail);
        Assert.Equal("Hello Chris", message.Subject);
        Assert.Equal("overview.pdf", Assert.Single(message.Attachments).FileName);
        Assert.Equal(ContactStatus.Contacted, lead.ContactStatus);
        var evt = Assert.Single(lead.OutreachEvents);
        Assert.Equal(OutreachEventType.ManualEmailSent, evt.Type);
        Assert.Contains(EmailStrings.SentTimeLabel, evt.Body);
        Assert.Contains($"{EmailStrings.DeliveryStatusLabel} {EmailStrings.SubmittedStatus}", evt.Body);
        Assert.Contains($"{EmailStrings.ProviderMessageIdLabel} provider-message-1", evt.Body);
        Assert.Contains($"{EmailStrings.TemplateLabel} Quick Introduction (Cold outreach) v1.0.0 [quick-introduction]", evt.Body);
        Assert.Contains($"{EmailStrings.AttachmentsLabel} overview.pdf", evt.Body);
        Assert.Equal(ContactStatus.Contacted, evt.NewContactStatus);
        Assert.True(repository.WasSaved);
    }

    [Fact]
    public async Task ManualEmailService_RejectsLeadWithoutEmail()
    {
        var lead = new Lead { DisplayName = "LinkedIn Only" };
        var service = new ManualEmailService(new SingleLeadRepository(lead), new RecordingEmailDeliveryService());

        var exception = await Assert.ThrowsAsync<ManualEmailException>(() =>
            service.SendAsync(lead.Id, new SendManualEmailRequest(null, "Hello", "Body", null, null, null, null, []), CancellationToken.None));

        Assert.Equal(EmailStrings.RecipientRequired, exception.Message);
    }

    [Fact]
    public async Task ManualEmailService_RejectsDoNotContactWithoutCallingDelivery()
    {
        var lead = new Lead { PublicEmail = "alex@example.com", ContactStatus = ContactStatus.DoNotContact };
        var repository = new SingleLeadRepository(lead);
        var delivery = new RecordingEmailDeliveryService();
        var exception = await Assert.ThrowsAsync<ManualEmailException>(() =>
            new ManualEmailService(repository, delivery).SendAsync(lead.Id,
                new SendManualEmailRequest(lead.PublicEmail, "Hello", "Body", null, null, null, null, []), default));
        Assert.Equal(EmailStrings.DoNotContact, exception.Message);
        Assert.Empty(delivery.Messages);
        Assert.Empty(lead.OutreachEvents);
        Assert.False(repository.WasSaved);
    }

    [Fact]
    public async Task ManualEmailService_RejectsRecipientThatDoesNotMatchSelectedLead()
    {
        var lead = new Lead
        {
            DisplayName = "Marc Garcia",
            PublicEmail = "marc@kodiotech.com",
            ContactStatus = ContactStatus.NotContacted
        };
        var repository = new SingleLeadRepository(lead);
        var delivery = new RecordingEmailDeliveryService();
        var service = new ManualEmailService(repository, delivery);

        var exception = await Assert.ThrowsAsync<ManualEmailException>(() =>
            service.SendAsync(
                lead.Id,
                new SendManualEmailRequest("other@example.com", "Hello", "Body", null, null, null, null, []),
                CancellationToken.None));

        Assert.Equal(EmailStrings.RecipientMismatch, exception.Message);
        Assert.Empty(delivery.Messages);
        Assert.Equal(ContactStatus.NotContacted, lead.ContactStatus);
        Assert.False(repository.WasSaved);
    }

    [Fact]
    public async Task ManualEmailService_DoesNotMarkContactedWhenDeliveryFails()
    {
        var lead = new Lead
        {
            DisplayName = "Vinicius Silva",
            PublicEmail = "vinicius@zanvexis.com",
            ContactStatus = ContactStatus.NotContacted
        };
        var repository = new SingleLeadRepository(lead);
        var service = new ManualEmailService(repository, new FailingEmailDeliveryService());

        var exception = await Assert.ThrowsAsync<ManualEmailException>(() =>
            service.SendAsync(
                lead.Id,
                new SendManualEmailRequest("vinicius@zanvexis.com", "Hello", "Body", null, null, null, null, []),
                CancellationToken.None));

        Assert.Equal("Provider rejected the message.", exception.Message);
        Assert.Equal(ContactStatus.NotContacted, lead.ContactStatus);
        Assert.Empty(lead.OutreachEvents);
        Assert.False(repository.WasSaved);
    }

    [Fact]
    public async Task ManualEmailService_TestEmailUsesSenderInboxAndDoesNotMarkContacted()
    {
        var lead = new Lead
        {
            DisplayName = "Chris Lindolph",
            PublicEmail = "chris@dscturbo.com",
            ContactStatus = ContactStatus.NotContacted
        };
        var repository = new SingleLeadRepository(lead);
        var delivery = new RecordingEmailDeliveryService();
        var service = new ManualEmailService(repository, delivery);

        await service.SendAsync(
            lead.Id,
            new SendManualEmailRequest(
                "chris@dscturbo.com",
                "Preview",
                "Body",
                null,
                null,
                null,
                null,
                [],
                TemplateId: "post-demo",
                TemplateVersion: "1.0.0",
                TemplateName: "Post Demo Follow-up",
                TemplateCategory: "Test outreach",
                IsTest: true),
            CancellationToken.None);

        var message = Assert.Single(delivery.Messages);
        Assert.Equal(EmailStrings.SenderEmail, message.ToEmail);
        Assert.Equal(EmailStrings.TestInboxName, message.ToName);
        Assert.Equal(ContactStatus.NotContacted, lead.ContactStatus);
        var evt = Assert.Single(lead.OutreachEvents);
        Assert.Equal(OutreachEventType.ManualEmailPrepared, evt.Type);
        Assert.Contains(EmailStrings.TestEmailLabel, evt.Body);
        Assert.Contains($"{EmailStrings.TemplateLabel} Post Demo Follow-up (Test outreach) v1.0.0 [post-demo]", evt.Body);
        Assert.Null(evt.NewContactStatus);
        Assert.True(repository.WasSaved);
    }

    [Fact]
    public void EmailTemplates_AllThreeTemplatesAreDiscoverableAndBranded()
    {
        var templateRoot = FindRepositoryFile("ui", "signalminer-dashboard", "src", "assets", "email-templates");
        var manifestPath = Path.Combine(templateRoot, "manifest.json");
        var emailStringsPath = FindRepositoryFile("ui", "signalminer-dashboard", "src", "app", "email-strings.ts");
        var emailStrings = File.ReadAllText(emailStringsPath);

        Assert.True(File.Exists(manifestPath));
        var manifestJson = File.ReadAllText(manifestPath);
        Assert.Contains("\"quick-introduction\"", manifestJson);
        Assert.Contains("\"relationship-value\"", manifestJson);
        Assert.Contains("\"demo-follow-up\"", manifestJson);
        Assert.DoesNotContain("Quick Introduction", manifestJson);
        Assert.DoesNotContain("Relationship Value", manifestJson);
        Assert.DoesNotContain("Demo Follow-up", manifestJson);

        var templateFiles = Directory.GetFiles(templateRoot, "template.html", SearchOption.AllDirectories);
        Assert.Equal(3, templateFiles.Length);
        foreach (var templateFile in templateFiles)
        {
            var html = File.ReadAllText(templateFile);
            Assert.Contains("{{sharedHeader}}", html);
            Assert.Contains("{{sharedSignature}}", html);
            Assert.Contains("{{sharedFooter}}", html);
            Assert.Contains("{{ctaWatchDemo}}", html);
            Assert.Contains("{{ctaChrome}}", html);
            Assert.Contains("{{ctaEdge}}", html);
            Assert.Contains("{{storeAvailability}}", html);
            Assert.DoesNotContain("Watch Demo", html);
            Assert.DoesNotContain("Add to Chrome", html);
            Assert.DoesNotContain("Get for Microsoft Edge", html);
            Assert.DoesNotContain("Available on the official Chrome and Microsoft Edge stores.", html);
            Assert.DoesNotContain("Zextri Growth Team", html);
        }

        Assert.Contains("Watch Demo", emailStrings);
        Assert.Contains("Add to Chrome", emailStrings);
        Assert.Contains("Get for Microsoft Edge", emailStrings);
        Assert.Contains("Available on the official Chrome and Microsoft Edge stores.", emailStrings);
    }

    [Fact]
    public void EmailTemplates_SharedPartsUseOfficialLogoAndWebsiteColors()
    {
        var templateRoot = FindRepositoryFile("ui", "signalminer-dashboard", "src", "assets", "email-templates");
        var header = File.ReadAllText(Path.Combine(templateRoot, "shared", "email-header.html"));
        var signature = File.ReadAllText(Path.Combine(templateRoot, "shared", "email-signature.html"));
        var footer = File.ReadAllText(Path.Combine(templateRoot, "shared", "email-footer.html"));
        var relationship = File.ReadAllText(Path.Combine(templateRoot, "relationship-value", "template.html"));

        Assert.Contains("{{logoUrl}}", header);
        Assert.Contains("{{brandName}}", header);
        Assert.Contains("{{senderName}}", signature);
        Assert.Contains("{{senderTitle}}", signature);
        Assert.Contains("#2563eb", footer + relationship);
        Assert.Contains("#6D28D9", relationship);
        Assert.Contains("{{unsubscribeUrl}}", footer);
        Assert.Contains("{{signatureWebsiteLabel}}", signature);
        Assert.DoesNotContain("{{businessInfo}}", footer);
        Assert.DoesNotContain("{{websiteUrl}}", footer);
        Assert.DoesNotContain("zextri.com", footer + signature);
        Assert.DoesNotContain("Zextri Growth Team", header + signature + footer + relationship);
    }

    [Fact]
    public void EmailTemplates_KeepVisibleCopyAndUrlsInSharedEmailStrings()
    {
        var templateRoot = FindRepositoryFile("ui", "signalminer-dashboard", "src", "assets", "email-templates");
        var emailStringsPath = FindRepositoryFile("ui", "signalminer-dashboard", "src", "app", "email-strings.ts");
        var emailStrings = File.ReadAllText(emailStringsPath);
        var files = Directory.GetFiles(templateRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var combinedTemplates = string.Join(Environment.NewLine, files.Select(File.ReadAllText));
        var centralizedValues = new[]
        {
            "Quick Introduction",
            "Relationship Value",
            "Demo Follow-up",
            "Watch Demo",
            "Add to Chrome",
            "Get for Microsoft Edge",
            "Available on the official Chrome and Microsoft Edge stores.",
            "https://www.youtube.com/watch?v=Zo66nD5CMsc",
            "https://www.youtube.com/watch?v=v_bxGZnQU5o",
            "https://www.youtube.com/watch?v=LOfbyaqfk3w",
            "https://chromewebstore.google.com/detail/zextri/jnfghdnpnebjlokdgfdfioncdpbmdiba",
            "https://microsoftedge.microsoft.com/addons/detail/zextri/lopeokgklkpknnflmmgkaefnmphjielc"
        };

        foreach (var value in centralizedValues)
        {
            Assert.Contains(value, emailStrings);
            Assert.DoesNotContain(value, combinedTemplates);
        }
    }

    [Fact]
    public void EmailTemplates_AreIncludedInAngularBuildAssets()
    {
        var angularJsonPath = FindRepositoryFile("ui", "signalminer-dashboard", "angular.json");
        using var angularJson = JsonDocument.Parse(File.ReadAllText(angularJsonPath));
        var assets = angularJson.RootElement.GetProperty("projects").GetProperty("signalminer-dashboard")
            .GetProperty("architect").GetProperty("build").GetProperty("options").GetProperty("assets");
        Assert.Contains("src/assets", assets.EnumerateArray().Select(asset => asset.GetString()));
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Could not find {Path.Combine(segments)}.");
    }

    private sealed class RecordingDiscoveryService(params Lead[] leads) : IGitHubDiscoveryService, IXDiscoveryService, ILinkedInDiscoveryService
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

    private sealed class RecordingWebsiteExtractionService : IWebsiteExtractionService
    {
        private readonly WebsiteSnapshot? snapshot;
        private readonly Exception? exception;

        public RecordingWebsiteExtractionService(WebsiteSnapshot? snapshot)
        {
            this.snapshot = snapshot;
        }

        public RecordingWebsiteExtractionService(Exception exception)
        {
            this.exception = exception;
        }

        public List<Lead> Calls { get; } = [];

        public Task<WebsiteSnapshot?> ExtractAsync(Lead lead, CancellationToken cancellationToken)
        {
            Calls.Add(lead);
            if (exception is not null)
            {
                throw exception;
            }

            return Task.FromResult(snapshot);
        }
    }

    private sealed class RecordingRepository : ILeadRepository
    {
        public List<Lead> Added { get; } = [];

        public Task<Lead?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<Lead?>(null);

        public Task<LeadSearchResult> SearchAsync(LeadSearchRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new LeadSearchResult([], 0));

        public Task<IReadOnlySet<string>> FindExistingImportKeysAsync(
            IEnumerable<Guid> leadIds,
            IEnumerable<string> emails,
            IEnumerable<string> linkedInUrls,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        public Task AddRangeAsync(IEnumerable<Lead> leads, CancellationToken cancellationToken)
        {
            Added.AddRange(leads);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class SingleLeadRepository(Lead lead) : ILeadRepository
    {
        public bool WasSaved { get; private set; }

        public Task<Lead?> GetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(id == lead.Id ? lead : null);

        public Task<LeadSearchResult> SearchAsync(LeadSearchRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new LeadSearchResult([], 0));

        public Task<IReadOnlySet<string>> FindExistingImportKeysAsync(
            IEnumerable<Guid> leadIds,
            IEnumerable<string> emails,
            IEnumerable<string> linkedInUrls,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        public Task AddRangeAsync(IEnumerable<Lead> leads, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            WasSaved = true;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingEmailDeliveryService : IEmailDeliveryService
    {
        public List<EmailMessage> Messages { get; } = [];

        public Task<EmailDeliveryResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            Messages.Add(message);
            return Task.FromResult(new EmailDeliveryResult(EmailStrings.SubmittedStatus, "provider-message-1", DateTimeOffset.Parse("2026-08-21T00:00:00Z")));
        }
    }

    private sealed class FailingEmailDeliveryService : IEmailDeliveryService
    {
        public Task<EmailDeliveryResult> SendAsync(EmailMessage message, CancellationToken cancellationToken) =>
            throw new ManualEmailException("Provider rejected the message.", 502);
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

    private sealed class ThrowingHttpMessageHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(exception);
    }
}
