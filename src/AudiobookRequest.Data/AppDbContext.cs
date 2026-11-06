using AudiobookRequest.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace AudiobookRequest.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<BookRequest> BookRequests => Set<BookRequest>();
    public DbSet<ProcessedBook> ProcessedBooks => Set<ProcessedBook>();
    public DbSet<AppSettings> Settings => Set<AppSettings>();
    public DbSet<ListeningHistoryCache> ListeningHistory => Set<ListeningHistoryCache>();
    public DbSet<LibraryBookCache> LibraryBooks => Set<LibraryBookCache>();
    public DbSet<BookMetadataOverride> BookOverrides => Set<BookMetadataOverride>();
    public DbSet<Follow> Follows => Set<Follow>();
    public DbSet<FollowFind> FollowFinds => Set<FollowFind>();
    public DbSet<ListeningDay> ListeningDays => Set<ListeningDay>();
    public DbSet<BookRating> BookRatings => Set<BookRating>();
    public DbSet<BookTag> BookTags => Set<BookTag>();
    public DbSet<BookBookmark> BookBookmarks => Set<BookBookmark>();
    public DbSet<ImportedSource> ImportedSources => Set<ImportedSource>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>(e =>
        {
            e.HasIndex(u => u.JellyfinUserId).IsUnique();
        });

        modelBuilder.Entity<BookRequest>(e =>
        {
            e.HasOne(r => r.RequestedByUser)
                .WithMany(u => u.Requests)
                .HasForeignKey(r => r.RequestedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(r => r.ProcessedBook)
                .WithOne(p => p.BookRequest)
                .HasForeignKey<ProcessedBook>(p => p.BookRequestId);
        });

        modelBuilder.Entity<ListeningHistoryCache>(e =>
        {
            e.HasOne(h => h.AppUser)
                .WithMany()
                .HasForeignKey(h => h.AppUserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(h => new { h.AppUserId, h.JellyfinItemId }).IsUnique();
        });

        modelBuilder.Entity<LibraryBookCache>(e =>
        {
            e.HasIndex(b => b.JellyfinItemId).IsUnique();
        });

        modelBuilder.Entity<BookMetadataOverride>(e =>
        {
            e.HasIndex(b => b.JellyfinItemId).IsUnique();
        });

        modelBuilder.Entity<Follow>(e =>
        {
            e.HasOne(f => f.AppUser)
                .WithMany()
                .HasForeignKey(f => f.AppUserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(f => new { f.AppUserId, f.Kind, f.Name }).IsUnique();
        });

        modelBuilder.Entity<FollowFind>(e =>
        {
            e.HasOne(f => f.Follow)
                .WithMany(f => f.Finds)
                .HasForeignKey(f => f.FollowId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(f => new { f.FollowId, f.Asin }).IsUnique();
        });

        modelBuilder.Entity<ListeningDay>(e =>
        {
            e.HasOne(d => d.AppUser)
                .WithMany()
                .HasForeignKey(d => d.AppUserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(d => new { d.AppUserId, d.Day, d.JellyfinItemId }).IsUnique();
        });

        modelBuilder.Entity<BookRating>(e =>
        {
            e.HasOne(r => r.AppUser).WithMany().HasForeignKey(r => r.AppUserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(r => new { r.AppUserId, r.JellyfinItemId }).IsUnique();
        });

        modelBuilder.Entity<BookTag>(e =>
        {
            e.HasOne(t => t.AppUser).WithMany().HasForeignKey(t => t.AppUserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(t => new { t.AppUserId, t.JellyfinItemId, t.TagKey }).IsUnique();
            e.HasIndex(t => new { t.AppUserId, t.TagKey });
        });

        modelBuilder.Entity<ImportedSource>(e =>
        {
            e.HasIndex(i => i.SourceKey).IsUnique();
        });

        modelBuilder.Entity<BookBookmark>(e =>
        {
            e.HasOne(b => b.AppUser).WithMany().HasForeignKey(b => b.AppUserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(b => new { b.AppUserId, b.JellyfinItemId });
            // A repeat of the same bookmark (sent again after a dropped connection) is recognised by this.
            e.HasIndex(b => new { b.AppUserId, b.ClientId }).IsUnique();
        });
    }
}
