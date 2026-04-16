using System.ComponentModel.DataAnnotations.Schema;

namespace FvCore.Models;

public class PlaylistMediaItem
{
    public int PlaylistId { get; set; }

    [ForeignKey(nameof(PlaylistId))]
    public Playlist Playlist { get; set; } = null!;

    public int MediaItemId { get; set; }

    [ForeignKey(nameof(MediaItemId))]
    public MediaItem MediaItem { get; set; } = null!;

    public int Orden { get; set; }
}
