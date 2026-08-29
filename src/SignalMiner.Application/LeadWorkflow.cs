using SignalMiner.Domain;

namespace SignalMiner.Application;

public sealed class LeadWorkflow(
    ILeadRepository repository,
    IGitHubDiscoveryService gitHubDiscovery,
    IXDiscoveryService xDiscovery,
    ILinkedInDiscoveryService linkedInDiscovery,
    IWebsiteExtractionService websiteExtraction,
    ILeadScoringService scoring) : ILeadWorkflow
{
    public async Task<IReadOnlyList<Lead>> DiscoverAsync(DiscoverLeadsRequest request, CancellationToken cancellationToken)
    {
        var leads = request.Source switch
        {
            DiscoverySource.GitHub => await gitHubDiscovery.DiscoverAsync(request, cancellationToken),
            DiscoverySource.X => await xDiscovery.DiscoverAsync(request, cancellationToken),
            DiscoverySource.LinkedIn => await linkedInDiscovery.DiscoverAsync(request, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Source, "Unsupported discovery source.")
        };

        foreach (var lead in leads)
        {
            var result = scoring.Score(lead);
            lead.FitScore = result.Score;
            lead.ScoreRationale = result.Rationale;
            lead.Status = LeadStatus.NeedsManualReview;
        }

        await repository.AddRangeAsync(leads, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return leads;
    }

    public async Task<Lead?> EnrichAsync(Guid id, CancellationToken cancellationToken)
    {
        var lead = await repository.GetAsync(id, cancellationToken);
        if (lead is null)
        {
            return null;
        }

        var snapshot = await websiteExtraction.ExtractAsync(lead, cancellationToken);
        if (snapshot is not null)
        {
            lead.WebsiteSnapshots.Add(snapshot);
            lead.PublicEmail ??= snapshot.PublicEmails.FirstOrDefault();
        }

        var result = scoring.Score(lead);
        lead.FitScore = result.Score;
        lead.ScoreRationale = result.Rationale;
        lead.Status = LeadStatus.Enriched;
        lead.UpdatedAt = DateTimeOffset.UtcNow;
        await repository.SaveChangesAsync(cancellationToken);
        return lead;
    }

    public async Task<Lead?> UpdateOutreachStatusAsync(Guid id, UpdateOutreachStatusRequest request, CancellationToken cancellationToken)
    {
        var lead = await repository.GetAsync(id, cancellationToken);
        if (lead is null)
        {
            return null;
        }

        lead.ContactStatus = request.Status;
        lead.UpdatedAt = DateTimeOffset.UtcNow;
        if (!string.IsNullOrWhiteSpace(request.Note))
        {
            lead.OutreachEvents.Add(new OutreachEvent
            {
                Type = OutreachEventType.StatusChanged,
                Body = request.Note,
                NewContactStatus = request.Status
            });
        }

        await repository.SaveChangesAsync(cancellationToken);
        return lead;
    }

    public async Task<OutreachEvent?> AddOutreachEventAsync(Guid id, OutreachEventRequest request, CancellationToken cancellationToken)
    {
        var lead = await repository.GetAsync(id, cancellationToken);
        if (lead is null)
        {
            return null;
        }

        var evt = new OutreachEvent
        {
            Type = request.Type,
            Body = request.Body,
            NewContactStatus = request.NewContactStatus
        };
        lead.OutreachEvents.Add(evt);
        if (request.NewContactStatus is not null)
        {
            lead.ContactStatus = request.NewContactStatus.Value;
        }

        lead.UpdatedAt = DateTimeOffset.UtcNow;
        await repository.SaveChangesAsync(cancellationToken);
        return evt;
    }
}
