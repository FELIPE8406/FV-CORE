using System.ComponentModel.DataAnnotations;

namespace FvCore.Models;

public class Playlist
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Nombre { get; set; } = string.Empty;

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    public ICollection<PlaylistMediaItem> PlaylistMediaItems { get; set; } = new List<PlaylistMediaItem>();
}
