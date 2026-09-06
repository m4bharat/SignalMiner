using SignalMiner.Domain;
using SignalMiner.Application;

namespace SignalMiner.Api.Dtos;

public sealed record LeadSearchDto(IReadOnlyList<LeadDto> Items, int Total);

public sealed record LeadDto(
    Guid Id,
    string DisplayName,
    string? RoleTitle,
    string? PublicEmail,
    string? WebsiteUrl,
    string? GitHubUrl,
    string? XUrl,
    string? LinkedInUrl,
    int FitScore,
    string ScoreRationale,
    LeadStatus Status,
    ContactStatus ContactStatus,
    string? Notes,
    CompanyDto? Company,
    IReadOnlyList<SourceProfileDto> SourceProfiles,
    IReadOnlyList<WebsiteSnapshotDto> WebsiteSnapshots,
    IReadOnlyList<LeadEmailLogDto> EmailLogs)
{
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public int? Rank { get; init; }
    public string? PriorityGroup { get; init; }
    public decimal? OutreachFitScore { get; init; }
    public string? ZextriSegment { get; init; }
    public string? CountryUnverified { get; init; }
    public string? PersonalizationAngle { get; init; }
    public string? OutreachScoreRationale { get; init; }
    public string? DataQualityFlags { get; init; }
    public string? RecommendedAction { get; init; }
    public int? OriginalFitScore { get; init; }
    public int? SourceRow { get; init; }
    public string? SourceSheet { get; init; }

    public static LeadDto From(Lead lead) => new(
        lead.Id,
        lead.DisplayName,
        lead.RoleTitle,
        lead.PublicEmail,
        lead.WebsiteUrl,
        lead.GitHubUrl,
        lead.XUrl,
        lead.LinkedInUrl,
        lead.FitScore,
        lead.ScoreRationale,
        lead.Status,
        lead.ContactStatus,
        lead.Notes,
        lead.Company is null ? null : CompanyDto.From(lead.Company),
        lead.SourceProfiles.Select(SourceProfileDto.From).ToArray(),
        lead.WebsiteSnapshots.Select(WebsiteSnapshotDto.From).ToArray(),
        LeadEmailLogDto.From(lead.OutreachEvents))
    {
        FirstName = lead.FirstName,
        LastName = lead.LastName,
        Rank = lead.Rank,
        PriorityGroup = lead.PriorityGroup,
        OutreachFitScore = lead.OutreachFitScore,
        ZextriSegment = lead.ZextriSegment,
        CountryUnverified = lead.CountryUnverified,
        PersonalizationAngle = lead.PersonalizationAngle,
        OutreachScoreRationale = lead.OutreachScoreRationale,
        DataQualityFlags = lead.DataQualityFlags,
        RecommendedAction = lead.RecommendedAction,
        OriginalFitScore = lead.OriginalFitScore,
        SourceRow = lead.SourceRow,
        SourceSheet = lead.SourceSheet
    };
}

public sealed record CompanyDto(Guid Id, string Name, string? Domain, string? Summary, string[] Keywords)
{
    public static CompanyDto From(Company company) => new(company.Id, company.Name, company.Domain, company.Summary, company.Keywords);
}

public sealed record SourceProfileDto(Guid Id, SourceKind Kind, string Url, string PublicHandle, string? Bio, int? PublicActivityCount, DateTimeOffset CapturedAt)
{
    public static SourceProfileDto From(SourceProfile profile) => new(profile.Id, profile.Kind, profile.Url, profile.PublicHandle, profile.Bio, profile.PublicActivityCount, profile.CapturedAt);
}

public sealed record WebsiteSnapshotDto(Guid Id, string Url, string Title, string? Description, string[] PublicEmails, string[] Links, int QualityScore, DateTimeOffset CapturedAt)
{
    public static WebsiteSnapshotDto From(WebsiteSnapshot snapshot) => new(snapshot.Id, snapshot.Url, snapshot.Title, snapshot.Description, snapshot.PublicEmails, snapshot.Links, snapshot.QualityScore, snapshot.CapturedAt);
}

public sealed record LeadEmailLogDto(
    DateTimeOffset OccurredAt,
    string Kind,
    string? Template,
    string Log,
    bool IsTest)
{
    public static IReadOnlyList<LeadEmailLogDto> From(IEnumerable<OutreachEvent> events) =>
        events
            .Where(evt => evt.Type is OutreachEventType.ManualEmailSent or OutreachEventType.ManualEmailPrepared)
            .OrderByDescending(evt => evt.OccurredAt)
            .Select(From)
            .ToArray();

    private static LeadEmailLogDto From(OutreachEvent evt)
    {
        var isTest = evt.Type == OutreachEventType.ManualEmailPrepared;
        return new LeadEmailLogDto(
            evt.OccurredAt,
            isTest ? EmailStrings.TestEmailKind : EmailStrings.SentEmailKind,
            ExtractLineValue(evt.Body, EmailStrings.TemplateLabel),
            evt.Body,
            isTest);
    }

    private static string? ExtractLineValue(string body, string label)
    {
        var line = body
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .FirstOrDefault(item => item.StartsWith(label, StringComparison.OrdinalIgnoreCase));
        return line is null ? null : line[label.Length..].Trim();
    }
}

public sealed record OutreachEventDto(Guid Id, OutreachEventType Type, string Body, ContactStatus? NewContactStatus, DateTimeOffset OccurredAt, string CreatedBy)
{
    public static OutreachEventDto From(OutreachEvent evt) => new(evt.Id, evt.Type, evt.Body, evt.NewContactStatus, evt.OccurredAt, evt.CreatedBy);
}
