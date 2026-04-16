using Microsoft.EntityFrameworkCore;

namespace ValiBlob.EFCore;

/// <summary>
/// Minimal <see cref="DbContext"/> that owns the <see cref="ResumableSessionEntity"/> table.
/// Consumers can inherit from this class to add it to their own <c>DbContext</c> if preferred,
/// or register it directly via <see cref="DependencyInjection.ServiceCollectionExtensions.AddValiEfCoreSessionStore(Microsoft.Extensions.DependencyInjection.IServiceCollection,System.Action{Microsoft.EntityFrameworkCore.DbContextOptionsBuilder})"/>.
/// </summary>
public class ValiResumableDbContext : DbContext
{
    public DbSet<ResumableSessionEntity> ResumableSessions => Set<ResumableSessionEntity>();

    public ValiResumableDbContext(DbContextOptions<ValiResumableDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ResumableSessionEntity>(e =>
        {
            e.HasKey(x => x.UploadId);
            e.ToTable("ValiBlob_ResumableSessions");
            e.Property(x => x.UploadId).HasMaxLength(128);
            e.Property(x => x.Path).HasMaxLength(2048);
            e.HasIndex(x => x.ExpiresAt); // supports efficient cleanup queries
        });
    }
}
