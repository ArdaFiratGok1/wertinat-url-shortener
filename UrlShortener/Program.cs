using Microsoft.EntityFrameworkCore;
using UrlShortener;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


builder.Services.AddScoped<UrlShorteningService>();//service scope edildi.


builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    
    await dbContext.Database.MigrateAsync();
}



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
    
    if (!Uri.TryCreate(request.NewLongUrl, UriKind.Absolute, out _))//g�nderilen yeni urlnin format� ge�erli mi kontrol�
    {
        return Results.BadRequest("Yeni hedef URL ge�ersiz.");
    }

    
    var existingUrl = await dbContext.ShortenedUrls
        .SingleOrDefaultAsync(s => s.Code == code);

    if (existingUrl is null)
    {
        
        return Results.NotFound(new { Message = "G�ncellenecek link bulunamad�." });
    }

    
    existingUrl.LongUrl = request.NewLongUrl;

    
    await dbContext.SaveChangesAsync();

    
    return Results.Ok(new
    {
        Message = "Link hedefi ba�ar�yla g�ncellendi.",
        ShortUrl = existingUrl.ShortUrl,
        NewDestination = existingUrl.LongUrl
    });
});

app.MapGet("/{code}", async (string code, ApplicationDbContext dbContext, HttpContext httpContext) =>
{
    var shortenedUrl = await dbContext.ShortenedUrls
        .SingleOrDefaultAsync(s => s.Code == code);

    if (shortenedUrl is null)
    {
        return Results.NotFound();
    }

    if (shortenedUrl.ExpirationDateUtc.HasValue &&
        shortenedUrl.ExpirationDateUtc.Value < DateTime.UtcNow)
    {
        
        return Results.NotFound(new { Message = "Bu linkin s�resi dolmu� veya ge�ersizdir." });
    }

    
    var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();//IP


    var referrer = httpContext.Request.Headers["Referer"].ToString();//hangi siteden


    var userAgent = httpContext.Request.Headers["User-Agent"].ToString();//taray�c�-cihaz info

 
    var log = new ClickLog
    {
        Id = Guid.NewGuid(),
        ShortenedUrlId = shortenedUrl.Id,
        ClickDateUtc = DateTime.UtcNow,
        IpAddress = ipAddress,
        ReferrerUrl = string.IsNullOrEmpty(referrer) ? null : referrer,
        UserAgent = string.IsNullOrEmpty(userAgent) ? null : userAgent 
    };


    dbContext.ClickLogs.Add(log);
    await dbContext.SaveChangesAsync();

    return Results.Redirect(shortenedUrl.LongUrl);
});



app.MapPost("shorten", async (
    ShortenUrlRequest request,
    UrlShorteningService urlShorteningService,
    ApplicationDbContext dbContext,
    HttpContext httpContext) =>
{

    if (!Uri.TryCreate(request.Url, UriKind.Absolute, out _))
    {
        return Results.BadRequest("Ge�ersiz URL.");
    }

    if (request.ExpiresInHours.HasValue && request.ExpiresInHours.Value <= 0)
    {
        return Results.BadRequest(new { Message = "Ge�erlilik s�resi (ExpiresInHours) 0'dan b�y�k olmal�d�r." });
    }


    string code;

    if (!string.IsNullOrEmpty(request.CustomCode))
    {

        if (request.CustomCode.Length > 100)
        {
            return Results.BadRequest(new { Message = "�zel kod 100 karakterden uzun olamaz." });
        }

        var isTaken = await dbContext.ShortenedUrls
            .AnyAsync(s => s.Code == request.CustomCode);

        if (isTaken)
        {

            return Results.BadRequest(new { Message = "Bu �zel kod ('alias') zaten kullan�l�yor." });
        }


        code = request.CustomCode;
    }
    else
    {

        code = await urlShorteningService.GenerateUniqueCode();
    }

    var requestHost = httpContext.Request;
    var shortUrl = $"{requestHost.Scheme}://{requestHost.Host}/{code}";

    var shortenedUrl = new ShortenedUrl
    {
        Id = Guid.NewGuid(),
        LongUrl = request.Url,
        Code = code, 
        ShortUrl = shortUrl,
        CreatedOnUtc = DateTime.UtcNow,

        ExpirationDateUtc = request.ExpiresInHours.HasValue
            ? DateTime.UtcNow.AddHours(request.ExpiresInHours.Value) 
            : null
    };

    dbContext.ShortenedUrls.Add(shortenedUrl);
    await dbContext.SaveChangesAsync();

    return Results.Ok(shortenedUrl.ShortUrl);
});

app.Run();


public record ShortenUrlRequest(string Url, string? CustomCode, int? ExpiresInHours);
public record UpdateUrlRequest(string NewLongUrl);