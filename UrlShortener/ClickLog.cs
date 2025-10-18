using System.ComponentModel.DataAnnotations;

public class ClickLog
{
    [Key] // Birincil anahtar
    public Guid Id { get; set; }

    public DateTime ClickDateUtc { get; set; }

    public string? IpAddress { get; set; } // Tıklayanın IP adresi (nullable)

    public string? ReferrerUrl { get; set; } // Hangi siteden geldiği (nullable)

    public string? UserAgent { get; set; } // Hangi tarayıcı/cihaz (nullable)

    // --- İlişkiyi kurmak için ---
    public Guid ShortenedUrlId { get; set; } // Hangi kısa linkin tıklandığı
}