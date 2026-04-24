using Microsoft.EntityFrameworkCore;
using PortfolioApi.Models;

namespace PortfolioApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<RecoveryCode> RecoveryCodes => Set<RecoveryCode>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<Passkey> Passkeys => Set<Passkey>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.HasIndex(u => u.Username).IsUnique();
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Username).HasMaxLength(64);
            e.Property(u => u.Email).HasMaxLength(255);
        });

        b.Entity<Post>(e =>
        {
            e.HasIndex(p => p.Slug).IsUnique();
            e.Property(p => p.Title).HasMaxLength(200);
            e.Property(p => p.Slug).HasMaxLength(200);
            e.Property(p => p.Blocks).HasColumnType("jsonb");
            // Tags map to a Postgres text[] — a GIN index lets `tag = ANY(tags)`
            // queries stay fast when the post count grows.
            e.Property(p => p.Tags).HasColumnType("text[]");
            e.HasIndex(p => p.Tags).HasMethod("gin");
            e.HasOne(p => p.Author)
                .WithMany(u => u.Posts)
                .HasForeignKey(p => p.AuthorId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Comment>(e =>
        {
            e.Property(c => c.Body).HasMaxLength(2000);
            e.HasOne(c => c.Post)
                .WithMany(p => p.Comments)
                .HasForeignKey(c => c.PostId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(c => c.Author)
                .WithMany(u => u.Comments)
                .HasForeignKey(c => c.AuthorId)
                // NULL on author delete = anonymised. Required for AVG: a user
                // can leave their comments behind without identifying them.
                .OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<RefreshToken>(e =>
        {
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.HasOne(t => t.User)
                .WithMany()
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<RecoveryCode>(e =>
        {
            // Lookup by hash to verify a code on login.
            e.HasIndex(r => r.CodeHash);
            e.HasOne(r => r.User)
                .WithMany()
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AuditEvent>(e =>
        {
            e.Property(x => x.Kind).HasMaxLength(64);
            e.Property(x => x.Detail).HasMaxLength(500);
            // List by user, newest first — covers the only access pattern.
            e.HasIndex(x => new { x.UserId, x.At });
            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Passkey>(e =>
        {
            // Lookup by credential id at the assertion step.
            e.HasIndex(p => p.CredentialId).IsUnique();
            e.Property(p => p.Name).HasMaxLength(64);
            e.HasOne(p => p.User)
                .WithMany()
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
