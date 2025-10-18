using Microsoft.EntityFrameworkCore;
using UrlShortener;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


builder.Services.AddScoped<UrlShorteningService>();//service scope edildi.

// Veritaban� ba�lant�s�n� (DbContext) ekle
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();



if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapPut("/update/{code}", async (
    string code,
    UpdateUrlRequest request,
    ApplicationDbContext dbContext) =>
{
    // 1. G�nderilen YEN� URL'nin format� ge�erli mi?
    if (!Uri.TryCreate(request.NewLongUrl, UriKind.Absolute, out _))
    {
        return Results.BadRequest("Yeni hedef URL ge�ersiz.");
    }

    // 2. G�ncellenecek linki 'code' kullanarak veritaban�nda bul
    var existingUrl = await dbContext.ShortenedUrls
        .SingleOrDefaultAsync(s => s.Code == code);

    if (existingUrl is null)
    {
        // Bu koda sahip bir link yoksa, 404 d�n
        return Results.NotFound(new { Message = "G�ncellenecek link bulunamad�." });
    }

    // 3. Linkin hedefini (LongUrl) G�NCELLE
    existingUrl.LongUrl = request.NewLongUrl;

    // 4. De�i�ikli�i veritaban�na kaydet
    await dbContext.SaveChangesAsync();

    // 5. Ba�ar�l� oldu�una dair bir yan�t d�n
    return Results.Ok(new
    {
        Message = "Link hedefi ba�ar�yla g�ncellendi.",
        ShortUrl = existingUrl.ShortUrl,
        NewDestination = existingUrl.LongUrl
    });
});

// Y�NLEND�RME ENDPOINT'�
// �rn: GET http://localhost:5000/aBc12X
// Parametrelere 'HttpContext httpContext' eklendi�ine dikkat edin
app.MapGet("/{code}", async (string code, ApplicationDbContext dbContext, HttpContext httpContext) =>
{
    var shortenedUrl = await dbContext.ShortenedUrls
        .SingleOrDefaultAsync(s => s.Code == code);

    if (shortenedUrl is null)
    {
        return Results.NotFound();
    }

    // --- YEN� LOGLAMA KODU BURADA BA�LIYOR ---

    // 1. IP Adresini al
    var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();

    // 2. Referer (Hangi siteden geldi) bilgisini al
    //    Not: Ba�l���n ad� "Referer" (�ift 'r' yok), bu standart bir yaz�m hatas�d�r.
    var referrer = httpContext.Request.Headers["Referer"].ToString();

    // 3. User-Agent (Taray�c�/Cihaz) bilgisini al
    var userAgent = httpContext.Request.Headers["User-Agent"].ToString();

    // 4. Yeni bir ClickLog nesnesi olu�tur
    var log = new ClickLog
    {
        Id = Guid.NewGuid(),
        ShortenedUrlId = shortenedUrl.Id, // Hangi linkin t�kland���n� ba�l�yoruz
        ClickDateUtc = DateTime.UtcNow,
        IpAddress = ipAddress,
        ReferrerUrl = string.IsNullOrEmpty(referrer) ? null : referrer, // Bo�sa null olarak kaydet
        UserAgent = string.IsNullOrEmpty(userAgent) ? null : userAgent // Bo�sa null olarak kaydet
    };

    // 5. Logu veritaban�na ekle ve kaydet
    //    'await' kullan�yoruz ama y�nlendirmeyi bekletmiyoruz (arka planda kaydeder)
    dbContext.ClickLogs.Add(log);
    await dbContext.SaveChangesAsync();

    // --- YEN� LOGLAMA KODU BURADA B�TT� ---

    // 6. Kullan�c�y� as�l URL'ye y�nlendir
    return Results.Redirect(shortenedUrl.LongUrl);
});


// KISALTMA ENDPOINT'�
// �rn: POST http://localhost:5000/shorten
app.MapPost("shorten", async (
    ShortenUrlRequest request,
    UrlShorteningService urlShorteningService,
    ApplicationDbContext dbContext,
    HttpContext httpContext) =>
{
    // 1. URL ge�erlilik kontrol� (Ayn�)
    if (!Uri.TryCreate(request.Url, UriKind.Absolute, out _))
    {
        return Results.BadRequest("Ge�ersiz URL.");
    }

    // --- YEN� MANTIK BA�LANGICI ---
    string code; // Kullan�lacak kodu tutacak de�i�ken

    if (!string.IsNullOrEmpty(request.CustomCode))
    {
        // 2. KULLANICI �ZEL KOD G�NDERD�
        // Bu kodun veritaban�nda olup olmad���n� kontrol et
        if (request.CustomCode.Length > 100)
        {
            return Results.BadRequest(new { Message = "�zel kod 100 karakterden uzun olamaz." });
        }

        var isTaken = await dbContext.ShortenedUrls
            .AnyAsync(s => s.Code == request.CustomCode);

        if (isTaken)
        {
            // Varsa, hata d�n
            return Results.BadRequest(new { Message = "Bu �zel kod ('alias') zaten kullan�l�yor." });
        }

        // Al�nmam��sa, bu kodu kullan
        code = request.CustomCode;
    }
    else
    {
        // 3. KULLANICI �ZEL KOD G�NDERMED�
        // Eskisi gibi rastgele bir kod �ret
        code = await urlShorteningService.GenerateUniqueCode();
    }
    // --- YEN� MANTIK B�T��� ---

    // 4. Linki olu�tur (Kalan kod ayn�)
    var requestHost = httpContext.Request;
    var shortUrl = $"{requestHost.Scheme}://{requestHost.Host}/{code}";

    var shortenedUrl = new ShortenedUrl
    {
        Id = Guid.NewGuid(),
        LongUrl = request.Url,
        Code = code, // 'code' de�i�keni art�k ya �zeldir ya da rastgeledir
        ShortUrl = shortUrl,
        CreatedOnUtc = DateTime.UtcNow
    };

    dbContext.ShortenedUrls.Add(shortenedUrl);
    await dbContext.SaveChangesAsync();

    return Results.Ok(shortenedUrl.ShortUrl);
});
// --- 4. Uygulamay� �al��t�r ---
app.Run();

// API'mizin POST iste�inde body'den ne bekledi�ini tan�mlayan basit bir 'record'
public record ShortenUrlRequest(string Url, string? CustomCode);
public record UpdateUrlRequest(string NewLongUrl);