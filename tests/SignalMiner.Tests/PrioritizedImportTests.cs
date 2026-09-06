using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using SignalMiner.Application;
using SignalMiner.Domain;
using SignalMiner.Infrastructure;
using Xunit;

namespace SignalMiner.Tests;

public sealed class PrioritizedImportTests
{
    private static readonly string[] Headers = ["Rank", "Priority Group", "Outreach Fit /10", "Outreach Status", "Display Name", "First Name", "Last Name", "Role / Title", "Zextri Segment", "Company", "Professional Email", "Company Domain", "Country (Unverified)", "LinkedIn", "Website", "X URL", "GitHub URL", "Personalization Angle", "Score Rationale", "Data Quality Flags", "Recommended Action", "Original Fit Score", "Lead Id", "Source Row"];

    private static string[] Row(Guid id, string email = "alex@example.com", string score = "8.3") =>
        ["1", "B — Verify Before Send", score, "NotContacted", "Alex Example", "Alex", "Example", "Founder", "Founder / Executive", "Example", email, "example.com", "United States", "https://linkedin.com/in/alex", "https://example.com/about", "https://x.com/alex", "https://github.com/alex", "Maintaining relationships", "Email matches domain", "Verify geography", "Verify before sending", "8", id.ToString(), "895"];

    [Fact]
    public async Task ReadsPrioritizedSheetsAndSkipsSummaryAndRepeatedSourceData()
    {
        var id = Guid.NewGuid();
        var holdRow = Row(Guid.NewGuid(), "other@example.com", "5.4");
        holdRow[0] = "2";
        holdRow[1] = "D — Hold / Exclude";
        holdRow[13] = "NULL";
        holdRow[20] = "Do not email";
        using var file = Workbook(
            ("Summary", [["Zextri Outreach Readiness"], ["Metric", "Count"]]),
            ("Ready 9plus", [Headers, ["", "", ""]]),
            ("Verify Before Send", [Headers, Row(id)]),
            ("Hold or Exclude", [Headers, holdRow]),
            ("Source Data", [["First Name", "Last Name", "Professional Email"], ["Raw", "Duplicate", "alex@example.com"]]),
            ("Scoring Rules", [["Category", "Maximum"]]));
        var repository = new MemoryRepository();
        var result = await new LeadImportService(repository).ImportAsync(file, "leads.xlsx", true, default);

        Assert.Empty(result.Issues);
        Assert.Equal(2, result.TotalRows);
        Assert.Equal(2, result.ImportedRows);
        var lead = repository.Leads.Single(lead => lead.Id == id);
        Assert.Equal(8.3m, lead.OutreachFitScore);
        Assert.Equal(8, lead.OriginalFitScore); // Original /100 must never be scaled to 80.
        Assert.Equal(8, lead.FitScore);
        Assert.Equal("Alex", lead.FirstName);
        Assert.Equal("Founder", lead.RoleTitle);
        Assert.Equal("Founder / Executive", lead.ZextriSegment);
        Assert.Equal("United States", lead.CountryUnverified);
        Assert.Equal("example.com", lead.Company?.Domain);
        Assert.Equal("https://example.com/about", lead.WebsiteUrl);
        Assert.Equal("https://x.com/alex", lead.XUrl);
        Assert.Equal("https://github.com/alex", lead.GitHubUrl);
        Assert.Equal("Maintaining relationships", lead.PersonalizationAngle);
        Assert.Equal("Email matches domain", lead.OutreachScoreRationale);
        Assert.Equal("Verify geography", lead.DataQualityFlags);
        Assert.Equal(895, lead.SourceRow);
        Assert.Equal("Verify Before Send", lead.SourceSheet);
        Assert.True(lead.IsImported);
        Assert.Null(lead.Notes);
        Assert.Empty(lead.ScoreRationale);
        Assert.Null(repository.Leads.Single(lead => lead.Id != id).LinkedInUrl);
        Assert.Equal(ContactStatus.NotContacted, repository.Leads[1].ContactStatus);
        Assert.Equal("Do not email", repository.Leads[1].RecommendedAction);
    }

    [Fact]
    public async Task SkipsExistingIdWithoutUpdatingTheRecord()
    {
        var existing = new Lead { DisplayName = "Existing contact" };
        var repository = new MemoryRepository();
        repository.Leads.Add(existing);
        var importer = new LeadImportService(repository);
        using var file = Workbook(("Verify Before Send", [Headers, Row(existing.Id)]));
        var preview = await importer.ImportAsync(file, "leads.xlsx", false, default);
        Assert.True(Assert.Single(preview.PreviewRows).IsDuplicate);
        Assert.Equal(1, preview.SkippedRows);
        Assert.False(repository.Saved);
        file.Position = 0;
        var commit = await importer.ImportAsync(file, "leads.xlsx", true, default);
        Assert.Equal(0, commit.ImportedRows);
        Assert.Equal(1, commit.SkippedRows);
        Assert.Single(repository.Leads);
        Assert.Null(existing.OutreachFitScore);
        Assert.Equal("Existing contact", existing.DisplayName);
        Assert.False(repository.Saved);
    }

    [Fact]
    public async Task ManualCsvUsesCanonicalFieldsAndPreservesQuotedNotes()
    {
        var csv = """"
            Display Name,Professional Email,Role / Title,Company,Company Domain,LinkedIn,Notes,Outreach Status,Original Fit Score
            "Alex, Example",alex@example.com,Founder,Example,example.com,linkedin.com/in/alex,"First line,
            second line with ""quotes""",NotContacted,8
            """";
        using var file = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var repository = new MemoryRepository();
        var importer = new LeadImportService(repository);
        var preview = await importer.ImportAsync(file, "manual-contact.csv", false, default);
        Assert.Equal(1, preview.ValidRows);
        Assert.Empty(repository.Leads);
        Assert.False(repository.Saved);
        file.Position = 0;
        var result = await importer.ImportAsync(file, "manual-contact.csv", true, default);
        Assert.Empty(result.Issues);
        var lead = Assert.Single(repository.Leads);
        Assert.Equal("Alex, Example", lead.DisplayName);
        Assert.Equal(8, lead.FitScore);
        Assert.Equal(8, lead.OriginalFitScore);
        Assert.Contains("second line with \"quotes\"", lead.Notes);
        Assert.True(lead.IsImported);
        Assert.Equal("https://example.com", lead.WebsiteUrl);
        Assert.Null(lead.OutreachScoreRationale);
    }

    [Fact]
    public async Task SkipsRepeatedEmailAcrossDifferentLeadIds()
    {
        using var file = Workbook(("Verify Before Send", [Headers, Row(Guid.NewGuid()), Row(Guid.NewGuid())]));
        var repository = new MemoryRepository();
        var result = await new LeadImportService(repository).ImportAsync(file, "leads.xlsx", true, default);
        Assert.Equal(1, result.ImportedRows);
        Assert.Equal(1, result.SkippedRows);
        Assert.Single(repository.Leads);
    }

    [Theory]
    [InlineData("11")]
    [InlineData("-1")]
    [InlineData("unknown")]
    [InlineData("8.333")]
    public async Task RejectsInvalidOutreachScores(string score)
    {
        using var file = Workbook(("Verify Before Send", [Headers, Row(Guid.NewGuid(), score: score)]));
        var repository = new MemoryRepository();
        var result = await new LeadImportService(repository).ImportAsync(file, "leads.xlsx", true, default);
        Assert.Contains(result.Issues, issue => issue.Field == "Outreach Fit /10");
        Assert.Empty(repository.Leads);
    }

    [Theory]
    [InlineData("999")]
    [InlineData("Misspelled")]
    public async Task InvalidStatusReportsItsWorksheet(string status)
    {
        var row = Row(Guid.NewGuid());
        row[3] = status;
        using var file = Workbook(("Hold or Exclude", [Headers, row]));
        var repository = new MemoryRepository();
        var result = await new LeadImportService(repository).ImportAsync(file, "leads.xlsx", true, default);
        var issue = Assert.Single(result.Issues);
        Assert.Equal("Outreach Status", issue.Field);
        Assert.Equal("Hold or Exclude", issue.SourceSheet);
        Assert.Empty(repository.Leads);
    }

    [Theory]
    [InlineData("bad@@example.com")]
    [InlineData("Alex <alex@example.com>")]
    public async Task RejectsMalformedMailboxValues(string email)
    {
        using var file = Workbook(("Verify Before Send", [Headers, Row(Guid.NewGuid(), email)]));
        var repository = new MemoryRepository();
        var result = await new LeadImportService(repository).ImportAsync(file, "leads.xlsx", true, default);
        Assert.Contains(result.Issues, issue => issue.Field == "Professional Email");
        Assert.Empty(repository.Leads);
    }

    [Fact]
    public async Task EquivalentLinkedInUrlsAreDuplicates()
    {
        var first = Row(Guid.NewGuid(), "");
        var second = Row(Guid.NewGuid(), "");
        first[13] = "http://linkedin.com/in/alex/?trk=source";
        second[13] = "https://www.linkedin.com/in/alex";
        using var file = Workbook(("Verify Before Send", [Headers, first, second]));
        var repository = new MemoryRepository();
        var result = await new LeadImportService(repository).ImportAsync(file, "leads.xlsx", true, default);
        Assert.Equal(1, result.ImportedRows);
        Assert.Equal(1, result.SkippedRows);
        Assert.Equal("https://www.linkedin.com/in/alex", Assert.Single(repository.Leads).LinkedInUrl);
    }

    [Theory]
    [InlineData("https://in.linkedin.com/in/alex", "https://www.linkedin.com/in/alex")]
    [InlineData("https://linkedin.com.evil.example/in/alex", null)]
    public void LinkedInNormalizationAcceptsRegionalHostsButRejectsLookalikes(string value, string? expected)
    {
        Assert.Equal(expected, PublicProfileUrlDiscoveryService.NormalizeLinkedInUrl(value));
    }

    [Theory]
    [InlineData("Display Name,Display Name\nAlex,Other")]
    [InlineData("Display Name,Notes\nAlex,\"Unclosed")]
    public async Task MalformedCsvFailsBeforeSaving(string csv)
    {
        using var file = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var repository = new MemoryRepository();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new LeadImportService(repository).ImportAsync(file, "leads.csv", true, default));
        Assert.False(repository.Saved);
    }

    [Fact]
    public async Task TruncatedWorkbookFailsBeforeArchiveSeeks()
    {
        using var file = new MemoryStream(Encoding.UTF8.GetBytes("not an xlsx"));
        var repository = new MemoryRepository();
        await Assert.ThrowsAsync<InvalidDataException>(() => new LeadImportService(repository).ImportAsync(file, "bad.xlsx", true, default));
        Assert.False(repository.Saved);
    }

    [Fact]
    public async Task RepeatedIdWithoutContactDetailsIsImportedOnlyOnce()
    {
        var row = Row(Guid.NewGuid(), "");
        row[13] = "NULL";
        using var file = Workbook(("Hold or Exclude", [Headers, row, row]));
        var repository = new MemoryRepository();
        var result = await new LeadImportService(repository).ImportAsync(file, "leads.xlsx", true, default);
        Assert.Empty(result.Issues);
        Assert.Equal(1, result.ImportedRows);
        Assert.Equal(1, result.SkippedRows);
    }

    [Fact]
    public void PostgreSqlModelStoresWorkbookScoreWithDecimalPrecision()
    {
        using var db = new SignalMinerDbContext(new DbContextOptionsBuilder<SignalMinerDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only").Options);
        var sql = db.Database.GenerateCreateScript();
        Assert.Contains("\"OutreachFitScore\" numeric(4,2)", sql);
        Assert.Contains("\"OriginalFitScore\" integer", sql);
        Assert.Contains("\"DataQualityFlags\" text", sql);
    }

    // Use relationship targets that deliberately differ from sheet1.xml.
    private static MemoryStream Workbook(params (string Name, string[][] Rows)[] sheets)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace package = "http://schemas.openxmlformats.org/package/2006/relationships";
            var workbook = new XElement(ns + "workbook", new XElement(ns + "sheets",
                sheets.Select((sheet, i) => new XElement(ns + "sheet", new XAttribute("name", sheet.Name), new XAttribute(rel + "id", $"rId{i}")))));
            var relationships = new XElement(package + "Relationships", sheets.Select((sheet, i) =>
                new XElement(package + "Relationship", new XAttribute("Id", $"rId{i}"), new XAttribute("Target", $"/xl/worksheets/data{i}.xml"))));
            Write("xl/workbook.xml", workbook);
            Write("xl/_rels/workbook.xml.rels", relationships);
            for (var i = 0; i < sheets.Length; i++)
                Write($"xl/worksheets/data{i}.xml", new XElement(ns + "worksheet", new XElement(ns + "sheetData",
                    sheets[i].Rows.Select(row => new XElement(ns + "row", row.Select(value => new XElement(ns + "c",
                        new XAttribute("t", "inlineStr"), new XElement(ns + "is", new XElement(ns + "t", value)))))))));
            void Write(string path, XElement xml)
            {
                using var writer = new StreamWriter(archive.CreateEntry(path).Open(), Encoding.UTF8);
                writer.Write(xml.ToString());
            }
        }
        stream.Position = 0;
        return stream;
    }

    private sealed class MemoryRepository : ILeadRepository
    {
        public List<Lead> Leads { get; } = [];
        public bool Saved { get; private set; }
        public Task<Lead?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Leads.FirstOrDefault(lead => lead.Id == id));
        public Task<LeadSearchResult> SearchAsync(LeadSearchRequest request, CancellationToken cancellationToken) => Task.FromResult(new LeadSearchResult(Leads, Leads.Count));
        public Task<IReadOnlySet<string>> FindExistingImportKeysAsync(IEnumerable<Guid> leadIds, IEnumerable<string> emails, IEnumerable<string> linkedInUrls, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(Leads.SelectMany(lead => new[] { $"id:{lead.Id}", $"email:{lead.PublicEmail}", $"linkedin:{lead.LinkedInUrl}" }).ToHashSet(StringComparer.OrdinalIgnoreCase));
        public Task AddRangeAsync(IEnumerable<Lead> leads, CancellationToken cancellationToken) { Leads.AddRange(leads); return Task.CompletedTask; }
        public Task SaveChangesAsync(CancellationToken cancellationToken) { Saved = true; return Task.CompletedTask; }
    }
}
