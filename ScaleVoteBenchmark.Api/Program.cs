using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ScaleVoteBenchmark.Api;
using ScaleVoteBenchmark.Api.Cache;
using ScaleVoteBenchmark.Api.Interfaces;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

bool cacheEnabled = !bool.TryParse(builder.Configuration["Cache:Enabled"], out bool ce) || ce;
if (cacheEnabled)
{
    builder.Services.AddMemoryCache();
    builder.Services.AddSingleton<ICache, MemoryCacheRepository>();
}
else
{
    builder.Services.AddSingleton<ICache, NullCacheRepository>();
}

builder.Services.AddScoped<RepoFactory>();

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

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "ScaleVoteBenchmark API",
        Version = "v1",
        Description = "REST API za glasovanje i dohvat rezultata, koristi se za testiranje skaliranja u Azureu."
    });

    var jwtScheme = new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Unijeti isključivo JWT token dobiven putem /api/auth/login (bez prefiksa 'Bearer ')."
    };

    options.AddSecurityDefinition("Bearer", jwtScheme);
    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        { jwtScheme, Array.Empty<string>() }
    });
});

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

app.UseAuthentication();
app.UseAuthorization();

// Swagger je namjerno dostupan isključivo u Development okruženju,
// kako API dokumentacija ne bi bila javno izložena na Azureu.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "ScaleVoteBenchmark API v1");
    });
}

app.MapControllers();

app.Run();
