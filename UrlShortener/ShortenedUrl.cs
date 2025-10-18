namespace UrlShortener
{
    public class ShortenedUrl
    {
        public Guid Id { get; set; }
        public string LongUrl { get; set; } = string.Empty;
        public string ShortUrl { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public DateTime CreatedOnUtc { get; set; }

        public DateTime? ExpirationDateUtc { get; set; }//girilen saat gectikten sonra calısmayacak. Eğer null girilirse süresiz olur.
    }
}
