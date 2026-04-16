using System.ComponentModel.DataAnnotations;

namespace FvCore.Models;

public class Artist
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(300)]
    public string Nombre { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? FotoUrl { get; set; }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public int TrackCount { get; set; }

    public ICollection<MediaItem> MediaItems { get; set; } = new List<MediaItem>();
}
