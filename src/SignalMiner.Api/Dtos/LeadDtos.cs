using SignalMiner.Domain;

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
    IReadOnlyList<WebsiteSnapshotDto> WebsiteSnapshots)
{
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
        lead.WebsiteSnapshots.Select(WebsiteSnapshotDto.From).ToArray());
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

public sealed record OutreachEventDto(Guid Id, OutreachEventType Type, string Body, ContactStatus? NewContactStatus, DateTimeOffset OccurredAt, string CreatedBy)
{
    public static OutreachEventDto From(OutreachEvent evt) => new(evt.Id, evt.Type, evt.Body, evt.NewContactStatus, evt.OccurredAt, evt.CreatedBy);
}
