using System.ComponentModel.DataAnnotations;

public class ClickLog
{
    [Key] 
    public Guid Id { get; set; }

    public DateTime ClickDateUtc { get; set; }

    public string? IpAddress { get; set; } 

    public string? ReferrerUrl { get; set; } 

    public string? UserAgent { get; set; } 

    
    public Guid ShortenedUrlId { get; set; } 
}
