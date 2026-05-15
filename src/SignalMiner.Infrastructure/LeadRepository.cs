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
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            var q = request.Query.ToLower();
            query = query.Where(x =>
                x.DisplayName.ToLower().Contains(q) ||
                (x.Company != null && x.Company.Name.ToLower().Contains(q)) ||
                (x.PublicEmail != null && x.PublicEmail.ToLower().Contains(q)));
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

    public async Task AddRangeAsync(IEnumerable<Lead> leads, CancellationToken cancellationToken) =>
        await db.Leads.AddRangeAsync(leads, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
