using Microsoft.EntityFrameworkCore;
using SignalMiner.Application;
using SignalMiner.Domain;

namespace SignalMiner.Infrastructure;

public sealed class LeadRepository(SignalMinerDbContext db) : ILeadRepository
{
    public Task<Lead?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Leads
            .Include(x => x.Company)
            .Include(x => x.SourceProfiles)
            .Include(x => x.WebsiteSnapshots)
            .Include(x => x.OutreachEvents)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<LeadSearchResult> SearchAsync(LeadSearchRequest request, CancellationToken cancellationToken)
    {
        var query = db.Leads
            .Include(x => x.Company)
            .Include(x => x.SourceProfiles)
            .Include(x => x.WebsiteSnapshots)
            .Include(x => x.OutreachEvents)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            var rawQuery = request.Query.Trim();
            var q = rawQuery.ToLower();
            var hasLeadId = Guid.TryParse(rawQuery, out var leadId);
            query = query.Where(x =>
                (hasLeadId && x.Id == leadId) ||
                x.DisplayName.ToLower().Contains(q) ||
                (x.RoleTitle != null && x.RoleTitle.ToLower().Contains(q)) ||
                (x.Company != null && x.Company.Name.ToLower().Contains(q)) ||
                (x.Company != null && x.Company.Domain != null && x.Company.Domain.ToLower().Contains(q)) ||
                (x.PublicEmail != null && x.PublicEmail.ToLower().Contains(q)) ||
                (x.GitHubUrl != null && x.GitHubUrl.ToLower().Contains(q)) ||
                (x.XUrl != null && x.XUrl.ToLower().Contains(q)) ||
                (x.LinkedInUrl != null && x.LinkedInUrl.ToLower().Contains(q)) ||
                (x.WebsiteUrl != null && x.WebsiteUrl.ToLower().Contains(q)) ||
                (x.Notes != null && x.Notes.ToLower().Contains(q)) ||
                x.SourceProfiles.Any(profile =>
                    profile.Url.ToLower().Contains(q) ||
                    profile.PublicHandle.ToLower().Contains(q) ||
                    (profile.Bio != null && profile.Bio.ToLower().Contains(q))));
        }

        if (request.MinFitScore is not null)
        {
            query = query.Where(x => x.FitScore >= request.MinFitScore);
        }

        if (request.ContactStatus is not null)
        {
            query = query.Where(x => x.ContactStatus == request.ContactStatus);
        }

        if (request.Status is not null)
        {
            query = query.Where(x => x.Status == request.Status);
        }

        if (request.SourceKind is not null)
        {
            query = query.Where(x => x.SourceProfiles.Any(profile => profile.Kind == request.SourceKind));
        }

        if (request.ImportedOnly == true)
        {
            query = query.Where(x => x.Notes != null && x.Notes.Contains("Imported from curated launch contacts file"));
        }

        if (request.HasEmail is not null)
        {
            query = request.HasEmail.Value
                ? query.Where(x => x.PublicEmail != null && x.PublicEmail != string.Empty)
                : query.Where(x => x.PublicEmail == null || x.PublicEmail == string.Empty);
        }

        if (request.HasLinkedIn is not null)
        {
            query = request.HasLinkedIn.Value
                ? query.Where(x => x.LinkedInUrl != null && x.LinkedInUrl != string.Empty)
                : query.Where(x => x.LinkedInUrl == null || x.LinkedInUrl == string.Empty);
        }

        if (request.HasWebsite is not null)
        {
            query = request.HasWebsite.Value
                ? query.Where(x => x.WebsiteUrl != null && x.WebsiteUrl != string.Empty)
                : query.Where(x => x.WebsiteUrl == null || x.WebsiteUrl == string.Empty);
        }

        var total = await query.CountAsync(cancellationToken);
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var items = await query
            .OrderByDescending(x => x.FitScore)
            .ThenByDescending(x => x.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new LeadSearchResult(items, total);
    }

    public async Task<IReadOnlySet<string>> FindExistingImportKeysAsync(
        IEnumerable<string> emails,
        IEnumerable<string> linkedInUrls,
        CancellationToken cancellationToken)
    {
        var emailSet = emails
            .Where(email => !string.IsNullOrWhiteSpace(email))
            .Select(email => email.Trim().ToLower())
            .Distinct()
            .ToArray();
        var linkedInSet = linkedInUrls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url.Trim().ToLower())
            .Distinct()
            .ToArray();

        if (emailSet.Length == 0 && linkedInSet.Length == 0)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var matches = await db.Leads
            .AsNoTracking()
            .Where(lead =>
                (lead.PublicEmail != null && emailSet.Contains(lead.PublicEmail.ToLower())) ||
                (lead.LinkedInUrl != null && linkedInSet.Contains(lead.LinkedInUrl.ToLower())))
            .Select(lead => new { lead.PublicEmail, lead.LinkedInUrl })
            .ToArrayAsync(cancellationToken);

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var match in matches)
        {
            if (!string.IsNullOrWhiteSpace(match.PublicEmail))
            {
                keys.Add($"email:{match.PublicEmail.Trim().ToLower()}");
            }

            if (!string.IsNullOrWhiteSpace(match.LinkedInUrl))
            {
                keys.Add($"linkedin:{match.LinkedInUrl.Trim().ToLower()}");
            }
        }

        return keys;
    }

    public async Task AddRangeAsync(IEnumerable<Lead> leads, CancellationToken cancellationToken) =>
        await db.Leads.AddRangeAsync(leads, cancellationToken);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        await MarkMissingTrackedDependentsAsAddedAsync(cancellationToken);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!await MarkMissingTrackedDependentsAsAddedAsync(cancellationToken))
            {
                throw;
            }

            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<bool> MarkMissingTrackedDependentsAsAddedAsync(CancellationToken cancellationToken)
    {
        var changed = false;

        foreach (var entry in db.ChangeTracker.Entries<WebsiteSnapshot>().Where(entry => entry.State == EntityState.Modified))
        {
            if (!await db.WebsiteSnapshots.AsNoTracking().AnyAsync(snapshot => snapshot.Id == entry.Entity.Id, cancellationToken))
            {
                entry.State = EntityState.Added;
                changed = true;
            }
        }

        foreach (var entry in db.ChangeTracker.Entries<SourceProfile>().Where(entry => entry.State == EntityState.Modified))
        {
            if (!await db.SourceProfiles.AsNoTracking().AnyAsync(profile => profile.Id == entry.Entity.Id, cancellationToken))
            {
                entry.State = EntityState.Added;
                changed = true;
            }
        }

        foreach (var entry in db.ChangeTracker.Entries<OutreachEvent>().Where(entry => entry.State == EntityState.Modified))
        {
            if (!await db.OutreachEvents.AsNoTracking().AnyAsync(evt => evt.Id == entry.Entity.Id, cancellationToken))
            {
                entry.State = EntityState.Added;
                changed = true;
            }
        }

        return changed;
    }
}
