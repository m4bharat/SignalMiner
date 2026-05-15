using SignalMiner.Domain;

namespace SignalMiner.Application;

public enum DiscoverySource
{
    GitHub,
    X
}

public sealed record DiscoverLeadsRequest(string Query, int Limit = 25, DiscoverySource Source = DiscoverySource.GitHub);

public sealed record LeadSearchRequest(
    string? Query,
    int? MinFitScore,
    ContactStatus? ContactStatus,
    LeadStatus? Status,
    string[]? Keywords,
    int Page = 1,
    int PageSize = 5);

public sealed record UpdateOutreachStatusRequest(ContactStatus Status, string? Note);

public sealed record OutreachEventRequest(OutreachEventType Type, string Body, ContactStatus? NewContactStatus);

public sealed record LeadScore(int Score, string Rationale);

public sealed record LeadSearchResult(IReadOnlyList<Lead> Items, int Total);

public sealed class DiscoveryRateLimitException(string message, DateTimeOffset? retryAfter = null) : Exception(message)
{
    public DateTimeOffset? RetryAfter { get; } = retryAfter;
}

public interface ILeadRepository
{
    Task<Lead?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<LeadSearchResult> SearchAsync(LeadSearchRequest request, CancellationToken cancellationToken);
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

public interface IWebsiteExtractionService
{
    Task<WebsiteSnapshot?> ExtractAsync(Lead lead, CancellationToken cancellationToken);
}

public interface ILeadScoringService
{
    LeadScore Score(Lead lead);
}

public interface ILeadWorkflow
{
    Task<IReadOnlyList<Lead>> DiscoverAsync(DiscoverLeadsRequest request, CancellationToken cancellationToken);
    Task<Lead?> EnrichAsync(Guid id, CancellationToken cancellationToken);
    Task<Lead?> UpdateOutreachStatusAsync(Guid id, UpdateOutreachStatusRequest request, CancellationToken cancellationToken);
    Task<OutreachEvent?> AddOutreachEventAsync(Guid id, OutreachEventRequest request, CancellationToken cancellationToken);
}
