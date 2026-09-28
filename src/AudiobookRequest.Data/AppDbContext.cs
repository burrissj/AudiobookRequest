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
    }
}
