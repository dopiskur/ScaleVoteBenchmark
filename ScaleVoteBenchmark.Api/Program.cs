using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ScaleVoteBenchmark.Lib;
using ScaleVoteBenchmark.Lib.Cache;
using ScaleVoteBenchmark.Lib.Interfaces;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddMemoryCache();

builder.Services.AddSingleton<ICache, MemoryCacheRepository>();
builder.Services.AddScoped<RepoFactory>();

// CORS - dozvoljava pozive isključivo s ScaleVoteBenchmark.Web aplikacije.
// Adresu je potrebno prilagoditi stvarnoj domeni MVC aplikacije.
builder.Services.AddCors(options =>
{
    options.AddPolicy("ScaleVoteBenchmark.WebPolicy", policy =>
    {
        policy.WithOrigins(builder.Configuration["AllowedWebOrigin"] ?? "http://localhost:5100")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!)),
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// ------------------------------------------------------------
// Provjera dostupnosti baze podataka pri pokretanju aplikacije.
// Umjesto da se pogrešan connection string otkrije tek kod prvog
// stvarnog HTTP zahtjeva, greška se ovdje odmah ispisuje u konzolu.
// Ponašanje pri neuspjehu (zaustavi aplikaciju ili samo upozori) bira
// se postavkom "Startup:FailFastOnDbCheck" u appsettings.json.
// ------------------------------------------------------------
{
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("StartupDbCheck");
    bool failFast = !bool.TryParse(app.Configuration["Startup:FailFastOnDbCheck"], out bool ff) || ff;

    using var scope = app.Services.CreateScope();
    var repoFactory = scope.ServiceProvider.GetRequiredService<RepoFactory>();

    try
    {
        repoFactory.GetRepo().TestConnection();
        logger.LogInformation("Provjera konekcije s bazom podataka uspješna (DatabaseProvider={Provider}).",
            app.Configuration["DatabaseProvider"]);
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex,
            "Provjera konekcije s bazom podataka NEUSPJEŠNA (DatabaseProvider={Provider}). " +
            "Provjeri ConnectionStrings u appsettings.json, firewall pravila na Azureu i " +
            "je li baza pokrenuta.",
            app.Configuration["DatabaseProvider"]);

        if (failFast)
        {
            throw;
        }
    }
}

app.UseCors("ScaleVoteBenchmark.WebPolicy");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
