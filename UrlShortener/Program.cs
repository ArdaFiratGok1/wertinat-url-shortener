using Microsoft.EntityFrameworkCore;
using UrlShortener;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


builder.Services.AddScoped<UrlShorteningService>();//service scope edildi.

// Veritabaný baðlantýsýný (DbContext) ekle
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
    // 1. Gönderilen YENÝ URL'nin formatý geçerli mi?
    if (!Uri.TryCreate(request.NewLongUrl, UriKind.Absolute, out _))
    {
        return Results.BadRequest("Yeni hedef URL geçersiz.");
    }

    // 2. Güncellenecek linki 'code' kullanarak veritabanýnda bul
    var existingUrl = await dbContext.ShortenedUrls
        .SingleOrDefaultAsync(s => s.Code == code);

    if (existingUrl is null)
    {
        // Bu koda sahip bir link yoksa, 404 dön
        return Results.NotFound(new { Message = "Güncellenecek link bulunamadý." });
    }

    // 3. Linkin hedefini (LongUrl) GÜNCELLE
    existingUrl.LongUrl = request.NewLongUrl;

    // 4. Deðiþikliði veritabanýna kaydet
    await dbContext.SaveChangesAsync();

    // 5. Baþarýlý olduðuna dair bir yanýt dön
    return Results.Ok(new
    {
        Message = "Link hedefi baþarýyla güncellendi.",
        ShortUrl = existingUrl.ShortUrl,
        NewDestination = existingUrl.LongUrl
    });
});

// YÖNLENDÝRME ENDPOINT'Ý
// Örn: GET http://localhost:5000/aBc12X
// Parametrelere 'HttpContext httpContext' eklendiðine dikkat edin
app.MapGet("/{code}", async (string code, ApplicationDbContext dbContext, HttpContext httpContext) =>
{
    var shortenedUrl = await dbContext.ShortenedUrls
        .SingleOrDefaultAsync(s => s.Code == code);

    if (shortenedUrl is null)
    {
        return Results.NotFound();
    }

    // --- YENÝ LOGLAMA KODU BURADA BAÞLIYOR ---

    // 1. IP Adresini al
    var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();

    // 2. Referer (Hangi siteden geldi) bilgisini al
    //    Not: Baþlýðýn adý "Referer" (çift 'r' yok), bu standart bir yazým hatasýdýr.
    var referrer = httpContext.Request.Headers["Referer"].ToString();

    // 3. User-Agent (Tarayýcý/Cihaz) bilgisini al
    var userAgent = httpContext.Request.Headers["User-Agent"].ToString();

    // 4. Yeni bir ClickLog nesnesi oluþtur
    var log = new ClickLog
    {
        Id = Guid.NewGuid(),
        ShortenedUrlId = shortenedUrl.Id, // Hangi linkin týklandýðýný baðlýyoruz
        ClickDateUtc = DateTime.UtcNow,
        IpAddress = ipAddress,
        ReferrerUrl = string.IsNullOrEmpty(referrer) ? null : referrer, // Boþsa null olarak kaydet
        UserAgent = string.IsNullOrEmpty(userAgent) ? null : userAgent // Boþsa null olarak kaydet
    };

    // 5. Logu veritabanýna ekle ve kaydet
    //    'await' kullanýyoruz ama yönlendirmeyi bekletmiyoruz (arka planda kaydeder)
    dbContext.ClickLogs.Add(log);
    await dbContext.SaveChangesAsync();

    // --- YENÝ LOGLAMA KODU BURADA BÝTTÝ ---

    // 6. Kullanýcýyý asýl URL'ye yönlendir
    return Results.Redirect(shortenedUrl.LongUrl);
});


// KISALTMA ENDPOINT'Ý
// Örn: POST http://localhost:5000/shorten
app.MapPost("shorten", async (
    ShortenUrlRequest request,
    UrlShorteningService urlShorteningService,
    ApplicationDbContext dbContext,
    HttpContext httpContext) =>
{
    // 1. URL geçerlilik kontrolü (Ayný)
    if (!Uri.TryCreate(request.Url, UriKind.Absolute, out _))
    {
        return Results.BadRequest("Geçersiz URL.");
    }

    // --- YENÝ MANTIK BAÞLANGICI ---
    string code; // Kullanýlacak kodu tutacak deðiþken

    if (!string.IsNullOrEmpty(request.CustomCode))
    {
        // 2. KULLANICI ÖZEL KOD GÖNDERDÝ
        // Bu kodun veritabanýnda olup olmadýðýný kontrol et
        if (request.CustomCode.Length > 100)
        {
            return Results.BadRequest(new { Message = "Özel kod 100 karakterden uzun olamaz." });
        }

        var isTaken = await dbContext.ShortenedUrls
            .AnyAsync(s => s.Code == request.CustomCode);

        if (isTaken)
        {
            // Varsa, hata dön
            return Results.BadRequest(new { Message = "Bu özel kod ('alias') zaten kullanýlýyor." });
        }

        // Alýnmamýþsa, bu kodu kullan
        code = request.CustomCode;
    }
    else
    {
        // 3. KULLANICI ÖZEL KOD GÖNDERMEDÝ
        // Eskisi gibi rastgele bir kod üret
        code = await urlShorteningService.GenerateUniqueCode();
    }
    // --- YENÝ MANTIK BÝTÝÞÝ ---

    // 4. Linki oluþtur (Kalan kod ayný)
    var requestHost = httpContext.Request;
    var shortUrl = $"{requestHost.Scheme}://{requestHost.Host}/{code}";

    var shortenedUrl = new ShortenedUrl
    {
        Id = Guid.NewGuid(),
        LongUrl = request.Url,
        Code = code, // 'code' deðiþkeni artýk ya özeldir ya da rastgeledir
        ShortUrl = shortUrl,
        CreatedOnUtc = DateTime.UtcNow
    };

    dbContext.ShortenedUrls.Add(shortenedUrl);
    await dbContext.SaveChangesAsync();

    return Results.Ok(shortenedUrl.ShortUrl);
});
// --- 4. Uygulamayý Çalýþtýr ---
app.Run();

// API'mizin POST isteðinde body'den ne beklediðini tanýmlayan basit bir 'record'
public record ShortenUrlRequest(string Url, string? CustomCode);
public record UpdateUrlRequest(string NewLongUrl);