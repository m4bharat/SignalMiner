using Microsoft.EntityFrameworkCore;
using SignalMiner.Domain;

namespace SignalMiner.Infrastructure;

public sealed class SignalMinerDbContext(DbContextOptions<SignalMinerDbContext> options) : DbContext(options)
{
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<EmailSuppression> EmailSuppressions => Set<EmailSuppression>();
    public DbSet<EmailSubmission> EmailSubmissions => Set<EmailSubmission>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<OutreachEvent> OutreachEvents => Set<OutreachEvent>();
    public DbSet<SourceProfile> SourceProfiles => Set<SourceProfile>();
    public DbSet<WebsiteSnapshot> WebsiteSnapshots => Set<WebsiteSnapshot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EmailSuppression>(entity =>
        {
            entity.HasKey(x => x.NormalizedEmail);
            entity.Property(x => x.NormalizedEmail).HasMaxLength(320);
            entity.Property(x => x.Details).HasMaxLength(2000);
        });
        modelBuilder.Entity<EmailSubmission>().HasIndex(x => x.StartedAt);
        modelBuilder.Entity<Lead>(entity =>
        {
            entity.HasIndex(x => x.FitScore);
            entity.HasIndex(x => x.ContactStatus);
            entity.Property(x => x.ScoreRationale).HasMaxLength(1000);
            entity.Property(x => x.OutreachFitScore).HasPrecision(4, 2);
        });

        modelBuilder.Entity<Company>()
            .Property(x => x.Keywords)
            .HasColumnType("text[]");

        modelBuilder.Entity<WebsiteSnapshot>()
            .Property(x => x.PublicEmails)
            .HasColumnType("text[]");

        modelBuilder.Entity<WebsiteSnapshot>()
            .Property(x => x.Links)
            .HasColumnType("text[]");
    }
}
