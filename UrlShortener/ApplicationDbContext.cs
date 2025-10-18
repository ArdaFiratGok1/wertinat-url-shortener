using Microsoft.EntityFrameworkCore;
using UrlShortener;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<ShortenedUrl> ShortenedUrls { get; set; }

    public DbSet<ClickLog> ClickLogs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ShortenedUrl>(builder =>
        {
            builder
                .Property(s => s.Code)
                .HasMaxLength(100);//en fazla 100 karakterlik bir kısaltma yazılabilir (https://.../ekip-basvuru-formu vb.)

            builder
                .HasIndex(s => s.Code)
                .IsUnique();
        });
    }
}