using SignalMiner.Domain;

namespace SignalMiner.Application;

public enum DiscoverySource
{
    GitHub,
    X,
    LinkedIn
}

public sealed record DiscoverLeadsRequest(string Query, int Limit = 25, DiscoverySource Source = DiscoverySource.GitHub);

public sealed record LeadSearchRequest(
    string? Query,
    int? MinFitScore,
    ContactStatus? ContactStatus,
    LeadStatus? Status,
    SourceKind? SourceKind,
    bool? ImportedOnly,
    bool? HasEmail,
    bool? HasLinkedIn,
    bool? HasWebsite,
    int Page = 1,
    int PageSize = 5);

public sealed record UpdateOutreachStatusRequest(ContactStatus Status, string? Note);

public sealed record OutreachEventRequest(OutreachEventType Type, string Body, ContactStatus? NewContactStatus);

public sealed record SendManualEmailRequest(
    string? ToEmail,
    string Subject,
    string Body,
    string? BodyHtml,
    string? ReplyTo,
    string? Cc,
    string? Bcc,
    IReadOnlyList<EmailAttachment> Attachments,
    bool IsTest = false,
    string? TemplateId = null,
    string? TemplateVersion = null,
    string? TemplateName = null,
    string? TemplateCategory = null);

public sealed record EmailAttachment(string FileName, string ContentType, byte[] Content);

public sealed record EmailMessage(
    string ToEmail,
    string ToName,
    string Subject,
    string Body,
    bool IsBodyHtml,
    string? ReplyTo,
    IReadOnlyList<string> Cc,
    IReadOnlyList<string> Bcc,
    IReadOnlyList<EmailAttachment> Attachments);

public sealed record EmailDeliveryResult(
    string Status,
    string? ProviderMessageId,
    DateTimeOffset SubmittedAt);

public sealed record LeadScore(int Score, string Rationale);

public sealed record LeadSearchResult(IReadOnlyList<Lead> Items, int Total);

public sealed record LeadImportResult(
    int TotalRows,
    int ValidRows,
    int ImportedRows,
    int SkippedRows,
    IReadOnlyList<LeadImportIssue> Issues,
    IReadOnlyList<LeadImportPreviewRow> PreviewRows);

public sealed record LeadImportIssue(int RowNumber, string Field, string Message, string? SourceSheet = null);

public sealed record LeadImportPreviewRow(
    int RowNumber,
    string DisplayName,
    string? RoleTitle,
    string? Company,
    string? PublicEmail,
    string? LinkedInUrl,
    string? Segment,
    int FitScore,
    ContactStatus ContactStatus,
    bool IsDuplicate,
    string? DuplicateReason)
{
    public int? Rank { get; init; }
    public string? PriorityGroup { get; init; }
    public decimal? OutreachFitScore { get; init; }
    public string? DataQualityFlags { get; init; }
    public string? RecommendedAction { get; init; }
    public string? SourceSheet { get; init; }
}

public sealed class DiscoveryRateLimitException(string message, DateTimeOffset? retryAfter = null) : Exception(message)
{
    public DateTimeOffset? RetryAfter { get; } = retryAfter;
}

public sealed class DiscoveryAuthenticationException(string message) : Exception(message);

public sealed class DiscoverySourceUnavailableException(string message) : Exception(message);

public sealed class WebsiteExtractionException(string message) : Exception(message);

public sealed class ManualEmailException(string message, int statusCode = 400) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public interface ILeadRepository
{
    Task<Lead?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<LeadSearchResult> SearchAsync(LeadSearchRequest request, CancellationToken cancellationToken);
    Task<IReadOnlySet<string>> FindExistingImportKeysAsync(IEnumerable<Guid> leadIds, IEnumerable<string> emails, IEnumerable<string> linkedInUrls, CancellationToken cancellationToken);
    Task AddRangeAsync(IEnumerable<Lead> leads, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IGitHubDiscoveryService
{
    Task<IReadOnlyList<Lead>> DiscoverAsync(DiscoverLeadsRequest request, CancellationToken cancellationToken);
}

public interface IXDiscoveryService
{
    Task<IReadOnlyList<Lead>> DiscoverAsync(DiscoverLeadsRequest request, CancellationToken cancellationToken);
}

public interface ILinkedInDiscoveryService
{
    Task<IReadOnlyList<Lead>> DiscoverAsync(DiscoverLeadsRequest request, CancellationToken cancellationToken);
}

public interface IWebsiteExtractionService
{
    Task<WebsiteSnapshot?> ExtractAsync(Lead lead, CancellationToken cancellationToken);
}

public interface ILeadScoringService
{
    LeadScore Score(Lead lead);
}

public interface ILeadImportService
{
    Task<LeadImportResult> ImportAsync(Stream file, string fileName, bool commit, CancellationToken cancellationToken);
}

public interface IEmailDeliveryService
{
    Task<EmailDeliveryResult> SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

public interface IManualEmailService
{
    Task<Lead?> SendAsync(Guid leadId, SendManualEmailRequest request, CancellationToken cancellationToken);
}

public interface ILeadWorkflow
{
    Task<IReadOnlyList<Lead>> DiscoverAsync(DiscoverLeadsRequest request, CancellationToken cancellationToken);
    Task<Lead?> EnrichAsync(Guid id, CancellationToken cancellationToken);
    Task<Lead?> UpdateOutreachStatusAsync(Guid id, UpdateOutreachStatusRequest request, CancellationToken cancellationToken);
    Task<OutreachEvent?> AddOutreachEventAsync(Guid id, OutreachEventRequest request, CancellationToken cancellationToken);
}
