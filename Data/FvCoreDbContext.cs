using Microsoft.EntityFrameworkCore;
using FvCore.Models;

namespace FvCore.Data;

public class FvCoreDbContext : DbContext
{
    public FvCoreDbContext(DbContextOptions<FvCoreDbContext> options) : base(options) { }

    public DbSet<MediaItem> MediaItems => Set<MediaItem>();
    public DbSet<Artist> Artists => Set<Artist>();
    public DbSet<Playlist> Playlists => Set<Playlist>();
    public DbSet<PlaylistMediaItem> PlaylistMediaItems => Set<PlaylistMediaItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Composite key for PlaylistMediaItem
        modelBuilder.Entity<PlaylistMediaItem>()
            .HasKey(pmi => new { pmi.PlaylistId, pmi.MediaItemId });

        modelBuilder.Entity<PlaylistMediaItem>()
            .HasOne(pmi => pmi.Playlist)
            .WithMany(p => p.PlaylistMediaItems)
            .HasForeignKey(pmi => pmi.PlaylistId);

        modelBuilder.Entity<PlaylistMediaItem>()
            .HasOne(pmi => pmi.MediaItem)
            .WithMany(m => m.PlaylistMediaItems)
            .HasForeignKey(pmi => pmi.MediaItemId);

        // Index for fast lookup by path (idempotent scanner)
        modelBuilder.Entity<MediaItem>()
            .HasIndex(m => m.RutaArchivo)
            .IsUnique();

        // Index for artist name uniqueness
        modelBuilder.Entity<Artist>()
            .HasIndex(a => a.Nombre)
            .IsUnique();

        // Index for favorites query
        modelBuilder.Entity<MediaItem>()
            .HasIndex(m => m.IsFavorite);

        // Index for media type filtering
        modelBuilder.Entity<MediaItem>()
            .HasIndex(m => m.Tipo);
    }
}
