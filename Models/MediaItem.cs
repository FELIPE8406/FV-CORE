using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FvCore.Models;

public enum MediaType
{
    Audio,
    Video
}

public class MediaItem
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(500)]
    public string Titulo { get; set; } = string.Empty;

    [Required, MaxLength(1000)]
    public string RutaArchivo { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? GoogleDriveId { get; set; }

    public TimeSpan? Duracion { get; set; }

    [MaxLength(100)]
    public string? Genero { get; set; }

    public MediaType Tipo { get; set; } = MediaType.Audio;

    [MaxLength(50)]
    public string? Calidad { get; set; }

    public bool IsFavorite { get; set; }

    [MaxLength(300)]
    public string? AlbumName { get; set; }

    [MaxLength(300)]
    public string? ArtistName { get; set; }

    [MaxLength(1000)]
    public string? CoverPath { get; set; }

    public int? ArtistId { get; set; }

    [ForeignKey(nameof(ArtistId))]
    public Artist? Artist { get; set; }

    public ICollection<PlaylistMediaItem> PlaylistMediaItems { get; set; } = new List<PlaylistMediaItem>();
}
