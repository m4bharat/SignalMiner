namespace SignalMiner.Domain;

public enum LeadStatus
{
    New,
    Enriched,
    NeedsManualReview,
    Qualified,
    Disqualified,
    Archived
}

public enum ContactStatus
{
    NotContacted,
    ReadyForManualOutreach,
    Contacted,
    Replied,
    NotInterested,
    DoNotContact
}

public enum SourceKind
{
    GitHub,
    Website,
    X,
    LinkedInProfileUrlOnly,
    Manual
}

public enum OutreachEventType
{
    Note,
    ManualEmailPrepared,
    ManualEmailSent,
    CallLogged,
    StatusChanged
}

public sealed class Lead
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string DisplayName { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public int? Rank { get; set; }
    public string? PriorityGroup { get; set; }
    public decimal? OutreachFitScore { get; set; }
    public string? ZextriSegment { get; set; }
    public bool IsImported { get; set; }
    public string? CountryUnverified { get; set; }
    public string? PersonalizationAngle { get; set; }
    public string? OutreachScoreRationale { get; set; }
    public string? DataQualityFlags { get; set; }
    public string? RecommendedAction { get; set; }
    public int? OriginalFitScore { get; set; }
    public int? SourceRow { get; set; }
    public string? SourceSheet { get; set; }
    public string? RoleTitle { get; set; }
    public string? PublicEmail { get; set; }
    public string? WebsiteUrl { get; set; }
    public string? GitHubUrl { get; set; }
    public string? XUrl { get; set; }
    public string? LinkedInUrl { get; set; }
    public int FitScore { get; set; }
    public string ScoreRationale { get; set; } = string.Empty;
    public LeadStatus Status { get; set; } = LeadStatus.New;
    public ContactStatus ContactStatus { get; set; } = ContactStatus.NotContacted;
    public string? Notes { get; set; }
    public Guid? CompanyId { get; set; }
    public Company? Company { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<SourceProfile> SourceProfiles { get; set; } = [];
    public List<OutreachEvent> OutreachEvents { get; set; } = [];
    public List<WebsiteSnapshot> WebsiteSnapshots { get; set; } = [];
}

public sealed class Company
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Domain { get; set; }
    public string? Summary { get; set; }
    public string[] Keywords { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<Lead> Leads { get; set; } = [];
}

public sealed class OutreachEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LeadId { get; set; }
    public Lead Lead { get; set; } = null!;
    public OutreachEventType Type { get; set; }
    public string Body { get; set; } = string.Empty;
    public ContactStatus? NewContactStatus { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public string CreatedBy { get; set; } = "manual";
}

public sealed class SourceProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LeadId { get; set; }
    public Lead Lead { get; set; } = null!;
    public SourceKind Kind { get; set; }
    public string Url { get; set; } = string.Empty;
    public string PublicHandle { get; set; } = string.Empty;
    public string? Bio { get; set; }
    public int? PublicActivityCount { get; set; }
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class WebsiteSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LeadId { get; set; }
    public Lead Lead { get; set; } = null!;
    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string[] PublicEmails { get; set; } = [];
    public string[] Links { get; set; } = [];
    public int QualityScore { get; set; }
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
}
