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
        Description = "REST API for voting and result retrieval, used for testing scaling in Azure."
    });

    var jwtScheme = new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Enter only the JWT token obtained from /api/auth/login (without the 'Bearer ' prefix)."
    };

    options.AddSecurityDefinition("Bearer", jwtScheme);
    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        { jwtScheme, Array.Empty<string>() }
    });
});

var app = builder.Build();

// ------------------------------------------------------------
// Database availability check and schema bootstrap at application
// startup. Instead of a wrong connection string (or a missing
// table) only being discovered on the first real HTTP request,
// the error is printed to the console immediately here.
// EnsureSchema() creates the Vote table and its stored
// procedures/functions using the same connection details from
// appsettings.json, but only if they do not already exist - an
// already-provisioned database is never touched. The behavior on
// failure (stop the application or just warn) is chosen via the
// "Startup:FailFastOnDbCheck" setting in appsettings.json.
// ------------------------------------------------------------
{
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("StartupDbCheck");
    bool failFast = !bool.TryParse(app.Configuration["Startup:FailFastOnDbCheck"], out bool ff) || ff;

    using var scope = app.Services.CreateScope();
    var repoFactory = scope.ServiceProvider.GetRequiredService<RepoFactory>();

    try
    {
        var repo = repoFactory.GetRepo();
        repo.TestConnection();
        logger.LogInformation("Database connection check succeeded (DatabaseProvider={Provider}).",
            app.Configuration["DatabaseProvider"]);

        repo.EnsureSchema();
        logger.LogInformation("Database schema check completed (DatabaseProvider={Provider}).",
            app.Configuration["DatabaseProvider"]);
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex,
            "Database connection or schema check FAILED (DatabaseProvider={Provider}). " +
            "Check ConnectionStrings in appsettings.json, firewall rules on Azure, " +
            "whether the database is running, and whether the configured user has " +
            "permission to create tables/procedures.",
            app.Configuration["DatabaseProvider"]);

        if (failFast)
        {
            throw;
        }
    }
}

// Serves the static dashboard page (wwwroot/index.html) at the
// application's root URL.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

// Swagger is intentionally available only in the Development
// environment, so the API documentation is not publicly exposed
// on Azure.
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
