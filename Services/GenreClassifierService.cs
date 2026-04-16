namespace FvCore.Services;

/// <summary>
/// Placeholder AI genre classification service.
/// Simulates an API call to classify music by analyzing the file/folder name.
/// </summary>
public class GenreClassifierService
{
    private static readonly Dictionary<string, string> GenreKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Metal subgenres
        { "metal", "Metal" },
        { "thrash", "Thrash Metal" },
        { "death", "Death Metal" },
        { "black metal", "Black Metal" },
        { "power metal", "Power Metal" },
        { "symphonic", "Symphonic Metal" },
        { "gothic", "Gothic Metal" },
        { "nu metal", "Nu Metal" },
        { "heavy", "Heavy Metal" },
        { "doom", "Doom Metal" },

        // Rock subgenres
        { "rock", "Rock" },
        { "punk", "Punk Rock" },
        { "grunge", "Grunge" },
        { "alternative", "Alternative Rock" },
        { "indie", "Indie Rock" },
        { "hard rock", "Hard Rock" },
        { "classic rock", "Classic Rock" },
        { "progressive", "Progressive Rock" },

        // Electronic
        { "electronic", "Electronic" },
        { "techno", "Techno" },
        { "synth", "Synthwave" },

        // Other
        { "pop", "Pop" },
        { "rap", "Rap" },
        { "hip hop", "Hip Hop" },
        { "jazz", "Jazz" },
        { "blues", "Blues" },
        { "classical", "Classical" },
        { "reggae", "Reggae" },
        { "country", "Country" },
        { "folk", "Folk" },
        { "soul", "Soul" },
        { "r&b", "R&B" },
    };

    // Known artist-to-genre mappings for better classification
    private static readonly Dictionary<string, string> ArtistGenreMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "METALLICA", "Thrash Metal" },
        { "IRON MAIDEN", "Heavy Metal" },
        { "LINKIN PARK", "Nu Metal / Alternative" },
        { "RAMMSTEIN", "Industrial Metal" },
        { "SYSTEM OF A DOWN", "Alternative Metal" },
        { "SLIPKNOT", "Nu Metal" },
        { "KORN", "Nu Metal" },
        { "EVANESCENCE", "Gothic Metal" },
        { "CRADLE OF FILTH", "Extreme Metal" },
        { "HELLOWEEN", "Power Metal" },
        { "STRATOVARIUS", "Power Metal" },
        { "RHAPSODY", "Symphonic Power Metal" },
        { "DRAGON FORCE", "Power Metal" },
        { "GAMMARAY", "Power Metal" },
        { "DEF LEPPARD", "Hard Rock" },
        { "WHITESNAKE", "Hard Rock" },
        { "BON JOVY", "Hard Rock" },
        { "SKID ROW", "Hard Rock / Glam Metal" },
        { "CINDERELLA", "Glam Metal" },
        { "Gun`s and Roses", "Hard Rock" },
        { "QUEEN", "Rock" },
        { "OASIS", "Britpop / Alternative Rock" },
        { "DISTURBED", "Nu Metal" },
        { "GODSMACK", "Post-Grunge / Metal" },
        { "PAPA ROACH", "Nu Metal / Rock" },
        { "THREE DAYS GRACE", "Post-Grunge / Alternative Rock" },
        { "MY CHEMICAL ROMANCE", "Alternative Rock / Emo" },
        { "LIMP BIZKIT", "Nu Metal / Rap Metal" },
        { "STATIC X", "Industrial Metal" },
        { "COAL CHAMBER", "Nu Metal" },
        { "MARILYN MANSON", "Industrial Metal / Rock" },
        { "OZZY OSBOURNE", "Heavy Metal / Hard Rock" },
        { "RATA BLANCA", "Heavy Metal" },
        { "KRAKEN", "Heavy Metal" },
        { "APOCALIPTICA", "Symphonic Metal / Cello Metal" },
        { "Children Of Bodom", "Melodic Death Metal" },
        { "BLONDIE", "New Wave / Pop" },
        { "Depeche Mode Violator", "Synthpop / Electronic" },
        { "R.E.M", "Alternative Rock" },
        { "The Offspring", "Punk Rock" },
        { "RASMUS", "Alternative Rock" },
        { "Heroes del Silencio", "Rock en Español" },
        { "Angeles del infierno", "Heavy Metal" },
        { "Europe", "Hard Rock / Glam Metal" },
        { "DAMM YANKEES", "Hard Rock" },
    };

    /// <summary>
    /// Simulates an AI API call to classify music genre based on file/folder name.
    /// In production, this would call an ML model or external API.
    /// </summary>
    public string GetGenreFromAI(string fileName, string? artistName = null, string? albumName = null)
    {
        // Priority 1: Known artist mapping
        if (!string.IsNullOrEmpty(artistName) && ArtistGenreMap.TryGetValue(artistName.Trim(), out var artistGenre))
        {
            return artistGenre;
        }

        // Priority 2: Keyword analysis on all available text
        var searchText = $"{fileName} {artistName} {albumName}".ToLowerInvariant();

        // Check multi-word keywords first (more specific)
        foreach (var (keyword, genre) in GenreKeywords.OrderByDescending(kv => kv.Key.Length))
        {
            if (searchText.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return genre;
            }
        }

        // Priority 3: Default classification
        return "Rock"; // Safe default for a metal/rock-heavy library
    }
}
